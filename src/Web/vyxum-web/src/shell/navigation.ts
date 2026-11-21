// Menu entries of the shell. An entry is shown only when its module is loaded in the host.
// Groups without visible entries are hidden.

export type NavGroupId = 'core' | 'modules' | 'system' | 'settings';

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
} as const;

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
  // Screens of separately developed modules are added in a later phase.
  { id: 'modules', labelKey: 'shell.Modules', items: [] },
  { id: 'system', labelKey: 'shell.System', items: [] },
  {
    id: 'settings',
    labelKey: 'setup.Settings',
    items: [
      { labelKey: 'devices.Title', path: routes.settingsDevices },
      { labelKey: 'setup.AboutTitle', path: routes.settingsAbout },
    ],
  },
];

/** Groups with only the entries whose module is loaded; empty groups are left out. */
export function visibleNavGroups(hasModule: (id: string) => boolean, groups: readonly NavGroup[] = navGroups): NavGroup[] {
  return groups
    .map((group) => ({ ...group, items: group.items.filter((item) => item.module === undefined || hasModule(item.module)) }))
    .filter((group) => group.items.length > 0);
}
