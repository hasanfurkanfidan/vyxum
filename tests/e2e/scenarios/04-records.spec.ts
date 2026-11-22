import { readFileSync } from 'node:fs';
import { expectCleanScreen, exact, test, expect } from '../support/vyxum';

/** Records of the day: the record saved on the weighing screen, Excel export, and cancelling a record with a reason. */
test('Records: list, export to Excel and cancel a record', async ({ page, t, shot }) => {
  await page.goto('/records');
  await expect(page.getByRole('heading', { name: t('weighing.RecordsTitle'), exact: true })).toBeVisible();
  const row = page.getByRole('row').filter({ hasText: 'Lor peyniri 250 g' }).first();
  await expect(row).toContainText('P-2026-001');
  await expect(row).toContainText(/252/);
  await expectCleanScreen(page);
  await shot('kayitlar');

  await test.step('Export to Excel', async () => {
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: t('weighing.ExportExcel') }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/\.xlsx$/);
    const bytes = readFileSync(await file.path());
    expect(bytes.subarray(0, 2).toString('latin1'), 'An .xlsx file is a ZIP archive').toBe('PK');
  });

  await test.step('Cancel the record: the reason is required', async () => {
    await row.getByRole('button', { name: t('weighing.CancelRecord') }).click();
    const dialog = page.getByRole('dialog', { name: t('weighing.CancelRecord') });
    await dialog.getByRole('button', { name: exact(t('weighing.CancelRecord')) }).click();
    await expect(dialog.getByText(t('weighing.CancelReasonRequired'))).toBeVisible();
    await dialog.getByLabel(t('weighing.CancelReason')).fill('Yanlış ürün seçildi');
    await shot('iptal-nedeni');
    await dialog.getByRole('button', { name: exact(t('weighing.CancelRecord')) }).click();
    await expect(page.getByRole('row').filter({ hasText: 'Lor peyniri 250 g' }).first()).toContainText(t('weighing.Cancelled'));
    await expect(page.getByText(t('weighing.ValidSummary', 0, 1))).toBeVisible();
    await shot('kayit-iptal-edildi');
  });
});
