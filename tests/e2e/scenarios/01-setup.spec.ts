import { expectCleanScreen, exact, test, expect } from '../support/vyxum';

/**
 * The first start on an empty database, as a customer meets it: the wizard from the database step to the weighing
 * screen. The scale step adds the simulated network scale by hand (no serial port exists on the test machine),
 * the license step rejects a file that is not a license and then goes on free.
 */
test('Setup wizard on an empty database', async ({ page, t, shot, scale, licenses }) => {
  await scale.set({ value: 572, unit: 'g', state: 'stable' });
  await page.goto('/');
  await expect(page).toHaveURL(/\/setup$/);

  await test.step('Database: keep the records on this computer', async () => {
    await expect(page.getByRole('heading', { name: t('setup.DatabaseTitle'), exact: true })).toBeVisible();
    await expect(page.getByText(t('setup.PrivacyNote'))).toBeVisible();
    await expectCleanScreen(page);
    await shot('veritabani');
    await page.getByRole('button', { name: exact(t('setup.Next')) }).click();
  });

  await test.step('Scale: no port found, add the network scale by hand and confirm its live weight', async () => {
    await expect(page.getByRole('heading', { name: t('setup.ScaleTitle'), exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: t('devices.NoneFound'), exact: true })).toBeVisible({ timeout: 30_000 });
    await shot('terazi-bulunamadi');
    await page.getByRole('button', { name: t('devices.Manual') }).click();
    await page.getByLabel(t('devices.FieldTransport')).selectOption('tcp');
    await page.getByLabel(t('devices.FieldHost')).fill('127.0.0.1');
    await page.getByLabel(t('devices.FieldTcpPort')).fill(process.env.VYXUM_E2E_SCALE_PORT ?? '5391');
    await page.getByLabel(t('devices.FieldProtocol')).selectOption('mt-sics');
    await shot('terazi-elle');
    await page.getByRole('button', { name: t('devices.SaveAndTest') }).click();
    await expect(page.getByText(t('devices.ConfirmTitle'))).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(/572/).first()).toBeVisible({ timeout: 15_000 });
    await shot('terazi-canli-agirlik');
    await page.getByRole('button', { name: t('devices.ConfirmScale') }).click();
  });

  await test.step('Company', async () => {
    await expect(page.getByRole('heading', { name: t('setup.CompanyTitle'), exact: true })).toBeVisible();
    await page.getByRole('button', { name: exact(t('setup.Next')) }).click();
    await expect(page.getByText(t('setup.CompanyNameRequired'))).toBeVisible();
    await page.getByLabel(t('setup.CompanyName')).fill('Deneme Gıda A.Ş.');
    await shot('firma');
    await page.getByRole('button', { name: exact(t('setup.Next')) }).click();
  });

  await test.step('License: a file that is not a license is rejected, then go on free', async () => {
    await expect(page.getByRole('heading', { name: t('license.StepHeading'), exact: true })).toBeVisible();
    await page.getByText(t('license.OptionFile')).click();
    await page.locator('input[type=file]').setInputFiles(await licenses.get('not-a-license'));
    await expect(page.getByText(t('license.Error.invalid_format'))).toBeVisible();
    await shot('lisans-gecersiz-dosya');
    await page.getByText(t('license.OptionFree')).click();
    await shot('lisans-ucretsiz');
    await page.getByRole('button', { name: exact(t('setup.Next')) }).click();
  });

  await test.step('Products: add two products', async () => {
    await expect(page.getByRole('heading', { name: t('setup.ProductsTitle'), exact: true })).toBeVisible();
    for (const [code, name] of [['URN-001', 'Beyaz peynir 500 g'], ['URN-002', 'Kaşar 1 kg']] as const) {
      await page.getByLabel(t('products.Name'), { exact: true }).fill(name);
      await page.getByLabel(t('products.Code'), { exact: true }).fill(code);
      await page.getByRole('button', { name: exact(t('products.Save')) }).click();
      await expect(page.getByText(name)).toBeVisible();
    }
    await shot('urunler');
    await page.getByRole('button', { name: exact(t('setup.Next')) }).click();
  });

  await test.step('Done: go to weighing', async () => {
    await expect(page.getByRole('heading', { name: t('setup.DoneTitle'), exact: true })).toBeVisible();
    await shot('kurulum-tamam');
    await page.getByRole('button', { name: t('setup.GoToWeighing') }).click();
    await expect(page).not.toHaveURL(/\/setup/);
    await expect(page.getByText(/572/).first()).toBeVisible({ timeout: 15_000 });
    await expectCleanScreen(page);
    await shot('tartim-ilk-acilis');
  });
});
