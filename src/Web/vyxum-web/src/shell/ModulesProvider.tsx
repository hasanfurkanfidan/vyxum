import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { getUiModules, type UiModule } from '../lib/api';
import type { LoadStatus } from '../i18n/I18nProvider';

export interface ModulesContextValue {
  status: LoadStatus;
  modules: readonly UiModule[];
  capabilities: readonly string[];
  /** True when the module with this id is loaded in the host (for example "weighing"). */
  hasModule: (id: string) => boolean;
  /** True when a loaded module offers this capability (for example "products.catalog"). */
  hasCapability: (capability: string) => boolean;
  reload: () => Promise<void>;
}

const ModulesContext = createContext<ModulesContextValue | null>(null);

export function ModulesProvider({ children }: { children: ReactNode }) {
  const [modules, setModules] = useState<UiModule[]>([]);
  const [capabilities, setCapabilities] = useState<string[]>([]);
  const [status, setStatus] = useState<LoadStatus>('loading');
  const latest = useRef(0);

  const load = useCallback(async () => {
    const id = ++latest.current;
    setStatus('loading');
    try {
      const result = await getUiModules();
      if (id !== latest.current) return;
      setModules(result.modules);
      setCapabilities(result.capabilities);
      setStatus('ready');
    } catch (error) {
      if (id !== latest.current) return;
      console.error('Loading the module list failed.', error);
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const value = useMemo<ModulesContextValue>(() => {
    const ids = new Set(modules.map((m) => m.id));
    const caps = new Set(capabilities);
    return {
      status,
      modules,
      capabilities,
      hasModule: (moduleId) => ids.has(moduleId),
      hasCapability: (capability) => caps.has(capability),
      reload: load,
    };
  }, [status, modules, capabilities, load]);

  return <ModulesContext.Provider value={value}>{children}</ModulesContext.Provider>;
}

export function useModules(): ModulesContextValue {
  const context = useContext(ModulesContext);
  if (!context) throw new Error('useModules must be used inside ModulesProvider.');
  return context;
}
