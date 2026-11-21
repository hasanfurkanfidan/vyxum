import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { getUiStrings } from '../lib/api';
import { toCulture, writeCultureCookie, type Culture } from '../lib/culture';

export type TranslateFn = (key: string) => string;
export type LoadStatus = 'loading' | 'ready' | 'error';

export interface I18nContextValue {
  /** Culture of the strings currently shown ("tr" or "en"). Use it for the format helpers. */
  culture: Culture;
  status: LoadStatus;
  /** True once strings were loaded at least once; stays true while a language switch loads. */
  loaded: boolean;
  t: TranslateFn;
  /** Remembers the culture in the cookie, reloads the strings and updates `<html lang>`. */
  setCulture: (culture: Culture) => Promise<void>;
  /** Loads the strings again for the current cookie culture. */
  reload: () => Promise<void>;
}

const warned = new Set<string>();

function createTranslate(strings: Readonly<Record<string, string>>, loaded: boolean): TranslateFn {
  return (key: string) => {
    const value = strings[key];
    if (value !== undefined) return value;
    // Before the first load every key is missing; only warn about keys missing from loaded strings.
    if (import.meta.env.DEV && loaded && !warned.has(key)) {
      warned.add(key);
      console.warn(`Missing UI string: ${key}`);
    }
    return key;
  };
}

const I18nContext = createContext<I18nContextValue | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [strings, setStrings] = useState<Record<string, string>>({});
  const [culture, setCultureState] = useState<Culture>(() => toCulture(document.documentElement.lang));
  const [status, setStatus] = useState<LoadStatus>('loading');
  const [loaded, setLoaded] = useState(false);
  const latest = useRef(0);

  const load = useCallback(async () => {
    const id = ++latest.current;
    setStatus('loading');
    try {
      const result = await getUiStrings();
      if (id !== latest.current) return;
      const next = toCulture(result.culture);
      setStrings(result.strings);
      setCultureState(next);
      document.documentElement.lang = next;
      setLoaded(true);
      setStatus('ready');
    } catch (error) {
      if (id !== latest.current) return;
      console.error('Loading UI strings failed.', error);
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const setCulture = useCallback(
    async (next: Culture) => {
      writeCultureCookie(next);
      await load();
    },
    [load],
  );

  const t = useMemo(() => createTranslate(strings, loaded), [strings, loaded]);

  const value = useMemo<I18nContextValue>(
    () => ({ culture, status, loaded, t, setCulture, reload: load }),
    [culture, status, loaded, t, setCulture, load],
  );

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18nContextValue {
  const context = useContext(I18nContext);
  if (!context) throw new Error('useI18n must be used inside I18nProvider.');
  return context;
}

/** Returns `t(key)`. A missing key renders the key itself and warns once in development. */
export function useT(): TranslateFn {
  return useI18n().t;
}

/** Culture of the shown strings, for formatDecimal, formatMoney and the date helpers. */
export function useCulture(): Culture {
  return useI18n().culture;
}
