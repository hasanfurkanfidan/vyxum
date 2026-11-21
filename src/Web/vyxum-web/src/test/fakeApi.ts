// Test double for the host API: answers fetch calls from a route table.
import { vi } from 'vitest';
import type { UiModules } from '../lib/api';
import { readCultureCookie } from '../lib/culture';

export type FakeHandler = (url: URL, init: RequestInit | undefined) => Response | Promise<Response>;

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  });
}

/** Sample strings per culture, as the host would merge them from the resource files. */
export const sampleStrings: Record<'tr' | 'en', Record<string, string>> = {
  tr: {
    'shell.AppName': 'vyxum',
    'shell.Core': 'Çekirdek',
    'shell.Modules': 'Modüller',
    'shell.System': 'Sistem',
    'shell.Weighing': 'Tartım',
    'shell.Records': 'Kayıtlar',
    'shell.Products': 'Ürünler',
    'shell.Language': 'Dil',
    'shell.Turkish': 'Türkçe',
    'shell.English': 'English',
    'shell.Loading': 'Yükleniyor',
    'shell.Error': 'Sunucuya ulaşılamadı.',
    'shell.Retry': 'Yeniden dene',
    'weighing.Title': 'Tartım',
    'records.Title': 'Tartım kayıtları',
    'products.Title': 'Ürün kartları',
  },
  en: {
    'shell.AppName': 'vyxum',
    'shell.Core': 'Core',
    'shell.Modules': 'Modules',
    'shell.System': 'System',
    'shell.Weighing': 'Weighing',
    'shell.Records': 'Records',
    'shell.Products': 'Products',
    'shell.Language': 'Language',
    'shell.Turkish': 'Türkçe',
    'shell.English': 'English',
    'shell.Loading': 'Loading',
    'shell.Error': 'The server could not be reached.',
    'shell.Retry': 'Try again',
    'weighing.Title': 'Weighing',
    'records.Title': 'Weighing records',
    'products.Title': 'Product cards',
  },
};

export interface FakeApiOptions {
  modules?: string[];
  capabilities?: string[];
  routes?: Record<string, FakeHandler>;
}

/** Replaces global fetch. `/api/ui/strings` answers in the language of the culture cookie. */
export function installFakeApi(options: FakeApiOptions = {}) {
  const modules: UiModules = {
    modules: (options.modules ?? ['weighing', 'products']).map((id) => ({ id, version: '1.0.0' })),
    capabilities: options.capabilities ?? [],
  };

  const routes: Record<string, FakeHandler> = {
    'GET /api/ui/modules': () => jsonResponse(modules),
    'GET /api/ui/strings': () => {
      const culture = readCultureCookie() ?? 'tr';
      return jsonResponse({ culture, strings: sampleStrings[culture] });
    },
    ...options.routes,
  };

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, 'http://localhost');
    const method = (init?.method ?? 'GET').toUpperCase();
    const handler = routes[`${method} ${url.pathname}`];
    if (!handler) return jsonResponse({ title: 'Not found' }, 404);
    return handler(url, init);
  });

  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

/** Number of calls to one route, for example `callsTo(fetchMock, 'GET /api/ui/strings')`. */
export function callsTo(fetchMock: ReturnType<typeof installFakeApi>, route: string): number {
  const [method, path] = route.split(' ');
  return fetchMock.mock.calls.filter(([input, init]) => {
    const href = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    return (init?.method ?? 'GET').toUpperCase() === method && new URL(href, 'http://localhost').pathname === path;
  }).length;
}
