// Culture-aware formatting for numbers, money and dates.
// Weights and prices arrive from the API as invariant decimal strings ("1.248", "572", "-0.004")
// so that the number of transmitted decimals is preserved. They are never converted to
// floating point here: Intl.NumberFormat formats numeric strings exactly.

const decimalPattern = /^[-+]?\d+(\.\d+)?$/;

/** Number of digits after the invariant decimal point ("1.248" -> 3, "572" -> 0). */
export function decimalPlaces(value: string): number {
  const dot = value.indexOf('.');
  return dot < 0 ? 0 : value.length - dot - 1;
}

/** True when the value is an invariant decimal string such as "1.248" or "-0.004". */
export function isDecimalString(value: string): boolean {
  return decimalPattern.test(value.trim());
}

const numberFormats = new Map<string, Intl.NumberFormat>();

function numberFormat(culture: string, options: Intl.NumberFormatOptions): Intl.NumberFormat {
  const key = `${culture}|${JSON.stringify(options)}`;
  let format = numberFormats.get(key);
  if (!format) {
    format = new Intl.NumberFormat(culture, options);
    numberFormats.set(key, format);
  }
  return format;
}

/**
 * Formats an invariant decimal string by culture and keeps the transmitted number of decimals.
 * tr: "1.248" -> "1,248", "12345.5" -> "12.345,5"; en: "1.248" -> "1.248", "12345.5" -> "12,345.5".
 * A value that is not a decimal string is returned unchanged.
 */
export function formatDecimal(value: string, culture: string): string {
  const trimmed = value.trim();
  if (!decimalPattern.test(trimmed)) return value;
  const digits = decimalPlaces(trimmed);
  return numberFormat(culture, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
    useGrouping: true,
  }).format(trimmed as Intl.StringNumericLiteral);
}

/** Formats a weight value; the unit is shown separately by the screen. */
export function formatWeight(value: string, culture: string): string {
  return formatDecimal(value, culture);
}

/**
 * Formats a money amount with the currency symbol of the given ISO 4217 code.
 * The amount may be an invariant decimal string or a number. Decimals follow the currency
 * (two for TRY, EUR, USD) unless `fractionDigits` is given.
 */
export function formatMoney(amount: string | number, currency: string, culture: string, fractionDigits?: number): string {
  const value = typeof amount === 'string' ? amount.trim() : amount;
  if (typeof value === 'string' && !decimalPattern.test(value)) return amount.toString();
  const options: Intl.NumberFormatOptions = { style: 'currency', currency };
  if (fractionDigits !== undefined) {
    options.minimumFractionDigits = fractionDigits;
    options.maximumFractionDigits = fractionDigits;
  }
  return numberFormat(culture, options).format(value as number | Intl.StringNumericLiteral);
}

export interface DateFormatOptions {
  /** IANA time zone. Defaults to the browser's local zone. */
  timeZone?: string;
}

function toDate(value: string | Date): Date | undefined {
  const date = typeof value === 'string' ? new Date(value) : value;
  return Number.isNaN(date.getTime()) ? undefined : date;
}

function formatWith(value: string | Date, culture: string, base: Intl.DateTimeFormatOptions, options: DateFormatOptions): string {
  const date = toDate(value);
  if (!date) return typeof value === 'string' ? value : '';
  const all: Intl.DateTimeFormatOptions = { ...base };
  if (options.timeZone) all.timeZone = options.timeZone;
  return new Intl.DateTimeFormat(culture, all).format(date);
}

/** Date only. tr: "21.11.2026", en: "11/21/2026". The input is a UTC ISO 8601 string or a Date. */
export function formatDate(value: string | Date, culture: string, options: DateFormatOptions = {}): string {
  return formatWith(value, culture, { year: 'numeric', month: '2-digit', day: '2-digit' }, options);
}

/** Time of day with hours and minutes, 24-hour clock in tr. */
export function formatTime(value: string | Date, culture: string, options: DateFormatOptions = {}): string {
  return formatWith(value, culture, { hour: '2-digit', minute: '2-digit' }, options);
}

/** Date and time. */
export function formatDateTime(value: string | Date, culture: string, options: DateFormatOptions = {}): string {
  return formatWith(
    value,
    culture,
    { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' },
    options,
  );
}

/** Record numbers come from the identity column and are shown zero padded to six digits. */
export function formatRecordNumber(value: number | string): string {
  return String(value).padStart(6, '0');
}
