import { useCallback, useEffect, useRef, useState } from 'react';
import { useCulture, useT } from '../i18n/I18nProvider';
import { formatDecimal } from '../lib/format';
import { listProducts, type Product } from '../lib/products';
import { PageHeader } from '../shell/PageHeader';
import { ProductCard } from './ProductCard';
import '../styles/products.css';

const searchDelayMs = 250;

type ListStatus = 'loading' | 'ready' | 'error';

/** Selection state of the card: nothing, a new product, or an existing one. `key` remounts the card. */
interface CardState {
  key: number;
  product: Product | null;
}

function useDebounced<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/** Product cards: list with search on the left, the product card form on the right. */
export function ProductsPage() {
  const t = useT();
  const culture = useCulture();
  const [search, setSearch] = useState('');
  const [showInactive, setShowInactive] = useState(false);
  const [items, setItems] = useState<Product[] | null>(null);
  const [status, setStatus] = useState<ListStatus>('loading');
  const [card, setCard] = useState<CardState | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const cardKey = useRef(0);
  const query = useDebounced(search, searchDelayMs);
  const [reloadTick, setReloadTick] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setStatus('loading');
    listProducts(query, showInactive, { signal: controller.signal })
      .then((products) => {
        if (controller.signal.aborted) return;
        setItems(products);
        setStatus('ready');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        console.error('Loading products failed.', error);
        setStatus('error');
      });
    return () => controller.abort();
  }, [query, showInactive, reloadTick]);

  const refresh = useCallback(() => setReloadTick((tick) => tick + 1), []);

  const select = (product: Product) => {
    setSelectedId(product.id);
    setCard({ key: ++cardKey.current, product });
  };

  const addProduct = () => {
    setSelectedId(null);
    setCard({ key: ++cardKey.current, product: null });
  };

  // The card keeps showing the saved product; only the list and the highlight follow.
  const changed = useCallback(
    (product: Product) => {
      setSelectedId(product.id);
      refresh();
    },
    [refresh],
  );

  const count = items === null ? null : t('products.CountSuffix').replace('{0}', String(items.length));
  const eyebrow = count === null ? t('products.Breadcrumb') : `${t('products.Breadcrumb')} / ${count}`;
  const dash = '–';

  return (
    <div className="page">
      <PageHeader
        title={t('products.Title')}
        eyebrow={eyebrow}
        actions={
          <button type="button" className="button button--primary" onClick={addProduct}>
            {t('products.AddProduct')}
          </button>
        }
      />

      <div className="products">
        <section className="section products__list" aria-label={t('products.Breadcrumb')}>
          <div className="products__toolbar">
            <input
              className="input products__search"
              type="search"
              value={search}
              placeholder={t('products.SearchPlaceholder')}
              aria-label={t('products.SearchPlaceholder')}
              onChange={(event) => setSearch(event.target.value)}
            />
            <label className="products__toggle">
              <input type="checkbox" checked={showInactive} onChange={(event) => setShowInactive(event.target.checked)} />
              <span>{t('products.ShowInactive')}</span>
            </label>
          </div>

          {status === 'error' ? (
            <div className="status status--inline" role="alert">
              <p className="status__text">{t('shell.Error')}</p>
              <button type="button" className="button button--primary" onClick={refresh}>
                {t('shell.Retry')}
              </button>
            </div>
          ) : items === null ? (
            <p className="status__text" role="status">
              {t('shell.Loading')}
            </p>
          ) : items.length === 0 ? (
            <p className="status__text" role="status">
              {t('products.Empty')}
            </p>
          ) : (
            <div className="products__scroll">
              <table className="table products__table" aria-busy={status === 'loading'}>
                <thead>
                  <tr>
                    <th scope="col">{t('products.Code')}</th>
                    <th scope="col">{t('products.Product')}</th>
                    <th scope="col" className="numeric">
                      {t('products.UnitPricePerKg')}
                    </th>
                    <th scope="col" className="numeric">
                      {t('products.PackageTare')}
                    </th>
                    <th scope="col" className="numeric">
                      {t('products.ShelfLife')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((product) => {
                    const selected = product.id === selectedId;
                    return (
                      <tr
                        key={product.id}
                        className={selected ? 'is-selected' : undefined}
                        onClick={() => select(product)}
                      >
                        <td className="mono muted">{product.code}</td>
                        <td className="products__name">
                          <button
                            type="button"
                            className="products__pick"
                            aria-current={selected ? 'true' : undefined}
                            onClick={(event) => {
                              event.stopPropagation();
                              select(product);
                            }}
                          >
                            {product.name}
                          </button>
                          {product.isActive ? null : <span className="state--warning products__inactive">{t('products.Inactive')}</span>}
                        </td>
                        <td className="numeric">{product.unitPrice === null ? dash : formatDecimal(product.unitPrice, culture)}</td>
                        <td className="numeric">{product.packageTare === null ? dash : formatDecimal(product.packageTare, culture)}</td>
                        <td className="numeric">
                          {product.shelfLifeDays === null ? dash : t('products.Days').replace('{0}', String(product.shelfLifeDays))}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>

        {card ? (
          <div className="products__card">
            <ProductCard key={card.key} product={card.product} onChanged={changed} />
          </div>
        ) : null}
      </div>
    </div>
  );
}
