import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useCulture, useT } from '../i18n/I18nProvider';
import { ApiError, RuleError, ruleErrorKey, ValidationError } from '../lib/api';
import { formatDecimal, formatMoney, formatTime, formatWeight } from '../lib/format';
import {
  createRecord,
  getDevices,
  getWeighingSettings,
  listRecentRecords,
  listWeighingProducts,
  liveDevicePath,
  newRequestId,
  normalizeDevice,
  parseLiveJson,
  zeroDevice,
  type CreateRecordInput,
  type DeviceConnectionState,
  type LiveDevice,
  type LiveWeight,
  type LiveWeightState,
  type WeighingProduct,
  type WeighingRecord,
  type WeighingSettings,
  type ZeroResult,
} from '../lib/weighing';
import {
  add,
  amountOf,
  decimalToString,
  isKnownUnit,
  isZero,
  parseDecimal,
  subtract,
  tareFromKilograms,
  withScale,
  type Decimal,
} from '../lib/weights';
import { routes } from '../shell/navigation';
import { PageHeader } from '../shell/PageHeader';
import '../styles/weighing.css';

const searchDelayMs = 250;
const recentTake = 5;
/** While no scale is known, the device list is asked again this often. */
const devicePollMs = 5000;
/** Wait before opening the live stream again after the server closed it. */
const streamRetryMs = 3000;
/** How long the "saved" message stays. */
const savedNoticeMs = 4000;
/** Waits between save attempts after a network error or a server error; the last one repeats. */
export const saveRetryDelaysMs: readonly number[] = [500, 1000, 2000, 4000, 8000];

const deviceStorageKey = 'vyxum.weighing.device';
const lotStorageKey = 'vyxum.weighing.lot';

function readStored(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStored(key: string, value: string | null): void {
  try {
    if (value === null || value === '') window.localStorage.removeItem(key);
    else window.localStorage.setItem(key, value);
  } catch {
    // Storage can be blocked; the screen works without it.
  }
}

function useDebounced<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

const connectionKeys: Record<DeviceConnectionState, string> = {
  Connected: 'weighing.DeviceConnected',
  Connecting: 'weighing.DeviceConnecting',
  Disconnected: 'weighing.DeviceDisconnected',
  Error: 'devices.ScaleError',
};

const weightStateKeys: Record<LiveWeightState, string> = {
  Stable: 'weighing.StateStable',
  Motion: 'weighing.StateMotion',
  Overload: 'weighing.StateOverload',
  Underload: 'weighing.StateUnderload',
};

const zeroKeys: Record<ZeroResult, string> = {
  Done: 'weighing.ZeroDone',
  NotSupported: 'weighing.ZeroNotSupported',
  Rejected: 'weighing.ZeroRejected',
  Unavailable: 'weighing.ZeroUnavailable',
};

type Tone = 'info' | 'positive' | 'warning';

/** A message under the buttons. The key is translated when rendered, so a language switch updates it. */
interface Notice {
  tone: Tone;
  key: string;
  /** Text in front of the translated key, for example the record number. */
  prefix?: string;
}

/** Tare taken with "Take tare": the gross at that moment, in the scale's unit. */
interface StationTare {
  value: string;
  unit: string | null;
}

/** What the screen shows for one reading. Every weight has the same number of decimals. */
interface Preview {
  gross?: Decimal;
  tare?: Decimal;
  net?: Decimal;
  /** True when the scale itself reports the tare or a net weight; the software tare is then not used. */
  deviceTare: boolean;
}

/**
 * Gross, tare and net for display only (decision V-29); the server computes them again on save.
 * A tare or net reported by the scale wins, then the station tare, then the product's package tare (kg).
 */
function preview(weight: LiveWeight | null, product: WeighingProduct | null, stationTare: StationTare | null): Preview {
  const reading = parseDecimal(weight?.value);
  if (!weight || !reading) return { deviceTare: false };

  const reportedTare = parseDecimal(weight.tare);
  let gross: Decimal;
  let tare: Decimal | undefined;
  let net: Decimal | undefined;
  let deviceTare = false;

  if (weight.isNet === true || (reportedTare !== undefined && !isZero(reportedTare))) {
    deviceTare = true;
    tare = reportedTare ?? { units: 0n, scale: reading.scale };
    if (weight.isNet === false) {
      gross = reading;
      net = subtract(gross, tare);
    } else {
      net = reading;
      gross = add(net, tare);
    }
  } else {
    gross = reading;
    const station = stationTare && stationTare.unit === weight.unit ? parseDecimal(stationTare.value) : undefined;
    const packageTare = parseDecimal(product?.packageTare);
    if (station) {
      tare = station;
    } else if (packageTare && !isZero(packageTare)) {
      tare = weight.unit ? tareFromKilograms(packageTare, weight.unit) : undefined;
    } else {
      tare = { units: 0n, scale: reading.scale };
    }
    net = tare ? subtract(gross, tare) : undefined;
  }

  const scale = Math.max(gross.scale, tare?.scale ?? 0);
  return {
    gross: withScale(gross, scale),
    tare: tare ? withScale(tare, scale) : undefined,
    net: net ? withScale(net, scale) : undefined,
    deviceTare,
  };
}

function isRetryable(error: unknown): boolean {
  return error instanceof ApiError && (error.status === 0 || error.status >= 500);
}

/** Resource key for a failed save that is not retried. */
function saveErrorKey(error: unknown): string {
  if (error instanceof RuleError) return ruleErrorKey(error);
  if (error instanceof ValidationError) {
    const first = Object.values(error.errors).flat()[0];
    if (first) return first;
  }
  return 'weighing.SaveFailed';
}

type LoadStatus = 'loading' | 'ready' | 'error';

/** Operator screen (design source: BTartim): live weight, tare, zero, save, product choice and recent weighings. */
export function WeighingPage() {
  const t = useT();
  const culture = useCulture();
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  // Settings
  const [settings, setSettings] = useState<WeighingSettings | null>(null);
  const [settingsStatus, setSettingsStatus] = useState<LoadStatus>('loading');
  const [settingsTick, setSettingsTick] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setSettingsStatus('loading');
    getWeighingSettings({ signal: controller.signal })
      .then((result) => {
        if (controller.signal.aborted) return;
        setSettings(result);
        setSettingsStatus('ready');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        console.error('Loading the weighing settings failed.', error);
        setSettingsStatus('error');
      });
    return () => controller.abort();
  }, [settingsTick]);

  // Devices
  const [devices, setDevices] = useState<LiveDevice[] | null>(null);
  const [devicesStatus, setDevicesStatus] = useState<LoadStatus>('loading');
  const [devicesTick, setDevicesTick] = useState(0);
  const [chosenDeviceId, setChosenDeviceId] = useState<string | null>(() => readStored(deviceStorageKey));
  useEffect(() => {
    const controller = new AbortController();
    getDevices({ signal: controller.signal })
      .then((list) => {
        if (controller.signal.aborted) return;
        setDevices(list);
        setDevicesStatus('ready');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        console.error('Loading the devices failed.', error);
        setDevicesStatus('error');
      });
    return () => controller.abort();
  }, [devicesTick]);

  // Until a scale appears, ask again now and then (the agent may start after the screen).
  const noDevice = devicesStatus === 'ready' && devices !== null && devices.length === 0;
  useEffect(() => {
    if (!noDevice) return;
    const timer = setInterval(() => setDevicesTick((tick) => tick + 1), devicePollMs);
    return () => clearInterval(timer);
  }, [noDevice]);

  const deviceId = useMemo(() => {
    if (!devices || devices.length === 0) return null;
    if (chosenDeviceId && devices.some((d) => d.deviceId === chosenDeviceId)) return chosenDeviceId;
    return devices[0]?.deviceId ?? null;
  }, [devices, chosenDeviceId]);

  const chooseDevice = (id: string) => {
    setChosenDeviceId(id);
    writeStored(deviceStorageKey, id);
  };

  // Live stream of the selected device
  const [live, setLive] = useState<LiveDevice | null>(null);
  const [streamLost, setStreamLost] = useState(false);
  const [streamTick, setStreamTick] = useState(0);

  useEffect(() => {
    if (!deviceId || typeof EventSource === 'undefined') return;
    const source = new EventSource(liveDevicePath(deviceId));
    let retry: ReturnType<typeof setTimeout> | undefined;
    source.onopen = () => setStreamLost(false);
    source.onmessage = (event: MessageEvent<string>) => {
      try {
        setLive(normalizeDevice(parseLiveJson<LiveDevice>(event.data)));
        setStreamLost(false);
      } catch (error) {
        console.error('A live device event could not be read.', error);
      }
    };
    source.onerror = () => {
      setStreamLost(true);
      // EventSource reconnects by itself, except after an HTTP error; then open a new one later.
      if (source.readyState === EventSource.CLOSED) {
        retry = setTimeout(() => setStreamTick((tick) => tick + 1), streamRetryMs);
      }
    };
    return () => {
      if (retry !== undefined) clearTimeout(retry);
      source.close();
      setStreamLost(false);
    };
  }, [deviceId, streamTick]);

  // Products
  const productsAvailable = settings?.productsAvailable === true;
  const [search, setSearch] = useState('');
  const query = useDebounced(search, searchDelayMs);
  const [products, setProducts] = useState<WeighingProduct[] | null>(null);
  const [productsStatus, setProductsStatus] = useState<LoadStatus>('loading');
  const [productsTick, setProductsTick] = useState(0);
  const [product, setProduct] = useState<WeighingProduct | null>(null);
  useEffect(() => {
    if (!productsAvailable) return;
    const controller = new AbortController();
    setProductsStatus('loading');
    listWeighingProducts(query, { signal: controller.signal })
      .then((list) => {
        if (controller.signal.aborted) return;
        setProducts(list);
        setProductsStatus('ready');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        console.error('Loading the products failed.', error);
        setProductsStatus('error');
      });
    return () => controller.abort();
  }, [productsAvailable, query, productsTick]);

  // Lot
  const [lot, setLot] = useState(() => readStored(lotStorageKey) ?? '');
  const changeLot = (value: string) => {
    setLot(value);
    writeStored(lotStorageKey, value);
  };

  // Recent weighings of the selected device
  const [recent, setRecent] = useState<WeighingRecord[] | null>(null);
  useEffect(() => {
    if (!deviceId) {
      setRecent(null);
      return;
    }
    const controller = new AbortController();
    listRecentRecords(deviceId, recentTake, { signal: controller.signal })
      .then((list) => {
        if (!controller.signal.aborted) setRecent(list);
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        console.error('Loading the recent weighings failed.', error);
        setRecent([]);
      });
    return () => controller.abort();
  }, [deviceId]);

  // Tare, messages, save and zero
  const [stationTare, setStationTare] = useState<StationTare | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [saving, setSaving] = useState(false);
  const [zeroing, setZeroing] = useState(false);
  const savingRef = useRef(false);
  const noticeTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const showNotice = useCallback((next: Notice | null, clearAfterMs?: number) => {
    if (noticeTimer.current !== undefined) clearTimeout(noticeTimer.current);
    noticeTimer.current = undefined;
    setNotice(next);
    if (next && clearAfterMs !== undefined) {
      noticeTimer.current = setTimeout(() => {
        if (mounted.current) setNotice(null);
      }, clearAfterMs);
    }
  }, []);
  useEffect(() => () => clearTimeout(noticeTimer.current), []);

  // A station tare belongs to one scale.
  useEffect(() => setStationTare(null), [deviceId]);

  // The stream's latest snapshot of the selected device, or the one from the device list until it arrives.
  const current = live && live.deviceId === deviceId ? live : (devices?.find((d) => d.deviceId === deviceId) ?? null);
  const weight = current?.lastWeight ?? null;
  const shown = preview(weight, product, stationTare);
  const connected = current?.state === 'Connected' && !streamLost;
  const stable = weight?.state === 'Stable' && weight.value !== null;
  const zeroSupported = current?.supportedCommands === undefined || current.supportedCommands.includes('Zero');

  const takeTare = () => {
    if (!weight || !stable || !shown.gross) return;
    setStationTare({ value: decimalToString(shown.gross), unit: weight.unit });
  };

  const save = async () => {
    if (savingRef.current || !deviceId) return;
    savingRef.current = true;
    setSaving(true);
    const input: CreateRecordInput = {
      requestId: newRequestId(),
      deviceId,
      productId: product?.id ?? null,
      lot: lot.trim() === '' ? null : lot.trim(),
      tare: stationTare && stationTare.unit === weight?.unit ? stationTare.value : null,
    };
    showNotice({ tone: 'info', key: 'weighing.Saving' });

    // Retried with the same request id until the server answers, so a lost answer never saves twice.
    for (let attempt = 0; mounted.current; attempt++) {
      try {
        const record = await createRecord(input);
        if (!mounted.current) break;
        setRecent((list) => [record, ...(list ?? []).filter((r) => r.id !== record.id)].slice(0, recentTake));
        setStationTare(null);
        showNotice({ tone: 'positive', key: 'weighing.Saved', prefix: record.number }, savedNoticeMs);
        break;
      } catch (error) {
        if (!mounted.current) break;
        if (isRetryable(error)) {
          showNotice({ tone: 'warning', key: 'weighing.Retrying' });
          const wait = saveRetryDelaysMs[Math.min(attempt, saveRetryDelaysMs.length - 1)] ?? 1000;
          await delay(wait);
          continue;
        }
        console.error('Saving the weighing failed.', error);
        showNotice({ tone: 'warning', key: saveErrorKey(error) });
        break;
      }
    }

    savingRef.current = false;
    if (mounted.current) setSaving(false);
  };

  const zero = async () => {
    if (!deviceId || zeroing) return;
    setZeroing(true);
    try {
      const response = await zeroDevice(deviceId);
      if (!mounted.current) return;
      const key = zeroKeys[response.result] ?? 'weighing.ZeroUnavailable';
      showNotice({ tone: response.result === 'Done' ? 'positive' : 'warning', key }, savedNoticeMs);
    } catch (error) {
      if (!mounted.current) return;
      console.error('Zeroing the scale failed.', error);
      showNotice({ tone: 'warning', key: 'weighing.ZeroUnavailable' });
    } finally {
      if (mounted.current) setZeroing(false);
    }
  };

  // Text helpers
  const unit = weight?.unit ?? null;
  const dash = '–';
  const withUnit = (value: Decimal | undefined): string =>
    value === undefined ? dash : unit ? `${formatWeight(decimalToString(value), culture)} ${unit}` : formatWeight(decimalToString(value), culture);

  // Package tare in kg, shown as it is when the scale's unit is unknown and it cannot be converted.
  const packageTareKg = product?.packageTare ? `${formatDecimal(product.packageTare, culture)} kg` : null;
  const price = parseDecimal(product?.unitPrice);
  const amount =
    settings && price && shown.net && unit && isKnownUnit(unit) ? amountOf(shown.net, unit, price, settings.amountDecimals) : undefined;

  const eyebrowParts = [t('weighing.Title')];
  if (product) eyebrowParts.push(product.code);
  else if (productsAvailable) eyebrowParts.push(t('weighing.NoProduct'));
  if (lot.trim() !== '') eyebrowParts.push(lot.trim());

  if (settingsStatus !== 'ready' || !settings) {
    return (
      <div className="page weighing">
        <PageHeader title={t('weighing.Title')} />
        {settingsStatus === 'error' ? (
          <div className="status" role="alert">
            <p className="status__text">{t('shell.Error')}</p>
            <button type="button" className="button button--primary" onClick={() => setSettingsTick((tick) => tick + 1)}>
              {t('shell.Retry')}
            </button>
          </div>
        ) : (
          <p className="status__text" role="status">
            {t('shell.Loading')}
          </p>
        )}
      </div>
    );
  }

  let deviceLine: ReactNode = null;
  if (noDevice) {
    deviceLine = <span className="weighing__device state--warning">{t('shell.NoDevice')}</span>;
  } else if (current && deviceId) {
    const state: DeviceConnectionState = streamLost ? 'Disconnected' : current.state;
    deviceLine = (
      <span className={`weighing__device ${state === 'Connected' ? 'state--positive' : 'state--warning'}`} role="status">
        {`${t(connectionKeys[state])}, ${deviceId}`}
        {state === 'Error' && current.lastError ? ` (${current.lastError})` : null}
      </span>
    );
  }

  const actions = (
    <div className="weighing__header-side">
      {devices && devices.length > 1 ? (
        <label className="weighing__device-select">
          <span className="field__label">{t('weighing.SelectDevice')}</span>
          <select className="input input--mono" value={deviceId ?? ''} onChange={(event) => chooseDevice(event.target.value)}>
            {devices.map((d) => (
              <option key={d.deviceId} value={d.deviceId}>
                {d.agentName ? `${d.deviceId} / ${d.agentName}` : d.deviceId}
              </option>
            ))}
          </select>
        </label>
      ) : null}
      {deviceLine}
    </div>
  );

  let liveBody: ReactNode;
  if (devicesStatus === 'error' && !devices) {
    liveBody = (
      <div className="status status--inline" role="alert">
        <p className="status__text">{t('shell.Error')}</p>
        <button type="button" className="button button--primary" onClick={() => setDevicesTick((tick) => tick + 1)}>
          {t('shell.Retry')}
        </button>
      </div>
    );
  } else if (devices === null) {
    liveBody = (
      <p className="status__text" role="status">
        {t('shell.Loading')}
      </p>
    );
  } else if (noDevice) {
    liveBody = (
      <p className="weighing__empty" role="status">
        {t('shell.NoDevice')}
      </p>
    );
  } else {
    const stateWord = weight ? (
      <span className={`weighing__state ${weight.state === 'Stable' ? 'state--positive' : 'state--warning'}`}>
        {t(weightStateKeys[weight.state])}
      </span>
    ) : (
      <span className="weighing__state state--warning">{t('weighing.NoReading')}</span>
    );
    liveBody = (
      <>
        <div className="weighing__net-head">
          <span className="weighing__net-label">{t('weighing.Net')}</span>
          {stateWord}
        </div>
        <div className={`weighing__weight${connected ? '' : ' is-stale'}`} aria-live="off">
          <output className="weighing__value" data-testid="live-net">
            {shown.net ? formatWeight(decimalToString(shown.net), culture) : dash}
          </output>
          {unit ? <span className="weighing__unit">{unit}</span> : null}
        </div>
        <div className="weighing__figures">
          <div className="weighing__figure">
            <span>{t('weighing.Tare')}</span>
            <span className="weighing__figure-value" data-testid="tare">
              {shown.tare === undefined && packageTareKg ? packageTareKg : withUnit(shown.tare)}
            </span>
          </div>
          <div className="weighing__figure">
            <span>{t('weighing.Gross')}</span>
            <span className="weighing__figure-value" data-testid="gross">
              {withUnit(shown.gross)}
            </span>
          </div>
          {product?.unitPrice ? (
            <div className="weighing__figure">
              <span>{`${t('weighing.Amount')}, ${t('weighing.PricePerKg').replace(
                '{0}',
                formatMoney(product.unitPrice, settings.currency, culture),
              )}`}</span>
              <span className="weighing__figure-value" data-testid="amount">
                {amount ? formatMoney(decimalToString(amount), settings.currency, culture, settings.amountDecimals) : dash}
              </span>
            </div>
          ) : null}
        </div>
      </>
    );
  }

  const hasDevice = deviceId !== null;
  return (
    <div className="page weighing">
      <PageHeader title={product?.name ?? t('weighing.Title')} eyebrow={eyebrowParts.join(' / ')} actions={actions} />

      <div className="weighing__body">
        <section className="section weighing__live" aria-label={t('weighing.LiveWeight')}>
          {liveBody}

          <div className="weighing__buttons">
            <button
              type="button"
              className="button button--primary button--operator weighing__save"
              disabled={!hasDevice || saving}
              onClick={() => void save()}
            >
              {t('weighing.SaveRecord')}
            </button>
            {settings.canPrintLabel ? (
              <button type="button" className="button button--operator" disabled>
                {t('weighing.SaveAndPrint')}
              </button>
            ) : null}
            {stationTare ? (
              <button type="button" className="button button--quiet button--operator" onClick={() => setStationTare(null)}>
                {t('weighing.ClearTare')}
              </button>
            ) : (
              <button
                type="button"
                className="button button--quiet button--operator"
                disabled={!hasDevice || !stable || shown.deviceTare}
                onClick={takeTare}
              >
                {t('weighing.TakeTare')}
              </button>
            )}
            <button
              type="button"
              className="button button--quiet button--operator"
              disabled={!hasDevice || zeroing || !zeroSupported}
              onClick={() => void zero()}
            >
              {t('weighing.Zero')}
            </button>
          </div>

          <p className={`weighing__notice${notice ? ` weighing__notice--${notice.tone}` : ''}`} role="status" aria-live="polite">
            {notice ? (notice.prefix ? `${notice.prefix} ${t(notice.key)}` : t(notice.key)) : null}
          </p>
        </section>

        <aside className="weighing__side">
          <section className="section weighing__lot">
            <label className="field">
              <span className="field__label">{t('weighing.Lot')}</span>
              <input
                className="input input--mono weighing__input"
                type="text"
                value={lot}
                maxLength={64}
                onChange={(event) => changeLot(event.target.value)}
              />
            </label>
          </section>

          {productsAvailable ? (
            <section className="section weighing__products" aria-label={t('weighing.ProductSelection')}>
              <label className="field">
                <span className="field__label">{t('weighing.ProductSearch')}</span>
                <input
                  className="input input--mono weighing__input"
                  type="search"
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                />
              </label>
              {productsStatus === 'error' ? (
                <div className="status status--inline" role="alert">
                  <p className="status__text">{t('shell.Error')}</p>
                  <button type="button" className="button" onClick={() => setProductsTick((tick) => tick + 1)}>
                    {t('shell.Retry')}
                  </button>
                </div>
              ) : (
                <div className="weighing__product-list" aria-busy={productsStatus === 'loading'}>
                  <button
                    type="button"
                    className={`weighing__product${product === null ? ' is-selected' : ''}`}
                    aria-pressed={product === null}
                    onClick={() => setProduct(null)}
                  >
                    <span className="weighing__product-code">{dash}</span>
                    <span>{t('weighing.NoProduct')}</span>
                    <span />
                  </button>
                  {(products ?? []).map((p) => {
                    const selected = product?.id === p.id;
                    return (
                      <button
                        key={p.id}
                        type="button"
                        className={`weighing__product${selected ? ' is-selected' : ''}`}
                        aria-pressed={selected}
                        onClick={() => setProduct(p)}
                      >
                        <span className="weighing__product-code">{p.code}</span>
                        <span className="weighing__product-name">{p.name}</span>
                        <span className="weighing__product-price">{p.unitPrice ? formatDecimal(p.unitPrice, culture) : ''}</span>
                      </button>
                    );
                  })}
                </div>
              )}
            </section>
          ) : null}

          <section className="section weighing__recent" aria-label={t('weighing.RecentRecords')}>
            <div className="weighing__recent-head">
              <span className="section__label">{t('weighing.RecentRecords')}</span>
              <Link to={routes.records} className="weighing__all">
                {t('weighing.AllRecords')}
              </Link>
            </div>
            {recent === null ? null : recent.length === 0 ? (
              <p className="status__text weighing__recent-empty">{t('weighing.NoRecent')}</p>
            ) : (
              <ul className="weighing__recent-list">
                {recent.map((r) => (
                  <li key={r.id} className={`weighing__recent-row${r.status === 'Cancelled' ? ' is-cancelled' : ''}`}>
                    <span className="weighing__recent-time">{formatTime(r.createdAt, culture)}</span>
                    <span className="weighing__recent-name">
                      {r.productName ?? t('weighing.NoProduct')}
                      {r.status === 'Cancelled' ? <span className="state--warning weighing__recent-flag">{t('weighing.Cancelled')}</span> : null}
                    </span>
                    <span className="weighing__recent-net">
                      {r.unit ? `${formatWeight(r.net, culture)} ${r.unit}` : formatWeight(r.net, culture)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </aside>
      </div>
    </div>
  );
}
