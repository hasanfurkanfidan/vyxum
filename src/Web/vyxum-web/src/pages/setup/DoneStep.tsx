import { useState } from 'react';
import { useT } from '../../i18n/I18nProvider';
import { completeSetup } from '../../lib/setup';
import { StepLayout, useSetupErrorKey } from './StepLayout';

export interface DoneStepProps {
  /** Called after the server marked the setup as complete. */
  onCompleted: () => void;
}

/** Last screen: marks the setup as complete and opens weighing. */
export function DoneStep({ onCompleted }: DoneStepProps) {
  const t = useT();
  const errorKeyOf = useSetupErrorKey();
  const [busy, setBusy] = useState(false);
  const [errorKey, setErrorKey] = useState<string | null>(null);

  const finish = async () => {
    if (busy) return;
    setBusy(true);
    setErrorKey(null);
    try {
      await completeSetup();
      onCompleted();
    } catch (error) {
      setErrorKey(errorKeyOf(error));
      setBusy(false);
    }
  };

  return (
    <StepLayout
      title={t('setup.DoneTitle')}
      description={t('setup.DoneDescription')}
      errorKey={errorKey}
      actions={
        <button type="button" className="button button--primary wizard__button" disabled={busy} onClick={() => void finish()}>
          {t('setup.GoToWeighing')}
        </button>
      }
    />
  );
}
