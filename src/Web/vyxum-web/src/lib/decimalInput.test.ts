import { describe, expect, it } from 'vitest';
import { toInputDecimal, toInvariantDecimal } from './decimalInput';

describe('toInvariantDecimal', () => {
  it('accepts comma and dot in tr', () => {
    expect(toInvariantDecimal('0,012', 'tr')).toBe('0.012');
    expect(toInvariantDecimal('0.012', 'tr')).toBe('0.012');
    expect(toInvariantDecimal(' 780,50 ', 'tr')).toBe('780.50');
  });

  it('removes grouping dots in tr when a decimal comma follows', () => {
    expect(toInvariantDecimal('1.234,50', 'tr')).toBe('1234.50');
  });

  it('keeps en input with a dot and removes grouping commas', () => {
    expect(toInvariantDecimal('780.00', 'en')).toBe('780.00');
    expect(toInvariantDecimal('1,234.5', 'en')).toBe('1234.5');
  });

  it('leaves unrecognisable text for the server to reject', () => {
    expect(toInvariantDecimal('abc', 'tr')).toBe('abc');
    expect(toInvariantDecimal('0,012', 'en')).toBe('0,012');
    expect(toInvariantDecimal('1,2,3', 'tr')).toBe('1,2,3');
  });
});

describe('toInputDecimal', () => {
  it('uses the culture decimal separator and keeps all decimals', () => {
    expect(toInputDecimal('780.00', 'tr')).toBe('780,00');
    expect(toInputDecimal('0.012', 'en')).toBe('0.012');
    expect(toInputDecimal(null, 'tr')).toBe('');
  });
});
