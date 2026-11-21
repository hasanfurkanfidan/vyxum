import { useEffect, useState } from 'react';
import { liveDevicePath, normalizeDevice, parseLiveJson, type LiveDevice } from '../../lib/weighing';

/** Wait before opening the live stream again after the server closed it. */
const streamRetryMs = 3000;

export interface LiveDeviceState {
  /** Latest snapshot from the stream, or null before the first event. */
  device: LiveDevice | null;
  /** True while the stream is interrupted. */
  lost: boolean;
}

/** Follows one device over the Server-Sent Events stream `/api/devices/{id}/live`. */
export function useLiveDevice(deviceId: string | null): LiveDeviceState {
  const [device, setDevice] = useState<LiveDevice | null>(null);
  const [lost, setLost] = useState(false);
  const [tick, setTick] = useState(0);

  useEffect(() => setDevice(null), [deviceId]);

  useEffect(() => {
    if (!deviceId || typeof EventSource === 'undefined') return;
    const source = new EventSource(liveDevicePath(deviceId));
    let retry: ReturnType<typeof setTimeout> | undefined;
    source.onopen = () => setLost(false);
    source.onmessage = (event: MessageEvent<string>) => {
      try {
        setDevice(normalizeDevice(parseLiveJson<LiveDevice>(event.data)));
        setLost(false);
      } catch (error) {
        console.error('A live device event could not be read.', error);
      }
    };
    source.onerror = () => {
      setLost(true);
      // EventSource reconnects by itself, except after an HTTP error; then open a new one later.
      if (source.readyState === EventSource.CLOSED) {
        retry = setTimeout(() => setTick((value) => value + 1), streamRetryMs);
      }
    };
    return () => {
      if (retry !== undefined) clearTimeout(retry);
      source.close();
      setLost(false);
    };
  }, [deviceId, tick]);

  return { device, lost };
}
