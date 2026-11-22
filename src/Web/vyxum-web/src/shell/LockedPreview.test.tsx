import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { App, AppProviders } from '../App';
import { writeCultureCookie, type Culture } from '../lib/culture';
import type { UiContact, UiModule } from '../lib/api';
import { installFakeApi, jsonResponse, licenseState } from '../test/fakeApi';
import type { ModuleImporter } from './moduleLoader';

const preview = {
  description: 'Kayıtların listesini tutar.\nÜcretli bir modül örneği.',
  imageUrl: '/modules/samples.paid/preview/screen.svg',
};

function lockedModule(change: Partial<UiModule> = {}): UiModule {
  return { id: 'samples.paid', version: '1.0.0', requiresLicense: true, license: 'locked', name: 'Ücretli örnek', ui: null, preview, ...change };
}

function renderApp(path: string, importer?: ModuleImporter) {
  return render(
    <AppProviders {...(importer ? { moduleImporter: importer } : {})}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
}

const noContact: UiContact = { email: null, url: null };

function install(moduleList: UiModule[], extra: { contact?: UiContact; companyName?: string | null; licenseId?: string | null; clockAhead?: boolean } = {}) {
  return installFakeApi({
    moduleList,
    companyName: extra.companyName ?? 'Örnek Kuruyemiş A.Ş.',
    contact: extra.contact ?? noContact,
    routes: {
      'GET /api/license': () => jsonResponse(licenseState({ licenseId: extra.licenseId ?? null, clockAhead: extra.clockAhead ?? false })),
    },
  });
}

const languages: [Culture, { group: string; lock: string; button: string; dialog: string; copy: string; company: string; noContact: string }][] = [
  ['tr', { group: 'Vyxum Pro', lock: 'Kilitli', button: 'Deneme lisansı iste', dialog: 'Deneme lisansı iste', copy: 'Kopyala', company: 'Firma', noContact: "Vyxum'a ya da bayinize iletin." }],
  ['en', { group: 'Vyxum Pro', lock: 'Locked', button: 'Request a trial license', dialog: 'Request a trial license', copy: 'Copy', company: 'Company', noContact: 'Send it to Vyxum or your dealer.' }],
];

describe.each(languages)('Vyxum Pro group and locked screen (%s)', (culture, text) => {
  const prepare = () => writeCultureCookie(culture);

  it('lists only locked modules that declare a preview, with a lock mark, at the bottom of the menu', async () => {
    prepare();
    install([
      lockedModule(),
      lockedModule({ id: 'other.locked', name: 'Önizlemesiz', preview: null }),
      lockedModule({ id: 'licensed.one', name: 'Lisanslı', license: 'licensed', preview }),
      lockedModule({ id: 'readonly.one', name: 'Salt okunur', license: 'readOnly', preview }),
    ]);
    renderApp('/');

    const menu = await screen.findByRole('navigation');
    const group = (await within(menu).findByRole('heading', { name: text.group })).closest('section') as HTMLElement;
    const links = within(group).getAllByRole('link');
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAttribute('href', '/m/samples.paid');
    expect(within(links[0]!).getByRole('img', { name: text.lock })).toBeInTheDocument();
    expect(within(menu).queryByText('Önizlemesiz')).not.toBeInTheDocument();
    expect(within(menu).queryByText('Lisanslı')).not.toBeInTheDocument();
    expect(within(menu).queryByText('Salt okunur')).not.toBeInTheDocument();
    // The group is the last one before the licensee and language switch.
    const headings = within(menu).getAllByRole('heading').map((h) => h.id);
    expect(headings[headings.length - 1]).toBe('nav-group-pro');
  });

  it('has no Vyxum Pro group when no locked module has a preview', async () => {
    prepare();
    install([lockedModule({ preview: null })]);
    renderApp('/');
    await screen.findByRole('navigation');
    expect(screen.queryByRole('heading', { name: text.group })).not.toBeInTheDocument();
  });

  it('shows the blurred sample, the two lines, the button and the license link, and never loads the screens', async () => {
    prepare();
    install([lockedModule()]);
    const importer = vi.fn<ModuleImporter>();
    renderApp('/m/samples.paid', importer);

    expect(await screen.findByRole('heading', { level: 1, name: 'Ücretli örnek' })).toBeInTheDocument();
    expect(screen.getByText('Kayıtların listesini tutar.')).toBeInTheDocument();
    expect(screen.getByText('Ücretli bir modül örneği.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: text.button })).toBeInTheDocument();
    expect(screen.getAllByRole('link').some((l) => l.getAttribute('href') === '/settings/license')).toBe(true);

    const frame = document.querySelector('.preview__frame')!;
    expect(frame).toHaveAttribute('aria-hidden', 'true');
    expect(frame.querySelector('img')).toHaveAttribute('src', preview.imageUrl);
    expect(importer).not.toHaveBeenCalled();
  });

  it('keeps the plain placeholder for a locked module without a preview', async () => {
    prepare();
    install([lockedModule({ preview: null })]);
    renderApp('/m/samples.paid');
    expect(await screen.findByRole('link', { name: /lisans ayarlarını aç|open license settings/i })).toBeInTheDocument();
    expect(document.querySelector('.preview')).toBeNull();
  });

  it('builds the request text in the dialog and copies it', async () => {
    prepare();
    install([lockedModule()], { licenseId: 'LIC-9', companyName: 'Örnek Kuruyemiş A.Ş.' });
    const writeText = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup({ document });
    // After setup: the user-event library installs its own clipboard.
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    renderApp('/m/samples.paid');

    await user.click(await screen.findByRole('button', { name: text.button }));
    const dialog = await screen.findByRole('dialog', { name: text.dialog });
    const request = within(dialog).getByRole<HTMLTextAreaElement>('textbox');
    await vi.waitFor(() => expect(request.value).toContain('LIC-9'));
    expect(request.value).toContain(`${text.company}: Örnek Kuruyemiş A.Ş.`);
    expect(request.value).toContain('Ücretli örnek (samples.paid)');
    expect(request.value).toContain('0.1.0');
    expect(request.value).toMatch(/\d{4}-\d{2}-\d{2}/);

    await user.click(within(dialog).getByRole('button', { name: text.copy }));
    expect(writeText).toHaveBeenCalledWith(request.value);
    expect(await within(dialog).findByRole('status')).toBeInTheDocument();
  });

  it('selects the text when the clipboard API is missing', async () => {
    prepare();
    install([lockedModule()]);
    const user = userEvent.setup({ document });
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
    renderApp('/m/samples.paid');

    await user.click(await screen.findByRole('button', { name: text.button }));
    const dialog = await screen.findByRole('dialog');
    const request = within(dialog).getByRole<HTMLTextAreaElement>('textbox');
    await user.click(within(dialog).getByRole('button', { name: text.copy }));
    expect(document.activeElement).toBe(request);
    expect(request.selectionEnd - request.selectionStart).toBe(request.value.length);
    expect(within(dialog).queryByRole('status')).not.toBeInTheDocument();
  });

  it('shows the configured contact, or asks to pass the request on without an address', async () => {
    prepare();
    install([lockedModule()], { contact: { email: 'bayi@example.test', url: 'https://bayi.example.test' } });
    const user = userEvent.setup({ document });
    const first = renderApp('/m/samples.paid');
    await user.click(await screen.findByRole('button', { name: text.button }));
    let dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('link', { name: 'bayi@example.test' })).toHaveAttribute('href', 'mailto:bayi@example.test');
    expect(within(dialog).getByRole('link', { name: 'https://bayi.example.test' })).toHaveAttribute('href', 'https://bayi.example.test');
    expect(within(dialog).queryByText(text.noContact)).not.toBeInTheDocument();
    first.unmount();

    install([lockedModule()]);
    renderApp('/m/samples.paid');
    await user.click(await screen.findByRole('button', { name: text.button }));
    dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(text.noContact)).toBeInTheDocument();
    expect(within(dialog).queryAllByRole('link')).toHaveLength(0);

    await user.click(within(dialog).getByRole('button', { name: culture === 'tr' ? 'Kapat' : 'Close' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe.each([
  ['tr', 'Bilgisayar saati son görülen zamandan çok ileride'],
  ['en', "This computer's clock is far ahead"],
] as const)('clockAhead note (%s)', (culture, note) => {
  it('is a quiet line on the license page only, never in the shell', async () => {
    writeCultureCookie(culture);
    installFakeApi({
      license: { status: 'valid', licensee: 'Örnek A.Ş.', graceDaysLeft: null },
      routes: { 'GET /api/license': () => jsonResponse(licenseState({ clockAhead: true })) },
    });
    renderApp('/records');
    await screen.findByRole('navigation');
    expect(screen.queryByText(new RegExp(note))).not.toBeInTheDocument();
    expect(document.querySelector('.license-banner')).toBeNull();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('appears on the license page and is absent when the flag is false', async () => {
    writeCultureCookie(culture);
    installFakeApi({ routes: { 'GET /api/license': () => jsonResponse(licenseState({ clockAhead: true })) } });
    const view = renderApp('/settings/license');
    const line = await screen.findByText(new RegExp(note));
    expect(line).not.toHaveAttribute('role', 'alert');
    expect(line.closest('.license-banner')).toBeNull();
    view.unmount();

    installFakeApi({ routes: { 'GET /api/license': () => jsonResponse(licenseState({ clockAhead: false })) } });
    renderApp('/settings/license');
    await screen.findByRole('heading', { level: 1 });
    await vi.waitFor(() => expect(screen.getAllByText(/\S/).length).toBeGreaterThan(3));
    expect(screen.queryByText(new RegExp(note))).not.toBeInTheDocument();
  });
});
