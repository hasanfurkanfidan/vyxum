import { expectCleanScreen, exact, test, expect, type Translate } from '../support/vyxum';
import type { Page } from '@playwright/test';

function live(page: Page, t: Translate) {
  return page.getByRole('region', { name: t('weighing.LiveWeight') });
}

/**
 * The weighing screen against the simulated scale, through the real agent: live weight, unstable and overloaded
 * readings, the target gauge, saving a record, software tare, zero, and a scale that stops answering.
 */
test.describe('Weighing', () => {
  test.beforeEach(async ({ scale }) => {
    await scale.silent(false);
    await scale.set({ value: 0, unit: 'g', state: 'stable' });
  });

  test('Live weight follows the scale', async ({ page, t, shot, scale, allowedBrowserErrors }) => {
    await page.goto('/');
    await expect(page.getByText(t('weighing.DeviceConnected'), { exact: false }).first()).toBeVisible({ timeout: 15_000 });
    await scale.set({ value: 1250, unit: 'g', state: 'stable' });
    await expect(live(page, t)).toContainText(/1[.,]?250/);
    await expect(live(page, t)).toContainText(t('weighing.StateStable'));
    await expectCleanScreen(page);
    await shot('canli-agirlik-1250g');

    await scale.set({ value: 1310, state: 'motion' });
    await expect(live(page, t)).toContainText(t('weighing.StateMotion'));
    await shot('kararsiz');
    allowedBrowserErrors.push(/weighing\.NotStable/);
    await page.getByRole('button', { name: exact(t('weighing.SaveRecord')) }).click();
    await expect(page.getByText(t('weighing.NotStable'))).toBeVisible();
    await shot('kararsizken-kayit-reddedildi');

    await scale.set({ state: 'overload' });
    await expect(live(page, t)).toContainText(t('weighing.StateOverload'));
    await shot('asiri-yuk');
  });

  test('Target gauge and saving a record with product and lot', async ({ page, t, shot, scale }) => {
    await page.goto('/');
    await page.getByRole('region', { name: t('weighing.ProductSelection') }).getByText('Lor peyniri 250 g').click();
    await expect(page.getByLabel(t('weighing.Target'))).toBeVisible();

    await scale.set({ value: 240, unit: 'g', state: 'stable' });
    await expect(page.getByText(t('weighing.TargetUnder'))).toBeVisible();
    await shot('hedef-altinda');
    await scale.set({ value: 270, unit: 'g', state: 'stable' });
    await expect(page.getByText(t('weighing.TargetOver'))).toBeVisible();
    await shot('hedef-ustunde');
    await scale.set({ value: 252, unit: 'g', state: 'stable' });
    await expect(page.getByText(t('weighing.TargetWithin'))).toBeVisible();
    await shot('hedef-icinde');

    await page.getByLabel(t('weighing.Lot'), { exact: true }).fill('P-2026-001');
    await page.getByRole('button', { name: exact(t('weighing.SaveRecord')) }).click();
    const recent = page.getByRole('region', { name: t('weighing.RecentRecords') });
    await expect(recent).toContainText('Lor peyniri 250 g');
    await expect(recent).toContainText(/252/);
    await shot('kayit-kaydedildi');
  });

  test('Software tare and zero send no forbidden command to the scale', async ({ page, t, shot, scale }) => {
    await page.goto('/');
    await scale.set({ value: 35, unit: 'g', state: 'stable' });
    await expect(live(page, t)).toContainText(/35/);
    await page.getByRole('button', { name: exact(t('weighing.TakeTare')) }).click();
    await scale.set({ value: 535 });
    await expect(live(page, t)).toContainText(/500/);
    await shot('dara-alindi');
    await page.getByRole('button', { name: exact(t('weighing.ClearTare')) }).click();

    // Zero an empty pan, as an operator does; the simulated scale keeps the zero offset like a real one.
    await scale.set({ value: 0 });
    await expect(live(page, t)).toContainText(t('weighing.StateStable'));
    await page.getByRole('button', { name: exact(t('weighing.Zero')) }).click();
    await expect(page.getByText(t('weighing.ZeroDone'))).toBeVisible();
    await shot('sifirlandi');

    const status = await scale.status();
    expect(status.forbiddenCommands, 'Tare, immediate zero or reset reached the scale').toEqual([]);
    expect(status.commands.filter((c) => c === 'Z'), 'One press of Zero sends one Z').toHaveLength(1);
  });

  test('A scale that stops answering is shown and comes back by itself', async ({ page, t, shot, scale, allowedBrowserErrors }) => {
    await page.goto('/');
    await scale.set({ value: 410, unit: 'g', state: 'stable' });
    await expect(live(page, t)).toContainText(/410/);
    await scale.silent(true);
    await expect(page.getByText(t('weighing.DeviceConnected'))).toHaveCount(0, { timeout: 30_000 });
    await shot('terazi-cevap-vermiyor');
    // The last weight stays on the screen, greyed out; saving it must be refused.
    allowedBrowserErrors.push(/weighing\.DeviceNotConnected/);
    await page.getByRole('button', { name: exact(t('weighing.SaveRecord')) }).click();
    await expect(page.getByText(t('weighing.DeviceNotConnected'))).toBeVisible();
    await shot('baglanti-yokken-kayit-reddedildi');
    await scale.silent(false);
    await expect(page.getByText(t('weighing.DeviceConnected'), { exact: false }).first()).toBeVisible({ timeout: 45_000 });
    await expect(live(page, t)).toContainText(/410/);
    await shot('terazi-geri-geldi');
  });
});
