import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App, AppProviders } from '../../App';
import type { SetupStatus } from '../../lib/setup';
import { callsTo, installFakeApi, jsonResponse, setupStatus, type FakeHandler } from '../../test/fakeApi';
import { restartTiming } from './restart';

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname}</output>;
}

function renderApp(path = '/') {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={[path]}>
        <App />
        <LocationProbe />
      </MemoryRouter>
    </AppProviders>,
  );
}

const location = () => screen.getByTestId('location').textContent;

function bodyOf(init: RequestInit | undefined): unknown {
  return JSON.parse(String(init?.body ?? 'null')) as unknown;
}

interface WizardApi {
  status?: Partial<SetupStatus>;
  capabilities?: string[];
  routes?: Record<string, FakeHandler>;
}

function installWizardApi({ status, capabilities = [], routes = {} }: WizardApi = {}) {
  return installFakeApi({
    capabilities,
    routes: {
      'GET /api/setup/status': () => jsonResponse(setupStatus({ completed: false, ...status })),
      'POST /api/setup/database': () => jsonResponse({ restartRequired: false }),
      'POST /api/setup/company': (_url, init) => jsonResponse({ name: (bodyOf(init) as { name: string }).name }),
      'POST /api/setup/complete': () => jsonResponse({ completed: true, completedAt: '2026-11-21T10:00:00Z' }),
      ...routes,
    },
  });
}

const next = () => screen.getByRole('button', { name: 'İleri' });

async function heading(name: string) {
  return screen.findByRole('heading', { level: 1, name });
}

beforeEach(() => {
  restartTiming.pollMs = 10;
  restartTiming.manualAfterMs = 20000;
});

afterEach(() => {
  restartTiming.pollMs = 1000;
  restartTiming.manualAfterMs = 20000;
});

describe('setup redirect', () => {
  it('sends every page to the wizard while setup is not complete', async () => {
    installWizardApi();
    renderApp('/records');

    expect(await heading('Kayıtlar nerede saklansın?')).toBeInTheDocument();
    expect(location()).toBe('/setup');
    // The wizard has its own frame without the menu.
    expect(screen.queryByRole('link', { name: 'Tartım' })).not.toBeInTheDocument();
    expect(screen.getByText('Adım 1 / 3')).toBeInTheDocument();
  });

  it('accepts the Turkish address of the wizard', async () => {
    installWizardApi();
    renderApp('/kurulum');

    expect(await heading('Kayıtlar nerede saklansın?')).toBeInTheDocument();
    expect(location()).toBe('/setup');
  });

  it('opens the requested page when setup is complete', async () => {
    installFakeApi();
    renderApp('/records');

    expect(await heading('Tartım kayıtları')).toBeInTheDocument();
    expect(location()).toBe('/records');
  });

  it('sends the wizard address to weighing when setup is complete', async () => {
    installFakeApi();
    renderApp('/kurulum');

    expect(await heading('Tartım')).toBeInTheDocument();
    expect(location()).toBe('/');
  });

  it('shows the company name in the menu', async () => {
    installFakeApi({ companyName: 'Örnek Gıda' });
    renderApp('/');

    const menu = await screen.findByRole('navigation');
    expect(within(menu).getByText('Örnek Gıda')).toBeInTheDocument();
  });
});

describe('step rail', () => {
  it('marks the current step with a word, not only a color, and switches language', async () => {
    installWizardApi();
    const user = userEvent.setup();
    renderApp('/setup');

    await heading('Kayıtlar nerede saklansın?');
    const rail = screen.getByRole('navigation', { name: 'Kurulum' });
    const items = within(rail).getAllByRole('listitem');
    expect(items.map((item) => item.textContent)).toEqual(['01Veritabanışimdi', '02Terazi', '03Firma']);
    expect(items[0]).toHaveAttribute('aria-current', 'step');

    await user.click(within(rail).getByRole('button', { name: 'English' }));
    expect(await heading('Where should records be stored?')).toBeInTheDocument();
  });

  it('lists the products step when the product catalog is loaded', async () => {
    installWizardApi({ capabilities: ['products.catalog'] });
    renderApp('/setup');

    await heading('Kayıtlar nerede saklansın?');
    const rail = screen.getByRole('navigation', { name: 'Kurulum' });
    expect(within(rail).getByText('Ürünler')).toBeInTheDocument();
    expect(screen.getByText('Adım 1 / 4')).toBeInTheDocument();
  });
});

describe('database step', () => {
  it('saves the recommended local database and goes on', async () => {
    const fetchMock = installWizardApi();
    const user = userEvent.setup();
    renderApp('/setup');

    await heading('Kayıtlar nerede saklansın?');
    expect(screen.getByRole('radio', { name: /Bu bilgisayarda sakla/ })).toBeChecked();
    await user.click(next());

    expect(await heading('Terazi')).toBeInTheDocument();
    expect(callsTo(fetchMock, 'POST /api/setup/database')).toBe(1);
    const call = fetchMock.mock.calls.find(([input]) => String(input) === '/api/setup/database');
    expect(bodyOf(call?.[1])).toEqual({ provider: 'Sqlite' });
    const rail = screen.getByRole('navigation', { name: 'Kurulum' });
    expect(within(rail).getAllByRole('listitem')[0]).toHaveTextContent('tamamlandı');
  });

  it('keeps Next disabled for SQL Server until the current connection string was tested', async () => {
    let ok = false;
    const fetchMock = installWizardApi({
      routes: {
        'POST /api/setup/database/test': () =>
          jsonResponse(ok ? { ok: true, code: 'setup.DbWillBeCreated', databaseExists: false } : { ok: false, code: 'setup.DbLoginFailed', databaseExists: null }),
        'POST /api/setup/database': () => jsonResponse({ restartRequired: false }),
      },
    });
    const user = userEvent.setup();
    renderApp('/setup');

    await heading('Kayıtlar nerede saklansın?');
    await user.click(screen.getByRole('button', { name: 'Gelişmiş: SQL Server' }));
    await user.click(screen.getByRole('radio', { name: /SQL Server veritabanına bağlan/ }));
    expect(next()).toBeDisabled();
    expect(screen.getByText('Devam etmeden önce bağlantıyı deneyin.')).toBeInTheDocument();

    const field = screen.getByLabelText('Bağlantı dizesi');
    await user.type(field, 'Server=db;Database=Vyxum');
    await user.click(screen.getByRole('button', { name: 'Bağlantıyı dene' }));
    expect(await screen.findByText('Giriş yapılamadı. Kullanıcı adını ve şifreyi kontrol edin.')).toBeInTheDocument();
    expect(next()).toBeDisabled();

    ok = true;
    await user.click(screen.getByRole('button', { name: 'Bağlantıyı dene' }));
    expect(await screen.findByText('Bağlantı kuruldu. Veritabanı henüz yok; oluşturulacak.')).toBeInTheDocument();
    expect(next()).toBeEnabled();

    // A changed connection string needs a new test.
    await user.type(field, 'X');
    expect(next()).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Bağlantıyı dene' }));
    await waitFor(() => expect(next()).toBeEnabled());

    await user.click(next());
    expect(await heading('Terazi')).toBeInTheDocument();
    const call = fetchMock.mock.calls.find(([input]) => String(input) === '/api/setup/database');
    expect(bodyOf(call?.[1])).toEqual({ provider: 'SqlServer', connectionString: 'Server=db;Database=VyxumX' });
  });

  it('shows the error key of a failed save under the connection string', async () => {
    installWizardApi({
      routes: {
        'POST /api/setup/database/test': () => jsonResponse({ ok: true, code: 'setup.DbConnectionOk', databaseExists: true }),
        'POST /api/setup/database': () => jsonResponse({ errors: { connectionString: ['setup.DbServerNotFound'] } }, 400),
      },
    });
    const user = userEvent.setup();
    renderApp('/setup');

    await heading('Kayıtlar nerede saklansın?');
    await user.click(screen.getByRole('button', { name: 'Gelişmiş: SQL Server' }));
    await user.click(screen.getByRole('radio', { name: /SQL Server/ }));
    await user.type(screen.getByLabelText('Bağlantı dizesi'), 'Server=db;Database=Vyxum');
    await user.click(screen.getByRole('button', { name: 'Bağlantıyı dene' }));
    await waitFor(() => expect(next()).toBeEnabled());
    await user.click(next());

    expect(await screen.findByText('Sunucuya ulaşılamadı. Sunucu adını ve ağ bağlantısını kontrol edin.')).toBeInTheDocument();
    expect(screen.getByLabelText('Bağlantı dizesi')).toHaveAttribute('aria-invalid', 'true');
  });

  it('waits for the restart by polling the status and then continues', async () => {
    let phase: 'running' | 'down' | 'back' = 'running';
    let statusCalls = 0;
    const fetchMock = installWizardApi({
      routes: {
        'GET /api/setup/status': () => {
          statusCalls += 1;
          if (phase === 'down') throw new TypeError('Failed to fetch');
          return jsonResponse(setupStatus({ completed: false, restartPending: phase === 'running' && statusCalls > 1 }));
        },
        'POST /api/setup/database': () => jsonResponse({ restartRequired: true }),
      },
    });
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const user = userEvent.setup();
    renderApp('/setup');

    await heading('Kayıtlar nerede saklansın?');
    phase = 'down';
    await user.click(next());

    expect(await heading('Vyxum yeniden başlatılıyor…')).toBeInTheDocument();
    expect(screen.getByText('Bu birkaç saniye sürer. Sayfa kendiliğinden yenilenir.')).toBeInTheDocument();
    await waitFor(() => expect(callsTo(fetchMock, 'GET /api/setup/status')).toBeGreaterThan(3));
    expect(screen.getByRole('heading', { level: 1, name: 'Vyxum yeniden başlatılıyor…' })).toBeInTheDocument();

    phase = 'back';
    expect(await heading('Terazi')).toBeInTheDocument();
    expect(window.sessionStorage.getItem('vyxum.setup.restarting')).toBeNull();
  });

  it('asks for a manual restart when the host keeps reporting a pending restart', async () => {
    restartTiming.manualAfterMs = 30;
    installWizardApi({
      routes: {
        'GET /api/setup/status': () => jsonResponse(setupStatus({ completed: false, restartPending: true })),
      },
    });
    renderApp('/setup');

    expect(await heading('Vyxum yeniden başlatılıyor…')).toBeInTheDocument();
    expect(await screen.findByText("Ayar kaydedildi. Geçerli olması için Vyxum'u yeniden başlatın.")).toBeInTheDocument();
  });

  it('shows a full-page message when setup is not done on this computer', async () => {
    installWizardApi({
      routes: {
        'POST /api/setup/database': () => jsonResponse({ title: 'Forbidden', code: 'setup.LocalOnly' }, 403),
      },
    });
    const user = userEvent.setup();
    renderApp('/setup');

    await heading('Kayıtlar nerede saklansın?');
    await user.click(next());

    expect(await screen.findByRole('alert')).toHaveTextContent("Kurulum yalnızca Vyxum'un kurulu olduğu bilgisayardan yapılabilir.");
    expect(screen.queryByRole('navigation', { name: 'Kurulum' })).not.toBeInTheDocument();
  });

  it('shows a network failure inline', async () => {
    installWizardApi({
      routes: {
        'POST /api/setup/database': () => {
          throw new TypeError('Failed to fetch');
        },
      },
    });
    const user = userEvent.setup();
    renderApp('/setup');

    await heading('Kayıtlar nerede saklansın?');
    await user.click(next());

    expect(await screen.findByRole('alert')).toHaveTextContent('Sunucuya ulaşılamadı.');
    expect(screen.getByRole('heading', { level: 1, name: 'Kayıtlar nerede saklansın?' })).toBeInTheDocument();
  });
});

describe('company and products steps', () => {
  it('shows the validation error of the company name from its server key', async () => {
    window.sessionStorage.setItem('vyxum.setup.step', 'company');
    installWizardApi({
      routes: {
        'POST /api/setup/company': () => jsonResponse({ errors: { name: ['setup.CompanyNameRequired'] } }, 400),
      },
    });
    const user = userEvent.setup();
    renderApp('/setup');

    expect(await heading('Firma bilgisi')).toBeInTheDocument();
    await user.click(next());

    expect(await screen.findByText('Firma adı gerekli.')).toBeInTheDocument();
    expect(screen.getByLabelText('Firma adı')).toHaveAttribute('aria-invalid', 'true');
  });

  it('goes from the company step to done when the product catalog is not loaded', async () => {
    window.sessionStorage.setItem('vyxum.setup.step', 'company');
    const fetchMock = installWizardApi();
    const user = userEvent.setup();
    renderApp('/setup');

    await heading('Firma bilgisi');
    await user.type(screen.getByLabelText('Firma adı'), 'Örnek Gıda');
    await user.click(next());

    expect(await heading('Kurulum tamamlandı')).toBeInTheDocument();
    expect(screen.queryByText('Ürünler')).not.toBeInTheDocument();
    const call = fetchMock.mock.calls.find(([input]) => String(input) === '/api/setup/company');
    expect(bodyOf(call?.[1])).toEqual({ name: 'Örnek Gıda' });
  });

  it('adds products with the product card and lists them', async () => {
    window.sessionStorage.setItem('vyxum.setup.step', 'products');
    const fetchMock = installWizardApi({
      status: { company: { name: 'Örnek Gıda' } },
      capabilities: ['products.catalog'],
      routes: {
        'POST /api/products': (_url, init) => {
          const input = bodyOf(init) as { code: string; name: string };
          return jsonResponse({
            id: `id-${input.code}`,
            code: input.code,
            name: input.name,
            unitPrice: null,
            packageTare: null,
            shelfLifeDays: null,
            barcodePrefix: null,
            ingredients: null,
            targetWeight: null,
            lowerTolerance: null,
            upperTolerance: null,
            isActive: true,
            updatedAt: '2026-11-21T10:00:00Z',
          });
        },
      },
    });
    const user = userEvent.setup();
    renderApp('/setup');

    expect(await heading('İlk ürünler')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Atla' })).toBeInTheDocument();

    await user.type(screen.getByLabelText('Ürün adı'), 'Beyaz peynir');
    await user.type(screen.getByLabelText('PLU'), '101');
    await user.click(screen.getByRole('button', { name: 'Kaydet' }));

    const table = await screen.findByRole('table');
    expect(within(table).getByText('Beyaz peynir')).toBeInTheDocument();
    // A fresh card is shown for the next product.
    expect(screen.getByLabelText('Ürün adı')).toHaveValue('');
    expect(callsTo(fetchMock, 'POST /api/products')).toBe(1);

    await user.click(next());
    expect(await heading('Kurulum tamamlandı')).toBeInTheDocument();
  });

  it('keeps the step after a reload but not past the company step without a company name', async () => {
    window.sessionStorage.setItem('vyxum.setup.step', 'done');
    installWizardApi();
    renderApp('/setup');

    expect(await heading('Firma bilgisi')).toBeInTheDocument();
  });
});

describe('done screen', () => {
  it('completes the setup and opens weighing', async () => {
    window.sessionStorage.setItem('vyxum.setup.step', 'done');
    let completed = false;
    const fetchMock = installWizardApi({
      status: { company: { name: 'Örnek Gıda' } },
      routes: {
        'GET /api/setup/status': () => jsonResponse(setupStatus({ completed, company: { name: 'Örnek Gıda' } })),
        'POST /api/setup/complete': () => {
          completed = true;
          return jsonResponse({ completed: true, completedAt: '2026-11-21T10:00:00Z' });
        },
        'GET /api/ui/modules': () =>
          jsonResponse({
            modules: [{ id: 'weighing', version: '1.0.0' }],
            capabilities: [],
            companyName: completed ? 'Örnek Gıda' : null,
          }),
      },
    });
    const user = userEvent.setup();
    renderApp('/setup');

    expect(await heading('Kurulum tamamlandı')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Tartıma geç' }));

    expect(await heading('Tartım')).toBeInTheDocument();
    expect(location()).toBe('/');
    expect(callsTo(fetchMock, 'POST /api/setup/complete')).toBe(1);
    const menu = screen.getByRole('navigation');
    expect(await within(menu).findByText('Örnek Gıda')).toBeInTheDocument();
    expect(window.sessionStorage.getItem('vyxum.setup.step')).toBeNull();
  });
});
