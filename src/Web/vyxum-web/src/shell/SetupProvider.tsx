import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { LoadStatus } from '../i18n/I18nProvider';
import { getSetupStatus, type SetupStatus } from '../lib/setup';

export interface SetupContextValue {
  status: LoadStatus;
  /** Last known setup status, or null before the first answer. */
  setup: SetupStatus | null;
  /** Loads the status again and returns it. */
  reload: () => Promise<SetupStatus | null>;
  /** Changes the known status locally after a successful setup call. */
  update: (change: (current: SetupStatus) => SetupStatus) => void;
}

const SetupContext = createContext<SetupContextValue | null>(null);

/** Loads /api/setup/status once at start so the app can send a new installation to the wizard. */
export function SetupProvider({ children }: { children: ReactNode }) {
  const [setup, setSetup] = useState<SetupStatus | null>(null);
  const [status, setStatus] = useState<LoadStatus>('loading');
  const latest = useRef(0);

  const reload = useCallback(async () => {
    const id = ++latest.current;
    setStatus('loading');
    try {
      const result = await getSetupStatus();
      if (id !== latest.current) return result;
      setSetup(result);
      setStatus('ready');
      return result;
    } catch (error) {
      if (id !== latest.current) return null;
      console.error('Loading the setup status failed.', error);
      setStatus('error');
      return null;
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const update = useCallback((change: (current: SetupStatus) => SetupStatus) => {
    setSetup((current) => (current ? change(current) : current));
  }, []);

  const value = useMemo<SetupContextValue>(() => ({ status, setup, reload, update }), [status, setup, reload, update]);
  return <SetupContext.Provider value={value}>{children}</SetupContext.Provider>;
}

export function useSetup(): SetupContextValue {
  const context = useContext(SetupContext);
  if (!context) throw new Error('useSetup must be used inside SetupProvider.');
  return context;
}
