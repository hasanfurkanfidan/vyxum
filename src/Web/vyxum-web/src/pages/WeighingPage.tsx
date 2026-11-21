import { useT } from '../i18n/I18nProvider';
import { PageHeader } from '../shell/PageHeader';

/** Operator screen. The live weight, product selection and recording arrive in a later step. */
export function WeighingPage() {
  const t = useT();
  return (
    <div className="page">
      <PageHeader title={t('weighing.Title')} />
      <section className="section" />
    </div>
  );
}
