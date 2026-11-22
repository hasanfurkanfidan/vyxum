import { apiGet, type RequestOptions } from './api';

/** Name of a term in another language of the user interface. */
export interface GlossaryName {
  culture: string;
  name: string;
}

export interface GlossaryTerm {
  id: string;
  name: string;
  text: string;
  /** Where the term shows up in Vyxum; null when the term has no such line. */
  inVyxum: string | null;
  example: string | null;
  otherNames: GlossaryName[];
}

export interface GlossarySection {
  id: string;
  title: string;
  intro: string;
  terms: GlossaryTerm[];
}

export interface Glossary {
  culture: string;
  sections: GlossarySection[];
}

/** GET /api/glossary in the current UI language. */
export function getGlossary(options?: RequestOptions): Promise<Glossary> {
  return apiGet<Glossary>('/api/glossary', options);
}

// Letters folded for search, so that "sifirlama" finds "Sıfırlama" and "Dara" finds "dara". One character in, one out:
// match positions in the folded text are positions in the original text.
const folded: Record<string, string> = {
  ı: 'i', ş: 's', ğ: 'g', ç: 'c', ö: 'o', ü: 'u', â: 'a', î: 'i', û: 'u', é: 'e', '’': "'",
};

/** The text in lower case without Turkish marks, of the same length as `text`. */
export function foldForSearch(text: string): string {
  let result = '';
  for (const char of text) {
    const lower = char === 'I' ? 'ı' : char === 'İ' ? 'i' : (char.toLocaleLowerCase('tr')[0] ?? char);
    const plain = folded[lower] ?? lower;
    // Characters outside the basic plane count as two code units; keep the length in code units equal.
    result += char.length === 2 ? plain.padEnd(2, ' ') : plain;
  }
  return result;
}

/** Words of a search, folded. */
export function searchWords(query: string): string[] {
  return foldForSearch(query)
    .split(/[\s,;/]+/)
    .map((word) => word.trim())
    .filter((word) => word.length > 0);
}

export interface GlossaryHit {
  term: GlossaryTerm;
  section: GlossarySection;
}

function startsAWord(haystack: string, word: string): boolean {
  return haystack.startsWith(word) || haystack.includes(` ${word}`) || haystack.includes(`(${word}`) || haystack.includes(`/${word}`);
}

/**
 * Terms that contain every word of the search, best first: the exact name, then names that start with the words, then the name contains
 * them, then the name in the other language, then the explanation. Ties keep the glossary order.
 */
export function searchGlossary(sections: readonly GlossarySection[], query: string): GlossaryHit[] {
  const words = searchWords(query);
  if (words.length === 0) return [];

  const scored: { hit: GlossaryHit; score: number; order: number }[] = [];
  let order = 0;
  for (const section of sections) {
    for (const term of section.terms) {
      order += 1;
      const name = foldForSearch(term.name);
      const other = foldForSearch(term.otherNames.map((n) => n.name).join(' / '));
      const body = foldForSearch([term.text, term.inVyxum ?? '', term.example ?? ''].join(' '));
      // The exact name, in any language, comes before longer names that start with it ("Dara" before "Dara ve sıfırlama").
      const whole = words.join(' ');
      let score = name.trim() === whole || term.otherNames.some((n) => foldForSearch(n.name).trim() === whole) ? -1 : 0;
      let all = true;
      for (const word of words) {
        if (startsAWord(name, word)) score += 0;
        else if (name.includes(word)) score += 1;
        else if (startsAWord(other, word)) score += 2;
        else if (other.includes(word)) score += 3;
        else if (body.includes(word)) score += 6;
        else {
          all = false;
          break;
        }
      }
      if (all) scored.push({ hit: { term, section }, score, order });
    }
  }

  return scored.sort((a, b) => a.score - b.score || a.order - b.order).map((s) => s.hit);
}

/** Parts of `text` with the places where a search word matches marked, for highlighting. */
export function markMatches(text: string, words: readonly string[]): { text: string; match: boolean }[] {
  if (words.length === 0) return [{ text, match: false }];
  const haystack = foldForSearch(text);
  const marked = new Array<boolean>(text.length).fill(false);
  for (const word of words) {
    let from = 0;
    for (;;) {
      const at = haystack.indexOf(word, from);
      if (at < 0) break;
      for (let i = at; i < at + word.length; i++) marked[i] = true;
      from = at + word.length;
    }
  }

  const parts: { text: string; match: boolean }[] = [];
  for (let i = 0; i < text.length; i++) {
    const match = marked[i] ?? false;
    const last = parts[parts.length - 1];
    if (last && last.match === match) last.text += text[i];
    else parts.push({ text: text[i] ?? '', match });
  }
  return parts;
}
