import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { discoveryTiming } from '../../components/devices/ScaleSetupFlow';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { DeviceConfig, Discovery, DiscoveryCandidate } from '../../lib/devices';
import { callsTo, installFakeApi, jsonResponse, type FakeHandler } from '../../test/fakeApi';
import { FakeEventSource, liveDeviceJson } from '../../test/fakeEventSource';
import { ScaleStep } from './ScaleStep';

function bodyOf(init: RequestInit | undefined): Record<string, unknown> {
  return JSON.parse(String(init?.body ?? 'null')) as Record<string, unknown>;
}

const mtSics: DiscoveryCandidate = {
  key: 'COM3-9600-8N1-mt-sics',
  portName: 'COM3',
  host: null,
  port: null,
  transport: { type: 'serial', portName: 'COM3', baudRate: 9600, dataBits: 8, parity: 'None', stopBits: 'One', handshake: 'None' },
  protocol: { type: 'mt-sics', command: 'SI' },
  serialNumber: 'B123456789',
  reading: { value: '0.572', unit: 'kg', state: 'Stable' },
  description: 'MT-SICS 9600 8N1 COM3',
};

function discovery(change: Partial<Discovery>): Discovery {
  return {
    id: 'd1',
    status: 'Completed',
    code: null,
    agentName: 'PC',
    startedAt: '2026-11-21T10:00:00Z',
    ports: [],
    candidates: [],
    ...change,
  };
}

function config(id: string, change: Partial<DeviceConfig> = {}): DeviceConfig {
  return {
    id,
    name: 'Terazi 1',
    agentName: 'PC',
    transport: mtSics.transport,
    protocol: mtSics.protocol,
    capacity: null,
    division: null,
    unit: 'kg',
    isEnabled: true,
    createdAt: '2026-11-21T10:00:00Z',
    updatedAt: '2026-11-21T10:00:00Z',
    ...change,
  };
}

interface ScaleApi {
  configs?: DeviceConfig[];
  /** Answers of GET /api/devices/discover/d1, one per poll; the last one repeats. */
  polls?: Discovery[];
  routes?: Record<string, FakeHandler>;
}

function installScaleApi({ configs = [], polls = [discovery({})], routes = {} }: ScaleApi = {}) {
  let poll = 0;
  const saved: DeviceConfig[] = [...configs];
  return installFakeApi({
    routes: {
      'GET /api/devices/config': () => jsonResponse(saved),
      'POST /api/devices/config': (_url, init) => {
        const body = bodyOf(init) as unknown as DeviceConfig;
        const device = config(body.id, { ...body, createdAt: '2026-11-21T10:00:00Z', updatedAt: '2026-11-21T10:00:00Z' });
        saved.push(device);
        return jsonResponse(device, 201);
      },
      'PUT /api/devices/config/scale-1': (_url, init) => jsonResponse(config('scale-1', bodyOf(init) as Partial<DeviceConfig>)),
      'DELETE /api/devices/config/scale-1': () => {
        saved.splice(saved.findIndex((d) => d.id === 'scale-1'), 1);
        return new Response(null, { status: 204 });
      },
      'POST /api/devices/discover': () => jsonResponse({ id: 'd1' }, 202),
      'GET /api/devices/discover/d1': () => jsonResponse(polls[Math.min(poll++, polls.length - 1)]),
      ...routes,
    },
  });
}

function renderStep() {
  const onDone = vi.fn();
  const onSkip = vi.fn();
  render(
    <I18nProvider>
      <ScaleStep onDone={onDone} onSkip={onSkip} />
    </I18nProvider>,
  );
  return { onDone, onSkip };
}

beforeEach(() => {
  discoveryTiming.pollMs = 10;
  FakeEventSource.install();
});

afterEach(() => {
  discoveryTiming.pollMs = 500;
});

describe('scale step', () => {
  it('starts looking by itself and shows each port with its state as a word', async () => {
    const fetchMock = installScaleApi({
      polls: [
        discovery({
          status: 'Running',
          ports: [
            { portName: 'COM1', state: 'NotFound', detail: null },
            { portName: 'COM3', state: 'Scanning', detail: null },
            { portName: 'COM4', state: 'InUse', detail: null },
          ],
        }),
        discovery({
          ports: [
            { portName: 'COM1', state: 'NotFound', detail: null },
            { portName: 'COM3', state: 'Found', detail: null },
            { portName: 'COM4', state: 'InUse', detail: null },
          ],
          candidates: [mtSics],
        }),
      ],
    });
    renderStep();

    expect(await screen.findByRole('heading', { name: 'Terazi aranıyor…' })).toBeInTheDocument();
    expect(await screen.findByText('deneniyor')).toHaveClass('port-list__state');
    expect(screen.getByText('kullanımda')).toHaveClass('state--warning');
    expect(callsTo(fetchMock, 'POST /api/devices/discover')).toBe(1);

    expect(await screen.findByRole('heading', { name: 'Bulunan teraziler' })).toBeInTheDocument();
    expect(screen.getByText('terazi bulundu')).toHaveClass('state--positive');
    const row = screen.getByRole('button', { name: /MT-SICS · 9600 8N1 · COM3/ });
    expect(within(row).getByText('Seri no B123456789')).toBeInTheDocument();
    // The sample weight uses the culture's decimal comma.
    expect(within(row).getByText('0,572')).toBeInTheDocument();
  });

  it('saves the chosen scale, shows its live weight and finishes on confirmation', async () => {
    const fetchMock = installScaleApi({ polls: [discovery({ candidates: [mtSics] })] });
    const user = userEvent.setup();
    const { onDone } = renderStep();

    await user.click(await screen.findByRole('button', { name: /MT-SICS · 9600 8N1 · COM3/ }));
    expect(await screen.findByRole('heading', { name: 'Teraziye bir şey koyun' })).toBeInTheDocument();

    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST' && String(init.body).includes('scale-1'));
    const body = bodyOf(post?.[1]);
    expect(body).toMatchObject({ id: 'scale-1', name: 'Terazi 1', agentName: 'PC', unit: 'kg', transport: mtSics.transport, protocol: mtSics.protocol });

    FakeEventSource.of('/api/devices/scale-1/live').emit(liveDeviceJson('scale-1', { value: '1.250', unit: 'kg', state: 'Motion' }));
    expect(screen.getByTestId('device-live-weight')).toHaveTextContent('1,250');
    expect(screen.getByText('hareketli')).toBeInTheDocument();
    expect(screen.getByText('bağlı')).toHaveClass('state--positive');
    // The unit is taken from the reading.
    expect(screen.getByLabelText('Birim')).toHaveValue('kg');

    await user.click(screen.getByRole('button', { name: 'Bu terazi doğru' }));
    expect(onDone).toHaveBeenCalledTimes(1);
    expect(callsTo(fetchMock, 'PUT /api/devices/config/scale-1')).toBe(0);
  });

  it('saves capacity and division on confirmation and shows their validation errors', async () => {
    let attempt = 0;
    const fetchMock = installScaleApi({
      polls: [discovery({ candidates: [mtSics] })],
      routes: {
        'PUT /api/devices/config/scale-1': (_url, init) =>
          attempt++ === 0
            ? jsonResponse({ title: 'Invalid', errors: { Division: ['devices.DivisionAboveCapacity'] } }, 400)
            : jsonResponse(config('scale-1', bodyOf(init) as Partial<DeviceConfig>)),
      },
    });
    const user = userEvent.setup();
    const { onDone } = renderStep();

    await user.click(await screen.findByRole('button', { name: /COM3/ }));
    await user.type(await screen.findByLabelText('Kapasite (Max)'), '6');
    await user.type(screen.getByLabelText('Bölüntü (e)'), '10');
    await user.click(screen.getByRole('button', { name: 'Bu terazi doğru' }));

    expect(await screen.findByText('Bölüntü kapasiteden büyük olamaz.')).toBeInTheDocument();
    expect(onDone).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText('Bölüntü (e)'));
    await user.type(screen.getByLabelText('Bölüntü (e)'), '0,002');
    await user.click(screen.getByRole('button', { name: 'Bu terazi doğru' }));
    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    const put = fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT').at(-1);
    expect(bodyOf(put?.[1])).toMatchObject({ capacity: '6', division: '0.002', unit: 'kg' });
  });

  it('deletes the scale and goes back to the results for another scale', async () => {
    const fetchMock = installScaleApi({ polls: [discovery({ candidates: [mtSics] })] });
    const user = userEvent.setup();
    renderStep();

    await user.click(await screen.findByRole('button', { name: /COM3/ }));
    await user.click(await screen.findByRole('button', { name: 'Başka terazi' }));

    expect(await screen.findByRole('heading', { name: 'Bulunan teraziler' })).toBeInTheDocument();
    expect(callsTo(fetchMock, 'DELETE /api/devices/config/scale-1')).toBe(1);
  });

  it('offers the port results, a new search, the manual form and skip when nothing is found', async () => {
    const fetchMock = installScaleApi({
      polls: [discovery({ ports: [{ portName: 'COM1', state: 'NotFound', detail: null }, { portName: 'COM5', state: 'Error', detail: 'Access denied' }] })],
    });
    const user = userEvent.setup();
    const { onSkip } = renderStep();

    expect(await screen.findByRole('heading', { name: 'Terazi bulunamadı' })).toBeInTheDocument();
    expect(screen.getByText('terazi yok')).toBeInTheDocument();
    expect(screen.getByText('açılamadı')).toHaveClass('state--warning');
    expect(screen.getByText('Access denied')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Tekrar ara' }));
    await screen.findByRole('heading', { name: 'Terazi bulunamadı' });
    expect(callsTo(fetchMock, 'POST /api/devices/discover')).toBe(2);

    await user.click(screen.getByRole('button', { name: 'Elle ayarla' }));
    const port = screen.getByLabelText('Port');
    await user.type(port, 'COM7');
    await user.selectOptions(screen.getByLabelText('Hız (baud)'), '4800');
    await user.selectOptions(screen.getByLabelText('Veri, parite, dur biti'), '7E1');
    await user.click(screen.getByRole('button', { name: 'Kaydet ve dene' }));

    expect(await screen.findByRole('heading', { name: 'Teraziye bir şey koyun' })).toBeInTheDocument();
    const post = fetchMock.mock.calls.find(([input, init]) => String(input).endsWith('/api/devices/config') && init?.method === 'POST');
    expect(bodyOf(post?.[1])).toMatchObject({
      transport: { type: 'serial', portName: 'COM7', baudRate: 4800, dataBits: 7, parity: 'Even', stopBits: 'One' },
      protocol: { type: 'mt-sics', command: 'SI' },
    });

    // Skipping while the scale is not confirmed removes it again.
    await user.click(screen.getByRole('button', { name: 'Atla' }));
    await waitFor(() => expect(onSkip).toHaveBeenCalledTimes(1));
    expect(callsTo(fetchMock, 'DELETE /api/devices/config/scale-1')).toBe(1);
  });

  it('tries a network scale through discovery', async () => {
    const fetchMock = installScaleApi({ polls: [discovery({})] });
    const user = userEvent.setup();
    renderStep();

    await screen.findByRole('heading', { name: 'Terazi bulunamadı' });
    await user.click(screen.getByRole('button', { name: 'Elle ayarla' }));
    await user.selectOptions(screen.getByLabelText('Bağlantı türü'), 'tcp');
    await user.click(screen.getByRole('button', { name: 'Dene' }));
    expect(screen.getByText('IP adresi ya da bilgisayar adı zorunludur.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('IP adresi'), '192.168.1.50');
    await user.type(screen.getByLabelText('Ağ portu'), '4001');
    await user.click(screen.getByRole('button', { name: 'Dene' }));

    await waitFor(() => expect(callsTo(fetchMock, 'POST /api/devices/discover')).toBe(2));
    const last = fetchMock.mock.calls.filter(([input, init]) => String(input).endsWith('/api/devices/discover') && init?.method === 'POST').at(-1);
    expect(bodyOf(last?.[1])).toEqual({ tcp: { host: '192.168.1.50', port: 4001 } });
  });

  it('explains that the agent service is not running and retries', async () => {
    let calls = 0;
    const fetchMock = installScaleApi({
      routes: {
        'POST /api/devices/discover': () => (calls++ === 0 ? jsonResponse({ code: 'devices.NoAgent' }, 503) : jsonResponse({ id: 'd1' }, 202)),
      },
    });
    const user = userEvent.setup();
    const { onSkip } = renderStep();

    expect(await screen.findByRole('alert')).toHaveTextContent('Vyxum Agent hizmeti çalışmıyor.');
    await user.click(screen.getByRole('button', { name: 'Yeniden dene' }));
    expect(await screen.findByRole('heading', { name: 'Terazi bulunamadı' })).toBeInTheDocument();
    expect(callsTo(fetchMock, 'POST /api/devices/discover')).toBe(2);

    await user.click(screen.getByRole('button', { name: 'Atla' }));
    expect(onSkip).toHaveBeenCalledTimes(1);
  });

  it('shows a failed discovery with its code', async () => {
    installScaleApi({ polls: [discovery({ status: 'Failed', code: 'devices.AgentTimeout' })] });
    renderStep();
    expect(await screen.findByRole('alert')).toHaveTextContent('Vyxum Agent hizmeti zamanında yanıt vermedi.');
  });

  it('shows a scale that is already set up with its live weight instead of searching again', async () => {
    const fetchMock = installScaleApi({ configs: [config('scale-1')] });
    const user = userEvent.setup();
    const { onDone } = renderStep();

    expect(await screen.findByRole('heading', { name: 'Kayıtlı terazi' })).toBeInTheDocument();
    expect(callsTo(fetchMock, 'POST /api/devices/discover')).toBe(0);
    FakeEventSource.of('/api/devices/scale-1/live').emit(liveDeviceJson('scale-1', { value: '2.5', unit: 'kg' }));
    expect(screen.getByTestId('device-live-weight')).toHaveTextContent('2,5');

    await user.click(screen.getByRole('button', { name: 'Yeniden ara' }));
    await screen.findByRole('heading', { name: 'Terazi bulunamadı' });
    expect(callsTo(fetchMock, 'POST /api/devices/discover')).toBe(1);

    // A new scale gets the next free id.
    await user.click(screen.getByRole('button', { name: 'Elle ayarla' }));
    await user.type(screen.getByLabelText('Port'), 'COM4');
    await user.click(screen.getByRole('button', { name: 'Kaydet ve dene' }));
    await screen.findByRole('heading', { name: 'Teraziye bir şey koyun' });
    const post = fetchMock.mock.calls.find(([input, init]) => String(input).endsWith('/api/devices/config') && init?.method === 'POST');
    expect(bodyOf(post?.[1])).toMatchObject({ id: 'scale-2', name: 'Terazi 2' });
    expect(onDone).not.toHaveBeenCalled();
  });
});
