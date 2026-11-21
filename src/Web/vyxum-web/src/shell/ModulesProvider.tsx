import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { getUiModules, type UiLicense, type UiModule } from '../lib/api';
import type { LoadStatus } from '../i18n/I18nProvider';

export interface ModulesContextValue {
  status: LoadStatus;
  modules: readonly UiModule[];
  capabilities: readonly string[];
  /** Company name from the setup wizard, or null when none was entered. */
  companyName: string | null;
  /** Overall license state: status, licensee (when a license verifies) and grace days left. */
  license: UiLicense;
  /** True when the module with this id is loaded in the host (for example "weighing"). */
  hasModule: (id: string) => boolean;
  /** True when a loaded module offers this capability (for example "products.catalog"). */
  hasCapability: (capability: string) => boolean;
  /** Loads the list again, for example after a license change. A list that is already shown stays on screen meanwhile. */
  reload: () => Promise<void>;
}

const noLicense: UiLicense = { status: 'none', licensee: null, graceDaysLeft: null };

const ModulesContext = createContext<ModulesContextValue | null>(null);

export function ModulesProvider({ children }: { children: ReactNode }) {
  const [modules, setModules] = useState<UiModule[]>([]);
  const [capabilities, setCapabilities] = useState<string[]>([]);
  const [companyName, setCompanyName] = useState<string | null>(null);
  const [license, setLicense] = useState<UiLicense>(noLicense);
  const [status, setStatus] = useState<LoadStatus>('loading');
  const latest = useRef(0);
  const loaded = useRef(false);

  const load = useCallback(async () => {
    const id = ++latest.current;
    // A list that is already shown is refreshed quietly: the screen and its state are not replaced by "loading".
    if (!loaded.current) setStatus('loading');
    try {
      const result = await getUiModules();
      if (id !== latest.current) return;
      setModules(result.modules);
      setCapabilities(result.capabilities);
      setCompanyName(result.companyName ?? null);
      setLicense(result.license ?? noLicense);
      loaded.current = true;
      setStatus('ready');
    } catch (error) {
      if (id !== latest.current) return;
      console.error('Loading the module list failed.', error);
      if (!loaded.current) setStatus('error');
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
      companyName,
      license,
      hasModule: (moduleId) => ids.has(moduleId),
      hasCapability: (capability) => caps.has(capability),
      reload: load,
    };
  }, [status, modules, capabilities, companyName, license, load]);

  return <ModulesContext.Provider value={value}>{children}</ModulesContext.Provider>;
}

export function useModules(): ModulesContextValue {
  const context = useContext(ModulesContext);
  if (!context) throw new Error('useModules must be used inside ModulesProvider.');
  return context;
}
