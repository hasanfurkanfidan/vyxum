import { useT } from '../i18n/I18nProvider';
import { PageHeader } from '../shell/PageHeader';

/** Product cards. The list and the product form arrive in a later step. */
export function ProductsPage() {
  const t = useT();
  return (
    <div className="page">
      <PageHeader title={t('products.Title')} />
      <section className="section" />
    </div>
  );
}
