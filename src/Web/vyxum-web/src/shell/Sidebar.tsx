import { NavLink } from 'react-router-dom';
import { useT } from '../i18n/I18nProvider';
import { LanguageSwitch } from './LanguageSwitch';
import { useModules } from './ModulesProvider';
import { useModuleUi } from './ModuleUiProvider';
import { navGroups, visibleNavGroups, withModuleNav } from './navigation';

export function Sidebar() {
  const t = useT();
  const { hasModule, companyName } = useModules();
  const { navItems } = useModuleUi();
  const groups = visibleNavGroups(hasModule, withModuleNav(navGroups, navItems));

  return (
    <nav className="sidebar" aria-label={t('shell.AppName')}>
      <div className="sidebar__brand-block">
        <div className="sidebar__brand">{t('shell.AppName')}</div>
        {companyName ? <div className="sidebar__company">{companyName}</div> : null}
      </div>

      {groups.map((group) => (
        <section key={group.id} className="sidebar__group" aria-labelledby={`nav-group-${group.id}`}>
          <h2 id={`nav-group-${group.id}`} className="sidebar__heading">
            {t(group.labelKey)}
          </h2>
          <ul className="sidebar__list">
            {group.items.map((item) => (
              <li key={item.path}>
                <NavLink
                  to={item.path}
                  end
                  className={({ isActive }) => (isActive ? 'sidebar__link sidebar__link--active' : 'sidebar__link')}
                >
                  {t(item.labelKey)}
                </NavLink>
              </li>
            ))}
          </ul>
        </section>
      ))}

      <LanguageSwitch />
    </nav>
  );
}
