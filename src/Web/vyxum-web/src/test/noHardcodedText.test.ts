import { describe, expect, it } from 'vitest';
import { findHardcodedText } from './hardcodedText';

// Every component file of the app, as raw source. Test files are left out: they hold sample strings.
const sources = import.meta.glob<string>(['../**/*.tsx', '!../**/*.test.tsx'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('no hard-coded UI text', () => {
  it('finds the component files', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(5);
    expect(Object.keys(sources)).toContain('../shell/Sidebar.tsx');
  });

  it('has no UI text written directly in TSX', () => {
    const found = Object.entries(sources).flatMap(([file, source]) => findHardcodedText(source, file));
    expect(found.map((f) => `${f.file}:${f.line} "${f.text}"`)).toEqual([]);
  });
});

describe('hard-coded text checker', () => {
  const check = (source: string) => findHardcodedText(source).map((f) => f.text);

  it.each([
    ['text between tags', '<p>Kaydet</p>', 'Kaydet'],
    ['text next to an expression', '<span>{net} kg</span>', 'kg'],
    ['a literal child', `<p>{'Save'}</p>`, 'Save'],
    ['a template child', '<p>{`Total ${n}`}</p>', 'Total'],
    ['a literal in a conditional', `<p>{ok ? 'Yes' : t('a.No')}</p>`, 'Yes'],
    ['a literal after &&', `<p>{ok && 'Done'}</p>`, 'Done'],
    ['a title attribute', '<button title="Sıfırla" />', 'Sıfırla'],
    ['an aria-label expression', `<nav aria-label={'Menu'} />`, 'Menu'],
    ['a placeholder', '<input placeholder="Ürün ara" />', 'Ürün ara'],
  ])('reports %s', (_name, source, text) => {
    expect(check(source)).toEqual([text]);
  });

  it.each([
    ['translated text', `<p>{t('shell.Save')}</p>`],
    ['class names and ids', '<div className="page" id="x" lang="tr" />'],
    ['comparisons', `<p>{status === 'ready' ? t('a.Ok') : null}</p>`],
    ['numbers and punctuation', '<span>{a} / {b} – 12</span>'],
    ['entities', '<span>&nbsp;</span>'],
    ['data values', '<span>{product.name}</span>'],
    ['code outside JSX', `const key = 'shell.Weighing'; const x = <p>{t(key)}</p>;`],
  ])('allows %s', (_name, source) => {
    expect(check(source)).toEqual([]);
  });
});
