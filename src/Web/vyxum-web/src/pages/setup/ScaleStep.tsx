import { useT } from '../../i18n/I18nProvider';
import { StepLayout } from './StepLayout';

export interface ScaleStepProps {
  /** Called when a scale was set up and confirmed. */
  onDone: () => void;
  /** Called when the user goes on without a scale. */
  onSkip: () => void;
}

/**
 * Step 2: the scale. Placeholder until scale discovery is built; it only lets the user go on.
 * The wizard frame knows nothing else about this step, so it can be replaced on its own.
 */
export function ScaleStep({ onSkip }: ScaleStepProps) {
  const t = useT();
  return (
    <StepLayout
      title={t('setup.StepScale')}
      actions={
        <button type="button" className="button button--primary wizard__button" onClick={onSkip}>
          {t('setup.Skip')}
        </button>
      }
    />
  );
}
