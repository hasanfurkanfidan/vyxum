// Loads the screens of separately built modules (decision "Ücretli modül ekranları", phase F4).
// A module with screens embeds a compiled ES module in its assembly; the host serves it at /modules/{id}/...
// only when the module may load it and lists the entry in /api/ui/modules. The entry exports
// `register(host)`, which returns the module's routes (under /m/{id}/) and menu entries.
import type { ComponentType, ReactNode } from 'react';
import type { UiModule } from '../lib/api';
import { MIN_SDK_VERSION, SDK_VERSION } from '../sdk/contract';
import { licenseInfo, type ModuleLicenseInfo } from '../sdk/license';
import { modulePath, type ModuleNavItem, type NavGroupId } from './navigation';

/** What the application hands to a module's `register`. */
export interface ModuleHost {
  /** SDK version of the application. */
  sdkVersion: number;
  moduleId: string;
  /** Address the module's routes live under: `/m/{moduleId}`. */
  basePath: string;
  /** License state when the module was loaded; use `useModuleLicense` from `@vyxum/sdk` for the current one. */
  license: ModuleLicenseInfo;
  /** URL of another file of the module's embedded front end, e.g. `assetUrl('logo.svg')`. */
  assetUrl: (path: string) => string;
}

export interface ModuleRouteDefinition {
  /** Path relative to `/m/{moduleId}/`; empty for the module's start screen. May contain route parameters. */
  path: string;
  /** The screen: a component (rendered without props) or an element. */
  component?: ComponentType;
  element?: ReactNode;
  /** Capability that must be available for the route to exist. */
  capability?: string;
}

export interface ModuleNavDefinition {
  /** Resource key of the label; the module ships its strings in its own resource files. */
  labelKey: string;
  /** Path relative to `/m/{moduleId}/`, as in the routes. */
  path: string;
  /** Menu group; `modules` by default. */
  group?: Extract<NavGroupId, 'modules' | 'system' | 'settings'>;
}

/** What `register(host)` returns. */
export interface ModuleRegistration {
  routes?: ModuleRouteDefinition[];
  nav?: ModuleNavDefinition[];
}

export interface ModuleEntry {
  register: (host: ModuleHost) => ModuleRegistration;
}

export type LoadedModuleUi =
  | { id: string; status: 'loaded'; routes: ModuleRouteDefinition[]; nav: ModuleNavItem[] }
  | { id: string; status: 'failed' | 'incompatible'; error: string };

/** Imports a module entry by URL. Tests replace it. */
export type ModuleImporter = (url: string) => Promise<unknown>;

export const browserImporter: ModuleImporter = (url) => import(/* @vite-ignore */ url);

/** True when the module has screens the application may load: listed with an entry and not locked. */
export function isLoadable(module: UiModule): boolean {
  return module.ui != null && module.license !== 'locked';
}

/** True when this application can serve a module built against `sdkVersion`. */
export function isSdkCompatible(sdkVersion: number): boolean {
  return Number.isInteger(sdkVersion) && sdkVersion >= MIN_SDK_VERSION && sdkVersion <= SDK_VERSION;
}

function isEntry(value: unknown): value is ModuleEntry {
  return typeof value === 'object' && value !== null && typeof (value as { register?: unknown }).register === 'function';
}

function normalizePath(path: unknown): string {
  if (typeof path !== 'string') throw new Error('A route or menu path must be a string.');
  const relative = path.replace(/^\/+/, '').replace(/\/+$/, '');
  if (relative.split('/').some((segment) => segment === '..' || segment === '.')) {
    throw new Error(`Invalid module path '${path}'.`);
  }
  return relative;
}

function validate(id: string, registration: unknown): { routes: ModuleRouteDefinition[]; nav: ModuleNavItem[] } {
  if (typeof registration !== 'object' || registration === null) throw new Error('register(host) must return an object.');
  const { routes = [], nav = [] } = registration as ModuleRegistration;
  if (!Array.isArray(routes) || !Array.isArray(nav)) throw new Error('routes and nav must be arrays.');

  const checkedRoutes = routes.map((route) => {
    if (route.component === undefined && route.element === undefined) throw new Error('A route needs a component or an element.');
    return { ...route, path: normalizePath(route.path) };
  });
  const checkedNav = nav.map((item): ModuleNavItem => {
    if (typeof item.labelKey !== 'string' || item.labelKey.length === 0) throw new Error('A menu entry needs a labelKey.');
    const group = item.group ?? 'modules';
    if (group !== 'modules' && group !== 'system' && group !== 'settings') throw new Error(`Unknown menu group '${String(group)}'.`);
    return { labelKey: item.labelKey, path: modulePath(id, normalizePath(item.path)), module: id, group };
  });
  return { routes: checkedRoutes, nav: checkedNav };
}

/** Imports one module's entry and registers it. Never throws: a failure comes back as a `failed` result. */
export async function loadModuleUi(module: UiModule, importer: ModuleImporter = browserImporter): Promise<LoadedModuleUi> {
  const ui = module.ui;
  if (!ui) return { id: module.id, status: 'failed', error: 'The module has no screens to load.' };
  if (!isSdkCompatible(ui.sdkVersion)) {
    const error = `Module '${module.id}' needs SDK version ${ui.sdkVersion}; this application offers ${MIN_SDK_VERSION} to ${SDK_VERSION}.`;
    console.error(error);
    return { id: module.id, status: 'incompatible', error };
  }

  try {
    const entry = await importer(ui.entry);
    if (!isEntry(entry)) throw new Error('The entry does not export a register(host) function.');
    // The entry may state the SDK version it was really built against; it must agree with what the manifest said.
    const exported = (entry as { sdkVersion?: unknown }).sdkVersion;
    if (exported !== undefined && !isSdkCompatible(exported as number)) {
      const error = `Module '${module.id}' was built for SDK version ${String(exported)}; this application offers ${MIN_SDK_VERSION} to ${SDK_VERSION}.`;
      console.error(error);
      return { id: module.id, status: 'incompatible', error };
    }
    const basePath = modulePath(module.id);
    const host: ModuleHost = {
      sdkVersion: SDK_VERSION,
      moduleId: module.id,
      basePath,
      license: licenseInfo(module.license),
      assetUrl: (path) => new URL(normalizePath(path), new URL(ui.entry, window.location.href)).pathname,
    };
    return { id: module.id, status: 'loaded', ...validate(module.id, entry.register(host)) };
  } catch (error) {
    console.error(`Loading the screens of module '${module.id}' failed.`, error);
    return { id: module.id, status: 'failed', error: error instanceof Error ? error.message : String(error) };
  }
}

/** Loads the screens of every loadable module, side by side; one failing module does not stop the others. */
export function loadModuleUis(modules: readonly UiModule[], importer: ModuleImporter = browserImporter): Promise<LoadedModuleUi[]> {
  return Promise.all(modules.filter(isLoadable).map((module) => loadModuleUi(module, importer)));
}
