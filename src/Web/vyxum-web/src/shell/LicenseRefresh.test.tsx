import { act, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App, AppProviders } from '../App';
import { LicenseError, apiPost } from '../lib/api';
import { callsTo, installFakeApi, jsonResponse } from '../test/fakeApi';
import { licenseRefreshMs } from './ModulesProvider';
import type { ModuleImporter } from './moduleLoader';

function renderApp(path = '/records', importer?: ModuleImporter) {
  return render(
    <AppProviders {...(importer ? { moduleImporter: importer } : {})}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

const graceText = 'Lisans süresi doldu; ücretli modüller 12 gün daha çalışır.';
const readOnlyText = 'Lisans süresi bitti; ücretli modüller salt okunur.';

afterEach(() => {
  vi.useRealTimers();
});

describe('license state refresh', () => {
  it('refreshes the module list quietly when the window gains focus', async () => {
    let status: 'grace' | 'readOnly' = 'grace';
    const fetchMock = installFakeApi({
      routes: {
        'GET /api/ui/modules': () =>
          jsonResponse({
            modules: [{ id: 'weighing', version: '1.0.0' }],
            capabilities: [],
            license: { status, licensee: 'Örnek A.Ş.', graceDaysLeft: status === 'grace' ? 12 : null },
          }),
      },
    });
    renderApp();
    expect(await screen.findByText(graceText)).toBeInTheDocument();

    status = 'readOnly';
    const before = callsTo(fetchMock, 'GET /api/ui/modules');
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });

    expect(await screen.findByText(readOnlyText)).toBeInTheDocument();
    expect(callsTo(fetchMock, 'GET /api/ui/modules')).toBe(before + 1);
    // No loading state replaced the screen.
    expect(screen.queryByText('Yükleniyor')).not.toBeInTheDocument();
  });

  it('refreshes the module list on a timer', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const fetchMock = installFakeApi();
    renderApp();
    await screen.findByRole('navigation');
    const before = callsTo(fetchMock, 'GET /api/ui/modules');

    await act(async () => {
      vi.advanceTimersByTime(licenseRefreshMs);
    });

    await waitFor(() => expect(callsTo(fetchMock, 'GET /api/ui/modules')).toBe(before + 1));
  });

  it('reloads the module list when a request is denied by the license', async () => {
    const fetchMock = installFakeApi({
      routes: { 'POST /api/things': () => jsonResponse({ title: 'Forbidden', status: 403, code: 'license_read_only' }, 403) },
    });
    renderApp();
    await screen.findByRole('navigation');
    const before = callsTo(fetchMock, 'GET /api/ui/modules');

    let caught: unknown;
    await act(async () => {
      caught = await apiPost('/api/things', {}).catch((error: unknown) => error);
    });

    expect(caught).toBeInstanceOf(LicenseError);
    expect((caught as LicenseError).messageKey).toBe('license.Error.license_read_only');
    await waitFor(() => expect(callsTo(fetchMock, 'GET /api/ui/modules')).toBe(before + 1));
  });
});

describe('module screen loading', () => {
  it('tries a failed import again with the next module list refresh', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    installFakeApi({
      moduleList: [{ id: 'flaky', version: '1.0.0', license: 'licensed', ui: { entry: '/modules/flaky/index.js', sdkVersion: 1 } }],
    });
    let calls = 0;
    const importer: ModuleImporter = async () => {
      calls += 1;
      if (calls === 1) throw new TypeError('Failed to fetch dynamically imported module');
      return { register: () => ({ routes: [{ path: '', element: <p>Flaky screen</p> }] }) };
    };
    renderApp('/m/flaky', importer);

    expect(await screen.findByText('Bu modülün ekranları yüklenemedi.')).toBeInTheDocument();
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });

    expect(await screen.findByText('Flaky screen')).toBeInTheDocument();
    expect(calls).toBe(2);
  });
});
