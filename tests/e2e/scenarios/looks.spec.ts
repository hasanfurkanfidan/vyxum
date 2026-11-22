import { expectCleanScreen, test, expect } from '../support/vyxum';

/**
 * The main screens in the project's language and screen size (see playwright.config.ts: English, and a 1280×800
 * touch panel). Each screen must show no raw resource key, no sideways scrolling and no browser error; in English
 * no Turkish interface text may be left (the records' own data, such as product names, is Turkish on purpose).
 */
const screens = [
  { path: '/', name: 'tartim', heading: 'weighing.Title' },
  { path: '/records', name: 'kayitlar', heading: 'weighing.RecordsTitle' },
  { path: '/products', name: 'urunler', heading: 'products.Title' },
  { path: '/settings/devices', name: 'cihazlar', heading: 'devices.Title' },
  { path: '/settings/license', name: 'lisans', heading: 'license.Title' },
  { path: '/settings/about', name: 'hakkinda', heading: 'setup.AboutTitle' },
] as const;

/** Data typed in by the scenarios, and the language switch's own "Türkçe": Turkish on purpose. */
const typedData = [
  'Türkçe',
  'Deneme Gıda A.Ş.', 'Beyaz peynir 500 g', 'Kaşar 1 kg', 'Lor peyniri 250 g', 'Yanlış ürün seçildi', 'Paketleme terazisi', 'Ücretli örnek',
];

for (const screen of screens) {
  test(`Screen ${screen.path}`, async ({ page, t, shot, culture }) => {
    await page.goto(screen.path);
    await expect(page.getByRole('heading', { name: t(screen.heading), exact: true })).toBeVisible();
    await page.waitForLoadState('networkidle');
    await expectCleanScreen(page);
    if (culture === 'en') {
      let text = await page.locator('body').innerText();
      for (const data of typedData) text = text.replaceAll(data, '');
      const turkish = text.split('\n').filter((line) => /[ğĞışŞİçÇöÖüÜ]/.test(line));
      expect(turkish, 'Turkish interface text on an English screen').toEqual([]);
    }
    await shot(screen.name, { fullPage: true });
  });
}
