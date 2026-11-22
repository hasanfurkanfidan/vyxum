import { describe, expect, it } from 'vitest';
import {
  decimalPlaces,
  formatDate,
  formatDateOnly,
  formatDateTime,
  formatDecimal,
  formatMoney,
  formatRecordNumber,
  formatTime,
  formatWeight,
  isDecimalString,
} from './format';

// Intl may use a narrow no-break space between the amount and the symbol.
const normalizeSpaces = (text: string) => text.replace(/[  ]/g, ' ');

describe('formatWeight', () => {
  it.each([
    ['1.248', '1,248'],
    ['572', '572'],
    ['-0.004', '-0,004'],
    ['0.500', '0,500'],
    ['1248.5', '1.248,5'],
    ['12345678.123', '12.345.678,123'],
    ['0', '0'],
  ])('tr: %s -> %s', (input, expected) => {
    expect(formatWeight(input, 'tr')).toBe(expected);
  });

  it.each([
    ['1.248', '1.248'],
    ['572', '572'],
    ['-0.004', '-0.004'],
    ['0.500', '0.500'],
    ['1248.5', '1,248.5'],
  ])('en: %s -> %s', (input, expected) => {
    expect(formatWeight(input, 'en')).toBe(expected);
  });

  it('keeps every transmitted decimal without floating point rounding', () => {
    expect(formatDecimal('0.1000000000000000055511', 'en')).toBe('0.1000000000000000055511');
    expect(formatDecimal('9007199254740993.25', 'en')).toBe('9,007,199,254,740,993.25');
  });

  it('returns a value that is not a decimal string unchanged', () => {
    expect(formatWeight('abc', 'tr')).toBe('abc');
    expect(formatWeight('1,248', 'tr')).toBe('1,248');
    expect(formatWeight('', 'tr')).toBe('');
  });
});

describe('decimal helpers', () => {
  it('counts decimals', () => {
    expect(decimalPlaces('1.248')).toBe(3);
    expect(decimalPlaces('572')).toBe(0);
    expect(decimalPlaces('-0.004')).toBe(3);
  });

  it('recognises invariant decimal strings', () => {
    expect(isDecimalString('1.248')).toBe(true);
    expect(isDecimalString('-0.004')).toBe(true);
    expect(isDecimalString('1,248')).toBe(false);
    expect(isDecimalString('1e3')).toBe(false);
  });
});

describe('formatMoney', () => {
  it('formats Turkish lira in tr', () => {
    expect(normalizeSpaces(formatMoney('3833.16', 'TRY', 'tr'))).toBe('₺3.833,16');
  });

  it('formats Turkish lira in en', () => {
    expect(normalizeSpaces(formatMoney('3833.16', 'TRY', 'en'))).toBe('TRY 3,833.16');
  });

  it('formats euro and dollars from a number', () => {
    expect(normalizeSpaces(formatMoney(12.5, 'EUR', 'tr'))).toBe('€12,50');
    expect(normalizeSpaces(formatMoney(12.5, 'USD', 'en'))).toBe('$12.50');
  });

  it('uses the currency decimals unless told otherwise', () => {
    expect(normalizeSpaces(formatMoney('780', 'TRY', 'tr'))).toBe('₺780,00');
    expect(normalizeSpaces(formatMoney('780.125', 'TRY', 'tr', 3))).toBe('₺780,125');
  });

  it('returns an invalid amount unchanged', () => {
    expect(formatMoney('n/a', 'TRY', 'tr')).toBe('n/a');
  });
});

describe('date and time', () => {
  const utc = { timeZone: 'UTC' };
  const value = '2026-11-21T17:42:05Z';

  it('formats dates by culture', () => {
    expect(formatDate(value, 'tr', utc)).toBe('21.11.2026');
    expect(formatDate(value, 'en', utc)).toBe('11/21/2026');
  });

  it('formats times by culture', () => {
    expect(formatTime(value, 'tr', utc)).toBe('17:42');
    expect(normalizeSpaces(formatTime(value, 'en', utc))).toBe('05:42 PM');
  });

  it('converts UTC to the requested zone', () => {
    expect(formatTime(value, 'tr', { timeZone: 'Europe/Istanbul' })).toBe('20:42');
  });

  it('formats date and time together', () => {
    expect(formatDateTime(value, 'tr', utc)).toBe('21.11.2026 17:42');
  });

  it('returns an invalid date string unchanged', () => {
    expect(formatDate('not a date', 'tr')).toBe('not a date');
  });
});

describe('formatRecordNumber', () => {
  it('pads to six digits', () => {
    expect(formatRecordNumber(142)).toBe('000142');
    expect(formatRecordNumber('1234567')).toBe('1234567');
  });
});

describe('formatDateOnly', () => {
  it('shows a calendar date by culture without a time zone shift', () => {
    expect(formatDateOnly('2027-10-31', 'tr')).toBe('31.10.2027');
    expect(formatDateOnly('2027-01-01', 'en')).toBe('01/01/2027');
  });

  it('returns a value of another shape unchanged', () => {
    expect(formatDateOnly('soon', 'tr')).toBe('soon');
  });
});
