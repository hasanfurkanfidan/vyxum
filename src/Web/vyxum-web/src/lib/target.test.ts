import { describe, expect, it } from 'vitest';
import { barPercent, targetView, type TargetProduct } from './target';
import { decimalToString, parseDecimal, type Decimal } from './weights';

function d(text: string): Decimal {
  const value = parseDecimal(text);
  if (!value) throw new Error(`Not a decimal: ${text}`);
  return value;
}

function text(value: Decimal | undefined): string | undefined {
  return value === undefined ? undefined : decimalToString(value);
}

const coffee: TargetProduct = { targetWeight: '0.25', lowerTolerance: '0.005', upperTolerance: '0.01' };

describe('target view', () => {
  it('converts the target and the limits to the scale unit with the decimals of the net', () => {
    const view = targetView(coffee, d('0.240'), 'kg');
    expect(text(view?.target)).toBe('0.250');
    expect(text(view?.lower)).toBe('0.245');
    expect(text(view?.upper)).toBe('0.260');
    expect(view?.hasTolerance).toBe(true);

    const grams = targetView(coffee, d('240'), 'g');
    expect(text(grams?.target)).toBe('250');
    expect(text(grams?.lower)).toBe('245');
    expect(text(grams?.upper)).toBe('260');
  });

  it.each([
    ['0.200', 'under', '0.050', undefined],
    ['0.244', 'under', '0.006', undefined],
    ['0.245', 'within', '0.005', undefined],
    ['0.250', 'within', '0.000', undefined],
    ['0.260', 'within', undefined, '0.010'],
    ['0.261', 'over', undefined, '0.011'],
    ['-0.002', 'under', '0.252', undefined],
  ])('puts a net of %s %s the tolerance', (net, status, remaining, excess) => {
    const view = targetView(coffee, d(net), 'kg');
    expect(view?.status).toBe(status);
    expect(text(view?.remaining)).toBe(remaining);
    expect(text(view?.excess)).toBe(excess);
  });

  it('keeps the more precise decimals of the tolerances', () => {
    const view = targetView({ targetWeight: '0.50', lowerTolerance: '0.0025', upperTolerance: null }, d('0.49'), 'kg');
    expect(text(view?.lower)).toBe('0.4975');
    expect(text(view?.upper)).toBe('0.5000');
    expect(view?.status).toBe('under');
  });

  it('treats missing tolerances as zero so only the target itself is within', () => {
    const exact: TargetProduct = { targetWeight: '1', lowerTolerance: null, upperTolerance: null };
    expect(targetView(exact, d('1.000'), 'kg')?.status).toBe('within');
    expect(targetView(exact, d('0.999'), 'kg')?.status).toBe('under');
    expect(targetView(exact, d('1.001'), 'kg')?.status).toBe('over');
    expect(targetView(exact, d('1.000'), 'kg')?.hasTolerance).toBe(false);
  });

  it('does not let the lower limit go below zero', () => {
    const view = targetView({ targetWeight: '0.1', lowerTolerance: '0.5', upperTolerance: null }, d('0.000'), 'kg');
    expect(text(view?.lower)).toBe('0.000');
    expect(view?.status).toBe('within');
  });

  it('has no status and no difference without a net', () => {
    const view = targetView(coffee, undefined, 'kg');
    expect(text(view?.target)).toBe('0.250');
    expect(view?.status).toBeUndefined();
    expect(view?.remaining).toBeUndefined();
    expect(view?.excess).toBeUndefined();
  });

  it.each([
    ['no product', null, 'kg'],
    ['no target', { targetWeight: null, lowerTolerance: '0.1', upperTolerance: '0.1' }, 'kg'],
    ['a zero target', { targetWeight: '0.00', lowerTolerance: null, upperTolerance: null }, 'kg'],
    ['a negative target', { targetWeight: '-1', lowerTolerance: null, upperTolerance: null }, 'kg'],
    ['a target that is not a decimal', { targetWeight: '1,5', lowerTolerance: null, upperTolerance: null }, 'kg'],
    ['an unknown unit', coffee, 'pcs'],
    ['no unit', coffee, null],
  ])('is not shown for %s', (_name, product, unit) => {
    expect(targetView(product, d('1.000'), unit)).toBeUndefined();
  });

  it('ends the bar at 120% of the target, or past the upper limit when that is wider', () => {
    expect(text(targetView(coffee, d('0.250'), 'kg')?.barMax)).toBe('0.300');
    const wide = targetView({ targetWeight: '1', lowerTolerance: null, upperTolerance: '0.5' }, d('1.000'), 'kg');
    expect(text(wide?.barMax)).toBe('1.650');
  });
});

describe('bar position', () => {
  it('is a clamped percentage of the bar end', () => {
    expect(barPercent(d('0.150'), d('0.300'))).toBe(50);
    expect(barPercent(d('0.1'), d('0.300'))).toBe(33.33);
    expect(barPercent(d('-0.010'), d('0.300'))).toBe(0);
    expect(barPercent(d('0.450'), d('0.300'))).toBe(100);
    expect(barPercent(d('1'), d('0'))).toBe(0);
  });
});
