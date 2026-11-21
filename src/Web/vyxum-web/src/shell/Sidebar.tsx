import { NavLink } from 'react-router-dom';
import { useT } from '../i18n/I18nProvider';
import { LanguageSwitch } from './LanguageSwitch';
import { useModules } from './ModulesProvider';
import { useModuleUi } from './ModuleUiProvider';
import '../styles/license.css';
import { LockIcon } from '../components/LockIcon';
import { modulePath, navGroups, previewModules, visibleNavGroups, withModuleNav } from './navigation';

export function Sidebar() {
  const t = useT();
  const { hasModule, companyName, license, modules } = useModules();
  const { navItems } = useModuleUi();
  const groups = visibleNavGroups(hasModule, withModuleNav(navGroups, navItems));
  const pro = previewModules(modules);

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

      {pro.length > 0 ? (
        <section className="sidebar__group sidebar__group--pro" aria-labelledby="nav-group-pro">
          <h2 id="nav-group-pro" className="sidebar__heading">
            {t('shell.ProGroup')}
          </h2>
          <ul className="sidebar__list">
            {pro.map((module) => (
              <li key={module.id}>
                <NavLink
                  to={modulePath(module.id)}
                  end
                  className={({ isActive }) => (isActive ? 'sidebar__link sidebar__link--active' : 'sidebar__link sidebar__link--locked')}
                >
                  <span className="sidebar__link-name">{module.name ?? module.id}</span>
                  <LockIcon label={t('shell.ProLocked')} className="sidebar__lock" />
                </NavLink>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {license.licensee ? (
        <div className="sidebar__licensee">
          <span className="sidebar__licensee-label">{t('license.Licensee')}</span>
          <span className="sidebar__licensee-name">{license.licensee}</span>
        </div>
      ) : null}

      <LanguageSwitch />
    </nav>
  );
}
