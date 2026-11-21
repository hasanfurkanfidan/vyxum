import { describe, expect, it } from 'vitest';
import { visibleNavGroups, type NavGroup } from './navigation';

describe('visibleNavGroups', () => {
  const groups: NavGroup[] = [
    { id: 'core', labelKey: 'shell.Core', items: [{ labelKey: 'a.A', path: '/a', module: 'a' }] },
    { id: 'modules', labelKey: 'shell.Modules', items: [{ labelKey: 'b.B', path: '/b', module: 'b' }] },
    { id: 'system', labelKey: 'shell.System', items: [] },
  ];

  it('keeps a group that has a loaded module', () => {
    const visible = visibleNavGroups((id) => id === 'b', groups);
    expect(visible.map((g) => g.id)).toEqual(['modules']);
    expect(visible[0]?.items.map((i) => i.path)).toEqual(['/b']);
  });

  it('hides every group when nothing is loaded', () => {
    expect(visibleNavGroups(() => false, groups)).toEqual([]);
  });
});
