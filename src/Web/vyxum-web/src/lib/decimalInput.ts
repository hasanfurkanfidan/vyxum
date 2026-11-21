// Converts decimal text typed by the user into the invariant string the API expects ("0,012" -> "0.012"),
// and the other way round for editing. Nothing is converted to floating point.
import type { Culture } from './culture';

const trGrouping = /^[-+]?[1-9]\d{0,2}(\.\d{3})+,\d+$/;
const enGrouping = /^[-+]?[1-9]\d{0,2}(,\d{3})+(\.\d+)?$/;

/**
 * Normalises typed decimal text to an invariant string.
 * tr accepts "0,012" and "0.012" (and "1.234,50" with grouping dots); en accepts "0.012" (and "1,234.50").
 * Text that cannot be recognised is returned trimmed so the server can reject it with a field error.
 */
export function toInvariantDecimal(text: string, culture: Culture): string {
  let value = text.replace(/[\s  ]/g, '');
  if (culture === 'tr') {
    if (value.split(',').length === 2) {
      if (trGrouping.test(value)) value = value.replace(/\./g, '');
      value = value.replace(',', '.');
    }
  } else if (enGrouping.test(value)) {
    value = value.replace(/,/g, '');
  }
  return value;
}

/** Invariant decimal from the API shown for editing: no grouping, culture decimal separator. */
export function toInputDecimal(value: string | null | undefined, culture: Culture): string {
  if (value === null || value === undefined) return '';
  return culture === 'tr' ? value.replace('.', ',') : value;
}
