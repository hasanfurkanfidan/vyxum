import { Link, useMatch } from 'react-router-dom';
import { useT } from '../i18n/I18nProvider';
import { useModules } from './ModulesProvider';
import { routes } from './navigation';
import '../styles/license.css';

/**
 * Thin strip above the screen while the license is in its grace period or read-only. It comes from the module list
 * the shell already has, so there is no polling; it is left out on the license screen itself, which shows the same.
 */
export function LicenseBanner() {
  const t = useT();
  const { license } = useModules();
  const onLicensePage = useMatch(routes.settingsLicense) !== null;
  if (onLicensePage || (license.status !== 'grace' && license.status !== 'readOnly')) return null;

  const text =
    license.status === 'grace'
      ? t('license.BannerGrace').replace('{0}', String(license.graceDaysLeft ?? 0))
      : t('license.BannerReadOnly');
  return (
    <div className="license-banner" role="status">
      <span>{text}</span>
      <Link className="license-banner__link" to={routes.settingsLicense}>
        {t('license.BannerLink')}
      </Link>
    </div>
  );
}
