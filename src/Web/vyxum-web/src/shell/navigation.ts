import type { UiModule } from '../lib/api';

// Menu entries of the shell. An entry is shown only when its module is loaded in the host.
// Groups without visible entries are hidden.

export type NavGroupId = 'core' | 'modules' | 'system' | 'settings' | 'help';

export interface NavItem {
  /** Resource key of the label. */
  labelKey: string;
  path: string;
  /** Module id that must be loaded for the entry to appear; platform screens have none and always appear. */
  module?: string;
}

export interface NavGroup {
  id: NavGroupId;
  /** Resource key of the group heading. */
  labelKey: string;
  items: readonly NavItem[];
}

export const routes = {
  weighing: '/',
  records: '/records',
  products: '/products',
  /** First-run setup wizard (shown without the menu). */
  setup: '/setup',
  /** Turkish alias of the wizard address; it redirects to `setup`. */
  setupTurkish: '/kurulum',
  /** Settings screens of the platform. */
  settingsDevices: '/settings/devices',
  settingsAbout: '/settings/about',
  /** Modules and license settings. */
  settingsLicense: '/settings/license',
  /** Free glossary of the words used on the screens. */
  glossary: '/glossary',
  /** Screens of separately built modules: `/m/{moduleId}/...`. */
  module: '/m/:moduleId/*',
} as const;

/** Address of a module screen: `/m/{moduleId}` plus the module's own relative path. */
export function modulePath(moduleId: string, path = ''): string {
  const relative = path.replace(/^\/+/, '');
  return relative ? `/m/${moduleId}/${relative}` : `/m/${moduleId}`;
}

export const navGroups: readonly NavGroup[] = [
  {
    id: 'core',
    labelKey: 'shell.Core',
    items: [
      { labelKey: 'shell.Weighing', path: routes.weighing, module: 'weighing' },
      { labelKey: 'shell.Records', path: routes.records, module: 'weighing' },
      { labelKey: 'shell.Products', path: routes.products, module: 'products' },
    ],
  },
  // Screens of separately built modules register their entries at run time (see withModuleNav).
  { id: 'modules', labelKey: 'shell.Modules', items: [] },
  { id: 'system', labelKey: 'shell.System', items: [] },
  {
    id: 'settings',
    labelKey: 'setup.Settings',
    items: [
      { labelKey: 'devices.Title', path: routes.settingsDevices },
      { labelKey: 'license.Title', path: routes.settingsLicense },
      { labelKey: 'setup.AboutTitle', path: routes.settingsAbout },
    ],
  },
  {
    id: 'help',
    labelKey: 'shell.Help',
    items: [{ labelKey: 'glossary.Title', path: routes.glossary }],
  },
];

/** Groups with only the entries whose module is loaded; empty groups are left out. */
export function visibleNavGroups(hasModule: (id: string) => boolean, groups: readonly NavGroup[] = navGroups): NavGroup[] {
  return groups
    .map((group) => ({ ...group, items: group.items.filter((item) => item.module === undefined || hasModule(item.module)) }))
    .filter((group) => group.items.length > 0);
}

/** Locked paid modules that declare a preview: the "Vyxum Pro" group at the bottom of the menu lists them. */
export function previewModules(modules: readonly UiModule[]): UiModule[] {
  return modules.filter((m) => m.license === 'locked' && m.preview);
}

/** A menu entry a module registered, with the group it goes in. */
export interface ModuleNavItem extends NavItem {
  group: NavGroupId;
}

/** The groups with the entries modules registered appended to their group. */
export function withModuleNav(groups: readonly NavGroup[], items: readonly ModuleNavItem[]): NavGroup[] {
  return groups.map((group) => {
    const added = items.filter((item) => item.group === group.id).map(({ group: _group, ...item }) => item);
    return added.length > 0 ? { ...group, items: [...group.items, ...added] } : group;
  });
}
