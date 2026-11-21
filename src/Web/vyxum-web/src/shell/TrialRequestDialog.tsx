import { useEffect, useMemo, useRef, useState } from 'react';
import { useT } from '../i18n/I18nProvider';
import type { UiModule } from '../lib/api';
import { getLicense } from '../lib/license';
import { getSetupStatus } from '../lib/setup';
import { buildTrialRequest, copyText } from '../lib/trial';
import { useModules } from './ModulesProvider';
import '../styles/license.css';

/** Dialog with a request text for a trial license of a locked module, and where to send it. Works offline. */
export function TrialRequestDialog({ module, onClose }: { module: UiModule; onClose: () => void }) {
  const t = useT();
  const { companyName, contact } = useModules();
  const [version, setVersion] = useState<string | null>(null);
  const [licenseId, setLicenseId] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [date] = useState(() => new Date());
  const textRef = useRef<HTMLTextAreaElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    // Both are extras of the request text; the dialog works without them.
    void getSetupStatus({ signal: controller.signal }).then((s) => setVersion(s.version), () => undefined);
    void getLicense({ signal: controller.signal }).then((l) => setLicenseId(l.licenseId), () => undefined);
    closeRef.current?.focus();
    return () => controller.abort();
  }, []);

  const text = useMemo(
    () => buildTrialRequest({ company: companyName, moduleName: module.name ?? module.id, moduleId: module.id, version, licenseId, date }, t),
    [companyName, module, version, licenseId, date, t],
  );

  const copy = async () => {
    if (await copyText(text)) {
      setCopied(true);
    } else {
      // No clipboard API: select the text so that Ctrl+C works.
      textRef.current?.focus();
      textRef.current?.select();
    }
  };

  const contactParts = [
    contact.email ? (
      <a key="email" href={`mailto:${contact.email}`}>
        {contact.email}
      </a>
    ) : null,
    contact.url ? (
      <a key="url" href={contact.url} target="_blank" rel="noopener noreferrer">
        {contact.url}
      </a>
    ) : null,
  ].filter((part) => part !== null);

  return (
    <div className="trial__backdrop" onKeyDown={(e) => e.key === 'Escape' && onClose()}>
      <div className="trial" role="dialog" aria-modal="true" aria-labelledby="trial-title">
        <h2 id="trial-title" className="trial__title">
          {t('license.TrialTitle')}
        </h2>
        <p className="trial__text">{t('license.TrialIntro')}</p>
        <label className="trial__label" htmlFor="trial-request">
          {t('license.TrialRequestLabel')}
        </label>
        <textarea id="trial-request" ref={textRef} className="trial__request" readOnly rows={text.split('\n').length + 1} value={text} />
        <p className="trial__text">
          {contactParts.length > 0 ? (
            <>
              {t('license.TrialContact')}:{' '}
              {contactParts.map((part, index) => (
                <span key={index}>
                  {index > 0 ? ' · ' : null}
                  {part}
                </span>
              ))}
            </>
          ) : (
            t('license.TrialNoContact')
          )}
        </p>
        <div className="trial__actions">
          <button type="button" className="button button--primary" onClick={() => void copy()}>
            {t('license.TrialCopy')}
          </button>
          <button type="button" ref={closeRef} className="button button--quiet" onClick={onClose}>
            {t('license.TrialClose')}
          </button>
          {copied ? (
            <span className="trial__copied" role="status">
              {t('license.TrialCopied')}
            </span>
          ) : null}
        </div>
      </div>
    </div>
  );
}
