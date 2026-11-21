import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { App, AppProviders } from '../App';
import type { Product } from '../lib/products';
import { installFakeApi, jsonResponse, type FakeHandler } from '../test/fakeApi';

function product(overrides: Partial<Product> = {}): Product {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    code: '0417',
    name: 'Antep Fıstığı, kavrulmuş',
    unitPrice: '780.00',
    packageTare: '0.012',
    shelfLifeDays: 180,
    barcodePrefix: '29',
    ingredients: '*Antep fıstığı* (%98), tuz.',
    targetWeight: '0.250',
    lowerTolerance: '0.005',
    upperTolerance: null,
    isActive: true,
    updatedAt: '2026-11-21T10:00:00Z',
    ...overrides,
  };
}

const pistachio = product();
const apricot = product({
  id: '00000000-0000-0000-0000-000000000002',
  code: '0512',
  name: 'Kuru kayısı',
  unitPrice: '340.00',
  packageTare: '0.015',
  shelfLifeDays: 240,
});

function renderProducts() {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={['/products']}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

function bodyOf(init: RequestInit | undefined): Record<string, unknown> {
  return JSON.parse(String(init?.body)) as Record<string, unknown>;
}

function productRoutes(list: () => Product[] = () => [pistachio, apricot], extra: Record<string, FakeHandler> = {}) {
  return { 'GET /api/products': () => jsonResponse(list()), ...extra };
}

describe('products screen', () => {
  it('lists products with culture formatted values and the count in the eyebrow', async () => {
    installFakeApi({ routes: productRoutes() });
    renderProducts();

    expect(await screen.findByRole('heading', { level: 1, name: 'Ürün kartları' })).toBeInTheDocument();
    const row = (await screen.findByRole('button', { name: 'Antep Fıstığı, kavrulmuş' })).closest('tr');
    expect(row).not.toBeNull();
    const cells = within(row as HTMLElement).getAllByRole('cell').map((cell) => cell.textContent);
    expect(cells).toEqual(['0417', 'Antep Fıstığı, kavrulmuş', '780,00', '0,012', '180 gün']);
    expect(screen.getByText('Ürünler / 2 ürün')).toBeInTheDocument();
  });

  it('searches through the API after a pause and can include inactive products', async () => {
    const fetchMock = installFakeApi({ routes: productRoutes() });
    const user = userEvent.setup();
    renderProducts();
    await screen.findByRole('button', { name: 'Kuru kayısı' });

    await user.type(screen.getByRole('searchbox'), 'kay');
    await waitFor(() => {
      const urls = fetchMock.mock.calls.map(([input]) => String(input));
      expect(urls).toContain('/api/products?search=kay');
    });

    await user.click(screen.getByRole('checkbox', { name: 'Pasif ürünleri göster' }));
    await waitFor(() => {
      const urls = fetchMock.mock.calls.map(([input]) => String(input));
      expect(urls).toContain('/api/products?search=kay&includeInactive=true');
    });
  });

  it('shows the empty state', async () => {
    installFakeApi({ routes: productRoutes(() => []) });
    renderProducts();
    expect(await screen.findByText('Henüz ürün yok')).toBeInTheDocument();
  });

  it('shows an error with retry when the list cannot be loaded', async () => {
    let fail = true;
    installFakeApi({
      routes: { 'GET /api/products': () => (fail ? jsonResponse({ title: 'Down' }, 500) : jsonResponse([pistachio])) },
    });
    const user = userEvent.setup();
    renderProducts();

    expect(await screen.findByText('Sunucuya ulaşılamadı.')).toBeInTheDocument();
    fail = false;
    await user.click(screen.getByRole('button', { name: 'Yeniden dene' }));
    expect(await screen.findByRole('button', { name: 'Antep Fıstığı, kavrulmuş' })).toBeInTheDocument();
  });

  it('opens a product in the card with editable culture text and keeps it selected after saving', async () => {
    let current = pistachio;
    const fetchMock = installFakeApi({
      routes: productRoutes(() => [current], {
        [`PUT /api/products/${pistachio.id}`]: (_url, init) => {
          const body = bodyOf(init);
          current = { ...current, name: String(body.name), unitPrice: String(body.unitPrice) };
          return jsonResponse(current);
        },
      }),
    });
    const user = userEvent.setup();
    renderProducts();

    await user.click(await screen.findByRole('button', { name: 'Antep Fıstığı, kavrulmuş' }));
    expect(screen.getByLabelText('Birim fiyat (kg başına)')).toHaveValue('780,00');
    expect(screen.getByLabelText('Ambalaj darası (kg)')).toHaveValue('0,012');
    expect(screen.getByLabelText('Hedef ağırlık (kg)')).toHaveValue('0,250');
    expect(screen.getByLabelText('Alt tolerans (kg)')).toHaveValue('0,005');
    expect(screen.getByLabelText('Üst tolerans (kg)')).toHaveValue('');

    const name = screen.getByLabelText('Ürün adı');
    await user.clear(name);
    await user.type(name, 'Antep Fıstığı');
    const price = screen.getByLabelText('Birim fiyat (kg başına)');
    await user.clear(price);
    await user.type(price, '800,5');
    await user.click(screen.getByRole('button', { name: 'Kaydet' }));

    expect(await screen.findByText('Kaydedildi')).toBeInTheDocument();
    const put = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(bodyOf(put?.[1])).toMatchObject({ name: 'Antep Fıstığı', unitPrice: '800.5', packageTare: '0.012', shelfLifeDays: 180, targetWeight: '0.250', lowerTolerance: '0.005', upperTolerance: null });
    const row = (await screen.findByRole('button', { name: 'Antep Fıstığı' })).closest('tr');
    expect(row).toHaveClass('is-selected');
    expect(screen.getByLabelText('Birim fiyat (kg başına)')).toHaveValue('800.5'.replace('.', ','));
  });

  it('creates a product from Turkish comma input and sends invariant decimals', async () => {
    const created = product({ id: '00000000-0000-0000-0000-000000000009', code: '0999', name: 'Yeni', unitPrice: '12.50', packageTare: '0.012' });
    let list: Product[] = [];
    const fetchMock = installFakeApi({
      routes: productRoutes(() => list, {
        'POST /api/products': () => {
          list = [created];
          return jsonResponse(created, 201);
        },
      }),
    });
    const user = userEvent.setup();
    renderProducts();
    await screen.findByText('Henüz ürün yok');

    await user.click(screen.getByRole('button', { name: 'Ürün ekle' }));
    expect(screen.getByRole('heading', { level: 2, name: 'Yeni ürün' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Ürün adı'), 'Yeni');
    await user.type(screen.getByLabelText('PLU'), '0999');
    await user.type(screen.getByLabelText('Birim fiyat (kg başına)'), '12,5');
    await user.type(screen.getByLabelText('Ambalaj darası (kg)'), '0,012');
    await user.type(screen.getByLabelText('Raf ömrü (gün)'), '30');
    await user.type(screen.getByLabelText('Hedef ağırlık (kg)'), '0,25');
    await user.type(screen.getByLabelText('Alt tolerans (kg)'), '0,005');
    await user.type(screen.getByLabelText('Üst tolerans (kg)'), '0,01');
    await user.click(screen.getByRole('button', { name: 'Kaydet' }));

    expect(await screen.findByText('Kaydedildi')).toBeInTheDocument();
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST');
    expect(bodyOf(post?.[1])).toEqual({
      code: '0999',
      name: 'Yeni',
      unitPrice: '12.5',
      packageTare: '0.012',
      shelfLifeDays: 30,
      barcodePrefix: '',
      ingredients: '',
      targetWeight: '0.25',
      lowerTolerance: '0.005',
      upperTolerance: '0.01',
    });
    expect(await screen.findByText('Ürünler / 1 ürün')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ürünü pasifleştir' })).toBeInTheDocument();
  });

  it('shows validation errors next to their fields, translated from the returned keys', async () => {
    installFakeApi({
      routes: productRoutes(() => [], {
        'POST /api/products': () =>
          jsonResponse({
            title: 'Validation',
            errors: { code: ['products.CodeTaken'], name: ['products.NameRequired'], unitPrice: ['products.InvalidNumber'] },
          }, 400),
      }),
    });
    const user = userEvent.setup();
    renderProducts();
    await screen.findByText('Henüz ürün yok');
    await user.click(screen.getByRole('button', { name: 'Ürün ekle' }));
    await user.type(screen.getByLabelText('Birim fiyat (kg başına)'), 'abc');
    await user.click(screen.getByRole('button', { name: 'Kaydet' }));

    const code = await screen.findByLabelText('PLU');
    expect(code).toHaveAccessibleDescription('Bu PLU kodu başka bir üründe kullanılıyor.');
    expect(code).toBeInvalid();
    expect(screen.getByLabelText('Ürün adı')).toHaveAccessibleDescription('Ürün adı zorunludur.');
    expect(screen.getByLabelText('Birim fiyat (kg başına)')).toHaveAccessibleDescription('Geçerli bir sayı girin, örneğin 12.50.');
    expect(screen.queryByText('Kaydedildi')).not.toBeInTheDocument();
  });

  it('deactivates and activates a product from the card', async () => {
    let current = pistachio;
    const fetchMock = installFakeApi({
      routes: productRoutes(() => [current], {
        [`POST /api/products/${pistachio.id}/deactivate`]: () => {
          current = { ...current, isActive: false };
          return jsonResponse(current);
        },
        [`POST /api/products/${pistachio.id}/activate`]: () => {
          current = { ...current, isActive: true };
          return jsonResponse(current);
        },
      }),
    });
    const user = userEvent.setup();
    renderProducts();

    await user.click(await screen.findByRole('button', { name: 'Antep Fıstığı, kavrulmuş' }));
    await user.click(screen.getByRole('button', { name: 'Ürünü pasifleştir' }));

    const activate = await screen.findByRole('button', { name: 'Ürünü etkinleştir' });
    expect(screen.getAllByText('Pasif').length).toBeGreaterThan(0);
    expect(fetchMock.mock.calls.some(([input, init]) => String(input).endsWith('/deactivate') && init?.method === 'POST')).toBe(true);

    await user.click(activate);
    expect(await screen.findByRole('button', { name: 'Ürünü pasifleştir' })).toBeInTheDocument();
    expect(screen.queryByText('Pasif')).not.toBeInTheDocument();
  });
});
