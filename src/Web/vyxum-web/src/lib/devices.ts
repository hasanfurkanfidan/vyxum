// Clients for the device settings (/api/devices/config) and scale discovery (/api/devices/discover).
// Messages and codes are resource keys with the `devices.` prefix.
import { apiDelete, apiGet, apiPost, apiPut, type RequestOptions } from './api';
import type { LiveWeightState } from './weighing';

export type TransportType = 'serial' | 'tcp' | 'simulator';

/** Transport settings. Fields the screen does not know are kept as they are when saving. */
export interface TransportSettings {
  type: TransportType | string;
  portName?: string;
  baudRate?: number;
  dataBits?: number;
  parity?: string;
  stopBits?: string;
  handshake?: string;
  host?: string;
  port?: number;
  [key: string]: unknown;
}

export interface ProtocolSettings {
  type: string;
  command?: string;
  [key: string]: unknown;
}

export interface DeviceConfig {
  id: string;
  name: string;
  agentName: string | null;
  transport: TransportSettings;
  protocol: ProtocolSettings;
  /** Invariant decimal strings, for example "6" and "0.002". */
  capacity: string | null;
  division: string | null;
  unit: string | null;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface DeviceConfigInput {
  id?: string;
  name: string;
  agentName: string | null;
  transport: TransportSettings;
  protocol: ProtocolSettings;
  capacity: string | null;
  division: string | null;
  unit: string | null;
  isEnabled: boolean;
}

export type DiscoveryStatus = 'Running' | 'Completed' | 'Failed';
export type PortScanState = 'Pending' | 'Scanning' | 'Found' | 'NotFound' | 'InUse' | 'Error';

export interface DiscoveryPort {
  portName: string;
  state: PortScanState;
  detail: string | null;
}

export interface DiscoveryReading {
  value: string | null;
  unit: string | null;
  state: LiveWeightState;
}

export interface DiscoveryCandidate {
  key: string;
  portName: string | null;
  host: string | null;
  port: number | null;
  transport: TransportSettings;
  protocol: ProtocolSettings;
  serialNumber: string | null;
  reading: DiscoveryReading | null;
  description: string;
}

export interface Discovery {
  id: string;
  status: DiscoveryStatus;
  /** Resource key such as `devices.NoAgent`, or null. */
  code: string | null;
  agentName: string | null;
  startedAt: string;
  ports: DiscoveryPort[];
  candidates: DiscoveryCandidate[];
}

export interface DiscoveryRequest {
  agentName?: string;
  tcp?: { host: string; port: number };
}

const configPath = '/api/devices/config';

export function listDeviceConfigs(options?: RequestOptions): Promise<DeviceConfig[]> {
  return apiGet<DeviceConfig[]>(configPath, options);
}

export function getDeviceConfig(id: string, options?: RequestOptions): Promise<DeviceConfig> {
  return apiGet<DeviceConfig>(`${configPath}/${encodeURIComponent(id)}`, options);
}

export function createDeviceConfig(input: DeviceConfigInput): Promise<DeviceConfig> {
  return apiPost<DeviceConfig>(configPath, input);
}

export function updateDeviceConfig(id: string, input: DeviceConfigInput): Promise<DeviceConfig> {
  return apiPut<DeviceConfig>(`${configPath}/${encodeURIComponent(id)}`, input);
}

export function deleteDeviceConfig(id: string): Promise<void> {
  return apiDelete<void>(`${configPath}/${encodeURIComponent(id)}`);
}

export function startDiscovery(request: DiscoveryRequest = {}): Promise<{ id: string }> {
  return apiPost<{ id: string }>('/api/devices/discover', request);
}

export function getDiscovery(id: string, options?: RequestOptions): Promise<Discovery> {
  return apiGet<Discovery>(`/api/devices/discover/${encodeURIComponent(id)}`, options);
}

/** Codes of the discovery API. */
export const discoveryCodes = {
  running: 'devices.DiscoveryRunning',
  noAgent: 'devices.NoAgent',
  agentTimeout: 'devices.AgentTimeout',
  failed: 'devices.DiscoveryFailed',
} as const;

/** The input of an update request for a device as it is stored, with some fields changed. */
export function toInput(device: DeviceConfig, change: Partial<DeviceConfigInput> = {}): DeviceConfigInput {
  return {
    name: device.name,
    agentName: device.agentName,
    transport: device.transport,
    protocol: device.protocol,
    capacity: device.capacity,
    division: device.division,
    unit: device.unit,
    isEnabled: device.isEnabled,
    ...change,
  };
}

/** First free id of the form `scale-N` and its number. */
export function nextScaleId(existing: readonly string[]): { id: string; number: number } {
  const taken = new Set(existing);
  let number = 1;
  while (taken.has(`scale-${number}`)) number++;
  return { id: `scale-${number}`, number };
}

/** Serial settings offered by the manual form; the same set discovery tries (decision V-40). */
export const baudRates: readonly number[] = [9600, 19200, 4800, 2400, 38400, 115200];

export interface SerialFrame {
  dataBits: number;
  parity: string;
  stopBits: string;
}

export const serialFrames: readonly SerialFrame[] = [
  { dataBits: 8, parity: 'None', stopBits: 'One' },
  { dataBits: 7, parity: 'Even', stopBits: 'One' },
  { dataBits: 7, parity: 'Odd', stopBits: 'One' },
  { dataBits: 8, parity: 'Even', stopBits: 'One' },
  { dataBits: 8, parity: 'Odd', stopBits: 'One' },
];

const parityLetters: Record<string, string> = { None: 'N', Odd: 'O', Even: 'E', Mark: 'M', Space: 'S' };
const stopBitDigits: Record<string, string> = { One: '1', Two: '2', OnePointFive: '1.5' };

/** "8N1" style text of a serial frame; parts that are not set use the transport defaults (8, None, One). */
export function frameText(transport: Pick<TransportSettings, 'dataBits' | 'parity' | 'stopBits'>): string {
  const parity = transport.parity ?? 'None';
  const stop = transport.stopBits ?? 'One';
  return `${transport.dataBits ?? 8}${parityLetters[parity] ?? parity}${stopBitDigits[stop] ?? stop}`;
}

export function frameOf(text: string): SerialFrame {
  return serialFrames.find((f) => frameText(f) === text) ?? serialFrames[0]!;
}

/** Protocols the agent knows, with the resource key of their name. */
export const protocolTypes: readonly { type: string; labelKey: string }[] = [
  { type: 'mt-sics', labelKey: 'devices.ProtocolMtSics' },
  { type: 'toledo-continuous', labelKey: 'devices.ProtocolToledoContinuous' },
  { type: 'generic-ascii', labelKey: 'devices.ProtocolGenericAscii' },
];

/** Protocol settings for a protocol type chosen by hand. MT-SICS is polled with `SI` (decision V-28). */
export function protocolFor(type: string): ProtocolSettings {
  return type === 'mt-sics' ? { type, command: 'SI' } : { type };
}

/**
 * Short technical description of a connection, for example "MT-SICS · 9600 8N1 · COM3".
 * `protocolName` translates the protocol type; unknown types are shown as they are.
 */
export function connectionText(
  transport: TransportSettings,
  protocol: ProtocolSettings,
  protocolName: (type: string) => string,
  simulatorName: string,
): string {
  const parts = [protocolName(protocol.type)];
  if (transport.type === 'serial') {
    parts.push(`${transport.baudRate ?? 9600} ${frameText(transport)}`);
    if (transport.portName) parts.push(transport.portName);
  } else if (transport.type === 'tcp') {
    parts.push(`${transport.host ?? ''}:${transport.port ?? ''}`);
  } else if (transport.type === 'simulator') {
    parts.push(simulatorName);
  } else {
    parts.push(String(transport.type));
  }
  return parts.join(' · ');
}
