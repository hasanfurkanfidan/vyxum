import { useT } from '../i18n/I18nProvider';
import { PageHeader } from '../shell/PageHeader';

/** Weighing records. Filters, the table, cancelling and the export arrive in a later step. */
export function RecordsPage() {
  const t = useT();
  return (
    <div className="page">
      <PageHeader title={t('records.Title')} />
      <section className="section" />
    </div>
  );
}
