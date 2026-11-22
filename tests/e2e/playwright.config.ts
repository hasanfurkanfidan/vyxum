import { defineConfig, devices } from '@playwright/test';
import type { VyxumOptions } from './support/vyxum';

const baseURL = process.env.VYXUM_E2E_URL ?? 'http://127.0.0.1:5390';
const outputRoot = process.env.VYXUM_E2E_OUT ?? 'results';

// Windows always has Edge, so nothing has to be downloaded there. Elsewhere Playwright's own Chromium is used.
// VYXUM_E2E_BROWSER overrides: "chromium", "msedge" or "chrome".
const browser = process.env.VYXUM_E2E_BROWSER ?? (process.platform === 'win32' ? 'msedge' : 'chromium');
const channel = browser === 'chromium' ? {} : { channel: browser };

const desktop = { ...devices['Desktop Chrome'], ...channel, viewport: { width: 1366, height: 860 } };

/**
 * One server for the whole run, so scenarios run one after another in this order:
 *  1. setup: the first-run wizard on an empty database (every later project needs it);
 *  2. core: weighing, products, records, devices and license screens in Turkish;
 *  3. looks: the main screens in English, and in Turkish on a 1280×800 touch panel. The open-core screens have no
 *     dark theme or large touch layout yet; when a screen gets one, add a project with colorScheme: 'dark' here.
 * Real-device checks (real-scale.spec.ts) run only when VYXUM_E2E_REAL_URL is set.
 */
export default defineConfig<VyxumOptions>({
  testDir: 'scenarios',
  outputDir: `${outputRoot}/artifacts`,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [
    ['list'],
    ['html', { outputFolder: `${outputRoot}/html`, open: 'never' }],
    ['./support/summary-reporter.ts', { outputFolder: outputRoot }],
  ],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
    locale: 'tr-TR',
    timezoneId: 'Europe/Istanbul',
  },
  projects: [
    { name: 'setup', testMatch: /01-setup\.spec\.ts/, use: { ...desktop, culture: 'tr' } },
    {
      name: 'core',
      testMatch: /0[2-9]-.*\.spec\.ts/,
      dependencies: ['setup'],
      use: { ...desktop, culture: 'tr' },
    },
    {
      name: 'looks-en-light',
      testMatch: /looks\.spec\.ts/,
      dependencies: ['core'],
      use: { ...desktop, culture: 'en', locale: 'en-US', colorScheme: 'light' },
    },
    {
      name: 'looks-tr-touch',
      testMatch: /looks\.spec\.ts/,
      dependencies: ['core'],
      // A 10-inch touch panel in landscape, as on a production line.
      use: { ...desktop, culture: 'tr', viewport: { width: 1280, height: 800 }, hasTouch: true, isMobile: false },
    },
    {
      name: 'real-device',
      testMatch: /real-scale\.spec\.ts/,
      use: { ...desktop, culture: 'tr' },
    },
  ],
});
