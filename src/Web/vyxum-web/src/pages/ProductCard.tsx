import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useCulture, useT } from '../i18n/I18nProvider';
import { ValidationError } from '../lib/api';
import { toInputDecimal, toInvariantDecimal } from '../lib/decimalInput';
import {
  createProduct,
  productFields,
  setProductActive,
  updateProduct,
  type Product,
  type ProductField,
  type ProductInput,
  type SaleUnit,
} from '../lib/products';

interface FormValues {
  code: string;
  name: string;
  unitPrice: string;
  packageTare: string;
  shelfLifeDays: string;
  barcodePrefix: string;
  ingredients: string;
  targetWeight: string;
  lowerTolerance: string;
  upperTolerance: string;
  saleUnit: SaleUnit;
  unitCost: string;
}

type FieldErrors = Partial<Record<ProductField, readonly string[]>>;

const emptyValues: FormValues = {
  code: '',
  name: '',
  unitPrice: '',
  packageTare: '',
  shelfLifeDays: '',
  barcodePrefix: '',
  ingredients: '',
  targetWeight: '',
  lowerTolerance: '',
  upperTolerance: '',
  saleUnit: 'Kg',
  unitCost: '',
};

export interface ProductCardProps {
  /** The product to edit, or null for an empty card. The card keeps its own copy while it is shown. */
  product: Product | null;
  /** Called after every successful save, deactivate or activate, so the list can refresh. */
  onChanged: (product: Product) => void;
}

function toValues(product: Product | null, culture: 'tr' | 'en'): FormValues {
  if (!product) return emptyValues;
  return {
    code: product.code,
    name: product.name,
    unitPrice: toInputDecimal(product.unitPrice, culture),
    packageTare: toInputDecimal(product.packageTare, culture),
    shelfLifeDays: product.shelfLifeDays === null ? '' : String(product.shelfLifeDays),
    barcodePrefix: product.barcodePrefix ?? '',
    ingredients: product.ingredients ?? '',
    targetWeight: toInputDecimal(product.targetWeight, culture),
    lowerTolerance: toInputDecimal(product.lowerTolerance, culture),
    upperTolerance: toInputDecimal(product.upperTolerance, culture),
    saleUnit: product.saleUnit ?? 'Kg',
    unitCost: toInputDecimal(product.unitCost ?? null, culture),
  };
}

interface FieldProps {
  id: string;
  label: string;
  errors: readonly string[] | undefined;
  hint?: string | undefined;
  wide?: boolean;
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby': string | undefined }) => ReactNode;
}

function Field({ id, label, errors, hint, wide, children }: FieldProps) {
  const t = useT();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const describedBy = [errors?.length ? errorId : undefined, hint ? hintId : undefined].filter(Boolean).join(' ');
  return (
    <div className={wide ? 'field field--wide' : 'field'}>
      <label className="field__label" htmlFor={id}>
        {label}
      </label>
      {children({ id, 'aria-invalid': Boolean(errors?.length), 'aria-describedby': describedBy || undefined })}
      {hint ? (
        <p className="field__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {errors?.length ? (
        <p className="field__error" id={errorId}>
          {errors.map((key) => t(key)).join(' ')}
        </p>
      ) : null}
    </div>
  );
}

/** Product card form: create or edit one product. Mount it with a new `key` to show another product. */
export function ProductCard({ product: initial, onChanged }: ProductCardProps) {
  const t = useT();
  const culture = useCulture();
  const idPrefix = useId();
  const [product, setProduct] = useState<Product | null>(initial);
  const [values, setValues] = useState<FormValues>(() => toValues(initial, culture));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [failed, setFailed] = useState(false);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const nameRef = useRef<HTMLInputElement>(null);

  // A new card starts at the name field.
  useEffect(() => {
    if (initial === null) nameRef.current?.focus();
  }, [initial]);

  const set = (field: keyof FormValues, value: string) => {
    setValues((current) => ({ ...current, [field]: value }));
    setSaved(false);
    setErrors((current) => (current[field] ? { ...current, [field]: undefined } : current));
  };

  const finish = (result: Product) => {
    setProduct(result);
    setValues(toValues(result, culture));
    setErrors({});
    setFailed(false);
    onChanged(result);
  };

  const fail = (error: unknown) => {
    if (error instanceof ValidationError) {
      const next: FieldErrors = {};
      for (const field of productFields) {
        const keys = error.forField(field);
        if (keys.length > 0) next[field] = keys;
      }
      setErrors(next);
      setFailed(Object.keys(next).length === 0);
    } else {
      console.error('Saving the product failed.', error);
      setFailed(true);
    }
  };

  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;

    const shelfLife = values.shelfLifeDays.trim();
    if (shelfLife !== '' && !/^[-+]?\d+$/.test(shelfLife)) {
      setErrors({ shelfLifeDays: ['products.InvalidNumber'] });
      setFailed(false);
      return;
    }

    const optionalDecimal = (text: string) => (text.trim() === '' ? null : toInvariantDecimal(text, culture));
    const input: ProductInput = {
      code: values.code,
      name: values.name,
      unitPrice: optionalDecimal(values.unitPrice),
      packageTare: optionalDecimal(values.packageTare),
      shelfLifeDays: shelfLife === '' ? null : Number(shelfLife),
      barcodePrefix: values.barcodePrefix,
      ingredients: values.ingredients,
      targetWeight: optionalDecimal(values.targetWeight),
      lowerTolerance: optionalDecimal(values.lowerTolerance),
      upperTolerance: optionalDecimal(values.upperTolerance),
      saleUnit: values.saleUnit,
      unitCost: optionalDecimal(values.unitCost),
    };

    setBusy(true);
    setSaved(false);
    try {
      const result = product ? await updateProduct(product.id, input) : await createProduct(input);
      finish(result);
      setSaved(true);
    } catch (error) {
      fail(error);
    } finally {
      setBusy(false);
    }
  };

  const toggleActive = async () => {
    if (!product || busy) return;
    setBusy(true);
    setSaved(false);
    try {
      finish(await setProductActive(product.id, !product.isActive));
    } catch (error) {
      fail(error);
    } finally {
      setBusy(false);
    }
  };

  const headingId = `${idPrefix}-heading`;
  const fieldId = (name: string) => `${idPrefix}-${name}`;

  return (
    <section className="section product-card" aria-labelledby={headingId}>
      <div className="product-card__head">
        <h2 className="product-card__title" id={headingId}>
          {product ? product.name : t('products.NewProduct')}
        </h2>
        {product ? <span className="product-card__code mono">{product.code}</span> : null}
      </div>
      {product && !product.isActive ? <p className="state--warning">{t('products.Inactive')}</p> : null}

      <form className="product-card__form" onSubmit={(event) => void save(event)} noValidate>
        <div className="product-card__grid">
          <Field id={fieldId('name')} label={t('products.Name')} errors={errors.name} wide>
            {(a) => (
              <input
                {...a}
                ref={nameRef}
                className="input"
                type="text"
                value={values.name}
                aria-required="true"
                onChange={(event) => set('name', event.target.value)}
              />
            )}
          </Field>
          <Field id={fieldId('code')} label={t('products.Code')} errors={errors.code}>
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                value={values.code}
                aria-required="true"
                onChange={(event) => set('code', event.target.value)}
              />
            )}
          </Field>
          <Field id={fieldId('barcodePrefix')} label={t('products.BarcodePrefix')} errors={errors.barcodePrefix}>
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                value={values.barcodePrefix}
                onChange={(event) => set('barcodePrefix', event.target.value)}
              />
            )}
          </Field>
          <Field id={fieldId('unitPrice')} label={t('products.UnitPricePerKg')} errors={errors.unitPrice}>
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                inputMode="decimal"
                value={values.unitPrice}
                onChange={(event) => set('unitPrice', event.target.value)}
              />
            )}
          </Field>
          <Field id={fieldId('packageTare')} label={t('products.PackageTare')} errors={errors.packageTare}>
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                inputMode="decimal"
                value={values.packageTare}
                onChange={(event) => set('packageTare', event.target.value)}
              />
            )}
          </Field>
          <Field id={fieldId('unitCost')} label={t('products.UnitCostPerKg')} errors={errors.unitCost}>
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                inputMode="decimal"
                value={values.unitCost}
                onChange={(event) => set('unitCost', event.target.value)}
              />
            )}
          </Field>
          <Field id={fieldId('saleUnit')} label={t('products.SaleUnit')} errors={errors.saleUnit}>
            {(a) => (
              <select {...a} className="input" value={values.saleUnit} onChange={(event) => set('saleUnit', event.target.value)}>
                <option value="Kg">{t('products.SaleUnitKg')}</option>
                <option value="Piece">{t('products.SaleUnitPiece')}</option>
              </select>
            )}
          </Field>
          <Field id={fieldId('shelfLifeDays')} label={t('products.ShelfLife')} errors={errors.shelfLifeDays}>
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                inputMode="numeric"
                value={values.shelfLifeDays}
                onChange={(event) => set('shelfLifeDays', event.target.value)}
              />
            )}
          </Field>
          <Field
            id={fieldId('targetWeight')}
            label={t('products.TargetWeight')}
            hint={values.saleUnit === 'Piece' ? t('products.SaleUnitHint') : t('products.ToleranceHint')}
            errors={errors.targetWeight}
            wide
          >
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                inputMode="decimal"
                value={values.targetWeight}
                onChange={(event) => set('targetWeight', event.target.value)}
              />
            )}
          </Field>
          <Field id={fieldId('lowerTolerance')} label={t('products.LowerTolerance')} errors={errors.lowerTolerance}>
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                inputMode="decimal"
                value={values.lowerTolerance}
                onChange={(event) => set('lowerTolerance', event.target.value)}
              />
            )}
          </Field>
          <Field id={fieldId('upperTolerance')} label={t('products.UpperTolerance')} errors={errors.upperTolerance}>
            {(a) => (
              <input
                {...a}
                className="input input--mono"
                type="text"
                inputMode="decimal"
                value={values.upperTolerance}
                onChange={(event) => set('upperTolerance', event.target.value)}
              />
            )}
          </Field>
          <Field
            id={fieldId('ingredients')}
            label={t('products.Ingredients')}
            hint={t('products.IngredientsHint')}
            errors={errors.ingredients}
            wide
          >
            {(a) => (
              <textarea
                {...a}
                className="input input--area"
                rows={3}
                value={values.ingredients}
                onChange={(event) => set('ingredients', event.target.value)}
              />
            )}
          </Field>
        </div>

        {failed ? (
          <p className="field__error" role="alert">
            {t('shell.Error')}
          </p>
        ) : null}

        <div className="product-card__actions">
          <button type="submit" className="button button--primary product-card__save" disabled={busy}>
            {t('products.Save')}
          </button>
          {product ? (
            <button
              type="button"
              className={product.isActive ? 'button button--warning' : 'button'}
              disabled={busy}
              onClick={() => void toggleActive()}
            >
              {product.isActive ? t('products.Deactivate') : t('products.Activate')}
            </button>
          ) : null}
          <p className="state--positive product-card__saved" role="status">
            {saved ? t('products.Saved') : null}
          </p>
        </div>
      </form>
    </section>
  );
}
