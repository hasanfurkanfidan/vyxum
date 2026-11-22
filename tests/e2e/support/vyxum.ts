import { test as base, expect, type Page } from '@playwright/test';

/** Address of the server under test (the runner starts it; see run.mjs). */
export const baseUrl = process.env.VYXUM_E2E_URL ?? 'http://127.0.0.1:5390';

export type Culture = 'tr' | 'en';

/** Per-project settings: the language a scenario runs in. Colour scheme and touch come from Playwright's own options. */
export interface VyxumOptions {
  culture: Culture;
}

const stringCache = new Map<Culture, Promise<Record<string, string>>>();

/** The UI strings of the server in one language, exactly as the screens get them. */
export function loadStrings(culture: Culture): Promise<Record<string, string>> {
  let strings = stringCache.get(culture);
  if (!strings) {
    strings = fetch(`${baseUrl}/api/ui/strings`, { headers: { Cookie: cultureCookie(culture) } })
      .then((r) => {
        if (!r.ok) throw new Error(`GET /api/ui/strings returned ${r.status}.`);
        return r.json() as Promise<{ strings: Record<string, string> }>;
      })
      .then((r) => r.strings);
    stringCache.set(culture, strings);
  }
  return strings;
}

export function cultureCookie(culture: Culture): string {
  return `.AspNetCore.Culture=${encodeURIComponent(`c=${culture}|uic=${culture}`)}`;
}

/** Translates a resource key; `{0}`, `{1}` are filled with the values. Unknown keys fail the test. */
export type Translate = (key: string, ...values: (string | number)[]) => string;

/** Exact-text matcher for a translated string (Playwright treats a RegExp as "contains"). */
export function exact(text: string): RegExp {
  return new RegExp(`^\\s*${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`);
}

/** The simulated scale of the test server (an MT-SICS scale on a loopback TCP port). */
export interface FakeScale {
  /** Changes what the scale shows. */
  set(weight: { value?: number; unit?: string; state?: 'stable' | 'motion' | 'overload' | 'underload' }): Promise<void>;
  /** True: the scale stops answering, as if its cable were pulled out. False: it answers again. */
  silent(silent: boolean): Promise<void>;
  /** Commands the scale received (the newest 50) and any forbidden ones (tare, reset), which must stay empty. */
  status(): Promise<{ commands: string[]; forbiddenCommands: string[] }>;
}

/** License files signed with the test server's own key. See tests/Vyxum.E2E.Host/E2EServer.cs for the kinds. */
export type LicenseKind = 'valid' | 'perpetual' | 'not-yet' | 'grace' | 'read-only' | 'tampered' | 'not-a-license';

export interface LicenseFiles {
  get(kind: LicenseKind, options?: { modules?: string[]; stations?: number; licensee?: string }): Promise<{ name: string; mimeType: string; buffer: Buffer }>;
}

/** Takes a numbered screenshot that goes into the report. Use it after every step a person would look at. */
export type Shot = (name: string, options?: { fullPage?: boolean }) => Promise<void>;

interface Fixtures {
  /**
   * Browser errors a scenario expects, such as the console error the weighing screen logs when the server refuses
   * an unstable weight. Push a pattern before the step that causes it; every other error fails the scenario.
   */
  allowedBrowserErrors: RegExp[];
  t: Translate;
  shot: Shot;
  scale: FakeScale;
  licenses: LicenseFiles;
}

async function control(pathAndQuery: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(`${baseUrl}/e2e/${pathAndQuery}`, init);
  if (!response.ok) throw new Error(`${init?.method ?? 'GET'} /e2e/${pathAndQuery} returned ${response.status}: ${await response.text()}`);
  return response;
}

export const test = base.extend<Fixtures & VyxumOptions>({
  culture: ['tr', { option: true }],

  // Every page starts in the project's language.
  context: async ({ context, culture }, use) => {
    await context.addCookies([{ name: '.AspNetCore.Culture', value: encodeURIComponent(`c=${culture}|uic=${culture}`), url: baseUrl }]);
    await use(context);
  },

  // A script error or a console error fails the scenario. Failed requests the screen handles itself (a rejected
  // license file answers 400) are logged by the browser as "Failed to load resource" and are not counted.
  allowedBrowserErrors: async ({}, use) => {
    await use([]);
  },

  page: async ({ page, allowedBrowserErrors }, use) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => errors.push(`Script error: ${error.message}`));
    page.on('console', (message) => {
      if (message.type() === 'error' && !message.text().startsWith('Failed to load resource')) errors.push(`Console: ${message.text()}`);
    });
    await use(page);
    expect(errors.filter((e) => !allowedBrowserErrors.some((p) => p.test(e))), 'Errors in the browser').toEqual([]);
  },

  t: async ({ culture }, use) => {
    const strings = await loadStrings(culture);
    await use((key, ...values) => {
      const text = strings[key];
      if (text === undefined) throw new Error(`The UI string '${key}' does not exist (${culture}).`);
      return values.reduce<string>((result, value, index) => result.replaceAll(`{${index}}`, String(value)), text);
    });
  },

  // The summary reporter copies these into shots/<project>/<scenario>/NN-name.png.
  shot: async ({ page }, use, testInfo) => {
    await use(async (name, options) => {
      // Let animations and live updates settle so the picture shows what a person would see.
      await page.waitForTimeout(250);
      const body = await page.screenshot({ fullPage: options?.fullPage ?? false, animations: 'disabled' });
      await testInfo.attach(name, { body, contentType: 'image/png' });
    });
  },

  scale: async ({}, use) => {
    await use({
      async set(weight) {
        const query = new URLSearchParams();
        if (weight.value !== undefined) query.set('value', String(weight.value));
        if (weight.unit) query.set('unit', weight.unit);
        if (weight.state) query.set('state', weight.state);
        await control(`scale?${query}`, { method: 'POST' });
      },
      async silent(silent) {
        await control(`scale?silent=${silent}`, { method: 'POST' });
      },
      async status() {
        return (await (await control('scale')).json()) as { commands: string[]; forbiddenCommands: string[] };
      },
    });
  },

  licenses: async ({}, use) => {
    await use({
      async get(kind, options) {
        const query = new URLSearchParams({ kind });
        if (options?.modules) query.set('modules', options.modules.join(','));
        if (options?.stations) query.set('stations', String(options.stations));
        if (options?.licensee) query.set('licensee', options.licensee);
        const response = await control(`license?${query}`);
        return { name: `e2e-${kind}.vxlic`, mimeType: 'application/octet-stream', buffer: Buffer.from(await response.arrayBuffer()) };
      },
    });
  },
});

/**
 * Checks that the page shows no raw resource key (a missing translation shows the key, such as "setup.Next"),
 * does not scroll sideways, and logged no error in the browser console.
 */
export async function expectCleanScreen(page: Page): Promise<void> {
  const text = await page.locator('body').innerText();
  const rawKeys = text.match(/\b[a-z]+(?:\.[A-Z][A-Za-z]+)+\b/g) ?? [];
  expect(rawKeys, 'Raw resource keys on the screen (missing translations)').toEqual([]);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, 'The page scrolls sideways').toBeLessThanOrEqual(1);
}

export { expect };
