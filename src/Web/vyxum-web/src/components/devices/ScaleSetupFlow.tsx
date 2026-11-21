import { useCallback, useEffect, useRef, useState } from 'react';
import { useCulture, useT } from '../../i18n/I18nProvider';
import { NetworkError, RuleError, ValidationError } from '../../lib/api';
import {
  createDeviceConfig,
  deleteDeviceConfig,
  discoveryCodes,
  getDiscovery,
  listDeviceConfigs,
  nextScaleId,
  startDiscovery,
  toInput,
  updateDeviceConfig,
  type DeviceConfig,
  type Discovery,
  type DiscoveryCandidate,
  type DiscoveryRequest,
  type ProtocolSettings,
  type TransportSettings,
} from '../../lib/devices';
import { formatWeight } from '../../lib/format';
import { ConnectionFields, emptyConnection, settingsOf, type ConnectionDraft } from './ConnectionFields';
import { DeviceDetailsFields, detailsDraftOf, detailsOf, fieldErrorsOf, type DetailsDraft, type FieldErrors } from './DeviceDetailsFields';
import { fill, portStateKeys, portStateTones, readingStateKeys, toneClass, useConnectionText } from './deviceText';
import { LiveWeightPanel } from './LiveWeightPanel';

/** Poll interval of a running discovery; tests shorten it. */
export const discoveryTiming = { pollMs: 500 };

type Phase =
  | { kind: 'idle' }
  | { kind: 'searching' }
  | { kind: 'results' }
  | { kind: 'failed'; code: string }
  | { kind: 'confirm'; device: DeviceConfig };

export interface ScaleSetupFlowProps {
  /** Ids of the devices that already exist; the new scale gets the first free `scale-N`. */
  existingIds: readonly string[];
  /** Called after the user confirmed the live weight; the device is saved with its details. */
  onConfirmed: (device: DeviceConfig) => void;
  /** Reports the id of a scale that is saved but not confirmed yet (null when there is none). */
  onPendingChange?: (deviceId: string | null) => void;
}

function errorKeyOf(error: unknown): string {
  if (error instanceof RuleError) return error.code;
  if (!(error instanceof NetworkError)) console.error('A device request failed.', error);
  return 'shell.Error';
}

/**
 * Adds a scale: looks for scales on the agent's ports, lets the user pick one (or set the connection by hand),
 * saves it and shows its live weight until the user confirms it (decisions V-39, V-40).
 */
export function ScaleSetupFlow({ existingIds, onConfirmed, onPendingChange }: ScaleSetupFlowProps) {
  const t = useT();
  const culture = useCulture();
  const describe = useConnectionText();

  const [phase, setPhase] = useState<Phase>({ kind: 'searching' });
  const [discovery, setDiscovery] = useState<Discovery | null>(null);
  const [discoveryId, setDiscoveryId] = useState<string | null>(null);
  const [manualOpen, setManualOpen] = useState(false);
  const [connection, setConnection] = useState<ConnectionDraft>(emptyConnection);
  const [connectionErrors, setConnectionErrors] = useState<FieldErrors>({});
  const [details, setDetails] = useState<DetailsDraft>({ capacity: '', division: '', unit: '' });
  const [detailErrors, setDetailErrors] = useState<FieldErrors>({});
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const pendingChange = useRef(onPendingChange);
  pendingChange.current = onPendingChange;
  const pendingId = phase.kind === 'confirm' ? phase.device.id : null;
  useEffect(() => pendingChange.current?.(pendingId), [pendingId]);

  const search = useCallback(async (request: DiscoveryRequest = {}) => {
    setErrorKey(null);
    setPhase({ kind: 'searching' });
    setDiscovery(null);
    try {
      const { id } = await startDiscovery(request);
      if (mounted.current) setDiscoveryId(id);
    } catch (error) {
      if (!mounted.current) return;
      setPhase({ kind: 'failed', code: error instanceof NetworkError ? 'shell.Error' : errorKeyOf(error) });
    }
  }, []);

  // Start looking as soon as the flow is shown.
  const started = useRef(false);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void search();
  }, [search]);

  // Follow the running discovery.
  useEffect(() => {
    if (!discoveryId) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const poll = async () => {
      try {
        const result = await getDiscovery(discoveryId, { signal: controller.signal });
        if (controller.signal.aborted) return;
        setDiscovery(result);
        if (result.status === 'Running') {
          timer = setTimeout(() => void poll(), discoveryTiming.pollMs);
          return;
        }
        setDiscoveryId(null);
        setPhase(result.status === 'Completed' ? { kind: 'results' } : { kind: 'failed', code: result.code ?? discoveryCodes.failed });
      } catch (error) {
        if (controller.signal.aborted) return;
        setDiscoveryId(null);
        setPhase({ kind: 'failed', code: errorKeyOf(error) });
      }
    };
    void poll();
    return () => {
      controller.abort();
      if (timer !== undefined) clearTimeout(timer);
    };
  }, [discoveryId]);

  /** Saves a new scale with the first free id and opens the live check. */
  const create = async (transport: TransportSettings, protocol: ProtocolSettings, unit: string | null, fromManual: boolean) => {
    if (busy) return;
    setBusy(true);
    setErrorKey(null);
    setConnectionErrors({});
    let ids = [...existingIds];
    try {
      for (let attempt = 0; ; attempt++) {
        const { id, number } = nextScaleId(ids);
        try {
          const device = await createDeviceConfig({
            id,
            name: fill(t('devices.DefaultName'), number),
            agentName: discovery?.agentName ?? null,
            transport,
            protocol,
            capacity: null,
            division: null,
            unit,
            isEnabled: true,
          });
          if (!mounted.current) return;
          setDetails(detailsDraftOf(device, culture));
          setDetailErrors({});
          setPhase({ kind: 'confirm', device });
          return;
        } catch (error) {
          // Someone else took the id in the meantime: read the ids again and take the next one.
          if (attempt < 2 && error instanceof ValidationError && error.forField('id').includes('devices.IdTaken')) {
            ids = (await listDeviceConfigs()).map((d) => d.id);
            continue;
          }
          throw error;
        }
      }
    } catch (error) {
      if (!mounted.current) return;
      if (error instanceof ValidationError && fromManual) {
        setConnectionErrors(fieldErrorsOf(error));
      } else {
        setErrorKey(error instanceof ValidationError ? (Object.values(error.errors).flat()[0] ?? 'shell.Error') : errorKeyOf(error));
      }
    } finally {
      if (mounted.current) setBusy(false);
    }
  };

  const choose = (candidate: DiscoveryCandidate) =>
    void create(candidate.transport, candidate.protocol, candidate.reading?.unit ?? null, false);

  const saveManual = () => {
    const settings = settingsOf(connection);
    void create(settings.transport, settings.protocol, null, true);
  };

  const tryTcp = () => {
    const port = Number(connection.tcpPort.trim());
    const errors: Record<string, string[]> = {};
    if (connection.host.trim() === '') errors['transport.host'] = ['devices.HostRequired'];
    if (!Number.isInteger(port) || port < 1 || port > 65535) errors['transport.port'] = ['devices.PortInvalid'];
    setConnectionErrors(errors);
    if (Object.keys(errors).length > 0) return;
    void search({ tcp: { host: connection.host.trim(), port } });
  };

  /** "Another scale": removes the scale that was just saved and goes back to the results. */
  const another = async (device: DeviceConfig) => {
    if (busy) return;
    setBusy(true);
    setErrorKey(null);
    try {
      await deleteDeviceConfig(device.id);
      if (!mounted.current) return;
      setPhase(discovery ? { kind: 'results' } : { kind: 'idle' });
      if (!discovery) setManualOpen(true);
    } catch (error) {
      if (mounted.current) setErrorKey(errorKeyOf(error));
    } finally {
      if (mounted.current) setBusy(false);
    }
  };

  const confirm = async (device: DeviceConfig) => {
    if (busy) return;
    const values = detailsOf(details, culture);
    if (values.capacity === device.capacity && values.division === device.division && values.unit === device.unit) {
      setPhase({ kind: 'idle' });
      onConfirmed(device);
      return;
    }
    setBusy(true);
    setErrorKey(null);
    setDetailErrors({});
    try {
      const saved = await updateDeviceConfig(device.id, toInput(device, values));
      if (!mounted.current) return;
      setPhase({ kind: 'idle' });
      onConfirmed(saved);
    } catch (error) {
      if (!mounted.current) return;
      if (error instanceof ValidationError) setDetailErrors(fieldErrorsOf(error));
      else setErrorKey(errorKeyOf(error));
    } finally {
      if (mounted.current) setBusy(false);
    }
  };

  const errorLine = errorKey ? (
    <p className="field__error" role="alert">
      {t(errorKey)}
    </p>
  ) : null;

  if (phase.kind === 'confirm') {
    const device = phase.device;
    return (
      <div className="scale-flow">
        <section className="section scale-flow__section" aria-label={t('devices.ConfirmTitle')}>
          <div className="scale-flow__head">
            <h2 className="scale-flow__title">{t('devices.ConfirmTitle')}</h2>
            <p className="scale-flow__hint">{t('devices.ConfirmHint')}</p>
          </div>
          <p className="scale-flow__device">
            <span className="scale-flow__device-name">{device.name}</span>
            <span className="mono muted">{describe(device.transport, device.protocol)}</span>
          </p>
          <LiveWeightPanel deviceId={device.id} />
        </section>
        <section className="section scale-flow__section" aria-label={t('devices.OptionalDetails')}>
          <div className="scale-flow__head">
            <h2 className="scale-flow__title">{t('devices.OptionalDetails')}</h2>
            <p className="scale-flow__hint">{t('devices.OptionalHint')}</p>
          </div>
          <DeviceDetailsFields draft={details} onChange={setDetails} errors={detailErrors} />
        </section>
        {errorLine}
        <div className="scale-flow__buttons">
          <button type="button" className="button button--primary scale-flow__button" disabled={busy} onClick={() => void confirm(device)}>
            {t('devices.ConfirmScale')}
          </button>
          <button type="button" className="button scale-flow__button" disabled={busy} onClick={() => void another(device)}>
            {t('devices.OtherScale')}
          </button>
        </div>
      </div>
    );
  }

  const ports = discovery?.ports ?? [];
  const candidates = discovery?.candidates ?? [];
  const searching = phase.kind === 'searching';
  const finished = ports.filter((p) => p.state !== 'Pending' && p.state !== 'Scanning').length;

  return (
    <div className="scale-flow">
      {searching ? (
        <section className="section scale-flow__section" aria-label={t('devices.Searching')} aria-busy="true">
          <div className="scale-flow__head" role="status">
            <h2 className="scale-flow__title">{t('devices.Searching')}</h2>
            <p className="scale-flow__hint">{t('devices.SearchingHint')}</p>
          </div>
          {ports.length > 0 ? (
            <div className="progress" role="progressbar" aria-valuemin={0} aria-valuemax={ports.length} aria-valuenow={finished} aria-label={t('devices.Ports')}>
              <div className="progress__bar" style={{ width: `${Math.round((finished / ports.length) * 100)}%` }} />
            </div>
          ) : null}
          <PortList ports={ports} />
        </section>
      ) : null}

      {phase.kind === 'failed' ? (
        <section className="section scale-flow__section" aria-label={t('devices.NoneFound')}>
          <p className="scale-flow__problem" role="alert">
            {t(phase.code)}
          </p>
          <div className="scale-flow__buttons">
            <button type="button" className="button scale-flow__button" onClick={() => void search()}>
              {t('devices.Retry')}
            </button>
            {manualOpen ? null : (
              <button type="button" className="button button--quiet scale-flow__button" onClick={() => setManualOpen(true)}>
                {t('devices.Manual')}
              </button>
            )}
          </div>
        </section>
      ) : null}

      {phase.kind === 'idle' ? (
        <div className="scale-flow__buttons">
          <button type="button" className="button scale-flow__button" disabled={busy} onClick={() => void search()}>
            {t('devices.SearchAgain')}
          </button>
        </div>
      ) : null}

      {phase.kind === 'results' ? (
        <>
          {candidates.length > 0 ? (
            <section className="section scale-flow__section" aria-label={t('devices.Found')}>
              <div className="scale-flow__head">
                <h2 className="scale-flow__title">{t('devices.Found')}</h2>
                <p className="scale-flow__hint">{t('devices.FoundHint')}</p>
              </div>
              <ul className="candidates">
                {candidates.map((candidate) => (
                  <li key={candidate.key}>
                    <button type="button" className="candidate" disabled={busy} onClick={() => choose(candidate)}>
                      <span className="candidate__text">
                        <span className="candidate__description mono">{describe(candidate.transport, candidate.protocol)}</span>
                        {candidate.serialNumber ? (
                          <span className="candidate__serial muted">{fill(t('devices.SerialNumber'), candidate.serialNumber)}</span>
                        ) : null}
                        {candidate.reading ? (
                          <span className={`candidate__state ${candidate.reading.state === 'Stable' ? 'state--positive' : 'state--warning'}`}>
                            {t(readingStateKeys[candidate.reading.state])}
                          </span>
                        ) : null}
                      </span>
                      {candidate.reading?.value != null ? (
                        <span className="candidate__weight">
                          <span className="candidate__value">{formatWeight(candidate.reading.value, culture)}</span>
                          {candidate.reading.unit ? <span className="candidate__unit">{candidate.reading.unit}</span> : null}
                        </span>
                      ) : null}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ) : (
            <section className="section scale-flow__section" aria-label={t('devices.NoneFound')}>
              <div className="scale-flow__head">
                <h2 className="scale-flow__title state--warning">{t('devices.NoneFound')}</h2>
                <p className="scale-flow__hint">{ports.length > 0 ? t('devices.NoneFoundHint') : t('devices.NoPorts')}</p>
              </div>
            </section>
          )}
          {ports.length > 0 ? (
            <section className="section scale-flow__section" aria-label={t('devices.Ports')}>
              <h2 className="scale-flow__subtitle">{t('devices.Ports')}</h2>
              <PortList ports={ports} />
            </section>
          ) : null}
          {errorLine}
          <div className="scale-flow__buttons">
            <button type="button" className="button scale-flow__button" disabled={busy} onClick={() => void search()}>
              {t('devices.SearchAgain')}
            </button>
            {manualOpen ? null : (
              <button type="button" className="button button--quiet scale-flow__button" onClick={() => setManualOpen(true)}>
                {t('devices.Manual')}
              </button>
            )}
          </div>
        </>
      ) : null}

      {manualOpen && !searching ? (
        <section className="section scale-flow__section" aria-label={t('devices.ManualTitle')}>
          <div className="scale-flow__head">
            <h2 className="scale-flow__title">{t('devices.ManualTitle')}</h2>
            <p className="scale-flow__hint">{t('devices.ManualHint')}</p>
          </div>
          <ConnectionFields
            draft={connection}
            onChange={(next) => {
              setConnection(next);
              setConnectionErrors({});
            }}
            knownPorts={ports.map((p) => p.portName)}
            errors={connectionErrors}
          />
          {phase.kind !== 'results' ? errorLine : null}
          <div className="scale-flow__buttons">
            <button type="button" className="button button--primary scale-flow__button" disabled={busy} onClick={saveManual}>
              {t('devices.SaveAndTest')}
            </button>
            {connection.transportType === 'tcp' ? (
              <button type="button" className="button scale-flow__button" disabled={busy} onClick={tryTcp}>
                {t('devices.TryTcp')}
              </button>
            ) : null}
          </div>
        </section>
      ) : null}
    </div>
  );
}

/** Ports with their scan state as a word and a color. */
function PortList({ ports }: { ports: Discovery['ports'] }) {
  const t = useT();
  if (ports.length === 0) return null;
  return (
    <ul className="port-list">
      {ports.map((port) => (
        <li key={port.portName} className="port-list__row">
          <span className="port-list__name mono">{port.portName}</span>
          <span className={`port-list__state ${toneClass(portStateTones[port.state])}`}>{t(portStateKeys[port.state])}</span>
          {port.detail ? <span className="port-list__detail muted">{port.detail}</span> : null}
        </li>
      ))}
    </ul>
  );
}
