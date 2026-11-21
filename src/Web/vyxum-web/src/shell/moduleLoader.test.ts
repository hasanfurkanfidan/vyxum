import { describe, expect, it, vi } from 'vitest';
import type { UiModule } from '../lib/api';
import { SDK_VERSION } from '../sdk/contract';
import { isLoadable, isSdkCompatible, loadModuleUis, type ModuleHost, type ModuleImporter } from './moduleLoader';

function module(id: string, license: UiModule['license'], sdkVersion = 1): UiModule {
  return { id, version: '1.0.0', requiresLicense: true, license, ui: license === 'locked' ? null : { entry: `/modules/${id}/index.js`, sdkVersion } };
}

const Screen = () => null;

function entry(register: (host: ModuleHost) => unknown = () => ({ routes: [{ path: '', component: Screen }], nav: [{ labelKey: 'x.Title', path: '' }] })) {
  return { register };
}

describe('moduleLoader', () => {
  it('treats only modules with screens that are not locked as loadable', () => {
    expect(isLoadable(module('a', 'licensed'))).toBe(true);
    expect(isLoadable(module('a', 'readOnly'))).toBe(true);
    expect(isLoadable(module('a', 'locked'))).toBe(false);
    expect(isLoadable({ id: 'products', version: '1.0.0' })).toBe(false);
    // A locked module is never imported even if an entry was listed.
    expect(isLoadable({ ...module('a', 'licensed'), license: 'locked' })).toBe(false);
  });

  it('imports only loadable modules and registers their routes and menu entries', async () => {
    const importer = vi.fn<ModuleImporter>(async () => entry());
    const modules = [module('samples.paid', 'licensed'), module('samples.locked', 'locked'), { id: 'products', version: '1.0.0' }];

    const results = await loadModuleUis(modules, importer);

    expect(importer).toHaveBeenCalledTimes(1);
    expect(importer).toHaveBeenCalledWith('/modules/samples.paid/index.js');
    expect(results).toHaveLength(1);
    const [result] = results;
    expect(result?.status).toBe('loaded');
    if (result?.status !== 'loaded') return;
    expect(result.routes.map((r) => r.path)).toEqual(['']);
    expect(result.nav).toEqual([{ labelKey: 'x.Title', path: '/m/samples.paid', module: 'samples.paid', group: 'modules' }]);
  });

  it('passes the host with the license state and the base path', async () => {
    const register = vi.fn((_host: ModuleHost) => ({}));
    await loadModuleUis([module('samples.paid', 'readOnly')], async () => entry(register));

    const host = register.mock.calls[0]?.[0] as ModuleHost;
    expect(host.moduleId).toBe('samples.paid');
    expect(host.basePath).toBe('/m/samples.paid');
    expect(host.sdkVersion).toBe(SDK_VERSION);
    expect(host.license).toEqual({ state: 'readOnly', canWrite: false });
    expect(host.assetUrl('img/logo.svg')).toBe('/modules/samples.paid/img/logo.svg');
  });

  it('keeps loading the other modules when one import fails', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const importer: ModuleImporter = async (url) => {
      if (url.includes('broken')) throw new TypeError('Failed to fetch dynamically imported module');
      return entry();
    };

    const results = await loadModuleUis([module('broken', 'licensed'), module('good', 'licensed')], importer);

    expect(results.map((r) => [r.id, r.status])).toEqual([
      ['broken', 'failed'],
      ['good', 'loaded'],
    ]);
  });

  it('reports a module without register, or with a throwing or invalid register, as failed', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const entries: Record<string, unknown> = {
      none: {},
      throws: entry(() => {
        throw new Error('boom');
      }),
      invalid: entry(() => ({ routes: [{ path: '' }] })),
      traversal: entry(() => ({ nav: [{ labelKey: 'x.A', path: '../../settings' }] })),
    };
    const results = await loadModuleUis(
      Object.keys(entries).map((id) => module(id, 'licensed')),
      async (url) => entries[url.split('/')[2] ?? ''],
    );

    expect(results.map((r) => r.status)).toEqual(['failed', 'failed', 'failed', 'failed']);
  });

  it('rejects a module built for a newer SDK without importing it', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const importer = vi.fn<ModuleImporter>(async () => entry());

    const [result] = await loadModuleUis([module('future', 'licensed', SDK_VERSION + 1)], importer);

    expect(result?.status).toBe('incompatible');
    expect(importer).not.toHaveBeenCalled();
    expect(isSdkCompatible(SDK_VERSION)).toBe(true);
    expect(isSdkCompatible(0)).toBe(false);
  });

  it('rejects a module whose entry exports a newer sdkVersion than its manifest says', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const importer = vi.fn<ModuleImporter>(async () => ({ ...entry(), sdkVersion: SDK_VERSION + 1 }));

    const [result] = await loadModuleUis([module('sneaky', 'licensed')], importer);

    expect(result?.status).toBe('incompatible');
  });

  it('accepts an entry whose exported sdkVersion is supported', async () => {
    const importer = vi.fn<ModuleImporter>(async () => ({ ...entry(), sdkVersion: SDK_VERSION }));

    const [result] = await loadModuleUis([module('fine', 'licensed')], importer);

    expect(result?.status).toBe('loaded');
  });
});
