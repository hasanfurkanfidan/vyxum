import type { Page } from '@playwright/test';
import { expectCleanScreen, exact, test, expect, type LicenseFiles, type LicenseKind, type Translate } from '../support/vyxum';

/** The sample paid module of the open core stands in for the paid modules (see tests/e2e/README.md for real ones). */
const paidModule = { id: 'samples.paid', nameKey: 'samples-paid.ModuleName', titleKey: 'samples-paid.Title', addKey: 'samples-paid.Add' };

async function upload(page: Page, licenses: LicenseFiles, kind: LicenseKind) {
  await page.locator('input[type=file]').setInputFiles(await licenses.get(kind, { modules: [paidModule.id] }));
}

/** A pattern for a translated text whose number placeholder was filled with "#". */
function withNumber(text: string): RegExp {
  return new RegExp(text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace('#', '\\d+'));
}

/** Replacing a license that still works asks first; the answer is yes. */
async function confirmReplaceIfAsked(page: Page, t: Translate) {
  const dialog = page.getByRole('dialog', { name: t('license.ReplaceTitle') });
  try {
    await dialog.waitFor({ state: 'visible', timeout: 3000 });
  } catch {
    return;
  }
  await dialog.getByRole('button', { name: exact(t('license.ReplaceYes')) }).click();
}

function licenseBlock(page: Page, t: Translate) {
  return page.getByRole('region', { name: t('license.SectionLicense') });
}

/**
 * The license life cycle, as in the manual license trial: a locked paid module shows a blurred preview under
 * "Vyxum Pro"; damaged files are refused; a valid file opens the module; an ended license shows the grace strip and
 * then becomes read-only; removing the license locks the module again. Files are signed by the test server's own key.
 */
test.describe.serial('License', () => {
  test('Without a license the paid module is locked and shows its preview', async ({ page, t, shot }) => {
    await page.goto('/');
    // Locked paid modules are listed under "Vyxum Pro" with a lock; licensed ones move to "Modules".
    const proGroup = page.getByRole('navigation').getByRole('region').filter({ has: page.getByRole('heading', { name: t('shell.ProGroup') }) });
    await expect(proGroup.getByRole('link', { name: t(paidModule.nameKey) })).toBeVisible();
    await proGroup.getByRole('link', { name: t(paidModule.nameKey) }).click();
    await expect(page.getByText(t('shell.PreviewLicenseNote'))).toBeVisible();
    await expect(page.getByRole('button', { name: t('shell.PreviewRequestTrial') })).toBeVisible();
    await expectCleanScreen(page);
    await shot('kilitli-modul-onizleme');

    await page.getByRole('button', { name: t('shell.PreviewRequestTrial') }).click();
    await expect(page.getByText(t('license.TrialIntro'))).toBeVisible();
    await shot('deneme-lisansi-istegi');
    await page.getByRole('button', { name: t('license.TrialClose') }).click();

    await page.getByRole('link', { name: t('shell.ModuleLicenseSettings') }).click();
    await expect(licenseBlock(page, t)).toContainText(t('license.StatusNone'));
    await shot('lisans-ekrani-lisanssiz');
  });

  test('Damaged and foreign files are refused', async ({ page, t, shot, licenses }) => {
    await page.goto('/settings/license');
    await upload(page, licenses, 'tampered');
    await expect(page.getByText(t('license.Error.invalid_signature'))).toBeVisible();
    await shot('degistirilmis-dosya-reddedildi');
    await upload(page, licenses, 'not-a-license');
    await expect(page.getByText(t('license.Error.invalid_format'))).toBeVisible();
    await shot('lisans-olmayan-dosya-reddedildi');
    await expect(licenseBlock(page, t)).toContainText(t('license.StatusNone'));
  });

  test('A valid license opens the paid module', async ({ page, t, shot, licenses }) => {
    await page.goto('/settings/license');
    await upload(page, licenses, 'valid');
    await expect(page.getByText(t('license.LoadedFor', 'Deneme Gıda A.Ş.'))).toBeVisible();
    await expect(licenseBlock(page, t)).toContainText(t('license.StatusValid'));
    await expect(licenseBlock(page, t)).toContainText('Deneme Gıda A.Ş.');
    await shot('gecerli-lisans-yuklendi', { fullPage: true });

    await page.getByRole('navigation').getByRole('link', { name: t(paidModule.titleKey) }).click();
    await expect(page.getByRole('heading', { name: t(paidModule.titleKey), exact: true })).toBeVisible();
    await page.getByRole('button', { name: t(paidModule.addKey) }).click();
    await expect(page.getByTestId('samples-paid-entries').getByRole('listitem')).toHaveCount(1);
    await shot('ucretli-modul-acik');
  });

  test('An ended license shows the grace strip, then read-only', async ({ page, t, shot, licenses }) => {
    await page.goto('/settings/license');
    // Replacing a license that works asks first.
    await upload(page, licenses, 'grace');
    const replace = page.getByRole('dialog', { name: t('license.ReplaceTitle') });
    await expect(replace).toContainText(t('license.ReplaceConfirm'));
    await shot('lisans-degistirme-sorusu');
    await replace.getByRole('button', { name: exact(t('license.ReplaceYes')) }).click();
    await expect(licenseBlock(page, t)).toContainText(withNumber(t('license.StatusGrace', '#')));
    await expect(page.getByRole('alert')).toContainText(withNumber(t('license.UploadedGrace', '#')));
    await shot('ek-sure-lisans-ekrani');

    // The strip is shown on every other screen.
    await page.goto('/');
    await expect(page.getByText(withNumber(t('license.BannerGrace', '#')))).toBeVisible();
    await expect(page.getByRole('link', { name: t('license.BannerLink') })).toBeVisible();
    await shot('ek-sure-seridi');

    await page.goto('/settings/license');
    await upload(page, licenses, 'read-only');
    await confirmReplaceIfAsked(page, t);
    await expect(licenseBlock(page, t)).toContainText(t('license.StatusReadOnly'));
    await page.goto('/');
    await expect(page.getByText(t('license.BannerReadOnly'))).toBeVisible();
    await shot('salt-okunur-seridi');

    // The module still shows its records but offers no action.
    await page.getByRole('navigation').getByRole('link', { name: t(paidModule.titleKey) }).click();
    await expect(page.getByTestId('samples-paid-entries').getByRole('listitem')).toHaveCount(1);
    await expect(page.getByRole('button', { name: t(paidModule.addKey) })).toHaveCount(0);
    await shot('ucretli-modul-salt-okunur');
  });

  test('Removing the license locks the module again and keeps its records', async ({ page, t, shot, licenses }) => {
    await page.goto('/settings/license');
    await page.getByRole('button', { name: t('license.Remove') }).click();
    const dialog = page.getByRole('dialog', { name: t('license.Remove') });
    await expect(dialog).toContainText(t('license.RemoveConfirm'));
    await shot('lisans-kaldirma-sorusu');
    await dialog.getByRole('button', { name: exact(t('license.RemoveYes')) }).click();
    await expect(page.getByText(t('license.Removed'))).toBeVisible();
    await expect(licenseBlock(page, t)).toContainText(t('license.StatusNone'));
    await shot('lisans-kaldirildi');

    await page.getByRole('navigation').getByRole('link', { name: t(paidModule.nameKey) }).click();
    await expect(page.getByText(t('shell.PreviewLicenseNote'))).toBeVisible();

    // Loading the license again shows the record made before: nothing was deleted.
    await page.goto('/settings/license');
    await upload(page, licenses, 'valid');
    await expect(licenseBlock(page, t)).toContainText(t('license.StatusValid'));
    await page.getByRole('navigation').getByRole('link', { name: t(paidModule.titleKey) }).click();
    await expect(page.getByTestId('samples-paid-entries').getByRole('listitem')).toHaveCount(1);
    await shot('kayitlar-korundu');
  });
});
