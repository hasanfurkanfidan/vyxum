import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { App, AppProviders } from '../App';
import type { UiLicense } from '../lib/api';
import { installFakeApi, jsonResponse, licenseState } from '../test/fakeApi';

function renderApp(path: string) {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

const graceText = 'Lisans süresi doldu; ücretli modüller 12 gün daha çalışır.';
const readOnlyText = 'Lisans süresi bitti; ücretli modüller salt okunur.';

describe('license strip', () => {
  it('appears in the grace period with the days left and links to the license screen', async () => {
    installFakeApi({ license: { status: 'grace', licensee: 'Örnek A.Ş.', graceDaysLeft: 12 } });
    const user = userEvent.setup();
    renderApp('/records');

    const strip = await screen.findByText(graceText);
    expect(strip.closest('[role="status"]')).toBeInTheDocument();
    await user.click(within(strip.closest('.license-banner') as HTMLElement).getByRole('link', { name: 'Lisans ayarları' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Modüller ve lisans' })).toBeInTheDocument();
  });

  it('appears when the license is read-only', async () => {
    installFakeApi({ license: { status: 'readOnly', licensee: 'Örnek A.Ş.', graceDaysLeft: null } });
    renderApp('/records');
    expect(await screen.findByText(readOnlyText)).toBeInTheDocument();
  });

  it.each<UiLicense>([
    { status: 'valid', licensee: 'Örnek A.Ş.', graceDaysLeft: null },
    { status: 'none', licensee: null, graceDaysLeft: null },
    { status: 'invalid', licensee: null, graceDaysLeft: null },
  ])('is not shown for the state $status', async (license) => {
    installFakeApi({ license });
    renderApp('/records');
    await screen.findByRole('navigation');
    expect(screen.queryByText(graceText)).not.toBeInTheDocument();
    expect(screen.queryByText(readOnlyText)).not.toBeInTheDocument();
  });

  it('is not shown for an older host that sends no license state', async () => {
    installFakeApi();
    renderApp('/records');
    await screen.findByRole('navigation');
    expect(document.querySelector('.license-banner')).toBeNull();
  });

  it('is left out on the license screen, which shows the same state', async () => {
    installFakeApi({
      license: { status: 'grace', licensee: 'Örnek A.Ş.', graceDaysLeft: 12 },
      routes: { 'GET /api/license': () => jsonResponse(licenseState({ status: 'grace', graceDaysLeft: 12 })) },
    });
    renderApp('/settings/license');
    await screen.findByText('süresi doldu, ek süre 12 gün');
    expect(screen.queryByText(graceText)).not.toBeInTheDocument();
  });
});

describe('licensee in the menu', () => {
  it('shows the licensee name under the menu when a license verifies', async () => {
    installFakeApi({ license: { status: 'valid', licensee: 'Örnek Kuruyemiş A.Ş.', graceDaysLeft: null } });
    renderApp('/records');
    const menu = await screen.findByRole('navigation');
    expect(within(menu).getByText('Örnek Kuruyemiş A.Ş.')).toBeInTheDocument();
    expect(within(menu).getByText('Lisans sahibi')).toBeInTheDocument();
  });

  it('shows nothing without a license', async () => {
    installFakeApi({ license: { status: 'none', licensee: null, graceDaysLeft: null } });
    renderApp('/records');
    const menu = await screen.findByRole('navigation');
    expect(within(menu).queryByText('Lisans sahibi')).not.toBeInTheDocument();
  });
});

describe('placeholder of a module without a license', () => {
  it('is plain text with a heading, an explanation and a link to the license screen', async () => {
    installFakeApi({ moduleList: [{ id: 'labels', version: '1.0.0', requiresLicense: true, license: 'locked' }] });
    renderApp('/m/labels');

    expect(await screen.findByRole('heading', { level: 1, name: 'Bu modül için lisans gerekiyor.' })).toBeInTheDocument();
    expect(screen.getByText('Bu modülü içeren bir lisans dosyası yükleyin.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Lisans ayarlarını aç' })).toHaveAttribute('href', '/settings/license');
  });
});
