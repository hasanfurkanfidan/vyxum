import { useState } from 'react';
import { useT } from '../../i18n/I18nProvider';
import type { Product } from '../../lib/products';
import { ProductCard } from '../ProductCard';
import { StepLayout } from './StepLayout';
import '../../styles/products.css';

export interface ProductsStepProps {
  onDone: () => void;
}

/** Step 4 (only with the products catalog): add the first products, or skip. */
export function ProductsStep({ onDone }: ProductsStepProps) {
  const t = useT();
  const [added, setAdded] = useState<Product[]>([]);
  const [cardKey, setCardKey] = useState(0);

  const saved = (product: Product) => {
    setAdded((current) => [...current.filter((p) => p.id !== product.id), product]);
    // A fresh, empty card for the next product.
    setCardKey((key) => key + 1);
  };

  return (
    <StepLayout
      title={t('setup.ProductsTitle')}
      description={t('setup.ProductsDescription')}
      actions={
        added.length > 0 ? (
          <button type="button" className="button button--primary wizard__button" onClick={onDone}>
            {t('setup.Next')}
          </button>
        ) : (
          <button type="button" className="button wizard__button" onClick={onDone}>
            {t('setup.Skip')}
          </button>
        )
      }
    >
      {added.length > 0 ? (
        <section className="section wizard__section" aria-labelledby="setup-products-added">
          <h2 className="wizard__section-title" id="setup-products-added">
            {t('setup.ProductsAdded')}
          </h2>
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('products.Code')}</th>
                <th scope="col">{t('products.Product')}</th>
              </tr>
            </thead>
            <tbody>
              {added.map((product) => (
                <tr key={product.id}>
                  <td className="mono">{product.code}</td>
                  <td>{product.name}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ) : null}

      <div className="wizard__product-card">
        <ProductCard key={cardKey} product={null} onChanged={saved} />
      </div>
    </StepLayout>
  );
}
