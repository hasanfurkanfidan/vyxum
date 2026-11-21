import { useCulture, useT } from '../../i18n/I18nProvider';
import { formatWeight } from '../../lib/format';
import { barPercent, type TargetStatus, type TargetView } from '../../lib/target';
import { decimalToString, type Decimal } from '../../lib/weights';

const statusKeys: Record<TargetStatus, string> = {
  under: 'weighing.TargetUnder',
  within: 'weighing.TargetWithin',
  over: 'weighing.TargetOver',
};

const statusTones: Record<TargetStatus, string> = {
  under: 'target__status--muted',
  within: 'state--positive',
  over: 'state--warning',
};

interface TargetGaugeProps {
  view: TargetView;
  /** The scale's unit; the view's weights are already in it. */
  unit: string;
  /** The current net, for the bar; undefined while there is no reading. */
  net: Decimal | undefined;
  /** True when the weight is not live; the bar is then shown muted like the weight. */
  stale: boolean;
}

/**
 * Bar under the live weight: how far the net is from the product's target, with the tolerance band
 * and the target marked on it. Shown only for a product with a target weight.
 */
export function TargetGauge({ view, unit, net, stale }: TargetGaugeProps) {
  const t = useT();
  const culture = useCulture();
  const weight = (value: Decimal) => `${formatWeight(decimalToString(value), culture)} ${unit}`;
  const dash = '–';

  const status = view.status;
  const fill = net && status ? barPercent(net, view.barMax) : 0;
  const lowerAt = barPercent(view.lower, view.barMax);
  const upperAt = barPercent(view.upper, view.barMax);
  const targetAt = barPercent(view.target, view.barMax);
  const statusText = status ? t(statusKeys[status]) : t('weighing.NoReading');
  const barEnd = Number(decimalToString(view.barMax));
  const now = net ? Math.min(Math.max(Number(decimalToString(net)), 0), barEnd) : 0;

  const difference = view.excess ?? view.remaining;
  const differenceLabel = view.excess ? t('weighing.TargetExcess') : t('weighing.TargetRemaining');
  const range = `${formatWeight(decimalToString(view.lower), culture)} – ${weight(view.upper)}`;

  return (
    <div className={`target${stale ? ' is-stale' : ''}${status ? ` target--${status}` : ''}`} data-testid="target">
      <div className="target__head">
        <span className="target__label">{t('weighing.Target')}</span>
        <span className="target__value" data-testid="target-value">
          {weight(view.target)}
        </span>
        <span className={`target__status ${status ? statusTones[status] : 'target__status--muted'}`} data-testid="target-status">
          {statusText}
        </span>
      </div>

      <div
        className="target__bar"
        role="meter"
        aria-label={t('weighing.Target')}
        aria-valuemin={0}
        aria-valuemax={barEnd}
        aria-valuenow={now}
        aria-valuetext={net && status ? `${weight(net)}, ${statusText}` : statusText}
      >
        <span className="target__fill" style={{ width: `${fill}%` }} data-testid="target-fill" />
        {view.hasTolerance ? (
          <span className="target__band" style={{ left: `${lowerAt}%`, width: `${Math.round((upperAt - lowerAt) * 100) / 100}%` }} data-testid="target-band" />
        ) : null}
        <span className="target__mark" style={{ left: `${targetAt}%` }} />
      </div>

      <div className="target__figures">
        <div className="weighing__figure">
          <span>{differenceLabel}</span>
          <span className="weighing__figure-value target__difference" data-testid="target-difference">
            {difference ? weight(difference) : dash}
          </span>
        </div>
        <div className="weighing__figure">
          <span>{t('weighing.TargetTolerance')}</span>
          <span className="weighing__figure-value" data-testid="target-range">
            {view.hasTolerance ? range : dash}
          </span>
        </div>
      </div>
    </div>
  );
}
