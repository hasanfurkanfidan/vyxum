import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { useCulture, useT } from '../i18n/I18nProvider';
import { RuleError, ValidationError } from '../lib/api';
import { formatDate, formatMoney, formatRecordNumber, formatTime, formatWeight } from '../lib/format';
import {
  cancelRecord,
  dayRangeUtc,
  exportFallbackName,
  exportRecords,
  getWeighingSettings,
  listRecords,
  listWeighingProducts,
  localDateValue,
  maxRecordPageSize,
  recordPageSize,
  type RecordFilter,
  type RecordTotals,
  type WeighingProduct,
  type WeighingRecord,
  type WeighingSettings,
} from '../lib/records';
import { PageHeader } from '../shell/PageHeader';
import '../styles/records.css';

const lotDelayMs = 300;
const dash = '–';

type ListStatus = 'loading' | 'ready' | 'error';

function useDebounced<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/** Local date of a `YYYY-MM-DD` value. */
function dateOfValue(day: string): Date {
  const [year = 0, month = 1, date = 1] = day.split('-').map(Number);
  return new Date(year, month - 1, date);
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

interface CancelDialogProps {
  record: WeighingRecord;
  requireReason: boolean;
  columns: number;
  onDone: () => void;
  /** The server rejected the cancel because the record changed; the list should show its current state. */
  onConflict: () => void;
  onClose: () => void;
}

/** Inline dialog under a record row: asks for the reason and cancels the record. */
function CancelDialog({ record, requireReason, columns, onDone, onConflict, onClose }: CancelDialogProps) {
  const t = useT();
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const inputId = `cancel-reason-${record.id}`;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    if (requireReason && reason.trim().length === 0) {
      setErrors(['weighing.CancelReasonRequired']);
      return;
    }
    setBusy(true);
    setErrors([]);
    try {
      await cancelRecord(record.id, reason.trim());
      onDone();
    } catch (error) {
      setBusy(false);
      if (error instanceof ValidationError) {
        const keys = Object.values(error.errors).flat();
        setErrors(keys.length > 0 ? keys : ['shell.Error']);
      } else if (error instanceof RuleError) {
        setErrors([error.code]);
        if (error.status === 409) onConflict();
      } else {
        console.error('Cancelling the record failed.', error);
        setErrors(['shell.Error']);
      }
    }
  };

  return (
    <tr className="records__dialog-row">
      <td colSpan={columns}>
        <form className="records__dialog" role="dialog" aria-label={t('weighing.CancelRecord')} onSubmit={(event) => void submit(event)} noValidate>
          <p>{t('weighing.CancelConfirm').replace('{0}', formatRecordNumber(record.number))}</p>
          <div className="field">
            <label className="field__label" htmlFor={inputId}>
              {t('weighing.CancelReason')}
            </label>
            <input
              id={inputId}
              className="input"
              type="text"
              value={reason}
              required={requireReason}
              aria-invalid={errors.length > 0 ? 'true' : undefined}
              aria-describedby={errors.length > 0 ? `${inputId}-error` : undefined}
              onChange={(event) => setReason(event.target.value)}
              autoFocus
            />
            {errors.length > 0 ? (
              <div id={`${inputId}-error`} className="field__error" role="alert">
                {errors.map((key) => (
                  <p key={key}>{t(key)}</p>
                ))}
              </div>
            ) : null}
          </div>
          <div className="records__dialog-actions">
            <button type="submit" className="button button--warning" disabled={busy}>
              {t('weighing.CancelRecord')}
            </button>
            <button type="button" className="button button--quiet" onClick={onClose} disabled={busy}>
              {t('weighing.Back')}
            </button>
          </div>
        </form>
      </td>
    </tr>
  );
}

/** Weighing records: filters, the table with cancelled rows, totals, cancelling and the Excel export. */
export function RecordsPage() {
  const t = useT();
  const culture = useCulture();

  const [day, setDay] = useState(() => localDateValue());
  const [productId, setProductId] = useState('');
  const [lotInput, setLotInput] = useState('');
  const lot = useDebounced(lotInput, lotDelayMs);

  const [settings, setSettings] = useState<WeighingSettings | null>(null);
  const [products, setProducts] = useState<WeighingProduct[]>([]);

  const [items, setItems] = useState<WeighingRecord[] | null>(null);
  const [totals, setTotals] = useState<RecordTotals | null>(null);
  const [status, setStatus] = useState<ListStatus>('loading');
  const [loadingMore, setLoadingMore] = useState(false);
  const [reloadTick, setReloadTick] = useState(0);
  const [cancelling, setCancelling] = useState<number | null>(null);
  const [exporting, setExporting] = useState(false);
  const [exportFailed, setExportFailed] = useState(false);

  const keepCount = useRef(recordPageSize);
  const lastKey = useRef('');
  const moreController = useRef<AbortController | null>(null);

  // Settings and the product filter load once; the page works with defaults when they fail.
  useEffect(() => {
    const controller = new AbortController();
    getWeighingSettings({ signal: controller.signal })
      .then((value) => !controller.signal.aborted && setSettings(value))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) console.error('Loading weighing settings failed.', error);
      });
    listWeighingProducts({ signal: controller.signal })
      .then((value) => !controller.signal.aborted && setProducts(value))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) console.error('Loading weighing products failed.', error);
      });
    return () => controller.abort();
  }, []);

  const filter: RecordFilter = { ...dayRangeUtc(day), productId, lot };
  const filterKey = `${filter.from}|${filter.to}|${filter.productId}|${filter.lot.trim()}`;

  useEffect(() => {
    const controller = new AbortController();
    moreController.current?.abort();
    if (lastKey.current !== filterKey) {
      lastKey.current = filterKey;
      keepCount.current = recordPageSize;
      setCancelling(null);
    }
    setStatus('loading');
    listRecords(filter, 0, Math.min(Math.max(keepCount.current, recordPageSize), maxRecordPageSize), { signal: controller.signal })
      .then((result) => {
        if (controller.signal.aborted) return;
        setItems(result.items);
        setTotals(result.totals);
        keepCount.current = result.items.length;
        setStatus('ready');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        console.error('Loading records failed.', error);
        setStatus('error');
      });
    return () => controller.abort();
    // `filterKey` carries the date range, product and lot.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterKey, reloadTick]);

  const refresh = useCallback(() => setReloadTick((tick) => tick + 1), []);

  const total = totals === null ? 0 : totals.validCount + totals.cancelledCount;
  const hasMore = items !== null && items.length < total;

  const loadMore = async () => {
    if (items === null || loadingMore) return;
    const controller = new AbortController();
    moreController.current = controller;
    setLoadingMore(true);
    try {
      const result = await listRecords(filter, items.length, recordPageSize, { signal: controller.signal });
      if (controller.signal.aborted) return;
      const next = [...items, ...result.items];
      keepCount.current = next.length;
      setItems(next);
      setTotals(result.totals);
    } catch (error) {
      if (controller.signal.aborted) return;
      console.error('Loading more records failed.', error);
      setStatus('error');
    } finally {
      if (!controller.signal.aborted) setLoadingMore(false);
    }
  };

  const exportExcel = async () => {
    setExporting(true);
    setExportFailed(false);
    try {
      const file = await exportRecords(filter);
      downloadBlob(file.blob, file.fileName ?? exportFallbackName(day));
    } catch (error) {
      console.error('Exporting records failed.', error);
      setExportFailed(true);
    } finally {
      setExporting(false);
    }
  };

  const showOperator = settings?.showOperator === true;
  const columnCount = 9 + (showOperator ? 1 : 0);
  const currency = settings?.currency ?? items?.[0]?.currency ?? '';
  const date = dateOfValue(day);
  const weekday = new Intl.DateTimeFormat(culture, { weekday: 'long' }).format(date);
  const eyebrow = `${t('weighing.RecordsBreadcrumb')} / ${formatDate(date, culture)} ${weekday}`;

  const money = (amount: string | null, code: string) => (amount === null || code === '' ? dash : formatMoney(amount, code, culture));

  return (
    <div className="page">
      <PageHeader
        title={t('weighing.RecordsTitle')}
        eyebrow={eyebrow}
        actions={
          <button type="button" className="button button--primary" disabled={exporting} onClick={() => void exportExcel()}>
            {t('weighing.ExportExcel')}
          </button>
        }
      />

      {exportFailed ? (
        <p className="field__error" role="alert">
          {t('shell.Error')}
        </p>
      ) : null}

      <div className="records__filters">
        <div className="field">
          <label className="field__label" htmlFor="records-date">
            {t('weighing.Date')}
          </label>
          <input
            id="records-date"
            className="input input--mono"
            type="date"
            value={day}
            onChange={(event) => event.target.value !== '' && setDay(event.target.value)}
          />
        </div>
        <div className="field">
          <label className="field__label" htmlFor="records-product">
            {t('weighing.Product')}
          </label>
          <select id="records-product" className="input" value={productId} onChange={(event) => setProductId(event.target.value)}>
            <option value="">{t('weighing.AllProducts')}</option>
            {products.map((product) => (
              <option key={product.id} value={product.id}>
                {`${product.code} ${product.name}`}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label className="field__label" htmlFor="records-lot">
            {t('weighing.Lot')}
          </label>
          <input
            id="records-lot"
            className="input input--mono records__lot"
            type="text"
            value={lotInput}
            onChange={(event) => setLotInput(event.target.value)}
          />
        </div>
      </div>

      {status === 'error' ? (
        <div className="status" role="alert">
          <p className="status__text">{t('shell.Error')}</p>
          <button type="button" className="button button--primary" onClick={refresh}>
            {t('shell.Retry')}
          </button>
        </div>
      ) : items === null || totals === null ? (
        <p className="status__text" role="status">
          {t('shell.Loading')}
        </p>
      ) : (
        <div className="records__scroll">
          <table className="table records__table" aria-busy={status === 'loading'}>
            <thead>
              <tr>
                <th scope="col">{t('weighing.Time')}</th>
                <th scope="col">{t('weighing.RecordNo')}</th>
                <th scope="col">{t('weighing.Plu')}</th>
                <th scope="col">{t('weighing.Product')}</th>
                <th scope="col">{t('weighing.Lot')}</th>
                <th scope="col" className="numeric">
                  {t('weighing.Net')}
                </th>
                <th scope="col" className="numeric">
                  {t('weighing.Amount')}
                </th>
                {showOperator ? <th scope="col">{t('weighing.Operator')}</th> : null}
                <th scope="col" className="numeric">
                  {t('weighing.Status')}
                </th>
                <th scope="col">
                  <span className="records__sr-only">{t('weighing.CancelRecord')}</span>
                </th>
              </tr>
            </thead>
            {items.length === 0 ? (
              <tbody>
                <tr>
                  <td colSpan={columnCount} className="muted">
                    {t('weighing.Empty')}
                  </td>
                </tr>
              </tbody>
            ) : (
              <tbody>
                {items.map((record) => {
                  const cancelled = record.status === 'Cancelled';
                  return (
                    <RecordRows
                      key={record.id}
                      record={record}
                      cancelled={cancelled}
                      showOperator={showOperator}
                      columns={columnCount}
                      requireReason={settings?.requireCancelReason === true}
                      dialogOpen={cancelling === record.id}
                      money={money}
                      onOpenCancel={() => setCancelling(record.id)}
                      onCloseCancel={() => setCancelling(null)}
                      onConflict={refresh}
                      onCancelled={() => {
                        setCancelling(null);
                        refresh();
                      }}
                    />
                  );
                })}
              </tbody>
            )}
            <tfoot>
              <tr className="records__total">
                <td colSpan={5}>
                  {t('weighing.ValidSummary').replace('{0}', String(totals.validCount)).replace('{1}', String(totals.cancelledCount))}
                </td>
                <td className="numeric">
                  {totals.net.length === 0
                    ? dash
                    : totals.net.map((net) => (
                        <div key={net.unit ?? ''}>{net.unit ? `${formatWeight(net.value, culture)} ${net.unit}` : formatWeight(net.value, culture)}</div>
                      ))}
                </td>
                <td className="numeric">{money(totals.amount, currency)}</td>
                <td colSpan={columnCount - 7} className="records__note muted">
                  {t('weighing.CancelledNotInTotals')}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}

      {hasMore ? (
        <div className="records__more">
          <button type="button" className="button" disabled={loadingMore} onClick={() => void loadMore()}>
            {t('weighing.LoadMore')}
          </button>
        </div>
      ) : null}
    </div>
  );
}

interface RecordRowsProps {
  record: WeighingRecord;
  cancelled: boolean;
  showOperator: boolean;
  columns: number;
  requireReason: boolean;
  dialogOpen: boolean;
  money: (amount: string | null, currency: string) => string;
  onOpenCancel: () => void;
  onCloseCancel: () => void;
  onCancelled: () => void;
  onConflict: () => void;
}

function RecordRows({ record, cancelled, showOperator, columns, requireReason, dialogOpen, money, onOpenCancel, onCloseCancel, onCancelled, onConflict }: RecordRowsProps) {
  const t = useT();
  const culture = useCulture();
  const net = record.unit ? `${formatWeight(record.net, culture)} ${record.unit}` : formatWeight(record.net, culture);

  return (
    <>
      <tr className={cancelled ? 'records__row records__row--cancelled' : 'records__row'}>
        <td className="mono muted">{formatTime(record.createdAt, culture)}</td>
        <td className="mono">{formatRecordNumber(record.number)}</td>
        <td className="mono">{record.productCode ?? dash}</td>
        <td>{record.productName ?? dash}</td>
        <td className="mono">{record.lot ?? dash}</td>
        <td className="numeric">{net}</td>
        <td className="numeric">{money(record.amount, record.currency)}</td>
        {showOperator ? <td>{record.operator ?? dash}</td> : null}
        <td className={cancelled ? 'numeric state--warning records__status' : 'numeric muted records__status'}>
          {cancelled ? t('weighing.Cancelled') : t('weighing.Saved')}
        </td>
        <td className="records__action">
          {cancelled ? null : (
            <button type="button" className="button button--quiet records__cancel" onClick={onOpenCancel} aria-expanded={dialogOpen}>
              {t('weighing.CancelRecord')}
            </button>
          )}
        </td>
      </tr>
      {dialogOpen ? (
        <CancelDialog record={record} requireReason={requireReason} columns={columns} onDone={onCancelled} onConflict={onConflict} onClose={onCloseCancel} />
      ) : null}
    </>
  );
}
