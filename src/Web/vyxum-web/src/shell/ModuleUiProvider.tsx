import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { browserImporter, isLoadable, loadModuleUi, type LoadedModuleUi, type ModuleImporter } from './moduleLoader';
import { useModules } from './ModulesProvider';
import type { ModuleNavItem } from './navigation';

export interface ModuleUiContextValue {
  /** Load result of a module's screens; undefined while loading or when the module has none. */
  get: (moduleId: string) => LoadedModuleUi | undefined;
  /** Menu entries of the loaded modules that may show their screens now (not locked). */
  navItems: readonly ModuleNavItem[];
}

const ModuleUiContext = createContext<ModuleUiContextValue | null>(null);

/**
 * Loads the screens of the modules in the module list once the list is ready, and again for modules that become
 * loadable later (for example after a license is loaded). An imported module stays imported; when it becomes locked
 * its menu entries and routes are hidden.
 */
export function ModuleUiProvider({ children, importer = browserImporter }: { children: ReactNode; importer?: ModuleImporter }) {
  const { status, modules } = useModules();
  const [loaded, setLoaded] = useState<ReadonlyMap<string, LoadedModuleUi>>(new Map());
  const started = useRef(new Set<string>());

  useEffect(() => {
    if (status !== 'ready') return;
    for (const module of modules.filter(isLoadable)) {
      const key = `${module.id} ${module.ui?.entry ?? ''}`;
      if (started.current.has(key)) continue;
      started.current.add(key);
      void loadModuleUi(module, importer).then((result) => {
        setLoaded((current) => new Map(current).set(module.id, result));
      });
    }
  }, [status, modules, importer]);

  const value = useMemo<ModuleUiContextValue>(() => {
    const visible = new Set(modules.filter((m) => m.license !== 'locked').map((m) => m.id));
    const navItems = [...loaded.values()]
      .filter((result) => visible.has(result.id))
      .flatMap((result) => (result.status === 'loaded' ? result.nav : []));
    return { get: (moduleId) => loaded.get(moduleId), navItems };
  }, [loaded, modules]);

  return <ModuleUiContext.Provider value={value}>{children}</ModuleUiContext.Provider>;
}

export function useModuleUi(): ModuleUiContextValue {
  const context = useContext(ModuleUiContext);
  if (!context) throw new Error('useModuleUi must be used inside ModuleUiProvider.');
  return context;
}
