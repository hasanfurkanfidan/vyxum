import { useCulture, useT } from '../../i18n/I18nProvider';
import { formatWeight } from '../../lib/format';
import type { DeviceConnectionState, LiveDevice } from '../../lib/weighing';
import { connectionStateKeys, connectionTone, readingStateKeys, toneClass } from './deviceText';
import { useLiveDevice } from './useLiveDevice';

export interface ConnectionStatusProps {
  state: DeviceConnectionState;
  lastError?: string | null | undefined;
  className?: string;
}

/** Connection state as a word and a color; an error also shows the agent's error text. */
export function ConnectionStatus({ state, lastError, className }: ConnectionStatusProps) {
  const t = useT();
  return (
    <span className={`device-status ${toneClass(connectionTone(state))}${className ? ` ${className}` : ''}`}>
      {t(connectionStateKeys[state])}
      {state === 'Error' && lastError ? <span className="device-status__error">{lastError}</span> : null}
    </span>
  );
}

export interface LiveWeightViewProps {
  device: LiveDevice | null;
  /** True while the live stream is interrupted. */
  lost: boolean;
}

/** Big live weight with the reading state and the connection state as words. */
export function LiveWeightView({ device, lost }: LiveWeightViewProps) {
  const t = useT();
  const culture = useCulture();
  const state: DeviceConnectionState = lost ? 'Disconnected' : (device?.state ?? 'Connecting');
  const weight = device?.lastWeight ?? null;
  const connected = state === 'Connected';

  return (
    <div className="live-weight" aria-label={t('devices.LiveWeight')}>
      <div className="live-weight__head">
        <ConnectionStatus state={state} lastError={device?.lastError} />
        {weight ? (
          <span className={`live-weight__state ${weight.state === 'Stable' ? 'state--positive' : 'state--warning'}`}>
            {t(readingStateKeys[weight.state])}
          </span>
        ) : null}
      </div>
      {weight && weight.value !== null ? (
        <div className={`live-weight__weight${connected ? '' : ' is-stale'}`}>
          <output className="live-weight__value" data-testid="device-live-weight">
            {formatWeight(weight.value, culture)}
          </output>
          {weight.unit ? <span className="live-weight__unit">{weight.unit}</span> : null}
        </div>
      ) : (
        <p className="live-weight__empty">{weight ? t(readingStateKeys[weight.state]) : t('devices.NoReading')}</p>
      )}
    </div>
  );
}

/** Live weight of one device, read from its stream. */
export function LiveWeightPanel({ deviceId }: { deviceId: string }) {
  const { device, lost } = useLiveDevice(deviceId);
  return <LiveWeightView device={device} lost={lost} />;
}
