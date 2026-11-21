import { useState } from 'react';
import { Link } from 'react-router-dom';
import { LockIcon } from '../components/LockIcon';
import { useT } from '../i18n/I18nProvider';
import type { UiModule } from '../lib/api';
import { TrialRequestDialog } from './TrialRequestDialog';
import { routes } from './navigation';
import '../styles/license.css';

/** Screen of a locked module that declares a preview: its sample image blurred, what it does, and how to ask for a trial. */
export function LockedPreview({ module }: { module: UiModule }) {
  const t = useT();
  const [asking, setAsking] = useState(false);
  const preview = module.preview!;
  const lines = preview.description.split(/\r?\n/).filter((line) => line.trim().length > 0);

  return (
    <section className="preview" aria-labelledby="preview-title">
      <div className="preview__frame" aria-hidden="true">
        <img className="preview__image" src={preview.imageUrl} alt="" draggable={false} />
      </div>
      <div className="preview__info">
        <h1 id="preview-title" className="preview__title">
          <LockIcon className="preview__lock" />
          {module.name ?? module.id}
        </h1>
        {lines.map((line, index) => (
          <p key={index} className="preview__text">
            {line}
          </p>
        ))}
        <p className="preview__note">{t('shell.PreviewLicenseNote')}</p>
        <div className="preview__actions">
          <button type="button" className="button button--primary" onClick={() => setAsking(true)}>
            {t('shell.PreviewRequestTrial')}
          </button>
          <Link className="preview__link" to={routes.settingsLicense}>
            {t('shell.ModuleLicenseSettings')}
          </Link>
        </div>
      </div>
      {asking ? <TrialRequestDialog module={module} onClose={() => setAsking(false)} /> : null}
    </section>
  );
}
