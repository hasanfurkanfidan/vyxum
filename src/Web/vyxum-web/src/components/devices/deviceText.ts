// Resource keys and small text helpers shared by the device screens (wizard scale step and Settings > Devices).
import { useCallback } from 'react';
import { useT } from '../../i18n/I18nProvider';
import { connectionText, protocolTypes, type PortScanState, type ProtocolSettings, type TransportSettings } from '../../lib/devices';
import type { DeviceConnectionState, LiveWeightState } from '../../lib/weighing';

export type Tone = 'positive' | 'warning' | 'muted' | 'ink';

export const connectionStateKeys: Record<DeviceConnectionState, string> = {
  Connected: 'devices.StateConnected',
  Connecting: 'devices.StateConnecting',
  Disconnected: 'devices.StateDisconnected',
  Error: 'devices.StateError',
};

export function connectionTone(state: DeviceConnectionState): Tone {
  return state === 'Connected' ? 'positive' : 'warning';
}

export const readingStateKeys: Record<LiveWeightState, string> = {
  Stable: 'devices.ReadingStable',
  Motion: 'devices.ReadingMotion',
  Overload: 'devices.ReadingOverload',
  Underload: 'devices.ReadingUnderload',
};

export const portStateKeys: Record<PortScanState, string> = {
  Pending: 'devices.PortPending',
  Scanning: 'devices.PortScanning',
  Found: 'devices.PortFound',
  NotFound: 'devices.PortNotFound',
  InUse: 'devices.PortInUse',
  Error: 'devices.PortError',
};

export const portStateTones: Record<PortScanState, Tone> = {
  Pending: 'muted',
  Scanning: 'ink',
  Found: 'positive',
  NotFound: 'muted',
  InUse: 'warning',
  Error: 'warning',
};

export function toneClass(tone: Tone): string {
  switch (tone) {
    case 'positive':
      return 'state--positive';
    case 'warning':
      return 'state--warning';
    case 'muted':
      return 'muted';
    default:
      return '';
  }
}

/** Replaces `{0}`, `{1}`, ... in a translated text. */
export function fill(text: string, ...values: (string | number)[]): string {
  return values.reduce<string>((result, value, index) => result.split(`{${index}}`).join(String(value)), text);
}

/** Translated "MT-SICS · 9600 8N1 · COM3" text of a connection. */
export function useConnectionText(): (transport: TransportSettings, protocol: ProtocolSettings) => string {
  const t = useT();
  return useCallback(
    (transport: TransportSettings, protocol: ProtocolSettings) =>
      connectionText(
        transport,
        protocol,
        (type) => {
          const known = protocolTypes.find((p) => p.type === type);
          return known ? t(known.labelKey) : type;
        },
        t('devices.TransportSimulator'),
      ),
    [t],
  );
}
