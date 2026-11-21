import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';
import { App, AppProviders } from '../../App';
import { licenseErrorCodes, type LicenseState } from '../../lib/license';
import type { DeviceConfig } from '../../lib/devices';
import {
  callsTo,
  installFakeApi,
  jsonResponse,
  licenseState,
  noLicenseState,
  sampleStrings,
  type FakeHandler,
} from '../../test/fakeApi';
import { FakeEventSource } from '../../test/fakeEventSource';

function renderApp(path = '/settings/license') {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

const device = (id: string, name: string, isEnabled = true): DeviceConfig => ({
  id,
  name,
  agentName: null,
  transport: { type: 'serial', portName: 'COM3' },
  protocol: { type: 'mt-sics' },
  capacity: null,
  division: null,
  unit: 'kg',
  isEnabled,
  createdAt: '2026-11-21T10:00:00Z',
  updatedAt: '2026-11-21T10:00:00Z',
});

const devices = [device('scale-1', 'Peynir terazisi'), device('scale-2', 'Ağ terazisi'), device('scale-3', 'Kantar terazisi')];

interface PageApi {
  license?: LicenseState;
  routes?: Record<string, FakeHandler>;
}

function installPageApi({ license = licenseState(), routes = {} }: PageApi = {}) {
  return installFakeApi({
    modules: ['weighing', 'products'],
    moduleList: [{ id: 'labels', version: '1.0.0', requiresLicense: true, license: 'licensed', name: 'Etiket yönetimi', provides: ['labels.print'], requires: ['weighing.records'], optional: ['products.catalog'] }],
    license: { status: license.status, licensee: license.licensee?.name ?? null, graceDaysLeft: license.graceDaysLeft },
    routes: {
      'GET /api/license': () => jsonResponse(license),
      'GET /api/devices/config': () => jsonResponse(devices),
      ...routes,
    },
  });
}

function bodyOf(init: RequestInit | undefined): unknown {
  return JSON.parse(String(init?.body ?? 'null')) as unknown;
}

const licenseFile = '{"format":"vxlic/1","keyId":"vx-2026-1","payload":"e30=","signature":"AAAA"}';
const upload = (user: ReturnType<typeof userEvent.setup>) =>
  user.upload(screen.getByLabelText('Lisans dosyası'), new File([licenseFile], 'ornek.vxlic'));

beforeEach(() => {
  FakeEventSource.install();
});

describe('settings > modules and license', () => {
  it('is reached from the Settings group of the menu', async () => {
    installPageApi();
    const user = userEvent.setup();
    renderApp('/records');

    const menu = await screen.findByRole('navigation');
    await user.click(within(menu).getByRole('link', { name: 'Modüller ve lisans' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Modüller ve lisans' })).toBeInTheDocument();
  });

  it('shows the values of a valid license', async () => {
    installPageApi();
    renderApp();

    const section = await screen.findByRole('region', { name: 'Lisans' });
    expect(within(section).getByText('imza doğrulandı')).toHaveClass('state--positive');
    const row = (label: string) => within(section).getByText(label).closest('div') as HTMLElement;
    expect(within(row('Lisans sahibi')).getByText('Örnek Kuruyemiş A.Ş.')).toBeInTheDocument();
    expect(within(row('Lisans türü')).getByText('abonelik')).toBeInTheDocument();
    expect(within(row('Geçerlilik')).getByText('31.10.2027')).toBeInTheDocument();
    expect(within(row('İstasyon')).getByText('2 / 3')).toBeInTheDocument();
    // Labels is installed and licensed; formulation is licensed but not installed here.
    expect(within(row('Açılan modül')).getByText('1')).toBeInTheDocument();
    expect(within(row('Lisans no')).getByText('LIC-2026-0001')).toBeInTheDocument();
  });

  it('shows the installation block from the setup status', async () => {
    installPageApi();
    renderApp();

    const section = await screen.findByRole('region', { name: 'Bu kurulum' });
    expect(within(section).getByText('0.1.0')).toBeInTheDocument();
    expect(within(section).getByText('Bu bilgisayar (SQLite)')).toBeInTheDocument();
  });

  it('lists installed and licensed modules with kind and status chips', async () => {
    installPageApi({
      license: licenseState({
        modules: [
          { id: 'labels', installed: true, licensed: true, license: 'licensed' },
          { id: 'formulation', installed: false, licensed: true, license: 'locked' },
        ],
      }),
    });
    renderApp();

    const table = await screen.findByRole('table');
    const rows = within(table).getAllByRole('row').slice(1);
    const text = rows.map((r) => within(r).getAllByRole('cell').map((c) => c.textContent));
    expect(text).toEqual([
      ['Tartım', 'açık kaynak', '', '', 'açık'],
      ['Ürünler', 'açık kaynak', '', '', 'açık'],
      ['Etiket yönetimi', 'ücretli', 'labels.print', 'weighing.records; isteğe bağlı: products.catalog', 'açık'],
      ['formulation', 'ücretli', '', '', 'kurulu değil'],
    ]);
  });

  it('shows a locked paid module as "lisans yok"', async () => {
    installFakeApi({
      moduleList: [{ id: 'labels', version: '1.0.0', requiresLicense: true, license: 'locked' }],
      routes: { 'GET /api/license': () => jsonResponse(noLicenseState()) },
    });
    renderApp();

    const table = await screen.findByRole('table');
    const row = within(table).getByText('labels').closest('tr') as HTMLElement;
    expect(within(row).getByText('lisans yok')).toBeInTheDocument();
    expect(within(row).getByText('ücretli')).toBeInTheDocument();
  });

  it('shows the grace period and read-only states with words', async () => {
    installPageApi({ license: licenseState({ status: 'grace', graceDaysLeft: 12 }) });
    const { unmount } = renderApp();
    expect(await screen.findByText('süresi doldu, ek süre 12 gün')).toHaveClass('state--warning');
    unmount();

    installPageApi({ license: licenseState({ status: 'readOnly' }) });
    renderApp();
    expect(await screen.findByText('süresi doldu, salt okunur')).toBeInTheDocument();
  });

  it('shows why a stored file is invalid and the clock warning', async () => {
    installPageApi({
      license: licenseState({
        status: 'invalid',
        valid: false,
        error: 'invalid_signature',
        licensee: null,
        type: null,
        stationLimit: 0,
        stations: [],
        clockBehind: true,
        modules: [],
      }),
    });
    renderApp();

    expect(await screen.findByText(/^geçersiz: İmza tutmuyor/)).toBeInTheDocument();
    expect(screen.getByText(/saati, Vyxum'un daha önce gördüğü/)).toBeInTheDocument();
  });

  it('says so when there is no license and offers the upload but not the removal', async () => {
    installPageApi({ license: noLicenseState() });
    renderApp();

    expect(await screen.findByText('Lisans yüklenmedi. Tartım ve Ürünler lisanssız çalışır.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lisans dosyası yükle' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Lisansı kaldır' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'İstasyonlar' })).not.toBeInTheDocument();
  });
});

describe('license upload', () => {
  it('sends the file text, shows the result and reloads the module list', async () => {
    let received: RequestInit | undefined;
    const fetchMock = installPageApi({
      license: noLicenseState(),
      routes: {
        'POST /api/license': (_url, init) => {
          received = init;
          return jsonResponse(licenseState());
        },
      },
    });
    const user = userEvent.setup();
    renderApp();

    await screen.findByText('Lisans yüklenmedi. Tartım ve Ürünler lisanssız çalışır.');
    const modulesBefore = callsTo(fetchMock, 'GET /api/ui/modules');
    await upload(user);

    expect(await screen.findByText('Lisans yüklendi: Örnek Kuruyemiş A.Ş.')).toBeInTheDocument();
    expect(received?.body).toBe(licenseFile);
    expect(callsTo(fetchMock, 'GET /api/ui/modules')).toBe(modulesBefore + 1);
    expect(await screen.findByText('imza doğrulandı')).toBeInTheDocument();
    // The screen stayed in place while the list reloaded.
    expect(screen.getByRole('heading', { level: 1, name: 'Modüller ve lisans' })).toBeInTheDocument();
  });

  it.each(licenseErrorCodes)('shows the message of the error code %s', async (code) => {
    installPageApi({
      license: noLicenseState(),
      routes: { 'POST /api/license': () => jsonResponse({ title: 'Bad Request', status: 400, code }, 400) },
    });
    const user = userEvent.setup();
    renderApp();

    await screen.findByText('Lisans yüklenmedi. Tartım ve Ürünler lisanssız çalışır.');
    await upload(user);

    const message = sampleStrings.tr[`license.Error.${code}`];
    expect(message).toBeTruthy();
    expect(await screen.findByRole('alert')).toHaveTextContent(message!);
  });

  it('shows the server message when the server cannot be reached', async () => {
    installPageApi({
      license: noLicenseState(),
      routes: { 'POST /api/license': () => Promise.reject(new TypeError('Failed to fetch')) },
    });
    const user = userEvent.setup();
    renderApp();

    await screen.findByText('Lisans yüklenmedi. Tartım ve Ürünler lisanssız çalışır.');
    await upload(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('Sunucuya ulaşılamadı.');
  });
});

describe('license removal', () => {
  it('asks first and does nothing when cancelled', async () => {
    const fetchMock = installPageApi();
    const user = userEvent.setup();
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Lisansı kaldır' }));
    const dialog = screen.getByRole('dialog', { name: 'Lisansı kaldır' });
    expect(within(dialog).getByText(/Lisans kaldırılsın mı\?/)).toBeInTheDocument();
    expect(callsTo(fetchMock, 'DELETE /api/license')).toBe(0);

    await user.click(within(dialog).getByRole('button', { name: 'Vazgeç' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(callsTo(fetchMock, 'DELETE /api/license')).toBe(0);
  });

  it('removes the license after the confirmation and reloads the module list', async () => {
    let removed = false;
    const fetchMock = installPageApi({
      routes: {
        'GET /api/license': () => jsonResponse(removed ? noLicenseState() : licenseState()),
        'DELETE /api/license': () => {
          removed = true;
          return new Response(null, { status: 204 });
        },
      },
    });
    const user = userEvent.setup();
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Lisansı kaldır' }));
    const modulesBefore = callsTo(fetchMock, 'GET /api/ui/modules');
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Kaldır' }));

    expect(await screen.findByText('Lisans kaldırıldı.')).toBeInTheDocument();
    expect(callsTo(fetchMock, 'DELETE /api/license')).toBe(1);
    expect(callsTo(fetchMock, 'GET /api/ui/modules')).toBe(modulesBefore + 1);
    expect(screen.getByText('Lisans yüklenmedi. Tartım ve Ürünler lisanssız çalışır.')).toBeInTheDocument();
  });
});

describe('station choice', () => {
  it('lists the devices with the current choice and the limit', async () => {
    installPageApi();
    renderApp();

    const section = await screen.findByRole('region', { name: 'İstasyonlar' });
    const list = await within(section).findByRole('list', { name: 'Cihazlar' });
    expect(within(list).getByRole('checkbox', { name: 'Peynir terazisi' })).toBeChecked();
    expect(within(list).getByRole('checkbox', { name: 'Ağ terazisi' })).toBeChecked();
    expect(within(list).getByRole('checkbox', { name: 'Kantar terazisi' })).not.toBeChecked();
    expect(within(section).getByLabelText('İstasyon')).toHaveTextContent('2 / 3');
  });

  it('saves the choice and shows the new count', async () => {
    let received: unknown;
    installPageApi({
      routes: {
        'PUT /api/license/stations': (_url, init) => {
          received = bodyOf(init);
          return jsonResponse(licenseState({ stations: ['scale-1', 'scale-3'] }));
        },
      },
    });
    const user = userEvent.setup();
    renderApp();

    const section = await screen.findByRole('region', { name: 'İstasyonlar' });
    await user.click(await within(section).findByRole('checkbox', { name: 'Ağ terazisi' }));
    await user.click(within(section).getByRole('checkbox', { name: 'Kantar terazisi' }));
    await user.click(within(section).getByRole('button', { name: 'İstasyonları kaydet' }));

    expect(await within(section).findByText('İstasyonlar kaydedildi.')).toBeInTheDocument();
    expect(received).toEqual({ deviceIds: ['scale-1', 'scale-3'] });
    expect(within(await screen.findByRole('region', { name: 'Lisans' })).getByText('2 / 3')).toBeInTheDocument();
  });

  it('disables the unchecked devices once the limit is reached and enables them again', async () => {
    installPageApi({ license: licenseState({ stationLimit: 2 }) });
    const user = userEvent.setup();
    renderApp();

    const section = await screen.findByRole('region', { name: 'İstasyonlar' });
    const third = await within(section).findByRole('checkbox', { name: 'Kantar terazisi' });
    expect(third).toBeDisabled();
    expect(within(section).getByRole('checkbox', { name: 'Peynir terazisi' })).toBeEnabled();

    await user.click(within(section).getByRole('checkbox', { name: 'Ağ terazisi' }));
    expect(third).toBeEnabled();
  });

  it('shows the limit error when the server rejects the choice', async () => {
    // The page's license state is stale: the server already knows a lower limit.
    installPageApi({
      license: licenseState({ stationLimit: 3 }),
      routes: {
        'PUT /api/license/stations': () => jsonResponse({ status: 400, code: 'station_limit_exceeded' }, 400),
      },
    });
    const user = userEvent.setup();
    renderApp();

    const section = await screen.findByRole('region', { name: 'İstasyonlar' });
    await user.click(await within(section).findByRole('checkbox', { name: 'Kantar terazisi' }));
    expect(within(section).getByLabelText('İstasyon')).toHaveTextContent('3 / 3');
    await user.click(within(section).getByRole('button', { name: 'İstasyonları kaydet' }));

    expect(await within(section).findByRole('alert')).toHaveTextContent(sampleStrings.tr['license.Error.station_limit_exceeded']!);
    await waitFor(() => expect(within(section).queryByText('İstasyonlar kaydedildi.')).not.toBeInTheDocument());
  });
});
