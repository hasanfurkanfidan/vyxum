import { describe, expect, it } from 'vitest';
import { foldForSearch, markMatches, searchGlossary, searchWords, type GlossarySection, type GlossaryTerm } from './glossary';

function term(id: string, name: string, text: string, other = '', inVyxum: string | null = null): GlossaryTerm {
  return { id, name, text, inVyxum, example: null, otherNames: other ? [{ culture: 'en', name: other }] : [] };
}

const sections: GlossarySection[] = [
  {
    id: 'S1',
    title: 'Tartımın temelleri',
    intro: '',
    terms: [
      term('T1.01', 'Brüt', 'Toplam ağırlık: ürün ile ambalaj.', 'Gross'),
      term('T1.02', 'Dara', 'Kabın ağırlığı. Brütten düşülür.', 'Tare', '"Dara al" tuşu.'),
      term('T1.03', 'Sıfırlama', 'Boş terazi 0 gösterir. Daradan farklıdır.', 'Zero'),
    ],
  },
  { id: 'S2', title: 'Etiket', intro: '', terms: [term('T2.01', 'Ambalaj darası', 'Ürün kartındaki dara.', 'Packaging tare')] },
];

describe('glossary search', () => {
  it('folds Turkish letters and case without changing the length', () => {
    expect(foldForSearch('SIFIRLAMA İçerik Şeker')).toBe('sifirlama icerik seker');
    expect(foldForSearch('x̄ = 251,4 g')).toHaveLength('x̄ = 251,4 g'.length);
    expect(searchWords('  Dara,  al ')).toEqual(['dara', 'al']);
  });

  it('puts names that start with the word first, then names that contain it, then the text', () => {
    expect(searchGlossary(sections, 'dara').map((h) => h.term.id)).toEqual(['T1.02', 'T2.01', 'T1.03']);
  });

  it('finds a term without Turkish letters and by its English name', () => {
    expect(searchGlossary(sections, 'sifir').map((h) => h.term.id)).toEqual(['T1.03']);
    expect(searchGlossary(sections, 'tare').map((h) => h.term.id)).toEqual(['T1.02', 'T2.01']);
    // An exact English name wins over a longer Turkish name found by its English name.
    const pair = { id: 'S0', title: '', intro: '', terms: [term('T0.01', 'Dara ve sıfırlama', '', 'Tare and zero')] };
    expect(searchGlossary([pair, ...sections], 'tare').map((h) => h.term.id)).toEqual(['T1.02', 'T0.01', 'T2.01']);
  });

  it('needs every word to match and returns nothing for an empty search', () => {
    expect(searchGlossary(sections, 'dara kart').map((h) => h.term.id)).toEqual(['T2.01']);
    expect(searchGlossary(sections, '   ')).toEqual([]);
    expect(searchGlossary(sections, 'palet')).toEqual([]);
  });

  it('marks the matched letters in the original text', () => {
    expect(markMatches('Sıfırlama', ['sifir'])).toEqual([
      { text: 'Sıfır', match: true },
      { text: 'lama', match: false },
    ]);
    expect(markMatches('Dara', [])).toEqual([{ text: 'Dara', match: false }]);
  });
});
