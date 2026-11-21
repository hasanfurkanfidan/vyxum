// Clients for the live device endpoints (/api/devices) and the Weighing module API (/api/weighing).
import { apiGet, apiPost, apiText, withQuery, type RequestOptions } from './api';

export type DeviceConnectionState = 'Connecting' | 'Connected' | 'Disconnected' | 'Error';
export type LiveWeightState = 'Stable' | 'Motion' | 'Overload' | 'Underload';

export interface LiveWeight {
  deviceId: string;
  /** The weight as transmitted, as an invariant decimal string (see parseLiveJson). Null for overload and underload. */
  value: string | null;
  unit: string | null;
  state: LiveWeightState;
  tare: string | null;
  isNet: boolean | null;
  timestamp: string;
  receivedAt: string;
}

export interface LiveDevice {
  deviceId: string;
  agentName: string | null;
  state: DeviceConnectionState;
  lastError: string | null;
  lastWeight: LiveWeight | null;
  lastSeen: string;
  /** Commands the device accepts, for example "Zero". Absent when the host does not report them. */
  supportedCommands?: string[];
  /** Name, capacity (Max), division (e) and unit from the device settings, when the host reports them. */
  name?: string | null;
  capacity?: string | null;
  division?: string | null;
  unit?: string | null;
}

export type ZeroResult = 'Done' | 'NotSupported' | 'Rejected' | 'Unavailable';

export interface ZeroResponse {
  result: ZeroResult;
  detail?: string | null;
}

export interface WeighingSettings {
  requireStable: boolean;
  minimumNet: string;
  requireCancelReason: boolean;
  currency: string;
  amountDecimals: number;
  canPrintLabel: boolean;
  showOperator: boolean;
  productsAvailable: boolean;
}

export interface WeighingProduct {
  id: string;
  code: string;
  name: string;
  unitPrice: string | null;
  packageTare: string | null;
  shelfLifeDays: number | null;
  barcodePrefix: string | null;
}

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

export interface CreateRecordInput {
  requestId: string;
  deviceId: string;
  productId: string | null;
  lot: string | null;
  /** Station tare in the scale's unit, invariant decimal string. */
  tare: string | null;
}

// The device endpoints write weights as JSON numbers ("value": 1.260). JSON.parse would turn them
// into floating point and drop the trailing zero, so the numeric text of the weight fields is
// quoted before parsing. A quote inside a JSON string is escaped, so the pattern cannot match there.
const weightNumber = /"(value|tare|capacity|division)"(\s*:\s*)(-?\d+(?:\.\d+)?)(?=\s*[,}\]])/g;

/** Parses LiveDevice JSON (one object or an array) and keeps the weights as decimal strings. */
export function parseLiveJson<T>(text: string): T {
  return JSON.parse(text.replace(weightNumber, '"$1"$2"$3"')) as T;
}

/** Normalizes a weight field that may still arrive as a number (for example from a newer host that sends strings anyway). */
function weightText(value: unknown): string | null {
  if (typeof value === 'string') return value;
  if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  return null;
}

export function normalizeDevice(device: LiveDevice): LiveDevice {
  const result: LiveDevice = { ...device };
  if (device.capacity !== undefined) result.capacity = weightText(device.capacity);
  if (device.division !== undefined) result.division = weightText(device.division);
  const weight = device.lastWeight;
  if (weight) result.lastWeight = { ...weight, value: weightText(weight.value), tare: weightText(weight.tare) };
  return result;
}

export async function getDevices(options?: RequestOptions): Promise<LiveDevice[]> {
  // Read as text so that parseLiveJson can keep the decimals.
  const list = parseLiveJson<LiveDevice[]>(await apiText('/api/devices', options));
  return Array.isArray(list) ? list.map(normalizeDevice) : [];
}

export function liveDevicePath(deviceId: string): string {
  return `/api/devices/${encodeURIComponent(deviceId)}/live`;
}

export function zeroDevice(deviceId: string): Promise<ZeroResponse> {
  return apiPost<ZeroResponse>(`/api/devices/${encodeURIComponent(deviceId)}/zero`);
}

export function getWeighingSettings(options?: RequestOptions): Promise<WeighingSettings> {
  return apiGet<WeighingSettings>('/api/weighing/settings', options);
}

export function listWeighingProducts(search: string, options?: RequestOptions): Promise<WeighingProduct[]> {
  return apiGet<WeighingProduct[]>(withQuery('/api/weighing/products', { search: search.trim() }), options);
}

export function listRecentRecords(deviceId: string, take: number, options?: RequestOptions): Promise<WeighingRecord[]> {
  return apiGet<WeighingRecord[]>(withQuery('/api/weighing/records/recent', { deviceId, take }), options);
}

export function createRecord(input: CreateRecordInput): Promise<WeighingRecord> {
  return apiPost<WeighingRecord>('/api/weighing/records', input);
}

/** A new request id for one save attempt; retries of the same save reuse it. */
export function newRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = new Uint8Array(16);
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    crypto.getRandomValues(bytes);
  } else {
    for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256);
  }
  bytes[6] = ((bytes[6] ?? 0) & 0x0f) | 0x40;
  bytes[8] = ((bytes[8] ?? 0) & 0x3f) | 0x80;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
