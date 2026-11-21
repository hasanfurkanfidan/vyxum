// Where the live net weight stands against the selected product's target weight and tolerances.
// The product stores the target and the tolerances in kg; they are converted to the scale's unit so
// they compare with the net exactly. This is a preview for the operator only; nothing here is saved.

import { add, compare, parseDecimal, subtract, tareFromKilograms, withScale, type Decimal } from './weights';

/** Below the lower limit, between the limits (both included), or above the upper limit. */
export type TargetStatus = 'under' | 'within' | 'over';

/** The fields of a product that the target view reads; weights in kg as invariant decimal strings. */
export interface TargetProduct {
  targetWeight: string | null;
  lowerTolerance: string | null;
  upperTolerance: string | null;
}

/** Target, limits and the net's position, all in the scale's unit with the same number of decimals. */
export interface TargetView {
  target: Decimal;
  /** Target minus the lower tolerance, never below zero. */
  lower: Decimal;
  /** Target plus the upper tolerance. */
  upper: Decimal;
  /** True when the product has a lower or an upper tolerance above zero. */
  hasTolerance: boolean;
  /** End of the bar: past the upper limit, so the limits and an over-fill stay visible. */
  barMax: Decimal;
  /** Undefined while there is no net weight. */
  status?: TargetStatus;
  /** Target minus net while the net is at or below the target. */
  remaining?: Decimal;
  /** Net minus target while the net is above the target. */
  excess?: Decimal;
}

const zero: Decimal = { units: 0n, scale: 0 };

/** A non-negative weight in kg converted to `unit` like a package tare; undefined when missing or invalid. */
function fromKilograms(text: string | null, unit: string): Decimal | undefined {
  const kilograms = parseDecimal(text);
  if (!kilograms || kilograms.units < 0n) return undefined;
  return tareFromKilograms(kilograms, unit);
}

/** value x tenths / 10, exact. */
function tenthsOf(value: Decimal, tenths: bigint): Decimal {
  return { units: value.units * tenths, scale: value.scale + 1 };
}

function larger(a: Decimal, b: Decimal): Decimal {
  return compare(a, b) >= 0 ? a : b;
}

/**
 * The target view for a product and the current net, or undefined when the product has no target
 * above zero or the scale's unit cannot be converted from kg. A missing tolerance counts as zero.
 */
export function targetView(product: TargetProduct | null, net: Decimal | undefined, unit: string | null): TargetView | undefined {
  if (!product || !unit) return undefined;
  const target = fromKilograms(product.targetWeight, unit);
  if (!target || target.units === 0n) return undefined;
  const below = fromKilograms(product.lowerTolerance, unit) ?? zero;
  const above = fromKilograms(product.upperTolerance, unit) ?? zero;

  const scale = Math.max(target.scale, below.scale, above.scale, net?.scale ?? 0);
  const t = withScale(target, scale);
  const lower = withScale(larger(subtract(t, below), zero), scale);
  const upper = withScale(add(t, above), scale);
  // 120% of the target, and at least 110% of the upper limit.
  const barMax = withScale(larger(tenthsOf(t, 12n), tenthsOf(upper, 11n)), scale);

  const view: TargetView = { target: t, lower, upper, hasTolerance: below.units > 0n || above.units > 0n, barMax };
  if (!net) return view;

  const n = withScale(net, scale);
  view.status = compare(n, lower) < 0 ? 'under' : compare(n, upper) > 0 ? 'over' : 'within';
  if (compare(n, t) > 0) view.excess = subtract(n, t);
  else view.remaining = subtract(t, n);
  return view;
}

/** Position of `value` on a bar from 0 to `max`, as a percentage clamped to 0..100 with two decimals. */
export function barPercent(value: Decimal, max: Decimal): number {
  if (max.units <= 0n) return 0;
  const scale = Math.max(value.scale, max.scale);
  const v = withScale(value, scale).units;
  const m = withScale(max, scale).units;
  if (v <= 0n) return 0;
  if (v >= m) return 100;
  return Number((v * 10_000n) / m) / 100;
}
