import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useI18n } from '../../i18n/I18nProvider';
import type { SetupStatus } from '../../lib/setup';
import { LanguageSwitch } from '../../shell/LanguageSwitch';
import { useModules } from '../../shell/ModulesProvider';
import { routes } from '../../shell/navigation';
import { useSetup } from '../../shell/SetupProvider';
import { CompanyStep } from './CompanyStep';
import { DatabaseStep } from './DatabaseStep';
import { DoneStep } from './DoneStep';
import { LicenseStep } from './LicenseStep';
import { ProductsStep } from './ProductsStep';
import { ScaleStep } from './ScaleStep';
import { StepLayout, WizardStepContext, type WizardStepContextValue } from './StepLayout';
import { watchRestart } from './restart';
import '../../styles/setup.css';

export type SetupStepId = 'database' | 'scale' | 'company' | 'license' | 'products';
type ScreenId = SetupStepId | 'done';

interface StepDefinition {
  id: SetupStepId;
  labelKey: string;
}

const allSteps: readonly StepDefinition[] = [
  { id: 'database', labelKey: 'setup.StepDatabase' },
  { id: 'scale', labelKey: 'setup.StepScale' },
  { id: 'company', labelKey: 'setup.StepCompany' },
  { id: 'license', labelKey: 'license.StepTitle' },
  { id: 'products', labelKey: 'setup.StepProducts' },
];

/** The products step needs the products catalog of the Products module. */
export const productsCapability = 'products.catalog';

const stepStorageKey = 'vyxum.setup.step';
const restartStorageKey = 'vyxum.setup.restarting';

function readStorage(key: string): string | null {
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStorage(key: string, value: string | null): void {
  try {
    if (value === null) window.sessionStorage.removeItem(key);
    else window.sessionStorage.setItem(key, value);
  } catch {
    // Private mode or blocked storage: progress is then only kept until the page reloads.
  }
}

/**
 * Screen to open: the one remembered for this browser tab, but never past the company step while
 * no company name is saved (for example after switching to a new, empty database).
 */
function initialScreen(screens: readonly ScreenId[], status: SetupStatus): ScreenId {
  const stored = readStorage(stepStorageKey) as ScreenId | null;
  let screen: ScreenId = stored && screens.includes(stored) ? stored : stored === 'products' ? 'done' : 'database';
  if (!status.company.name && screens.indexOf(screen) > screens.indexOf('company')) screen = 'company';
  return screen;
}

/** Replaces `{0}`, `{1}`, ... in a translated text. */
function fill(text: string, ...values: (string | number)[]): string {
  return values.reduce<string>((result, value, index) => result.replace(`{${index}}`, String(value)), text);
}

type RestartState = 'none' | 'waiting' | 'manual';

/** First-run setup: a step rail on the left and the current step on the right (design source: BKurulum). */
export function SetupWizard() {
  const { t, culture } = useI18n();
  const { hasCapability, reload: reloadModules } = useModules();
  const { setup, update } = useSetup();
  const navigate = useNavigate();

  const steps = useMemo(() => allSteps.filter((s) => s.id !== 'products' || hasCapability(productsCapability)), [hasCapability]);
  const screens = useMemo<ScreenId[]>(() => [...steps.map((s) => s.id), 'done'], [steps]);

  // The wizard is only rendered once the status is known.
  const status = setup as SetupStatus;
  const [screen, setScreenState] = useState<ScreenId>(() => initialScreen(screens, status));
  const [restart, setRestart] = useState<RestartState>(() =>
    status.restartPending || readStorage(restartStorageKey) !== null ? 'waiting' : 'none',
  );
  const [localOnly, setLocalOnly] = useState(false);

  const setScreen = useCallback((next: ScreenId) => {
    writeStorage(stepStorageKey, next);
    setScreenState(next);
  }, []);

  const go = useCallback(
    (offset: number) => {
      const index = Math.min(Math.max(screens.indexOf(screen) + offset, 0), screens.length - 1);
      setScreen(screens[index] ?? 'database');
    },
    [screens, screen, setScreen],
  );

  const waiting = restart !== 'none';
  useEffect(() => {
    if (!waiting) return;
    const watch = watchRestart(
      (next) => {
        writeStorage(restartStorageKey, null);
        update(() => next);
        setRestart('none');
      },
      () => setRestart('manual'),
    );
    // One watch per wait: switching to the manual message does not restart the timer.
    return () => watch.stop();
  }, [waiting, update]);

  useEffect(() => {
    document.title = `${t('setup.Title')} · ${t('shell.AppName')}`;
  }, [t]);

  const databaseDone = (restartRequired: boolean) => {
    const nextScreen = screens[screens.indexOf('database') + 1] ?? 'done';
    setScreen(nextScreen);
    if (restartRequired) {
      writeStorage(restartStorageKey, '1');
      update((current) => ({ ...current, restartPending: true }));
      setRestart('waiting');
    }
  };

  const companyDone = (name: string) => {
    update((current) => ({ ...current, company: { name } }));
    go(1);
  };

  const completed = () => {
    writeStorage(stepStorageKey, null);
    writeStorage(restartStorageKey, null);
    update((current) => ({ ...current, completed: true }));
    void reloadModules();
    void navigate(routes.weighing, { replace: true });
  };

  const stepIndex = steps.findIndex((s) => s.id === screen);
  const context: WizardStepContextValue = {
    eyebrow: stepIndex >= 0 ? fill(t('setup.StepOf'), stepIndex + 1, steps.length) : undefined,
    onBack: screens.indexOf(screen) > 0 ? () => go(-1) : undefined,
    onLocalOnly: () => setLocalOnly(true),
  };

  let content: ReactNode;
  if (restart !== 'none') {
    content = (
      <WizardStepContext.Provider value={{ ...context, onBack: undefined }}>
        <div role="status" className="wizard__restart">
          <StepLayout
            title={t('setup.Restarting')}
            description={restart === 'manual' ? t('setup.RestartManual') : t('setup.RestartingDescription')}
          />
        </div>
      </WizardStepContext.Provider>
    );
  } else {
    let body: ReactNode;
    switch (screen) {
      case 'database':
        body = <DatabaseStep status={status} onDone={databaseDone} />;
        break;
      case 'scale':
        body = <ScaleStep onDone={() => go(1)} onSkip={() => go(1)} />;
        break;
      case 'company':
        body = <CompanyStep initialName={status.company.name} onDone={companyDone} />;
        break;
      case 'license':
        body = <LicenseStep onDone={() => go(1)} />;
        break;
      case 'products':
        body = <ProductsStep onDone={() => go(1)} />;
        break;
      default:
        body = <DoneStep onCompleted={completed} />;
    }
    content = (
      <WizardStepContext.Provider value={context}>
        {/* A new key per screen resets the step's own state when the user comes back to it. */}
        <div key={screen} className="wizard__step">
          {body}
        </div>
      </WizardStepContext.Provider>
    );
  }

  const currentIndex = screens.indexOf(screen);

  return (
    <div className="wizard" lang={culture}>
      <header className="wizard__header">
        <div className="wizard__brand">
          <span className="wizard__product">{t('shell.AppName')}</span>
          <span className="wizard__name">{t('setup.Title')}</span>
        </div>
        <span className="wizard__version mono">{fill(t('setup.Version'), status.version)}</span>
      </header>

      {localOnly ? (
        <main className="wizard__main wizard__main--full">
          <div className="status" role="alert">
            <p className="wizard__local-only">{t('setup.LocalOnly')}</p>
          </div>
        </main>
      ) : (
        <div className="wizard__body">
          <nav className="wizard__rail" aria-label={t('setup.Title')}>
            <ol className="steps">
              {steps.map((step, index) => {
                const position = screens.indexOf(step.id);
                const state = position < currentIndex ? 'done' : position === currentIndex ? 'current' : 'todo';
                return (
                  <li
                    key={step.id}
                    className={`steps__item steps__item--${state}`}
                    aria-current={state === 'current' ? 'step' : undefined}
                  >
                    <span className="steps__number mono">{String(index + 1).padStart(2, '0')}</span>
                    <span className="steps__text">
                      <span className="steps__label">{t(step.labelKey)}</span>
                      <span className="steps__note">
                        {state === 'done' ? t('setup.StepDone') : state === 'current' ? t('setup.StepCurrent') : null}
                      </span>
                    </span>
                  </li>
                );
              })}
            </ol>
            <p className="wizard__privacy">{t('setup.PrivacyNote')}</p>
            <LanguageSwitch />
          </nav>
          <main className="wizard__main">{content}</main>
        </div>
      )}
    </div>
  );
}

