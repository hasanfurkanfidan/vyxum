import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { installFakeApi } from '../test/fakeApi';
import { I18nProvider, useT } from './I18nProvider';

function Probe({ k }: { k: string }) {
  const t = useT();
  return <span data-testid="probe">{t(k)}</span>;
}

describe('useT', () => {
  it('returns the string for a known key', async () => {
    installFakeApi();
    render(
      <I18nProvider>
        <Probe k="shell.Weighing" />
      </I18nProvider>,
    );
    expect(await screen.findByText('Tartım')).toBeInTheDocument();
  });

  it('renders a missing key as the key itself and warns once', async () => {
    installFakeApi();
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    render(
      <I18nProvider>
        <Probe k="shell.DoesNotExist" />
        <Probe k="shell.DoesNotExist" />
      </I18nProvider>,
    );
    await waitFor(() => expect(warn).toHaveBeenCalledWith('Missing UI string: shell.DoesNotExist'));
    expect(screen.getAllByTestId('probe').map((e) => e.textContent)).toEqual(['shell.DoesNotExist', 'shell.DoesNotExist']);
    expect(warn.mock.calls.filter(([m]) => m === 'Missing UI string: shell.DoesNotExist')).toHaveLength(1);
  });

  it('takes the culture from the server response', async () => {
    document.documentElement.lang = '';
    installFakeApi({
      routes: {
        'GET /api/ui/strings': () =>
          new Response(JSON.stringify({ culture: 'en-US', strings: { 'shell.Weighing': 'Weighing' } }), {
            headers: { 'Content-Type': 'application/json' },
          }),
      },
    });
    render(
      <I18nProvider>
        <Probe k="shell.Weighing" />
      </I18nProvider>,
    );
    expect(await screen.findByText('Weighing')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('en');
  });
});
