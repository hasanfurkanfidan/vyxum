// Menu entries of the shell. An entry is shown only when its module is loaded in the host.
// Groups without visible entries are hidden.

export type NavGroupId = 'core' | 'modules' | 'system';

export interface NavItem {
  /** Resource key of the label. */
  labelKey: string;
  path: string;
  /** Module id that must be loaded for the entry to appear. */
  module: string;
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
];

/** Groups with only the entries whose module is loaded; empty groups are left out. */
export function visibleNavGroups(hasModule: (id: string) => boolean, groups: readonly NavGroup[] = navGroups): NavGroup[] {
  return groups
    .map((group) => ({ ...group, items: group.items.filter((item) => hasModule(item.module)) }))
    .filter((group) => group.items.length > 0);
}
