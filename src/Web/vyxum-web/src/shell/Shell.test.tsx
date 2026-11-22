import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { App, AppProviders } from '../App';
import { callsTo, installFakeApi, jsonResponse } from '../test/fakeApi';

function renderApp(path = '/') {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

async function findMenu() {
  return screen.findByRole('navigation');
}

describe('shell menu', () => {
  it('shows all core entries when weighing and products are loaded', async () => {
    installFakeApi({ modules: ['weighing', 'products'] });
    renderApp();

    const menu = await findMenu();
    const links = within(menu).getAllByRole('link').map((link) => link.textContent);
    expect(links).toEqual(['Tartım', 'Kayıtlar', 'Ürünler', 'Cihazlar', 'Modüller ve lisans', 'Hakkında', 'Sözlük']);
    expect(within(menu).getByRole('heading', { name: 'Ayarlar' })).toBeInTheDocument();
    expect(within(menu).getByRole('heading', { name: 'Çekirdek' })).toBeInTheDocument();
  });

  it('hides the module and system groups while they have no entries', async () => {
    installFakeApi({ modules: ['weighing', 'products'] });
    renderApp();

    const menu = await findMenu();
    expect(within(menu).queryByRole('heading', { name: 'Modüller' })).not.toBeInTheDocument();
    expect(within(menu).queryByRole('heading', { name: 'Sistem' })).not.toBeInTheDocument();
  });

  it('shows only products when weighing is not loaded and opens the products page', async () => {
    installFakeApi({ modules: ['products'] });
    renderApp('/');

    const menu = await findMenu();
    const links = within(menu).getAllByRole('link').map((link) => link.textContent);
    expect(links).toEqual(['Ürünler', 'Cihazlar', 'Modüller ve lisans', 'Hakkında', 'Sözlük']);
    expect(await screen.findByRole('heading', { level: 1, name: 'Ürün kartları' })).toBeInTheDocument();
  });

  it('shows weighing and records without products', async () => {
    installFakeApi({ modules: ['weighing'] });
    renderApp('/products');

    const menu = await findMenu();
    const links = within(menu).getAllByRole('link').map((link) => link.textContent);
    expect(links).toEqual(['Tartım', 'Kayıtlar', 'Cihazlar', 'Modüller ve lisans', 'Hakkında', 'Sözlük']);
    // The products route is not available, so the start page opens instead.
    expect(await screen.findByRole('heading', { level: 1, name: 'Tartım' })).toBeInTheDocument();
  });

  it('hides the core group when no core module is loaded', async () => {
    installFakeApi({ modules: [] });
    renderApp();

    const menu = await findMenu();
    // Only the settings screens of the platform remain.
    expect(within(menu).getAllByRole('link').map((link) => link.textContent)).toEqual(['Cihazlar', 'Modüller ve lisans', 'Hakkında', 'Sözlük']);
    expect(within(menu).queryByRole('heading', { name: 'Çekirdek' })).not.toBeInTheDocument();
  });

  it('marks the entry of the current page as active', async () => {
    installFakeApi();
    renderApp('/records');

    const menu = await findMenu();
    expect(within(menu).getByRole('link', { name: 'Kayıtlar' })).toHaveAttribute('aria-current', 'page');
    expect(within(menu).getByRole('link', { name: 'Tartım' })).not.toHaveAttribute('aria-current');
    expect(screen.getByRole('heading', { level: 1, name: 'Tartım kayıtları' })).toBeInTheDocument();
  });

  it('navigates between pages from the menu', async () => {
    installFakeApi();
    const user = userEvent.setup();
    renderApp('/');

    const menu = await findMenu();
    await user.click(within(menu).getByRole('link', { name: 'Ürünler' }));
    expect(screen.getByRole('heading', { level: 1, name: 'Ürün kartları' })).toBeInTheDocument();
  });

  it('shows an error with retry when the module list cannot be loaded', async () => {
    let fail = true;
    const fetchMock = installFakeApi({
      routes: {
        'GET /api/ui/modules': () =>
          fail ? jsonResponse({ title: 'Server error' }, 500) : jsonResponse({ modules: [{ id: 'weighing', version: '1.0.0' }], capabilities: [] }),
      },
    });
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const user = userEvent.setup();
    renderApp();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Sunucuya ulaşılamadı.');

    fail = false;
    await user.click(within(alert).getByRole('button', { name: 'Yeniden dene' }));
    expect(await findMenu()).toBeInTheDocument();
    expect(callsTo(fetchMock, 'GET /api/ui/modules')).toBe(2);
  });
});

describe('language switch', () => {
  it('sets the culture cookie, reloads the strings and updates the document language', async () => {
    const fetchMock = installFakeApi();
    const user = userEvent.setup();
    renderApp('/');

    const menu = await findMenu();
    expect(document.documentElement.lang).toBe('tr');
    expect(callsTo(fetchMock, 'GET /api/ui/strings')).toBe(1);

    await user.click(within(menu).getByRole('button', { name: 'English' }));

    expect(await within(menu).findByRole('link', { name: 'Weighing' })).toBeInTheDocument();
    expect(decodeURIComponent(document.cookie)).toContain('.AspNetCore.Culture=c=en|uic=en');
    expect(callsTo(fetchMock, 'GET /api/ui/strings')).toBe(2);
    expect(document.documentElement.lang).toBe('en');
    expect(screen.getByRole('heading', { level: 1, name: 'Weighing' })).toBeInTheDocument();
    expect(within(menu).getByRole('button', { name: 'English' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(menu).getByRole('button', { name: 'Türkçe' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('switches back to Turkish', async () => {
    document.cookie = `.AspNetCore.Culture=${encodeURIComponent('c=en|uic=en')}; path=/`;
    installFakeApi();
    const user = userEvent.setup();
    renderApp('/');

    const menu = await findMenu();
    expect(within(menu).getByRole('link', { name: 'Records' })).toBeInTheDocument();

    await user.click(within(menu).getByRole('button', { name: 'Türkçe' }));

    expect(await within(menu).findByRole('link', { name: 'Kayıtlar' })).toBeInTheDocument();
    expect(decodeURIComponent(document.cookie)).toContain('.AspNetCore.Culture=c=tr|uic=tr');
    expect(document.documentElement.lang).toBe('tr');
  });
});
