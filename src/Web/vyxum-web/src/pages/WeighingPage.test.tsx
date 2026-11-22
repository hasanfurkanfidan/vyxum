import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App, AppProviders } from '../App';
import type { WeighingProduct, WeighingRecord, WeighingSettings } from '../lib/weighing';
import { callsTo, installFakeApi, jsonResponse, type FakeHandler } from '../test/fakeApi';

/** Minimal EventSource double: the test pushes events with `emit`. */
class FakeEventSource {
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

  fail() {
    act(() => {
      this.readyState = FakeEventSource.CONNECTING;
      this.onerror?.(new Event('error'));
    });
  }

  close() {
    this.readyState = FakeEventSource.CLOSED;
  }

  static latest(): FakeEventSource {
    const source = FakeEventSource.instances.at(-1);
    if (!source) throw new Error('No EventSource was opened.');
    return source;
  }
}

interface WeightJson {
  value: string | null;
  unit?: string | null;
  state?: string;
  tare?: string | null;
  isNet?: boolean | null;
}

/**
 * LiveDevice JSON as the host writes it: weights are JSON numbers with their trailing zeros
 * ("value":1.260), which JSON.stringify could not produce.
 */
function deviceJson(weight: WeightJson | null, extra: Record<string, unknown> = {}, deviceId = 'scale-1'): string {
  const base = JSON.stringify({ deviceId, agentName: 'PC-1', state: 'Connected', lastError: null, lastSeen: '2026-11-21T10:00:00Z', ...extra });
  if (!weight) return base.replace(/}$/, ',"lastWeight":null}');
  const parts = [
    `"deviceId":${JSON.stringify(deviceId)}`,
    `"value":${weight.value ?? 'null'}`,
    `"unit":${JSON.stringify(weight.unit === undefined ? 'kg' : weight.unit)}`,
    `"state":${JSON.stringify(weight.state ?? 'Stable')}`,
    `"tare":${weight.tare ?? 'null'}`,
    `"isNet":${JSON.stringify(weight.isNet ?? null)}`,
    '"timestamp":"2026-11-21T10:00:00Z"',
    '"receivedAt":"2026-11-21T10:00:00Z"',
  ];
  return base.replace(/}$/, `,"lastWeight":{${parts.join(',')}}}`);
}

function rawJson(text: string, status = 200): Response {
  return new Response(text, { status, headers: { 'Content-Type': 'application/json' } });
}

const settings: WeighingSettings = {
  requireStable: true,
  minimumNet: '0',
  requireCancelReason: true,
  currency: 'TRY',
  amountDecimals: 2,
  canPrintLabel: false,
  showOperator: false,
  productsAvailable: true,
};

const pistachio: WeighingProduct = {
  id: '00000000-0000-0000-0000-000000000001',
  code: '0417',
  name: 'Antep Fıstığı, kavrulmuş',
  unitPrice: '780.00',
  packageTare: '0.012',
  shelfLifeDays: 180,
  barcodePrefix: '29',
  targetWeight: null,
  lowerTolerance: null,
  upperTolerance: null,
};

/** A product packed to a target: 250 g, 5 g under and 10 g over allowed. */
const coffee: WeighingProduct = {
  id: '00000000-0000-0000-0000-000000000002',
  code: '0520',
  name: 'Türk kahvesi',
  unitPrice: null,
  packageTare: null,
  shelfLifeDays: null,
  barcodePrefix: null,
  targetWeight: '0.25',
  lowerTolerance: '0.005',
  upperTolerance: '0.01',
};

function record(overrides: Partial<WeighingRecord> = {}): WeighingRecord {
  return {
    id: 142,
    number: '000142',
    createdAt: '2026-11-21T14:42:00Z',
    deviceId: 'scale-1',
    productId: pistachio.id,
    productCode: pistachio.code,
    productName: pistachio.name,
    lot: null,
    gross: '1.260',
    tare: '0.012',
    net: '1.248',
    unit: 'kg',
    unitPrice: '780.00',
    amount: '973.44',
    currency: 'TRY',
    status: 'Saved',
    cancelledAt: null,
    cancelReason: null,
    operator: null,
    ...overrides,
  };
}

interface Setup {
  devices?: string;
  settings?: Partial<WeighingSettings>;
  products?: WeighingProduct[];
  recent?: WeighingRecord[];
  routes?: Record<string, FakeHandler>;
}

function setup(options: Setup = {}) {
  const devices = options.devices ?? `[${deviceJson({ value: '1.260' })}]`;
  return installFakeApi({
    routes: {
      'GET /api/devices': () => rawJson(devices),
      'GET /api/weighing/settings': () => jsonResponse({ ...settings, ...options.settings }),
      'GET /api/weighing/products': () => jsonResponse(options.products ?? [pistachio]),
      'GET /api/weighing/records/recent': () => jsonResponse(options.recent ?? []),
      ...options.routes,
    },
  });
}

function renderWeighing() {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={['/']}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

function bodyOf(init: RequestInit | undefined): Record<string, unknown> {
  return JSON.parse(String(init?.body)) as Record<string, unknown>;
}

function postBodies(fetchMock: ReturnType<typeof installFakeApi>, path: string): Record<string, unknown>[] {
  return fetchMock.mock.calls
    .filter(([input, init]) => init?.method === 'POST' && String(input) === path)
    .map(([, init]) => bodyOf(init));
}

// The reading is on screen before the devices load; wait until the live stream is open too.
async function liveNet() {
  const net = await screen.findByTestId('live-net');
  await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
  return net;
}

async function expectNet(text: string) {
  await waitFor(() => expect(screen.getByTestId('live-net')).toHaveTextContent(text));
}

beforeEach(() => {
  FakeEventSource.instances = [];
  vi.stubGlobal('EventSource', FakeEventSource);
  window.localStorage.clear();
});

afterEach(() => {
  window.localStorage.clear();
});

describe('weighing screen', () => {
  it('shows the live weight with its decimals, the unit and the state as a word and a color', async () => {
    setup();
    renderWeighing();

    await liveNet();
    await expectNet('1,260');
    const live = screen.getByRole('region', { name: 'Canlı ağırlık' });
    expect(within(live).getByText('kg')).toBeInTheDocument();
    expect(within(live).getByText('sabit')).toHaveClass('state--positive');
    expect(screen.getByText('terazi bağlı, scale-1')).toHaveClass('state--positive');

    const source = FakeEventSource.latest();
    expect(source.url).toBe('/api/devices/scale-1/live');
    source.emit(deviceJson({ value: '2.500', state: 'Motion' }));
    await expectNet('2,500');
    expect(within(live).getByText('hareketli')).toHaveClass('state--warning');

    source.emit(deviceJson({ value: null, state: 'Overload' }));
    expect(within(live).getByText('aşırı yük')).toHaveClass('state--warning');
  });

  it('shows the connection lost state when the live stream fails', async () => {
    setup();
    renderWeighing();
    await liveNet();
    FakeEventSource.latest().fail();
    expect(await screen.findByText('terazi bağlı değil, scale-1')).toHaveClass('state--warning');
  });

  it('applies the package tare of the selected product and previews the amount', async () => {
    setup();
    const user = userEvent.setup();
    renderWeighing();
    await liveNet();

    await user.click(await screen.findByRole('button', { name: /Antep Fıstığı, kavrulmuş/ }));
    expect(screen.getByRole('heading', { level: 1, name: 'Antep Fıstığı, kavrulmuş' })).toBeInTheDocument();
    expect(screen.getByText('Tartım / 0417')).toBeInTheDocument();
    expect(screen.getByTestId('tare')).toHaveTextContent('0,012 kg');
    expect(screen.getByTestId('gross')).toHaveTextContent('1,260 kg');
    await expectNet('1,248');
    // 1.248 kg x 780.00 = 973.44
    expect(screen.getByTestId('amount').textContent?.replace(/\s/g, ' ')).toBe('₺973,44');
  });

  it('takes the current gross as the station tare and sends it with the save', async () => {
    const fetchMock = setup({
      devices: `[${deviceJson({ value: '0.250' })}]`,
      routes: { 'POST /api/weighing/records': () => jsonResponse(record(), 201) },
    });
    const user = userEvent.setup();
    renderWeighing();
    await expectNet('0,250');

    await user.click(screen.getByRole('button', { name: 'Dara al' }));
    expect(screen.getByTestId('tare')).toHaveTextContent('0,250 kg');
    await expectNet('0,000');

    FakeEventSource.latest().emit(deviceJson({ value: '1.500' }));
    await expectNet('1,250');

    await user.click(screen.getByRole('button', { name: 'Tartımı kaydet' }));
    await screen.findByText('000142 kaydedildi');
    expect(postBodies(fetchMock, '/api/weighing/records')[0]).toMatchObject({ deviceId: 'scale-1', productId: null, tare: '0.250' });
    // The station tare is reset after the save.
    expect(screen.getByRole('button', { name: 'Dara al' })).toBeInTheDocument();
  });

  it('does not take the tare while the weight is in motion', async () => {
    setup({ devices: `[${deviceJson({ value: '0.250', state: 'Motion' })}]` });
    renderWeighing();
    await liveNet();
    expect(screen.getByRole('button', { name: 'Dara al' })).toBeDisabled();
  });

  it('retries a save with the same request id after a network failure and adds it to the recent list', async () => {
    let attempts = 0;
    const fetchMock = setup({
      routes: {
        'POST /api/weighing/records': () => {
          attempts++;
          if (attempts === 1) throw new TypeError('Failed to fetch');
          if (attempts === 2) return jsonResponse({ title: 'Down' }, 503);
          return jsonResponse(record(), 201);
        },
      },
    });
    const user = userEvent.setup();
    renderWeighing();
    await liveNet();
    expect(await screen.findByText('Henüz tartım yok.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Parti'), 'L-261121-03');
    await user.click(screen.getByRole('button', { name: 'Tartımı kaydet' }));
    expect(await screen.findByText('Bağlantı koptu, yeniden deneniyor…')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tartımı kaydet' })).toBeDisabled();
    expect(screen.queryByText('000142 kaydedildi')).not.toBeInTheDocument();

    expect(await screen.findByText('000142 kaydedildi', undefined, { timeout: 4000 })).toBeInTheDocument();
    const bodies = postBodies(fetchMock, '/api/weighing/records');
    expect(bodies).toHaveLength(3);
    const ids = new Set(bodies.map((b) => b.requestId));
    expect(ids.size).toBe(1);
    expect(String(bodies[0]?.requestId)).toMatch(/^[0-9a-f-]{36}$/);
    expect(bodies[0]).toMatchObject({ lot: 'L-261121-03' });
    // Every attempt carries the reading the operator saw, not whatever the scale shows when it is sent.
    for (const body of bodies) {
      expect(body.reading).toEqual({ value: '1.260', unit: 'kg', receivedAt: '2026-11-21T10:00:00Z' });
    }

    const recent = screen.getByRole('region', { name: 'Son tartımlar' });
    expect(within(recent).getByText('Antep Fıstığı, kavrulmuş')).toBeInTheDocument();
    expect(within(recent).getByText('1,248 kg')).toBeInTheDocument();
    expect(within(recent).getByRole('link', { name: 'Tüm kayıtlar' })).toHaveAttribute('href', '/records');
    expect(screen.getByRole('button', { name: 'Tartımı kaydet' })).toBeEnabled();
    expect(window.localStorage.getItem('vyxum.weighing.lot')).toBe('L-261121-03');
  });

  it('gives up a save that cannot reach the server and tells the operator to check the recent weighings', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const fetchMock = setup({
        routes: {
          'POST /api/weighing/records': () => {
            throw new TypeError('Failed to fetch');
          },
        },
      });
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderWeighing();
      await liveNet();

      await user.click(screen.getByRole('button', { name: 'Tartımı kaydet' }));
      expect(await screen.findByText('Bağlantı koptu, yeniden deneniyor…')).toBeInTheDocument();
      await vi.advanceTimersByTimeAsync(8000);

      expect(
        await screen.findByText('Sunucuya ulaşılamadı, tartım kaydedilmemiş olabilir. Yeniden kaydetmeden önce son tartımları kontrol edin.'),
      ).toBeInTheDocument();
      expect(postBodies(fetchMock, '/api/weighing/records')).toHaveLength(5); // the first try and four retries
      expect(screen.getByRole('button', { name: 'Tartımı kaydet' })).toBeEnabled();
    } finally {
      vi.useRealTimers();
    }
  });

  it('uses a new request id for the next save', async () => {
    let id = 142;
    const fetchMock = setup({
      routes: { 'POST /api/weighing/records': () => jsonResponse(record({ id, number: `000${id++}` }), 201) },
    });
    const user = userEvent.setup();
    renderWeighing();
    await liveNet();
    await user.click(screen.getByRole('button', { name: 'Tartımı kaydet' }));
    await screen.findByText('000142 kaydedildi');
    await user.click(screen.getByRole('button', { name: 'Tartımı kaydet' }));
    await screen.findByText('000143 kaydedildi');
    const bodies = postBodies(fetchMock, '/api/weighing/records');
    expect(bodies[0]?.requestId).not.toBe(bodies[1]?.requestId);
  });

  it('shows a rejected save translated from its code and does not retry', async () => {
    const fetchMock = setup({
      routes: { 'POST /api/weighing/records': () => jsonResponse({ code: 'weighing.NotStable' }, 422) },
    });
    const user = userEvent.setup();
    renderWeighing();
    await liveNet();
    await user.click(screen.getByRole('button', { name: 'Tartımı kaydet' }));
    expect(await screen.findByText('Ağırlık sabit değil. Terazi durulunca yeniden deneyin.')).toBeInTheDocument();
    expect(callsTo(fetchMock, 'POST /api/weighing/records')).toBe(1);
    expect(screen.queryByText(/kaydedildi/)).not.toBeInTheDocument();
  });

  it('lists the recent weighings of the device', async () => {
    const fetchMock = setup({ recent: [record(), record({ id: 141, number: '000141', productName: null, net: '0.504', status: 'Cancelled' })] });
    renderWeighing();
    const recent = await screen.findByRole('region', { name: 'Son tartımlar' });
    expect(await within(recent).findByText('0,504 kg')).toBeInTheDocument();
    expect(within(recent).getByText('Ürünsüz')).toBeInTheDocument();
    expect(within(recent).getByText('iptal')).toBeInTheDocument();
    const urls = fetchMock.mock.calls.map(([input]) => String(input));
    expect(urls).toContain('/api/weighing/records/recent?deviceId=scale-1&take=5');
  });

  it('zeroes the scale and shows the result', async () => {
    let result = 'Done';
    const fetchMock = setup({ routes: { 'POST /api/devices/scale-1/zero': () => jsonResponse({ result }) } });
    const user = userEvent.setup();
    renderWeighing();
    await liveNet();

    await user.click(screen.getByRole('button', { name: 'Sıfırla' }));
    expect(await screen.findByText('Terazi sıfırlandı.')).toBeInTheDocument();
    expect(callsTo(fetchMock, 'POST /api/devices/scale-1/zero')).toBe(1);

    result = 'Rejected';
    await user.click(screen.getByRole('button', { name: 'Sıfırla' }));
    expect(await screen.findByText('Terazi sıfırlamayı reddetti. Kefeyi boşaltıp ağırlık sabitlenince deneyin.')).toBeInTheDocument();
  });

  it('disables zero when the device reports it cannot zero', async () => {
    setup({ devices: `[${deviceJson({ value: '1.260' }, { supportedCommands: [] })}]` });
    renderWeighing();
    await liveNet();
    expect(screen.getByRole('button', { name: 'Sıfırla' })).toBeDisabled();
  });

  it('shows that no scale is connected', async () => {
    setup({ devices: '[]' });
    renderWeighing();
    const live = await screen.findByRole('region', { name: 'Canlı ağırlık' });
    expect(await within(live).findByText('Bağlı terazi yok')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tartımı kaydet' })).toBeDisabled();
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it('lets the operator choose between several scales and remembers the choice', async () => {
    setup({ devices: `[${deviceJson({ value: '1.260' })},${deviceJson({ value: '5.00' }, {}, 'scale-2')}]` });
    const user = userEvent.setup();
    renderWeighing();
    await expectNet('1,260');

    await user.selectOptions(screen.getByLabelText('Terazi seçin'), 'scale-2');
    await expectNet('5,00');
    expect(FakeEventSource.latest().url).toBe('/api/devices/scale-2/live');
    expect(window.localStorage.getItem('vyxum.weighing.device')).toBe('scale-2');
  });

  it('hides the product column when no product catalog is loaded', async () => {
    setup({ settings: { productsAvailable: false } });
    renderWeighing();
    await liveNet();
    expect(screen.queryByRole('region', { name: 'Ürün seçimi' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Ürün, PLU ya da ad')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Kaydet ve etiket bas' })).not.toBeInTheDocument();
  });

  it('searches products through the API and offers no product at the top', async () => {
    const fetchMock = setup();
    const user = userEvent.setup();
    renderWeighing();
    const list = await screen.findByRole('region', { name: 'Ürün seçimi' });
    const buttons = await within(list).findAllByRole('button');
    expect(buttons[0]).toHaveTextContent('Ürünsüz');
    expect(buttons[0]).toHaveAttribute('aria-pressed', 'true');

    await user.type(screen.getByLabelText('Ürün, PLU ya da ad'), '041');
    await waitFor(() => {
      expect(fetchMock.mock.calls.map(([input]) => String(input))).toContain('/api/weighing/products?search=041');
    });
  });
  describe('target bar', () => {
    async function chooseCoffee(devices = `[${deviceJson({ value: '0.240' })}]`) {
      setup({ devices, products: [pistachio, coffee] });
      const user = userEvent.setup();
      renderWeighing();
      await liveNet();
      await user.click(await screen.findByRole('button', { name: /Türk kahvesi/ }));
      return screen.getByTestId('target');
    }

    it('shows the target, the remaining amount and the tolerance range of the selected product', async () => {
      const target = await chooseCoffee();
      expect(within(target).getByTestId('target-value')).toHaveTextContent('0,250 kg');
      expect(within(target).getByText('Kalan')).toBeInTheDocument();
      expect(within(target).getByTestId('target-difference')).toHaveTextContent('0,010 kg');
      expect(within(target).getByTestId('target-range')).toHaveTextContent('0,245 – 0,260 kg');
      expect(within(target).getByTestId('target-status')).toHaveTextContent('tolerans altında');

      const meter = within(target).getByRole('meter', { name: 'Hedef' });
      expect(meter).toHaveAttribute('aria-valuemin', '0');
      expect(meter).toHaveAttribute('aria-valuemax', '0.3');
      expect(meter).toHaveAttribute('aria-valuenow', '0.24');
      expect(meter).toHaveAttribute('aria-valuetext', '0,240 kg, tolerans altında');
      // 0.240 of a bar that ends at 0.300; the band runs from 0.245 to 0.260.
      expect(within(target).getByTestId('target-fill')).toHaveStyle({ width: '80%' });
      const band = within(target).getByTestId('target-band');
      expect(band.style.left).toBe('81.66%');
      expect(band.style.width).toBe('5%');
    });

    it('follows the live weight through the tolerance and past the target', async () => {
      const target = await chooseCoffee();
      const source = FakeEventSource.latest();

      source.emit(deviceJson({ value: '0.248', state: 'Motion' }));
      await expectNet('0,248');
      expect(within(target).getByTestId('target-status')).toHaveTextContent('tolerans içinde');
      expect(within(target).getByTestId('target-status')).toHaveClass('state--positive');
      expect(target).toHaveClass('target--within');
      expect(within(target).getByTestId('target-difference')).toHaveTextContent('0,002 kg');

      source.emit(deviceJson({ value: '0.256' }));
      await expectNet('0,256');
      expect(within(target).getByTestId('target-status')).toHaveTextContent('tolerans içinde');
      expect(within(target).getByText('Hedefi aşan')).toBeInTheDocument();
      expect(within(target).getByTestId('target-difference')).toHaveTextContent('0,006 kg');

      source.emit(deviceJson({ value: '0.400' }));
      await expectNet('0,400');
      expect(within(target).getByTestId('target-status')).toHaveTextContent('tolerans üstünde');
      expect(within(target).getByTestId('target-status')).toHaveClass('state--warning');
      expect(target).toHaveClass('target--over');
      expect(within(target).getByTestId('target-difference')).toHaveTextContent('0,150 kg');
      expect(within(target).getByTestId('target-fill')).toHaveStyle({ width: '100%' });
      expect(within(target).getByRole('meter')).toHaveAttribute('aria-valuenow', '0.3');
    });

    it('measures the net after the tare against the target', async () => {
      const target = await chooseCoffee(`[${deviceJson({ value: '0.012' })}]`);
      await userEvent.setup().click(screen.getByRole('button', { name: 'Dara al' }));
      FakeEventSource.latest().emit(deviceJson({ value: '0.262' }));
      await expectNet('0,250');
      expect(within(target).getByTestId('target-status')).toHaveTextContent('tolerans içinde');
      expect(within(target).getByText('Kalan')).toBeInTheDocument();
      expect(within(target).getByTestId('target-difference')).toHaveTextContent('0,000 kg');
    });

    it('converts the target to the unit of the scale', async () => {
      const target = await chooseCoffee(`[${deviceJson({ value: '251', unit: 'g' })}]`);
      expect(within(target).getByTestId('target-value')).toHaveTextContent('250 g');
      expect(within(target).getByTestId('target-range')).toHaveTextContent('245 – 260 g');
      expect(within(target).getByTestId('target-difference')).toHaveTextContent('1 g');
    });

    it('shows the target without a status while there is no reading', async () => {
      const target = await chooseCoffee(`[${deviceJson({ value: null, state: 'Overload' })}]`);
      expect(within(target).getByTestId('target-status')).toHaveTextContent('Teraziden henüz okuma gelmedi.');
      expect(within(target).getByTestId('target-difference')).toHaveTextContent('–');
      expect(within(target).getByTestId('target-fill')).toHaveStyle({ width: '0%' });
    });

    it('mutes the bar when the scale is not live', async () => {
      const target = await chooseCoffee();
      expect(target).not.toHaveClass('is-stale');
      FakeEventSource.latest().fail();
      await waitFor(() => expect(screen.getByTestId('target')).toHaveClass('is-stale'));
    });

    it('is not shown without a product, for a product without a target or for a unit it cannot convert', async () => {
      setup({ devices: `[${deviceJson({ value: '0.240' })}]`, products: [pistachio, coffee] });
      const user = userEvent.setup();
      renderWeighing();
      await liveNet();
      await screen.findByRole('button', { name: /Türk kahvesi/ });
      expect(screen.queryByTestId('target')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /Antep Fıstığı/ }));
      expect(screen.queryByTestId('target')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /Türk kahvesi/ }));
      expect(screen.getByTestId('target')).toBeInTheDocument();

      FakeEventSource.latest().emit(deviceJson({ value: '3', unit: 'pcs' }));
      await expectNet('3');
      expect(screen.queryByTestId('target')).not.toBeInTheDocument();
    });
  });
});
