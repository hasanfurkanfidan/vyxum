import { useId, useRef, useState, type ChangeEvent } from 'react';
import { useT } from '../../i18n/I18nProvider';
import { licenseFailureKey, uploadLicense, type LicenseState } from '../../lib/license';
import { useModules } from '../../shell/ModulesProvider';

export interface LicenseUploadProps {
  /** Resource key of the button text. */
  buttonKey?: string;
  /** Style of the button. */
  primary?: boolean;
  /** Called with the new license state after the server accepted the file. */
  onUploaded: (state: LicenseState) => void;
}

/**
 * Button that opens a file picker for a `.vxlic` file, sends its text to the server and, when it is accepted,
 * reloads the module list so that newly licensed modules open without reloading the page. A rejected file shows the
 * message of its error code under the button.
 */
export function LicenseUpload({ buttonKey = 'license.Upload', primary = false, onUploaded }: LicenseUploadProps) {
  const t = useT();
  const id = useId();
  const { reload } = useModules();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [errorKey, setErrorKey] = useState<string | null>(null);

  const choose = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    // The same file can be chosen again after a change (for example after a fix on the server).
    event.target.value = '';
    if (!file) return;
    setBusy(true);
    setErrorKey(null);
    let text: string;
    try {
      text = await file.text();
    } catch {
      setErrorKey('license.ReadFailed');
      setBusy(false);
      return;
    }
    try {
      const state = await uploadLicense(text);
      await reload();
      setBusy(false);
      onUploaded(state);
    } catch (error) {
      if (!(error instanceof Error && 'code' in error)) console.error('Uploading the license failed.', error);
      setErrorKey(licenseFailureKey(error));
      setBusy(false);
    }
  };

  return (
    <div className="license-upload">
      <input
        ref={input}
        id={`${id}-file`}
        className="license-upload__input"
        type="file"
        accept=".vxlic"
        tabIndex={-1}
        aria-label={t('license.FileInput')}
        onChange={(event) => void choose(event)}
      />
      <button
        type="button"
        className={primary ? 'button button--primary license-upload__button' : 'button license-upload__button'}
        disabled={busy}
        onClick={() => input.current?.click()}
      >
        {busy ? t('license.Uploading') : t(buttonKey)}
      </button>
      {errorKey ? (
        <p className="field__error" role="alert">
          {t(errorKey)}
        </p>
      ) : null}
    </div>
  );
}
