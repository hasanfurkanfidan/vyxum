import { useId, useState, type FormEvent } from 'react';
import { useT } from '../../i18n/I18nProvider';
import { ValidationError } from '../../lib/api';
import { saveCompany } from '../../lib/setup';
import { StepLayout, useSetupErrorKey } from './StepLayout';

export interface CompanyStepProps {
  initialName: string | null;
  onDone: (name: string) => void;
}

/** Step 3: the company name shown on screens and reports. */
export function CompanyStep({ initialName, onDone }: CompanyStepProps) {
  const t = useT();
  const id = useId();
  const errorKeyOf = useSetupErrorKey();
  const [name, setName] = useState(initialName ?? '');
  const [fieldErrors, setFieldErrors] = useState<readonly string[]>([]);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const formId = `${id}-form`;
  const fieldId = `${id}-name`;
  const errorId = `${fieldId}-error`;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setErrorKey(null);
    setFieldErrors([]);
    try {
      const result = await saveCompany(name);
      onDone(result.name);
    } catch (error) {
      if (error instanceof ValidationError) {
        const keys = error.forField('name');
        if (keys.length > 0) setFieldErrors(keys);
        else setErrorKey('shell.Error');
      } else {
        setErrorKey(errorKeyOf(error));
      }
      setBusy(false);
    }
  };

  return (
    <StepLayout
      title={t('setup.CompanyTitle')}
      description={t('setup.CompanyDescription')}
      errorKey={errorKey}
      actions={
        <button type="submit" form={formId} className="button button--primary wizard__button" disabled={busy}>
          {t('setup.Next')}
        </button>
      }
    >
      <form id={formId} className="section wizard__section" onSubmit={(event) => void submit(event)} noValidate>
        <div className="field wizard__field">
          <label className="field__label" htmlFor={fieldId}>
            {t('setup.CompanyName')}
          </label>
          <input
            id={fieldId}
            className="input"
            type="text"
            autoComplete="organization"
            maxLength={400}
            value={name}
            aria-required="true"
            aria-invalid={fieldErrors.length > 0}
            aria-describedby={fieldErrors.length > 0 ? errorId : undefined}
            onChange={(event) => {
              setName(event.target.value);
              setFieldErrors([]);
            }}
          />
          {fieldErrors.length > 0 ? (
            <p className="field__error" id={errorId}>
              {fieldErrors.map((key) => t(key)).join(' ')}
            </p>
          ) : null}
        </div>
      </form>
    </StepLayout>
  );
}
