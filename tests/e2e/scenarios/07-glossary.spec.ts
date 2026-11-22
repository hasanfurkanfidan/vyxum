import { expectCleanScreen, test, expect } from '../support/vyxum';

/**
 * Help, Glossary: free, needs no license. It opens from the menu, lists the sections, finds a term as the user types
 * (also without Turkish letters, and by its English name), says when nothing is found, and filters by section.
 */
test('Glossary: open from the menu, search and browse by section', async ({ page, t, shot }) => {
  await page.goto('/');
  const menu = page.getByRole('navigation', { name: t('shell.AppName') });
  await menu.getByRole('link', { name: t('glossary.Title'), exact: true }).click();
  await expect(page).toHaveURL(/\/glossary$/);
  await expect(page.getByRole('heading', { level: 1, name: t('glossary.Title'), exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'En çok karıştırılanlar' })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Dara', exact: true })).toContainText('İngilizcesi: Tare');
  await expectCleanScreen(page);
  await shot('sozluk');

  const search = page.getByRole('searchbox', { name: t('glossary.SearchLabel') });
  await search.fill('sifirlama');
  const first = page.getByRole('article').first();
  await expect(first).toHaveAccessibleName('Sıfırlama');
  await expect(first.locator('mark').first()).toHaveText('Sıfırlama');
  await shot('turkce-harfsiz-arama');

  await search.fill('tare');
  await expect(page.getByRole('article').first()).toHaveAccessibleName('Dara');
  await shot('ingilizce-adla-arama');

  await search.fill('zzzz');
  await expect(page.getByText(t('glossary.NoResults', 'zzzz'))).toBeVisible();
  await shot('sonuc-yok');

  await page.getByRole('button', { name: t('glossary.Clear') }).first().click();
  await expect(search).toHaveValue('');
  await page.getByRole('button', { name: 'Etiket ve barkod' }).click();
  await expect(page.getByRole('main').getByRole('heading', { level: 2 })).toHaveText(['Etiket ve barkod']);
  await expect(page.getByRole('article', { name: 'x yüksekliği' })).toContainText('80 cm²');
  await shot('bolum-etiket');
});
