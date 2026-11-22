import { useEffect, useMemo, useRef, useState } from 'react';
import { useCulture, useT } from '../i18n/I18nProvider';
import { NetworkError } from '../lib/api';
import { getGlossary, markMatches, searchGlossary, searchWords, type GlossarySection, type GlossaryTerm } from '../lib/glossary';
import { fill } from '../components/devices/deviceText';
import { PageHeader } from '../shell/PageHeader';
import '../styles/glossary.css';

const allSections = 'all';

/** Help > Glossary: free, searchable explanations of the words used on the screens and in the trade. */
export function GlossaryPage() {
  const t = useT();
  const culture = useCulture();
  const [sections, setSections] = useState<GlossarySection[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [tick, setTick] = useState(0);
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<string>(allSections);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    setFailed(false);
    getGlossary({ signal: controller.signal })
      .then((result) => {
        if (!controller.signal.aborted) setSections(result.sections);
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        if (!(error instanceof NetworkError)) console.error('Loading the glossary failed.', error);
        setFailed(true);
      });
    return () => controller.abort();
    // The glossary comes in the current language: load it again when the language changes.
  }, [tick, culture]);

  const words = useMemo(() => searchWords(query), [query]);
  const hits = useMemo(() => (sections && words.length > 0 ? searchGlossary(sections, query) : []), [sections, query, words]);
  const total = useMemo(() => sections?.reduce((sum, s) => sum + s.terms.length, 0) ?? 0, [sections]);

  const header = <PageHeader eyebrow={t('shell.Help')} title={t('glossary.Title')} />;

  if (!sections) {
    return (
      <div className="page">
        {header}
        {failed ? (
          <div className="status" role="alert">
            <p className="status__text">{t('shell.Error')}</p>
            <button type="button" className="button" onClick={() => setTick((value) => value + 1)}>
              {t('shell.Retry')}
            </button>
          </div>
        ) : (
          <div className="status" role="status">
            <p className="status__text">{t('shell.Loading')}</p>
          </div>
        )}
      </div>
    );
  }

  const searching = words.length > 0;
  const shown = selected === allSections ? sections : sections.filter((s) => s.id === selected);

  const clear = () => {
    setQuery('');
    searchRef.current?.focus();
  };

  return (
    <div className="page glossary">
      {header}
      <p className="glossary__intro">{t('glossary.Intro')}</p>

      <div className="glossary__search" role="search">
        <label className="field glossary__field">
          <span className="field__label">{t('glossary.SearchLabel')}</span>
          <input
            ref={searchRef}
            className="input glossary__input"
            type="search"
            value={query}
            placeholder={t('glossary.SearchPlaceholder')}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Escape') setQuery('');
            }}
          />
        </label>
        {query ? (
          <button type="button" className="button button--quiet glossary__clear" onClick={clear}>
            {t('glossary.Clear')}
          </button>
        ) : null}
      </div>

      {searching ? null : (
        <nav className="glossary__chips" aria-label={t('glossary.Sections')}>
          {[{ id: allSections, title: t('glossary.All') }, ...sections].map((section) => (
            <button
              key={section.id}
              type="button"
              className={section.id === selected ? 'glossary__chip glossary__chip--active' : 'glossary__chip'}
              aria-pressed={section.id === selected}
              onClick={() => setSelected(section.id)}
            >
              {section.title}
            </button>
          ))}
        </nav>
      )}

      <p className="glossary__count" role="status">
        {searching ? fill(t('glossary.ResultCount'), hits.length) : fill(t('glossary.TermCount'), selected === allSections ? total : (shown[0]?.terms.length ?? 0))}
      </p>

      {searching ? (
        hits.length > 0 ? (
          <ol className="glossary__list" aria-label={t('glossary.Title')}>
            {hits.map(({ term, section }) => (
              <li key={term.id}>
                <TermEntry term={term} words={words} sectionTitle={section.title} />
              </li>
            ))}
          </ol>
        ) : (
          <div className="status glossary__empty">
            <p className="status__text">{fill(t('glossary.NoResults'), query.trim())}</p>
            <p className="muted">{t('glossary.NoResultsHint')}</p>
            <button type="button" className="button" onClick={clear}>
              {t('glossary.Clear')}
            </button>
          </div>
        )
      ) : (
        shown.map((section) => (
          <section key={section.id} className="section glossary__section" aria-labelledby={`glossary-${section.id}`}>
            <header className="glossary__section-header">
              <h2 id={`glossary-${section.id}`} className="glossary__section-title">
                {section.title}
              </h2>
              <p className="muted">{section.intro}</p>
            </header>
            <ol className="glossary__list">
              {section.terms.map((term) => (
                <li key={term.id}>
                  <TermEntry term={term} words={words} />
                </li>
              ))}
            </ol>
          </section>
        ))
      )}

      <p className="glossary__note">{t('glossary.Note')}</p>
    </div>
  );
}

function Marked({ text, words }: { text: string; words: readonly string[] }) {
  return (
    <>
      {markMatches(text, words).map((part, index) => (part.match ? <mark key={index}>{part.text}</mark> : <span key={index}>{part.text}</span>))}
    </>
  );
}

function TermEntry({ term, words, sectionTitle }: { term: GlossaryTerm; words: readonly string[]; sectionTitle?: string }) {
  const t = useT();
  return (
    <article className="glossary__term" aria-labelledby={`term-${term.id}`}>
      <div className="glossary__term-head">
        <h3 id={`term-${term.id}`} className="glossary__name">
          <Marked text={term.name} words={words} />
        </h3>
        {term.otherNames.map((other) => (
          <span key={other.culture} className="glossary__other" lang={other.culture}>
            {t(`glossary.Language.${other.culture}`)}: <Marked text={other.name} words={words} />
          </span>
        ))}
        {sectionTitle ? <span className="glossary__in-section">{sectionTitle}</span> : null}
      </div>
      <p className="glossary__text">
        <Marked text={term.text} words={words} />
      </p>
      {term.inVyxum || term.example ? (
        <dl className="glossary__lines">
          {term.inVyxum ? (
            <div className="glossary__line">
              <dt>{t('glossary.InVyxum')}</dt>
              <dd>
                <Marked text={term.inVyxum} words={words} />
              </dd>
            </div>
          ) : null}
          {term.example ? (
            <div className="glossary__line">
              <dt>{t('glossary.Example')}</dt>
              <dd>
                <Marked text={term.example} words={words} />
              </dd>
            </div>
          ) : null}
        </dl>
      ) : null}
    </article>
  );
}
