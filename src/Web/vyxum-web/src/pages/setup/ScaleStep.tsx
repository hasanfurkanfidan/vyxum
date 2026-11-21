import { useCallback, useEffect, useRef, useState } from 'react';
import { useT } from '../../i18n/I18nProvider';
import { LiveWeightPanel } from '../../components/devices/LiveWeightPanel';
import { ScaleSetupFlow } from '../../components/devices/ScaleSetupFlow';
import { useConnectionText } from '../../components/devices/deviceText';
import { deleteDeviceConfig, listDeviceConfigs, type DeviceConfig } from '../../lib/devices';
import { StepLayout, useSetupErrorKey } from './StepLayout';
import '../../styles/devices.css';

export interface ScaleStepProps {
  /** Called when a scale was set up and confirmed. */
  onDone: () => void;
  /** Called when the user goes on without a scale. */
  onSkip: () => void;
}

type View = { kind: 'loading' } | { kind: 'error'; key: string } | { kind: 'existing'; device: DeviceConfig } | { kind: 'search' };

/**
 * Step 2: the scale (design source: BKurulum, "Terazi ve yazıcı"). Looks for the scale by itself and shows its
 * live weight for confirmation. A scale that is already set up is shown instead of searching again.
 */
export function ScaleStep({ onDone, onSkip }: ScaleStepProps) {
  const t = useT();
  const errorKeyOf = useSetupErrorKey();
  const describe = useConnectionText();
  const [view, setView] = useState<View>({ kind: 'loading' });
  const [devices, setDevices] = useState<DeviceConfig[]>([]);
  const [loadTick, setLoadTick] = useState(0);
  const pending = useRef<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setView({ kind: 'loading' });
    listDeviceConfigs({ signal: controller.signal })
      .then((list) => {
        if (controller.signal.aborted) return;
        setDevices(list);
        const first = list[0];
        setView(first ? { kind: 'existing', device: first } : { kind: 'search' });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        const key = errorKeyOf(error);
        if (key) setView({ kind: 'error', key });
      });
    return () => controller.abort();
  }, [loadTick, errorKeyOf]);

  const onPendingChange = useCallback((id: string | null) => {
    pending.current = id;
  }, []);

  // Skipping while a found scale waits for confirmation removes that scale again.
  const skip = async () => {
    const id = pending.current;
    pending.current = null;
    if (id) await deleteDeviceConfig(id).catch((error: unknown) => console.error('Removing the unconfirmed scale failed.', error));
    onSkip();
  };

  let content = null;
  let actions = (
    <button type="button" className="button wizard__button" onClick={() => void skip()}>
      {t('setup.Skip')}
    </button>
  );

  switch (view.kind) {
    case 'loading':
      content = (
        <div className="status" role="status">
          <p className="status__text">{t('shell.Loading')}</p>
        </div>
      );
      break;
    case 'error':
      content = (
        <div className="status" role="alert">
          <p className="status__text">{t(view.key)}</p>
          <button type="button" className="button" onClick={() => setLoadTick((tick) => tick + 1)}>
            {t('shell.Retry')}
          </button>
        </div>
      );
      break;
    case 'existing':
      content = (
        <section className="section scale-flow__section" aria-label={t('devices.Existing')}>
          <div className="scale-flow__head">
            <h2 className="scale-flow__title">{t('devices.Existing')}</h2>
          </div>
          <p className="scale-flow__device">
            <span className="scale-flow__device-name">{view.device.name}</span>
            <span className="mono muted">{describe(view.device.transport, view.device.protocol)}</span>
          </p>
          <LiveWeightPanel deviceId={view.device.id} />
          <div className="scale-flow__buttons">
            <button type="button" className="button scale-flow__button" onClick={() => setView({ kind: 'search' })}>
              {t('devices.Rescan')}
            </button>
          </div>
        </section>
      );
      actions = (
        <button type="button" className="button button--primary wizard__button" onClick={onDone}>
          {t('setup.Next')}
        </button>
      );
      break;
    default:
      content = <ScaleSetupFlow existingIds={devices.map((d) => d.id)} onConfirmed={onDone} onPendingChange={onPendingChange} />;
  }

  return (
    <StepLayout title={t('setup.ScaleTitle')} description={t('setup.ScaleDescription')} actions={actions}>
      {content}
      {view.kind === 'search' ? <p className="scale-flow__hint">{t('setup.ScaleLaterHint')}</p> : null}
    </StepLayout>
  );
}
