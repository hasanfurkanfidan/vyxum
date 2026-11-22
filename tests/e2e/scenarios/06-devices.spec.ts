import { expectCleanScreen, exact, test, expect } from '../support/vyxum';

/** Settings, Devices: the scale added in the wizard is listed as connected with its last weight, and can be renamed. */
test('Devices: the scale is listed, connected, and can be renamed', async ({ page, t, shot, scale }) => {
  await scale.set({ value: 777, unit: 'g', state: 'stable' });
  await page.goto('/settings/devices');
  await expect(page.getByRole('heading', { name: t('devices.Title'), exact: true })).toBeVisible();
  const row = page.getByRole('row').filter({ hasText: '127.0.0.1:' });
  await expect(row).toContainText(t('shell.Connected'));
  await expect(row).toContainText(/777/);
  await expectCleanScreen(page);
  await shot('cihazlar');

  await row.getByRole('button', { name: t('devices.Edit') }).click();
  await page.getByLabel(t('devices.FieldName'), { exact: true }).fill('Paketleme terazisi');
  await shot('cihaz-duzenle');
  await page.getByRole('button', { name: exact(t('devices.Save')) }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Paketleme terazisi' })).toContainText(t('shell.Connected'), { timeout: 20_000 });
  await shot('cihaz-adi-degisti');
});
