import { createContext, useCallback, useContext, type ReactNode } from 'react';
import { useT } from '../../i18n/I18nProvider';
import { NetworkError, RuleError } from '../../lib/api';
import { localOnlyCode } from '../../lib/setup';

/** What the wizard frame gives to the step that is shown. */
export interface WizardStepContextValue {
  /** Already translated "Step 2 of 4" line, or undefined for screens outside the numbered steps. */
  eyebrow: string | undefined;
  /** Goes to the previous step; undefined on the first step. */
  onBack: (() => void) | undefined;
  /** Shows the full-page "only from this computer" message. */
  onLocalOnly: () => void;
}

export const WizardStepContext = createContext<WizardStepContextValue>({
  eyebrow: undefined,
  onBack: undefined,
  onLocalOnly: () => {},
});

export function useWizardStep(): WizardStepContextValue {
  return useContext(WizardStepContext);
}

/**
 * Turns a failed setup call into the resource key of an inline message.
 * A request from another computer switches the wizard to the full-page message and returns null.
 */
export function useSetupErrorKey(): (error: unknown) => string | null {
  const { onLocalOnly } = useWizardStep();
  return useCallback(
    (error: unknown) => {
      if (error instanceof RuleError) {
        if (error.code === localOnlyCode) {
          onLocalOnly();
          return null;
        }
        return error.code;
      }
      if (!(error instanceof NetworkError)) console.error('A setup request failed.', error);
      return 'shell.Error';
    },
    [onLocalOnly],
  );
}

export interface StepLayoutProps {
  /** Already translated title. */
  title: string;
  description?: string | undefined;
  children?: ReactNode;
  /** Buttons on the right of the bottom row (the primary button last). */
  actions?: ReactNode;
  /** Inline message above the bottom row, as a resource key. */
  errorKey?: string | null | undefined;
}

/** Title, description, content and the bottom action row of one wizard screen (design source: BKurulum). */
export function StepLayout({ title, description, children, actions, errorKey }: StepLayoutProps) {
  const t = useT();
  const { eyebrow, onBack } = useWizardStep();

  return (
    <>
      <div className="wizard__intro">
        {eyebrow ? <span className="wizard__eyebrow">{eyebrow}</span> : null}
        <h1 className="wizard__title">{title}</h1>
        {description ? <p className="wizard__description">{description}</p> : null}
      </div>

      {children}

      {errorKey ? (
        <p className="field__error wizard__error" role="alert">
          {t(errorKey)}
        </p>
      ) : null}

      {actions !== undefined || onBack ? (
        <div className="wizard__actions">
          {onBack ? (
            <button type="button" className="button wizard__button" onClick={onBack}>
              {t('setup.Back')}
            </button>
          ) : (
            <span />
          )}
          <div className="wizard__actions-end">{actions}</div>
        </div>
      ) : null}
    </>
  );
}
