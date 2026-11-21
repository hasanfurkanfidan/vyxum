// Minimal EventSource double for the live device stream: tests push events with `emit`.
import { act } from '@testing-library/react';
import { vi } from 'vitest';

export class FakeEventSource {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSED = 2;
  static instances: FakeEventSource[] = [];

  readonly url: string;
  readyState = FakeEventSource.CONNECTING;
  onopen: ((event: Event) => void) | null = null;
  onmessage: ((event: MessageEvent<string>) => void) | null = null;
  onerror: ((event: Event) => void) | null = null;

  constructor(url: string) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }

  emit(data: string) {
    act(() => {
      this.readyState = FakeEventSource.OPEN;
      this.onmessage?.(new MessageEvent('message', { data }));
    });
  }

  close() {
    this.readyState = FakeEventSource.CLOSED;
  }

  /** The open stream of one device. */
  static of(url: string): FakeEventSource {
    const source = FakeEventSource.instances.filter((s) => s.url === url && s.readyState !== FakeEventSource.CLOSED).at(-1);
    if (!source) throw new Error(`No open EventSource for ${url}.`);
    return source;
  }

  static install() {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
  }
}

/** JSON of a live device event, with the weight as a JSON number as the host writes it. */
export function liveDeviceJson(
  deviceId: string,
  weight: { value: string | null; unit?: string; state?: string } | null,
  state = 'Connected',
  lastError: string | null = null,
): string {
  const lastWeight = weight
    ? `{"deviceId":"${deviceId}","value":${weight.value ?? 'null'},"unit":${weight.unit ? `"${weight.unit}"` : 'null'},"state":"${weight.state ?? 'Stable'}","tare":null,"isNet":null,"timestamp":"2026-11-21T10:00:00Z","receivedAt":"2026-11-21T10:00:00Z"}`
    : 'null';
  return `{"deviceId":"${deviceId}","agentName":"PC","state":"${state}","lastError":${lastError ? JSON.stringify(lastError) : 'null'},"lastWeight":${lastWeight},"lastSeen":"2026-11-21T10:00:00Z"}`;
}
