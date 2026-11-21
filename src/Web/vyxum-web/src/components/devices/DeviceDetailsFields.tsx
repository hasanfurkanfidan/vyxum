import { useId, type ReactNode } from 'react';
import { useT } from '../../i18n/I18nProvider';
import { ValidationError } from '../../lib/api';
import type { Culture } from '../../lib/culture';
import { toInputDecimal, toInvariantDecimal } from '../../lib/decimalInput';

export type FieldErrors = Readonly<Record<string, readonly string[]>>;

/** Validation errors by field, with every segment starting lower case (`Transport.PortName` -> `transport.portName`). */
export function fieldErrorsOf(error: ValidationError): Record<string, string[]> {
  const result: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(error.errors)) {
    const name = key
      .split('.')
      .map((part) => (part.length > 0 ? part[0]!.toLowerCase() + part.slice(1) : part))
      .join('.');
    result[name] = [...(result[name] ?? []), ...messages];
  }
  return result;
}

export interface FormFieldProps {
  label: string;
  errors?: readonly string[] | undefined;
  hint?: string | undefined;
  wide?: boolean;
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby': string | undefined }) => ReactNode;
}

/** Label, control and the translated error keys under it. */
export function FormField({ label, errors = [], hint, wide = false, children }: FormFieldProps) {
  const t = useT();
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const describedBy = [errors.length > 0 ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ');
  return (
    <div className={`field${wide ? ' device-form__wide' : ''}`}>
      <label className="field__label" htmlFor={id}>
        {label}
      </label>
      {children({ id, 'aria-invalid': errors.length > 0, 'aria-describedby': describedBy || undefined })}
      {hint ? (
        <p className="field__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {errors.length > 0 ? (
        <p className="field__error" id={errorId}>
          {errors.map((key) => t(key)).join(' ')}
        </p>
      ) : null}
    </div>
  );
}

/** Capacity, division and unit as typed on screen (culture decimal separator). */
export interface DetailsDraft {
  capacity: string;
  division: string;
  unit: string;
}

export function detailsDraftOf(
  values: { capacity: string | null; division: string | null; unit: string | null },
  culture: Culture,
): DetailsDraft {
  return {
    capacity: toInputDecimal(values.capacity, culture),
    division: toInputDecimal(values.division, culture),
    unit: values.unit ?? '',
  };
}

/** The draft as API values: invariant decimal strings, null for empty fields. */
export function detailsOf(draft: DetailsDraft, culture: Culture): { capacity: string | null; division: string | null; unit: string | null } {
  const decimal = (text: string) => (text.trim() === '' ? null : toInvariantDecimal(text, culture));
  return { capacity: decimal(draft.capacity), division: decimal(draft.division), unit: draft.unit.trim() === '' ? null : draft.unit.trim() };
}

export interface DeviceDetailsFieldsProps {
  draft: DetailsDraft;
  onChange: (draft: DetailsDraft) => void;
  errors?: FieldErrors;
}

/** Capacity (Max), division (e) and unit of a scale. */
export function DeviceDetailsFields({ draft, onChange, errors = {} }: DeviceDetailsFieldsProps) {
  const t = useT();
  const set = (change: Partial<DetailsDraft>) => onChange({ ...draft, ...change });
  return (
    <div className="device-form__grid">
      <FormField label={t('devices.FieldCapacity')} errors={errors.capacity}>
        {(props) => (
          <input {...props} className="input input--mono" type="text" inputMode="decimal" autoComplete="off" value={draft.capacity} onChange={(event) => set({ capacity: event.target.value })} />
        )}
      </FormField>
      <FormField label={t('devices.FieldDivision')} errors={errors.division}>
        {(props) => (
          <input {...props} className="input input--mono" type="text" inputMode="decimal" autoComplete="off" value={draft.division} onChange={(event) => set({ division: event.target.value })} />
        )}
      </FormField>
      <FormField label={t('devices.FieldUnit')} errors={errors.unit}>
        {(props) => (
          <input {...props} className="input input--mono" type="text" autoComplete="off" maxLength={16} value={draft.unit} onChange={(event) => set({ unit: event.target.value })} />
        )}
      </FormField>
    </div>
  );
}
