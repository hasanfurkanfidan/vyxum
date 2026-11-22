import { useEffect, type ReactNode } from 'react';
import { useI18n } from '../i18n/I18nProvider';
import { useModules } from './ModulesProvider';
import { LicenseBanner } from './LicenseBanner';
import { Sidebar } from './Sidebar';

/** Page frame: sidebar on the left, the routed screen on the right. */
export function Shell({ children }: { children: ReactNode }) {
  const i18n = useI18n();
  const modules = useModules();
  const { t, loaded } = i18n;

  // A failed language switch keeps the previous strings, so only a failed first load is an error.
  const failed = (i18n.status === 'error' && !loaded) || modules.status === 'error';
  const ready = loaded && modules.status === 'ready';

  useEffect(() => {
    if (loaded) document.title = t('shell.AppName');
  }, [loaded, t]);

  const retry = () => {
    if (i18n.status === 'error') void i18n.reload();
    if (modules.status === 'error') void modules.reload();
  };

  let content: ReactNode;
  if (failed) {
    content = (
      <div className="status" role="alert">
        <p className="status__text">{t('shell.Error')}</p>
        <button type="button" className="button button--primary" onClick={retry}>
          {t('shell.Retry')}
        </button>
      </div>
    );
  } else if (!ready) {
    // Before the first strings arrive there is nothing translated to show.
    content = (
      <div className="status" role="status">
        {loaded ? <p className="status__text">{t('shell.Loading')}</p> : null}
      </div>
    );
  } else {
    content = (
      <>
        <LicenseBanner />
        {children}
      </>
    );
  }

  return (
    <div className="shell" aria-busy={!ready && !failed}>
      <div className="shell__sidebar">{ready ? <Sidebar /> : null}</div>
      <main className="shell__main">{content}</main>
    </div>
  );
}
