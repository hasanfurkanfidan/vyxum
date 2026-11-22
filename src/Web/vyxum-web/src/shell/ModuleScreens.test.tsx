import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import * as React from 'react';
import * as ReactRouterDom from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { App, AppProviders } from '../App';
import type { UiModule } from '../lib/api';
import { SDK_EXPORTS, shimSource } from '../sdk/contract';
import { installShared } from '../sdk/shared';
import { installFakeApi, jsonResponse, type FakeHandler } from '../test/fakeApi';
import type { ModuleImporter } from './moduleLoader';
// The paid sample module's real screen, a hand-written ES module embedded in its assembly.
import sampleSource from '../../../../../tests/SampleModules/Vyxum.Samples.Paid/ui/index.js?raw';

const paidStrings: Record<string, string> = {
  'samples-paid.Title': 'Ücretli örnek',
  'samples-paid.Eyebrow': 'Örnek modül',
  'samples-paid.Empty': 'Henüz kayıt yok.',
  'samples-paid.Add': 'Kayıt ekle',
  'samples-paid.LoadFailed': 'Kayıtlar yüklenemedi.',
};

function dataUrl(source: string): string {
  return 'data:text/javascript;charset=utf-8,' + encodeURIComponent(source);
}

/**
 * Imports the sample module as the browser would: its bare specifiers resolve to the shim files (here as data URLs
 * instead of the import map), which read the application's shared global.
 */
const sampleImporter: ModuleImporter = async () => {
  installShared();
  const shims: Record<string, string> = {
    react: dataUrl(shimSource('react', Object.keys(React))),
    '@vyxum/sdk': dataUrl(shimSource('sdk', SDK_EXPORTS)),
  };
  const source = sampleSource.replace(/from '([^']+)'/g, (_match, specifier: string) => {
    const url = shims[specifier];
    if (!url) throw new Error(`The sample imports '${specifier}', which the test does not map.`);
    return `from "${url}"`;
  });
  return import(/* @vite-ignore */ dataUrl(source));
};

function paid(license: UiModule['license']): UiModule {
  return {
    id: 'samples.paid',
    version: '1.0.0',
    requiresLicense: true,
    license,
    ui: license === 'locked' ? null : { entry: '/modules/samples.paid/index.js', sdkVersion: 1 },
  };
}

function install(moduleList: UiModule[], routes: Record<string, FakeHandler> = {}) {
  const fetchMock = installFakeApi({
    modules: ['weighing', 'products'],
    moduleList,
    routes: {
      'GET /api/samples-paid/entries': () => jsonResponse(['Order: 2 x Flour']),
      ...routes,
    },
  });
  // Merge the module's strings into the strings response.
  const original = fetchMock.getMockImplementation()!;
  fetchMock.mockImplementation(async (input, init) => {
    const response = await original(input, init);
    const href = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    if (new URL(href, 'http://localhost').pathname !== '/api/ui/strings') return response;
    const body = (await response.json()) as { culture: string; strings: Record<string, string> };
    return jsonResponse({ ...body, strings: { ...body.strings, ...paidStrings } });
  });
  return fetchMock;
}

function renderApp(path: string, importer: ModuleImporter) {
  return render(
    <AppProviders moduleImporter={importer}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

describe('module screens', () => {
  it('loads a licensed module, shows it in the menu and renders its screen with the shared React', async () => {
    install([paid('licensed')]);
    const importer = vi.fn(sampleImporter);
    renderApp('/m/samples.paid', importer);

    expect(await screen.findByRole('heading', { level: 1, name: 'Ücretli örnek' })).toBeInTheDocument();
    expect(await screen.findByText('Order: 2 x Flour')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Kayıt ekle' })).toBeInTheDocument();
    expect(importer).toHaveBeenCalledWith('/modules/samples.paid/index.js');

    const menu = screen.getByRole('navigation');
    const group = within(menu).getByRole('heading', { name: 'Modüller' }).closest('section')!;
    expect(within(group).getByRole('link', { name: 'Ücretli örnek' })).toHaveAttribute('href', '/m/samples.paid');
  });

  it('passes the read-only state to the module, which hides its actions', async () => {
    install([paid('readOnly')]);
    renderApp('/m/samples.paid', sampleImporter);

    expect(await screen.findByText('Order: 2 x Flour')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Kayıt ekle' })).not.toBeInTheDocument();
  });

  it('does not import a locked module, shows no menu entry and explains the license on its address', async () => {
    install([paid('locked')]);
    const importer = vi.fn(sampleImporter);
    renderApp('/m/samples.paid', importer);

    expect(await screen.findByText('Bu modül için lisans gerekiyor.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Lisans ayarlarını aç' })).toHaveAttribute('href', '/settings/license');
    const menu = screen.getByRole('navigation');
    expect(within(menu).queryByRole('heading', { name: 'Modüller' })).not.toBeInTheDocument();
    expect(within(menu).queryByRole('link', { name: 'Ücretli örnek' })).not.toBeInTheDocument();
    expect(importer).not.toHaveBeenCalled();
  });

  it('keeps the application and other modules working when one module fails to load', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const broken: UiModule = { id: 'samples.broken', version: '1.0.0', license: 'licensed', ui: { entry: '/modules/samples.broken/index.js', sdkVersion: 1 } };
    install([broken, paid('licensed')]);
    const importer: ModuleImporter = async (url) => {
      if (url.includes('broken')) throw new TypeError('Failed to fetch dynamically imported module');
      return sampleImporter(url);
    };
    renderApp('/m/samples.broken', importer);

    expect(await screen.findByText('Bu modülün ekranları yüklenemedi.')).toBeInTheDocument();
    const menu = screen.getByRole('navigation');
    expect(await within(menu).findByRole('link', { name: 'Ücretli örnek' })).toBeInTheDocument();
    expect(within(menu).getByRole('link', { name: 'Tartım' })).toBeInTheDocument();
  });

  it('shows a module built for a newer SDK as not loadable', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    install([{ ...paid('licensed'), ui: { entry: '/modules/samples.paid/index.js', sdkVersion: 99 } }]);
    const importer = vi.fn(sampleImporter);
    renderApp('/m/samples.paid', importer);

    expect(await screen.findByText("Bu modül Vyxum'un daha yeni bir sürümünü gerektiriyor.")).toBeInTheDocument();
    expect(importer).not.toHaveBeenCalled();
  });

  it('keeps an error in a module screen inside that screen', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    install([paid('licensed')]);
    const Broken = () => {
      throw new Error('boom');
    };
    renderApp('/m/samples.paid', async () => ({
      register: () => ({ routes: [{ path: '', component: Broken }], nav: [{ labelKey: 'samples-paid.Title', path: '' }] }),
    }));

    expect(await screen.findByText('Bu ekranda bir hata oluştu.')).toBeInTheDocument();
    expect(within(screen.getByRole('navigation')).getByRole('link', { name: 'Ücretli örnek' })).toBeInTheDocument();
  });

  it('shows a page on the whole window without the menu and a sidebar component in the menu (SDK 2)', async () => {
    install([{ ...paid('licensed'), ui: { entry: '/modules/samples.paid/index.js', sdkVersion: 2 } }]);
    const importer: ModuleImporter = async () => ({
      register: () => ({
        routes: [{ path: '', element: <p>Inside the shell</p> }],
        pages: [{ path: 'sign-in', element: <h1>Who is weighing?</h1> }],
        sidebar: () => <p>Signed in: Ayşe Demir</p>,
      }),
    });
    const { unmount } = renderApp('/m/samples.paid/sign-in', importer);

    expect(await screen.findByRole('heading', { name: 'Who is weighing?' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    unmount();

    renderApp('/m/samples.paid', importer);
    expect(await screen.findByText('Inside the shell')).toBeInTheDocument();
    expect(within(screen.getByRole('navigation')).getByText('Signed in: Ayşe Demir')).toBeInTheDocument();
  });

  it('sends the user to the sign-in page a module names when the server answers 401, and keeps where to return', async () => {
    install([{ ...paid('licensed'), ui: { entry: '/modules/samples.paid/index.js', sdkVersion: 2 } }], {
      'GET /api/samples-paid/entries': () => jsonResponse({ code: 'users.SignInRequired', signInPath: '/m/samples.paid/sign-in' }, 401),
    });
    function SignIn() {
      const location = ReactRouterDom.useLocation();
      return <h1>{`Sign in, then ${new URLSearchParams(location.search).get('returnTo') ?? ''}`}</h1>;
    }
    const importer: ModuleImporter = async (url) => {
      const sample = (await sampleImporter(url)) as { register: (host: unknown) => { routes: unknown[]; nav: unknown[] } };
      return { register: (host: unknown) => ({ ...sample.register(host), pages: [{ path: 'sign-in', component: SignIn }] }) };
    };
    renderApp('/m/samples.paid', importer);

    expect(await screen.findByRole('heading', { name: 'Sign in, then /m/samples.paid' })).toBeInTheDocument();
  });

  it('uses the same router instance for module links', () => {
    installShared();
    const shared = (globalThis as unknown as Record<string, Record<string, unknown> | undefined>).__vyxumShared;
    expect(shared?.['react-router-dom']).toBe(ReactRouterDom);
  });
});
