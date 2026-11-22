import { useModules } from '../shell/ModulesProvider';

/** License state of a module, as `/api/ui/modules` reports it. */
export type ModuleLicenseState = 'free' | 'licensed' | 'grace' | 'readOnly' | 'locked';

export interface ModuleLicenseInfo {
  state: ModuleLicenseState;
  /** False when the module is read-only or locked: show the data, disable actions that change it. */
  canWrite: boolean;
}

export function licenseInfo(state: ModuleLicenseState | undefined): ModuleLicenseInfo {
  const value = state ?? 'free';
  return { state: value, canWrite: value === 'free' || value === 'licensed' || value === 'grace' };
}

/** Current license state of a module; updates when the license changes. */
export function useModuleLicense(moduleId: string): ModuleLicenseInfo {
  const { modules } = useModules();
  return licenseInfo(modules.find((m) => m.id === moduleId)?.license);
}
