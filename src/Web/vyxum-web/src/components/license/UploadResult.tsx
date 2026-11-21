import { useCulture, useT } from '../../i18n/I18nProvider';
import { formatDateOnly } from '../../lib/format';
import { uploadWarningKey, type LicenseState } from '../../lib/license';
import '../../styles/license.css';

/**
 * Message after the server accepted a license file. A file that works now gets a plain confirmation; a file that
 * has ended, is in its grace period or starts later gets a warning that says what that means.
 */
export function UploadResult({ state }: { state: LicenseState }) {
  const t = useT();
  const culture = useCulture();
  const warningKey = uploadWarningKey(state.status);
  if (warningKey) {
    const value = state.status === 'notYetValid' ? (state.validFrom ? formatDateOnly(state.validFrom, culture) : '') : String(state.graceDaysLeft ?? 0);
    return (
      <p className="license-block__notice" role="alert">
        {t(warningKey).replace('{0}', value)}
      </p>
    );
  }
  return (
    <p className="license-block__ok" role="status">
      {state.licensee ? t('license.LoadedFor').replace('{0}', state.licensee.name) : t('license.Uploaded')}
    </p>
  );
}
