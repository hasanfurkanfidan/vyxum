// Client for the Weighing module API (/api/weighing). Weights and money travel as invariant decimal strings.
import { apiFile, apiGet, apiPost, withQuery, type DownloadedFile, type RequestOptions } from './api';

export interface WeighingRecord {
  id: number;
  number: string;
  createdAt: string;
  deviceId: string;
  productId: string | null;
  productCode: string | null;
  productName: string | null;
  lot: string | null;
  gross: string;
  tare: string;
  net: string;
  unit: string | null;
  unitPrice: string | null;
  amount: string | null;
  currency: string;
  status: 'Saved' | 'Cancelled';
  cancelledAt: string | null;
  cancelReason: string | null;
  operator: string | null;
}

export interface NetTotal {
  unit: string | null;
  value: string;
}

export interface RecordTotals {
  validCount: number;
  cancelledCount: number;
  net: NetTotal[];
  amount: string | null;
}

export interface RecordList {
  items: WeighingRecord[];
  totals: RecordTotals;
}

export interface WeighingProduct {
  id: string;
  code: string;
  name: string;
}

export interface WeighingSettings {
  requireCancelReason: boolean;
  currency: string;
  showOperator: boolean;
}

export interface RecordFilter {
  /** Inclusive, UTC ISO. */
  from: string;
  /** Exclusive, UTC ISO. */
  to: string;
  productId: string;
  lot: string;
}

/** Page size of the record list; the API allows up to 500. */
export const recordPageSize = 100;
export const maxRecordPageSize = 500;

/** The local calendar day of a `YYYY-MM-DD` value as [local midnight, next local midnight) in UTC ISO. */
export function dayRangeUtc(day: string): { from: string; to: string } {
  const [year = 0, month = 1, date = 1] = day.split('-').map(Number);
  const start = new Date(year, month - 1, date);
  const next = new Date(year, month - 1, date + 1);
  return { from: start.toISOString(), to: next.toISOString() };
}

/** Today in the browser's time zone as `YYYY-MM-DD`, the value of a date input. */
export function localDateValue(date: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function filterQuery(filter: RecordFilter): Record<string, string | undefined> {
  return { from: filter.from, to: filter.to, productId: filter.productId, lot: filter.lot.trim() };
}

export function listRecords(filter: RecordFilter, skip: number, take: number, options?: RequestOptions): Promise<RecordList> {
  const path = withQuery('/api/weighing/records', { ...filterQuery(filter), includeCancelled: true, skip, take });
  return apiGet<RecordList>(path, options);
}

export function cancelRecord(id: number, reason: string): Promise<WeighingRecord> {
  return apiPost<WeighingRecord>(`/api/weighing/records/${id}/cancel`, { reason });
}

export function listWeighingProducts(options?: RequestOptions): Promise<WeighingProduct[]> {
  return apiGet<WeighingProduct[]>('/api/weighing/products', options);
}

export function getWeighingSettings(options?: RequestOptions): Promise<WeighingSettings> {
  return apiGet<WeighingSettings>('/api/weighing/settings', options);
}

/** The Excel export with the same filters as the list (cancelled records included, as in the list). */
export function exportRecords(filter: RecordFilter): Promise<DownloadedFile> {
  return apiFile(withQuery('/api/weighing/records/export', { ...filterQuery(filter), includeCancelled: true }));
}

/** Fallback name for the downloaded export, `weighing-records-YYYYMMDD.xlsx`. */
export function exportFallbackName(day: string): string {
  return `weighing-records-${day.replaceAll('-', '')}.xlsx`;
}
