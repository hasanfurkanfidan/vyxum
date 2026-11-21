import { useId, useState } from 'react';
import { LicenseUpload } from '../../components/license/LicenseUpload';
import { UploadResult } from '../../components/license/UploadResult';
import { useT } from '../../i18n/I18nProvider';
import type { LicenseState } from '../../lib/license';
import { StepLayout } from './StepLayout';
import '../../styles/license.css';

export interface LicenseStepProps {
  onDone: () => void;
}

type Choice = 'free' | 'file';

/**
 * Step 4: continue free (the default) or load a license file. Both lead on; uploading is optional and a license can
 * be loaded later under Settings, Modules and license.
 */
export function LicenseStep({ onDone }: LicenseStepProps) {
  const t = useT();
  const name = useId();
  const [choice, setChoice] = useState<Choice>('free');
  const [loaded, setLoaded] = useState<LicenseState | null>(null);

  return (
    <StepLayout
      title={t('license.StepHeading')}
      description={t('license.StepDescription')}
      actions={
        <button type="button" className="button button--primary wizard__button" onClick={onDone}>
          {t('setup.Next')}
        </button>
      }
    >
      <fieldset className="section wizard__section license-options">
        <legend className="license-options__legend">{t('license.StepTitle')}</legend>
        <div className="license-option">
          <label className="license-option__label">
            <input type="radio" name={name} checked={choice === 'free'} onChange={() => setChoice('free')} />
            <span className="license-option__text">
              <span className="license-option__title">{t('license.OptionFree')}</span>
              <span className="license-option__hint">{t('license.OptionFreeHint')}</span>
            </span>
          </label>
        </div>
        <div className="license-option">
          <label className="license-option__label">
            <input type="radio" name={name} checked={choice === 'file'} onChange={() => setChoice('file')} />
            <span className="license-option__text">
              <span className="license-option__title">{t('license.OptionFile')}</span>
              <span className="license-option__hint">{t('license.OptionFileHint')}</span>
            </span>
          </label>
          {choice === 'file' ? (
            <div className="license-option__body">
              <LicenseUpload buttonKey="license.ChooseFile" onUploaded={setLoaded} />
              {loaded ? <UploadResult state={loaded} /> : null}
            </div>
          ) : null}
        </div>
      </fieldset>
    </StepLayout>
  );
}
