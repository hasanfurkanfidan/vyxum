import { expectCleanScreen, exact, test, expect } from '../support/vyxum';

/** Product cards: add a product with a target weight and tolerances (the weighing scenario uses it), refuse a duplicate PLU, find it. */
test('Products: add a card, refuse a duplicate PLU, find by PLU', async ({ page, t, shot }) => {
  await page.goto('/products');
  await expect(page.getByRole('heading', { name: t('products.Title'), exact: true })).toBeVisible();
  await expect(page.getByText('Beyaz peynir 500 g')).toBeVisible();
  await expectCleanScreen(page);
  await shot('urun-listesi');

  await test.step('Add a product with a target weight', async () => {
    await page.getByRole('button', { name: t('products.AddProduct') }).click();
    await page.getByLabel(t('products.Name'), { exact: true }).fill('Lor peyniri 250 g');
    await page.getByLabel(t('products.Code'), { exact: true }).fill('URN-003');
    await page.getByLabel(t('products.UnitPricePerKg'), { exact: true }).fill('180');
    await page.getByLabel(t('products.TargetWeight'), { exact: true }).fill('0.250');
    await page.getByLabel(t('products.LowerTolerance'), { exact: true }).fill('0.005');
    await page.getByLabel(t('products.UpperTolerance'), { exact: true }).fill('0.010');
    // Sold by the piece: the target weight is the weight of one piece (work orders in pieces turn into kg with it).
    await page.getByLabel(t('products.UnitCostPerKg'), { exact: true }).fill('95');
    await page.getByLabel(t('products.SaleUnit'), { exact: true }).selectOption('Piece');
    await expect(page.getByText(t('products.SaleUnitHint'))).toBeVisible();
    await shot('yeni-urun-formu');
    await page.getByRole('button', { name: exact(t('products.Save')) }).click();
    await expect(page.getByText(t('products.Saved'))).toBeVisible();
    await expect(page.getByText('Lor peyniri 250 g').first()).toBeVisible();
    await shot('yeni-urun-kaydedildi');
  });

  await test.step('A second product with the same PLU is refused', async () => {
    await page.getByRole('button', { name: t('products.AddProduct') }).click();
    await page.getByLabel(t('products.Name'), { exact: true }).fill('Aynı kodlu ürün');
    await page.getByLabel(t('products.Code'), { exact: true }).fill('URN-003');
    await page.getByRole('button', { name: exact(t('products.Save')) }).click();
    await expect(page.getByText(t('products.CodeTaken'))).toBeVisible();
    await shot('ayni-plu-reddedildi');
  });

  await test.step('Find it by PLU', async () => {
    await page.goto('/products');
    await page.getByPlaceholder(t('products.SearchPlaceholder')).fill('URN-003');
    await expect(page.getByText('Lor peyniri 250 g').first()).toBeVisible();
    await expect(page.getByText('Kaşar 1 kg')).toHaveCount(0);
    await shot('plu-ile-arama');
  });
});
