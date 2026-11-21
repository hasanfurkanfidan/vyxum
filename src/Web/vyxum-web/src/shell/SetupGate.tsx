import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useI18n } from '../i18n/I18nProvider';
import { routes } from './navigation';
import { useModules } from './ModulesProvider';
import { useSetup } from './SetupProvider';

/**
 * Sends every page to the setup wizard until the setup is complete, and the wizard to weighing afterwards.
 * Waits for the strings, the module list and the setup status before showing anything.
 */
export function SetupGate({ children }: { children: ReactNode }) {
  const i18n = useI18n();
  const modules = useModules();
  const setup = useSetup();
  const location = useLocation();
  const { t, loaded } = i18n;

  // Once setup is complete the shell shows its own loading and error states for the module list.
  const wizardNeedsModules = setup.setup !== null && !setup.setup.completed;
  const failed =
    (i18n.status === 'error' && !loaded) ||
    (setup.status === 'error' && setup.setup === null) ||
    (wizardNeedsModules && modules.status === 'error');
  const ready = loaded && setup.setup !== null && (setup.setup.completed || modules.status === 'ready');

  if (failed) {
    const retry = () => {
      if (i18n.status === 'error') void i18n.reload();
      if (modules.status === 'error') void modules.reload();
      if (setup.status === 'error') void setup.reload();
    };
    return (
      <main className="gate">
        <div className="status" role="alert">
          <p className="status__text">{t('shell.Error')}</p>
          <button type="button" className="button button--primary" onClick={retry}>
            {t('shell.Retry')}
          </button>
        </div>
      </main>
    );
  }

  if (!ready || setup.setup === null) {
    return (
      <main className="gate" aria-busy="true">
        <div className="status" role="status">
          {loaded ? <p className="status__text">{t('shell.Loading')}</p> : null}
        </div>
      </main>
    );
  }

  const inWizard = location.pathname === routes.setup || location.pathname === routes.setupTurkish;
  if (!setup.setup.completed && !inWizard) return <Navigate to={routes.setup} replace />;
  if (setup.setup.completed && inWizard) return <Navigate to={routes.weighing} replace />;
  return children;
}
