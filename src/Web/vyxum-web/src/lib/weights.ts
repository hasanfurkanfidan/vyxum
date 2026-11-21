// Exact decimal arithmetic for the weighing screen's preview (tare, gross, net and amount).
// Values are invariant decimal strings ("1.260", "-0.004"). They are held as a BigInt of units plus
// a scale, so no decimals are lost through floating point. The server stays authoritative on save;
// these helpers only compute what the screen shows before saving.

export interface Decimal {
  /** The value times 10^scale. */
  units: bigint;
  /** Number of digits after the decimal point. */
  scale: number;
}

const decimalPattern = /^([-+]?)(\d+)(?:\.(\d+))?$/;

/** Parses an invariant decimal string; undefined when it is not one. */
export function parseDecimal(text: string | null | undefined): Decimal | undefined {
  if (text === null || text === undefined) return undefined;
  const match = decimalPattern.exec(text.trim());
  if (!match) return undefined;
  const [, sign = '', whole = '0', fraction = ''] = match;
  const units = BigInt(whole + fraction);
  return { units: sign === '-' ? -units : units, scale: fraction.length };
}

/** Invariant text with exactly `value.scale` decimals ("1.260"). */
export function decimalToString(value: Decimal): string {
  const negative = value.units < 0n;
  const digits = (negative ? -value.units : value.units).toString().padStart(value.scale + 1, '0');
  const whole = digits.slice(0, digits.length - value.scale);
  const fraction = digits.slice(digits.length - value.scale);
  const text = value.scale > 0 ? `${whole}.${fraction}` : whole;
  return negative && value.units !== 0n ? `-${text}` : text;
}

function pow10(exponent: number): bigint {
  return 10n ** BigInt(exponent);
}

/** numerator / denominator rounded half away from zero. */
function divideRound(numerator: bigint, denominator: bigint): bigint {
  if (denominator < 0n) {
    numerator = -numerator;
    denominator = -denominator;
  }
  const negative = numerator < 0n;
  const absolute = negative ? -numerator : numerator;
  const quotient = (absolute * 2n + denominator) / (denominator * 2n);
  return negative ? -quotient : quotient;
}

/** Changes the scale; fewer decimals round half away from zero. */
export function withScale(value: Decimal, scale: number): Decimal {
  if (scale === value.scale) return value;
  if (scale > value.scale) return { units: value.units * pow10(scale - value.scale), scale };
  return { units: divideRound(value.units, pow10(value.scale - scale)), scale };
}

/** Removes trailing zeros: 12.500 becomes 12.5. */
export function trimScale(value: Decimal): Decimal {
  let { units, scale } = value;
  while (scale > 0 && units % 10n === 0n) {
    units /= 10n;
    scale -= 1;
  }
  return { units, scale };
}

export function add(a: Decimal, b: Decimal): Decimal {
  const scale = Math.max(a.scale, b.scale);
  return { units: withScale(a, scale).units + withScale(b, scale).units, scale };
}

export function subtract(a: Decimal, b: Decimal): Decimal {
  return add(a, { units: -b.units, scale: b.scale });
}

export function multiply(a: Decimal, b: Decimal): Decimal {
  return { units: a.units * b.units, scale: a.scale + b.scale };
}

export function isZero(value: Decimal): boolean {
  return value.units === 0n;
}

export function isPositive(value: Decimal): boolean {
  return value.units > 0n;
}

// Units the screen can convert, with milligrams per unit, the same table as the Weighing module.
const milligramsPerUnit: Readonly<Record<string, Decimal>> = {
  mg: { units: 1n, scale: 0 },
  g: { units: 1_000n, scale: 0 },
  kg: { units: 1_000_000n, scale: 0 },
  t: { units: 1_000_000_000n, scale: 0 },
  lb: { units: 45_359_237n, scale: 2 },
  oz: { units: 28_349_523_125n, scale: 6 },
};

/** Largest number of decimals the Weighing module stores for a weight. */
export const weightMaxDecimals = 6;

function factor(unit: string | null | undefined): Decimal | undefined {
  if (!unit) return undefined;
  return milligramsPerUnit[unit.trim().toLowerCase()];
}

/** True when the unit can be converted (mg, g, kg, t, lb, oz, any case). */
export function isKnownUnit(unit: string | null | undefined): boolean {
  return factor(unit) !== undefined;
}

/**
 * Converts `value` from one unit to another, rounded to `scale` decimals.
 * Undefined when a unit is unknown.
 */
export function convert(value: Decimal, from: string, to: string, scale: number): Decimal | undefined {
  const source = factor(from);
  const target = factor(to);
  if (!source || !target) return undefined;
  // value * source / target, with every part brought to whole numbers first.
  const milligrams = multiply(value, source);
  const numerator = milligrams.units * pow10(target.scale + scale);
  const denominator = target.units * pow10(milligrams.scale);
  return { units: divideRound(numerator, denominator), scale };
}

/** Converts a package tare in kg to the scale's unit as the Weighing module does: 6 decimals, then trailing zeros removed. */
export function tareFromKilograms(kilograms: Decimal, unit: string): Decimal | undefined {
  const converted = convert(kilograms, 'kg', unit, weightMaxDecimals);
  return converted ? trimScale(converted) : undefined;
}

/** Amount = net in kg x price per kg, rounded half away from zero to `decimals`. Undefined for an unknown unit. */
export function amountOf(net: Decimal, unit: string, pricePerKg: Decimal, decimals: number): Decimal | undefined {
  const source = factor(unit);
  if (!source) return undefined;
  const exact = multiply(multiply(net, source), pricePerKg);
  // exact is in mg x price; divide by 1,000,000 mg per kg.
  const numerator = exact.units * pow10(decimals);
  const denominator = 1_000_000n * pow10(exact.scale);
  return { units: divideRound(numerator, denominator), scale: decimals };
}
