import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { App, AppProviders } from '../App';
import { installFakeApi, jsonResponse, type FakeHandler } from '../test/fakeApi';
import {
  dayRangeUtc,
  exportFallbackName,
  localDateValue,
  type RecordList,
  type WeighingProduct,
  type WeighingRecord,
  type WeighingSettings,
} from '../lib/records';

function record(overrides: Partial<WeighingRecord> = {}): WeighingRecord {
  return {
    id: 142,
    number: '000142',
    createdAt: new Date(2026, 10, 21, 17, 42).toISOString(),
    deviceId: 'scale-1',
    productId: '00000000-0000-0000-0000-000000000417',
    productCode: '0417',
    productName: 'Antep Fıstığı, kavrulmuş',
    lot: 'L-261121-03',
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

const saved = record();
const cancelled = record({
  id: 137,
  number: '000137',
  productCode: '0203',
  productName: 'Kaju, çiğ',
  net: '0.512',
  amount: '327.68',
  status: 'Cancelled',
  cancelReason: 'Yanlış ürün',
});

const products: WeighingProduct[] = [
  { id: '00000000-0000-0000-0000-000000000417', code: '0417', name: 'Antep Fıstığı, kavrulmuş' },
  { id: '00000000-0000-0000-0000-000000000203', code: '0203', name: 'Kaju, çiğ' },
];

function list(items: WeighingRecord[], overrides: Partial<RecordList['totals']> = {}): RecordList {
  const valid = items.filter((r) => r.status === 'Saved');
  const net = valid.reduce((sum, r) => sum + Number(r.net), 0);
  const amount = valid.reduce((sum, r) => sum + Number(r.amount), 0);
  return {
    items,
    totals: {
      validCount: valid.length,
      cancelledCount: items.length - valid.length,
      net: valid.length > 0 ? [{ unit: 'kg', value: net.toFixed(3) }] : [],
      amount: valid.length > 0 ? amount.toFixed(2) : null,
      ...overrides,
    },
  };
}

function settings(overrides: Partial<WeighingSettings> = {}): WeighingSettings {
  return { requireCancelReason: true, currency: 'TRY', showOperator: false, ...overrides };
}

function recordRoutes(options: {
  records?: () => RecordList | Response;
  settings?: WeighingSettings;
  extra?: Record<string, FakeHandler>;
}) {
  return {
    'GET /api/weighing/records': () => toResponse(options.records?.() ?? list([saved, cancelled])),
    'GET /api/weighing/products': () => jsonResponse(products),
    'GET /api/weighing/settings': () => jsonResponse(options.settings ?? settings()),
    ...options.extra,
  };
}

function toResponse(value: RecordList | Response): Response {
  return value instanceof Response ? value : jsonResponse(value);
}

function renderRecords() {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={['/records']}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

function requestUrls(fetchMock: ReturnType<typeof installFakeApi>, path: string): URL[] {
  return fetchMock.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((url) => url.pathname === path);
}

async function rowOf(text: string): Promise<HTMLElement> {
  const row = (await screen.findByText(text)).closest('tr');
  expect(row).not.toBeNull();
  return row as HTMLElement;
}

describe('records screen', () => {
  it('renders rows with culture formatting, the summary and the totals', async () => {
    installFakeApi({ routes: recordRoutes({ records: () => toResponse(list([saved, cancelled])) }) });
    renderRecords();

    expect(await screen.findByRole('heading', { level: 1, name: 'Tartım kayıtları' })).toBeInTheDocument();
    const row = await rowOf('000142');
    const cells = within(row)
      .getAllByRole('cell')
      .map((cell) => cell.textContent);
    expect(cells.slice(0, 6)).toEqual(['17:42', '000142', '0417', 'Antep Fıstığı, kavrulmuş', 'L-261121-03', '1,248 kg']);
    expect(cells[6]).toMatch(/973,44/);
    expect(cells[6]).toContain('₺');
    expect(cells[7]).toBe('kaydedildi');

    expect(screen.getByText('1 geçerli kayıt, 1 iptal')).toBeInTheDocument();
    expect(screen.getByText('iptaller toplama girmez')).toBeInTheDocument();
    const footer = within(screen.getAllByRole('row').at(-1) as HTMLElement);
    expect(footer.getByText('1,248 kg')).toBeInTheDocument();
    expect(footer.getByText(/973,44/)).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Operatör' })).not.toBeInTheDocument();
  });

  it('shows the eyebrow with the selected date and weekday', async () => {
    installFakeApi({ routes: recordRoutes({}) });
    renderRecords();
    await rowOf('000142');
    fireEvent.change(screen.getByLabelText('Tarih'), { target: { value: '2026-11-21' } });
    expect(await screen.findByText('Kayıtlar / 21.11.2026 Cumartesi')).toBeInTheDocument();
  });

  it('shows the operator column only when the setting says so', async () => {
    installFakeApi({
      routes: recordRoutes({
        settings: settings({ showOperator: true }),
        records: () => toResponse(list([record({ operator: 'Elif Kaya' })])),
      }),
    });
    renderRecords();
    expect(await screen.findByRole('columnheader', { name: 'Operatör' })).toBeInTheDocument();
    expect(screen.getByText('Elif Kaya')).toBeInTheDocument();
  });

  it('strikes cancelled rows through, shows the status word and offers no cancel action', async () => {
    installFakeApi({ routes: recordRoutes({}) });
    renderRecords();

    const row = await rowOf('000137');
    expect(row).toHaveClass('records__row--cancelled');
    expect(within(row).getByText('iptal')).toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: 'Kaydı iptal et' })).not.toBeInTheDocument();
    const savedRow = await rowOf('000142');
    expect(savedRow).not.toHaveClass('records__row--cancelled');
    expect(within(savedRow).getByRole('button', { name: 'Kaydı iptal et' })).toBeInTheDocument();
  });

  it('cancels a record with the reason and refreshes the list and the totals', async () => {
    let cancelledNow = false;
    let body: unknown;
    const afterCancel = record({ status: 'Cancelled', cancelReason: 'Yanlış tartım' });
    const fetchMock = installFakeApi({
      routes: recordRoutes({
        records: () => toResponse(cancelledNow ? list([afterCancel, cancelled]) : list([saved, cancelled])),
        extra: {
          'POST /api/weighing/records/142/cancel': (_url, init) => {
            body = JSON.parse(String(init?.body));
            cancelledNow = true;
            return jsonResponse(afterCancel);
          },
        },
      }),
    });
    const user = userEvent.setup();
    renderRecords();

    await user.click(within(await rowOf('000142')).getByRole('button', { name: 'Kaydı iptal et' }));
    const dialog = screen.getByRole('dialog', { name: 'Kaydı iptal et' });
    expect(within(dialog).getByText(/000142 numaralı kayıt iptal edilsin mi\?/)).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText('İptal nedeni'), 'Yanlış tartım');
    await user.click(within(dialog).getByRole('button', { name: 'Kaydı iptal et' }));

    await waitFor(() => expect(screen.getByText('0 geçerli kayıt, 2 iptal')).toBeInTheDocument());
    expect(body).toEqual({ reason: 'Yanlış tartım' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(await rowOf('000142')).toHaveClass('records__row--cancelled');
    expect(requestUrls(fetchMock, '/api/weighing/records')).toHaveLength(2);
  });

  it('requires a reason when the setting says so and does not call the API', async () => {
    const fetchMock = installFakeApi({ routes: recordRoutes({}) });
    const user = userEvent.setup();
    renderRecords();

    await user.click(within(await rowOf('000142')).getByRole('button', { name: 'Kaydı iptal et' }));
    const dialog = screen.getByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Kaydı iptal et' }));

    expect(within(dialog).getByRole('alert')).toHaveTextContent('İptal nedeni zorunludur.');
    expect(fetchMock.mock.calls.some(([, init]) => (init?.method ?? 'GET') === 'POST')).toBe(false);
  });

  it('cancels without a reason when none is required', async () => {
    let body: unknown;
    installFakeApi({
      routes: recordRoutes({
        settings: settings({ requireCancelReason: false }),
        extra: {
          'POST /api/weighing/records/142/cancel': (_url, init) => {
            body = JSON.parse(String(init?.body));
            return jsonResponse(saved);
          },
        },
      }),
    });
    const user = userEvent.setup();
    renderRecords();

    await user.click(within(await rowOf('000142')).getByRole('button', { name: 'Kaydı iptal et' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Kaydı iptal et' }));
    await waitFor(() => expect(body).toEqual({ reason: '' }));
  });

  it('shows the translated server errors of a cancel', async () => {
    installFakeApi({
      routes: recordRoutes({
        settings: settings({ requireCancelReason: false }),
        extra: {
          'POST /api/weighing/records/142/cancel': () => jsonResponse({ errors: { reason: ['weighing.CancelReasonRequired'] } }, 400),
        },
      }),
    });
    const user = userEvent.setup();
    renderRecords();

    await user.click(within(await rowOf('000142')).getByRole('button', { name: 'Kaydı iptal et' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Kaydı iptal et' }));
    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('İptal nedeni zorunludur.');
  });

  it('shows the conflict message and reloads when the record is already cancelled', async () => {
    const fetchMock = installFakeApi({
      routes: recordRoutes({
        extra: { 'POST /api/weighing/records/142/cancel': () => jsonResponse({ code: 'weighing.AlreadyCancelled' }, 409) },
      }),
    });
    const user = userEvent.setup();
    renderRecords();

    await user.click(within(await rowOf('000142')).getByRole('button', { name: 'Kaydı iptal et' }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText('İptal nedeni'), 'x');
    await user.click(within(dialog).getByRole('button', { name: 'Kaydı iptal et' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Bu kayıt zaten iptal edilmiş.');
    await waitFor(() => expect(requestUrls(fetchMock, '/api/weighing/records')).toHaveLength(2));
  });

  it('sends the selected local day as a UTC range together with product and lot', async () => {
    const fetchMock = installFakeApi({ routes: recordRoutes({}) });
    const user = userEvent.setup();
    renderRecords();
    await rowOf('000142');

    // The first request is today.
    const today = dayRangeUtc(localDateValue());
    const first = requestUrls(fetchMock, '/api/weighing/records')[0] as URL;
    expect(first.searchParams.get('from')).toBe(today.from);
    expect(first.searchParams.get('to')).toBe(today.to);
    expect(first.searchParams.get('includeCancelled')).toBe('true');
    expect(first.searchParams.get('skip')).toBe('0');
    expect(first.searchParams.get('take')).toBe('100');

    fireEvent.change(screen.getByLabelText('Tarih'), { target: { value: '2026-11-20' } });
    await user.selectOptions(screen.getByLabelText('Ürün'), '0203 Kaju, çiğ');
    await user.type(screen.getByLabelText('Parti'), 'L-261120-07');

    await waitFor(() => {
      const last = requestUrls(fetchMock, '/api/weighing/records').at(-1) as URL;
      expect(last.searchParams.get('lot')).toBe('L-261120-07');
    });
    const last = requestUrls(fetchMock, '/api/weighing/records').at(-1) as URL;
    const expected = {
      from: new Date(2026, 10, 20).toISOString(),
      to: new Date(2026, 10, 21).toISOString(),
    };
    expect(last.searchParams.get('from')).toBe(expected.from);
    expect(last.searchParams.get('to')).toBe(expected.to);
    expect(last.searchParams.get('productId')).toBe('00000000-0000-0000-0000-000000000203');
  });

  it('loads the next page with skip and take when there are more records', async () => {
    const first = Array.from({ length: 100 }, (_, i) => record({ id: 1000 - i, number: String(1000 - i).padStart(6, '0') }));
    const second = [record({ id: 900, number: '000900' })];
    const fetchMock = installFakeApi({
      routes: recordRoutes({
        records: () => toResponse({ ...list(first), totals: { ...list(first).totals, validCount: 101 } }),
        extra: {
          'GET /api/weighing/records': (url) => {
            const skip = Number(url.searchParams.get('skip'));
            const items = skip === 0 ? first : second;
            return jsonResponse({ items, totals: { ...list(first).totals, validCount: 101 } });
          },
        },
      }),
    });
    const user = userEvent.setup();
    renderRecords();

    await rowOf('001000');
    await user.click(screen.getByRole('button', { name: 'Daha fazla yükle' }));
    await rowOf('000900');
    const urls = requestUrls(fetchMock, '/api/weighing/records');
    expect(urls.at(-1)?.searchParams.get('skip')).toBe('100');
    expect(urls.at(-1)?.searchParams.get('take')).toBe('100');
    expect(screen.queryByRole('button', { name: 'Daha fazla yükle' })).not.toBeInTheDocument();
  });

  it('shows the empty message', async () => {
    installFakeApi({ routes: recordRoutes({ records: () => toResponse(list([])) }) });
    renderRecords();
    expect(await screen.findByText('Kayıt bulunamadı.')).toBeInTheDocument();
    expect(screen.getByText('0 geçerli kayıt, 0 iptal')).toBeInTheDocument();
  });

  it('shows an error with a retry that loads again', async () => {
    let fail = true;
    installFakeApi({
      routes: recordRoutes({ records: () => (fail ? jsonResponse({ title: 'boom' }, 500) : toResponse(list([saved]))) }),
    });
    const user = userEvent.setup();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderRecords();

    expect(await screen.findByRole('alert')).toHaveTextContent('Sunucuya ulaşılamadı.');
    fail = false;
    await user.click(screen.getByRole('button', { name: 'Yeniden dene' }));
    await rowOf('000142');
  });

  it('exports with the same filters and downloads the file under the server name', async () => {
    const createObjectURL = vi.fn(() => 'blob:records');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL }));
    const clicked: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(this.download);
    });
    const fetchMock = installFakeApi({
      routes: recordRoutes({
        extra: {
          'GET /api/weighing/records/export': () =>
            new Response(new Uint8Array([80, 75]), {
              headers: {
                'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                'Content-Disposition': 'attachment; filename="tartim-kayitlari-20261120.xlsx"',
              },
            }),
        },
      }),
    });
    const user = userEvent.setup();
    renderRecords();
    await rowOf('000142');
    fireEvent.change(screen.getByLabelText('Tarih'), { target: { value: '2026-11-20' } });
    await user.type(screen.getByLabelText('Parti'), 'L-1');
    await waitFor(() => expect(requestUrls(fetchMock, '/api/weighing/records').at(-1)?.searchParams.get('lot')).toBe('L-1'));

    await user.click(screen.getByRole('button', { name: "Excel'e aktar" }));
    await waitFor(() => expect(clicked).toEqual(['tartim-kayitlari-20261120.xlsx']));

    const exportUrl = requestUrls(fetchMock, '/api/weighing/records/export')[0] as URL;
    expect(exportUrl.searchParams.get('from')).toBe(new Date(2026, 10, 20).toISOString());
    expect(exportUrl.searchParams.get('to')).toBe(new Date(2026, 10, 21).toISOString());
    expect(exportUrl.searchParams.get('lot')).toBe('L-1');
    expect(exportUrl.searchParams.has('skip')).toBe(false);
    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:records');
  });

  it('falls back to a name built from the date when the server sends none', async () => {
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: () => 'blob:x', revokeObjectURL: () => undefined }));
    const clicked: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(this.download);
    });
    installFakeApi({
      routes: recordRoutes({ extra: { 'GET /api/weighing/records/export': () => new Response(new Uint8Array([80, 75])) } }),
    });
    const user = userEvent.setup();
    renderRecords();
    await rowOf('000142');
    fireEvent.change(screen.getByLabelText('Tarih'), { target: { value: '2026-11-20' } });
    await user.click(screen.getByRole('button', { name: "Excel'e aktar" }));
    await waitFor(() => expect(clicked).toEqual(['weighing-records-20261120.xlsx']));
    expect(exportFallbackName('2026-11-20')).toBe('weighing-records-20261120.xlsx');
  });
});

describe('record date helpers', () => {
  it('turns a local day into [local midnight, next local midnight) in UTC', () => {
    const range = dayRangeUtc('2026-11-21');
    expect(range.from).toBe(new Date(2026, 10, 21, 0, 0, 0, 0).toISOString());
    expect(range.to).toBe(new Date(2026, 10, 22, 0, 0, 0, 0).toISOString());
    // Month and year boundaries.
    expect(dayRangeUtc('2026-12-31').to).toBe(new Date(2027, 0, 1).toISOString());
  });
});
