import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { App, AppProviders } from '../App';
import type { Glossary } from '../lib/glossary';
import { readCultureCookie } from '../lib/culture';
import { installFakeApi, jsonResponse } from '../test/fakeApi';

const turkish: Glossary = {
  culture: 'tr',
  sections: [
    {
      id: 'S0',
      title: 'En çok karıştırılanlar',
      intro: 'Birbirine benzeyen kelimeler.',
      terms: [{ id: 'T0.01', name: 'Dara ve sıfırlama', text: 'Sıfırlama göstergeyi 0 yapar.', inVyxum: null, example: null, otherNames: [{ culture: 'en', name: 'Tare and zero' }] }],
    },
    {
      id: 'S1',
      title: 'Tartımın temelleri',
      intro: 'Her ekranın altında yatan kelimeler.',
      terms: [
        { id: 'T1.01', name: 'Brüt', text: 'Toplam ağırlık.', inVyxum: 'Tartım kaydında saklanır.', example: '1,260 kg.', otherNames: [{ culture: 'en', name: 'Gross' }] },
        { id: 'T1.02', name: 'Dara', text: 'Kabın ağırlığı.', inVyxum: 'Tartım ekranında "Dara al".', example: 'Boş poşet 0,012 kg.', otherNames: [{ culture: 'en', name: 'Tare' }] },
        { id: 'T1.03', name: 'PLU', text: 'Ürünün kısa kodu.', inVyxum: null, example: null, otherNames: [] },
      ],
    },
  ],
};

const english: Glossary = {
  culture: 'en',
  sections: [{ id: 'S1', title: 'Weighing basics', intro: 'The words behind every screen.', terms: [{ id: 'T1.02', name: 'Tare', text: 'The weight of the container.', inVyxum: null, example: null, otherNames: [{ culture: 'tr', name: 'Dara' }] }] }],
};

function renderGlossary() {
  const fetchMock = installFakeApi({
    routes: { 'GET /api/glossary': () => jsonResponse(readCultureCookie() === 'en' ? english : turkish) },
  });
  render(
    <AppProviders>
      <MemoryRouter initialEntries={['/glossary']}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  );
  return fetchMock;
}

describe('glossary page', () => {
  it('is in the menu and lists every section with its terms', async () => {
    renderGlossary();

    expect(await screen.findByRole('heading', { level: 2, name: 'En çok karıştırılanlar' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Sözlük' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sözlük' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('heading', { level: 2, name: 'Tartımın temelleri' })).toBeInTheDocument();
    expect(screen.getByText('4 terim')).toBeInTheDocument();

    const tare = screen.getByRole('article', { name: 'Dara' });
    expect(within(tare).getByText('Kabın ağırlığı.')).toBeInTheDocument();
    expect(within(tare).getByText('Tartım ekranında "Dara al".')).toBeInTheDocument();
    expect(within(tare).getByText('Boş poşet 0,012 kg.')).toBeInTheDocument();
    expect(tare).toHaveTextContent('İngilizcesi: Tare');
    expect(screen.getByRole('article', { name: 'PLU' })).not.toHaveTextContent('İngilizcesi');
  });

  it('shows one section when its chip is pressed', async () => {
    const user = userEvent.setup();
    renderGlossary();
    await user.click(await screen.findByRole('button', { name: 'Tartımın temelleri' }));

    expect(screen.getByRole('button', { name: 'Tartımın temelleri' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByRole('heading', { level: 2, name: 'En çok karıştırılanlar' })).not.toBeInTheDocument();
    expect(screen.getByText('3 terim')).toBeInTheDocument();
  });

  it('finds terms as the user types, without Turkish letters, and says when nothing is found', async () => {
    const user = userEvent.setup();
    renderGlossary();
    const search = await screen.findByRole('searchbox', { name: 'Sözlükte ara' });

    await user.type(search, 'dara');
    const results = screen.getAllByRole('article').map((a) => a.getAttribute('aria-labelledby'));
    expect(results).toEqual(['term-T1.02', 'term-T0.01']);
    expect(screen.getByText('2 sonuç')).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Bölümler' })).not.toBeInTheDocument();

    await user.clear(search);
    await user.type(search, 'sifirlama');
    expect(screen.getAllByRole('article')).toHaveLength(1);

    await user.clear(search);
    await user.type(search, 'palet');
    expect(screen.getByText('“palet” için bir şey bulunamadı.')).toBeInTheDocument();

    await user.click(screen.getAllByRole('button', { name: 'Temizle' })[0]!);
    expect(search).toHaveValue('');
    expect(screen.getAllByRole('article')).toHaveLength(4);
  });

  it('loads the glossary again in English when the language changes', async () => {
    const user = userEvent.setup();
    renderGlossary();
    await screen.findByRole('article', { name: 'Dara' });

    await user.click(screen.getByRole('button', { name: 'English' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Glossary' })).toBeInTheDocument();
    expect(await screen.findByRole('article', { name: 'Tare' })).toHaveTextContent('Turkish: Dara');
  });
});
