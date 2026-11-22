import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';
import { App, AppProviders } from '../../App';
import type { DeviceConfig } from '../../lib/devices';
import { callsTo, installFakeApi, jsonResponse, setupStatus, type FakeHandler } from '../../test/fakeApi';
import { FakeEventSource, liveDeviceJson } from '../../test/fakeEventSource';

function renderApp(path: string) {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

function bodyOf(init: RequestInit | undefined): Record<string, unknown> {
  return JSON.parse(String(init?.body ?? 'null')) as Record<string, unknown>;
}

const scale: DeviceConfig = {
  id: 'scale-1',
  name: 'Peynir terazisi',
  agentName: 'PC',
  transport: { type: 'serial', portName: 'COM3', baudRate: 9600, dataBits: 8, parity: 'None', stopBits: 'One', handshake: 'None', readTimeout: '00:00:01' },
  protocol: { type: 'mt-sics', command: 'SI', pollInterval: '00:00:00.200' },
  capacity: '6',
  division: '0.002',
  unit: 'kg',
  isEnabled: true,
  createdAt: '2026-11-21T10:00:00Z',
  updatedAt: '2026-11-21T10:00:00Z',
};

const network: DeviceConfig = {
  ...scale,
  id: 'scale-2',
  name: 'Ağ terazisi',
  transport: { type: 'tcp', host: '192.168.1.50', port: 4001 },
  protocol: { type: 'toledo-continuous' },
  capacity: null,
  division: null,
};

// Live devices as the host writes them: weights, capacity and division are JSON numbers.
const liveJson = `[
  ${liveDeviceJson('scale-1', { value: '1.248', unit: 'kg' }).replace('"lastSeen"', '"capacity":6,"division":0.002,"unit":"kg","lastSeen"')},
  ${liveDeviceJson('scale-2', null, 'Error', 'Connection refused')}
]`;

function installSettingsApi(routes: Record<string, FakeHandler> = {}) {
  return installFakeApi({
    routes: {
      'GET /api/devices/config': () => jsonResponse([scale, network]),
      'GET /api/devices': () => new Response(liveJson, { headers: { 'Content-Type': 'application/json' } }),
      ...routes,
    },
  });
}

beforeEach(() => {
  FakeEventSource.install();
});

describe('settings > devices', () => {
  it('is reached from the Settings group of the menu', async () => {
    installSettingsApi();
    const user = userEvent.setup();
    renderApp('/records');

    const menu = await screen.findByRole('navigation');
    expect(within(menu).getByRole('heading', { name: 'Ayarlar' })).toBeInTheDocument();
    await user.click(within(menu).getByRole('link', { name: 'Cihazlar' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Cihazlar' })).toBeInTheDocument();
  });

  it('lists the scales with live state, last weight, capacity and connection', async () => {
    installSettingsApi();
    renderApp('/settings/devices');

    const table = await screen.findByRole('table');
    const rows = within(table).getAllByRole('row');
    const first = rows[1]!;
    expect(within(first).getByText('Peynir terazisi')).toBeInTheDocument();
    expect(await within(first).findByText('bağlı')).toHaveClass('state--positive');
    expect(within(first).getByText('1,248 kg')).toBeInTheDocument();
    expect(within(first).getByText('Max 6 kg / e = 0,002 kg')).toBeInTheDocument();
    expect(within(first).getByText('MT-SICS · 9600 8N1 · COM3')).toBeInTheDocument();

    // The 'Error' state is shown as a word with the agent's error text.
    const second = rows[2]!;
    expect(within(second).getByText('hata')).toHaveClass('state--warning');
    expect(within(second).getByText('Connection refused')).toBeInTheDocument();
    expect(within(second).getByText('Toledo sürekli çıkış · 192.168.1.50:4001')).toBeInTheDocument();
  });

  it('edits a scale, keeps settings it does not show and shows validation errors from the server', async () => {
    let attempt = 0;
    const fetchMock = installSettingsApi({
      'PUT /api/devices/config/scale-1': (_url, init) =>
        attempt++ === 0
          ? jsonResponse({ title: 'Invalid', errors: { 'Transport.PortName': ['devices.PortNameRequired'], capacity: ['devices.MustBePositive'] } }, 400)
          : jsonResponse({ ...scale, ...bodyOf(init) }),
    });
    const user = userEvent.setup();
    renderApp('/settings/devices');

    const table = await screen.findByRole('table');
    await user.click(within(within(table).getAllByRole('row')[1]!).getByRole('button', { name: 'Düzenle' }));

    expect(screen.getByRole('heading', { name: 'Bağlantı ayarları' })).toBeInTheDocument();
    expect(screen.getByLabelText('Bölüntü (e)')).toHaveValue('0,002');
    // The live test of the device being edited.
    FakeEventSource.of('/api/devices/scale-1/live').emit(liveDeviceJson('scale-1', { value: '0.5', unit: 'kg' }));
    expect(screen.getByTestId('device-live-weight')).toHaveTextContent('0,5');

    await user.clear(screen.getByLabelText('Ad'));
    await user.type(screen.getByLabelText('Ad'), 'Kaşar terazisi');
    await user.clear(screen.getByLabelText('Port'));
    await user.clear(screen.getByLabelText('Kapasite (Max)'));
    await user.type(screen.getByLabelText('Kapasite (Max)'), '0');
    await user.click(screen.getByRole('button', { name: 'Kaydet' }));

    expect(await screen.findByText('Seri port zorunludur, örneğin COM3.')).toBeInTheDocument();
    expect(screen.getByText('Değer sıfırdan büyük olmalıdır.')).toBeInTheDocument();
    expect(screen.getByLabelText('Port')).toHaveAttribute('aria-invalid', 'true');

    await user.type(screen.getByLabelText('Port'), 'COM4');
    await user.clear(screen.getByLabelText('Kapasite (Max)'));
    await user.type(screen.getByLabelText('Kapasite (Max)'), '15');
    await user.click(screen.getByLabelText('Bu teraziyi oku'));
    await user.click(screen.getByRole('button', { name: 'Kaydet' }));

    expect(await screen.findByText('Kaydedildi.')).toBeInTheDocument();
    const put = fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT').at(-1);
    expect(bodyOf(put?.[1])).toMatchObject({
      name: 'Kaşar terazisi',
      capacity: '15',
      division: '0.002',
      isEnabled: false,
      transport: { type: 'serial', portName: 'COM4', baudRate: 9600, readTimeout: '00:00:01' },
      protocol: { type: 'mt-sics', command: 'SI', pollInterval: '00:00:00.200' },
    });
  });

  it('deletes a scale after confirmation', async () => {
    const fetchMock = installSettingsApi({ 'DELETE /api/devices/config/scale-2': () => new Response(null, { status: 204 }) });
    const user = userEvent.setup();
    renderApp('/settings/devices');

    const table = await screen.findByRole('table');
    await user.click(within(within(table).getAllByRole('row')[2]!).getByRole('button', { name: 'Sil' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Ağ terazisi silinsin mi?');
    expect(callsTo(fetchMock, 'DELETE /api/devices/config/scale-2')).toBe(0);

    await user.click(within(dialog).getByRole('button', { name: 'Sil' }));
    await waitFor(() => expect(callsTo(fetchMock, 'DELETE /api/devices/config/scale-2')).toBe(1));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(callsTo(fetchMock, 'GET /api/devices/config')).toBeGreaterThanOrEqual(2);
  });

  it('opens the discovery flow to add a scale', async () => {
    const fetchMock = installSettingsApi({
      'POST /api/devices/discover': () => jsonResponse({ code: 'devices.NoAgent' }, 503),
    });
    const user = userEvent.setup();
    renderApp('/settings/devices');

    await user.click(await screen.findByRole('button', { name: 'Terazi ekle' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Vyxum Agent hizmeti çalışmıyor.');
    expect(callsTo(fetchMock, 'POST /api/devices/discover')).toBe(1);

    await user.click(screen.getByRole('button', { name: 'Vazgeç' }));
    expect(await screen.findByRole('table')).toBeInTheDocument();
  });
});

describe('settings > about', () => {
  it('shows version, system requirement, database, data folder, last backup and how to update', async () => {
    installFakeApi({
      routes: {
        'GET /api/setup/status': () =>
          jsonResponse(
            setupStatus({
              version: '0.3.1',
              company: { name: 'Örnek Gıda' },
              dataDirectory: 'C:\\ProgramData\\Vyxum',
              lastBackup: { path: 'C:\\ProgramData\\Vyxum\\backups\\vyxum-20261121.db', at: '2026-11-21T08:30:00Z' },
            }),
          ),
      },
    });
    renderApp('/settings/about');

    expect(await screen.findByRole('heading', { level: 1, name: 'Hakkında' })).toBeInTheDocument();
    expect(await screen.findByText('0.3.1')).toBeInTheDocument();
    expect(screen.getByText('Windows 10 ya da 11, 64 bit')).toBeInTheDocument();
    expect(screen.getByText('Örnek Gıda')).toBeInTheDocument();
    expect(screen.getByText('Bu bilgisayar (SQLite)')).toBeInTheDocument();
    expect(screen.getByText('C:\\ProgramData\\Vyxum')).toBeInTheDocument();
    expect(screen.getByText('C:\\ProgramData\\Vyxum\\backups\\vyxum-20261121.db')).toBeInTheDocument();
    // Local time, formatted for the culture (day.month.year).
    const local = new Intl.DateTimeFormat('tr', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(
      new Date('2026-11-21T08:30:00Z'),
    );
    expect(screen.getByText(local)).toBeInTheDocument();
    expect(screen.getByText(/Yeni sürümün kurulum dosyasını çalıştırın/)).toBeInTheDocument();
  });

  it('shows SQL Server without a backup line', async () => {
    installFakeApi({
      routes: {
        'GET /api/setup/status': () => jsonResponse(setupStatus({ database: { provider: 'SqlServer', isDefault: false }, dataDirectory: null })),
      },
    });
    renderApp('/settings/about');

    expect(await screen.findByText('SQL Server')).toBeInTheDocument();
    expect(screen.getByText('SQL Server veritabanının yedeğini veritabanı yöneticiniz alır.')).toBeInTheDocument();
    expect(screen.queryByText('Son yedek')).not.toBeInTheDocument();
    expect(screen.queryByText('Veri klasörü')).not.toBeInTheDocument();
  });
});
