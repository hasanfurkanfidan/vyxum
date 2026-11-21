import { describe, expect, it } from 'vitest';
import { parseLiveJson, type LiveDevice } from './weighing';
import { amountOf, convert, decimalToString, parseDecimal, subtract, tareFromKilograms, withScale, type Decimal } from './weights';

function d(text: string): Decimal {
  const value = parseDecimal(text);
  if (!value) throw new Error(`Not a decimal: ${text}`);
  return value;
}

describe('decimal helpers', () => {
  it.each(['1.260', '-0.004', '572', '0.000', '12345.5'])('round-trips %s', (text) => {
    expect(decimalToString(d(text))).toBe(text);
  });

  it('rejects text that is not an invariant decimal', () => {
    expect(parseDecimal('1,2')).toBeUndefined();
    expect(parseDecimal('1e3')).toBeUndefined();
    expect(parseDecimal(null)).toBeUndefined();
  });

  it('subtracts with the larger scale and rounds half away from zero', () => {
    expect(decimalToString(subtract(d('1.26'), d('0.012')))).toBe('1.248');
    expect(decimalToString(withScale(d('0.0125'), 3))).toBe('0.013');
    expect(decimalToString(withScale(d('-0.0125'), 3))).toBe('-0.013');
  });

  it('converts between units', () => {
    expect(decimalToString(convert(d('1.5'), 'kg', 'g', 0) as Decimal)).toBe('1500');
    expect(decimalToString(convert(d('1'), 'lb', 'kg', 6) as Decimal)).toBe('0.453592');
    expect(convert(d('1'), 'kg', 'stone', 3)).toBeUndefined();
  });

  it('converts a package tare in kg like the Weighing module', () => {
    expect(decimalToString(tareFromKilograms(d('0.012'), 'g') as Decimal)).toBe('12');
    expect(decimalToString(tareFromKilograms(d('0.012'), 'KG') as Decimal)).toBe('0.012');
  });

  it('computes the amount from the net in kg', () => {
    expect(decimalToString(amountOf(d('1.248'), 'kg', d('780.00'), 2) as Decimal)).toBe('973.44');
    expect(decimalToString(amountOf(d('572'), 'g', d('100'), 2) as Decimal)).toBe('57.20');
    expect(amountOf(d('1'), 'pcs', d('1'), 2)).toBeUndefined();
  });
});

describe('live device JSON', () => {
  it('keeps the transmitted decimals of the weight and the tare', () => {
    const text =
      '{"deviceId":"s","agentName":"value","state":"Connected","lastError":null,' +
      '"lastWeight":{"deviceId":"s","value":1.260,"unit":"kg","state":"Stable","tare":0.010,"isNet":null,' +
      '"timestamp":"t","receivedAt":"t"},"lastSeen":"t"}';
    const device = parseLiveJson<LiveDevice>(text);
    expect(device.lastWeight?.value).toBe('1.260');
    expect(device.lastWeight?.tare).toBe('0.010');
    expect(device.agentName).toBe('value');
  });

  it('leaves null weights alone', () => {
    const device = parseLiveJson<LiveDevice>('{"lastWeight":{"value":null,"tare":null}}');
    expect(device.lastWeight?.value).toBeNull();
  });
});
