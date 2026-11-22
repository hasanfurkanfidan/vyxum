import { useEffect, useState, type ReactNode } from 'react';
import { useCulture, useT } from '../../i18n/I18nProvider';
import { NetworkError } from '../../lib/api';
import { formatDateTime } from '../../lib/format';
import { getSetupStatus, type SetupStatus } from '../../lib/setup';
import { PageHeader } from '../../shell/PageHeader';
import '../../styles/devices.css';

/** Settings > About: version, system requirement, database, data folder, last backup and how to update (decision V-43). */
export function AboutPage() {
  const t = useT();
  const culture = useCulture();
  const [status, setStatus] = useState<SetupStatus | null>(null);
  const [failed, setFailed] = useState(false);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setFailed(false);
    getSetupStatus({ signal: controller.signal })
      .then((result) => {
        if (!controller.signal.aborted) setStatus(result);
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        if (!(error instanceof NetworkError)) console.error('Loading the setup status failed.', error);
        setFailed(true);
      });
    return () => controller.abort();
  }, [tick]);

  const header = <PageHeader eyebrow={t('setup.Settings')} title={t('setup.AboutTitle')} />;

  if (!status) {
    return (
      <div className="page">
        {header}
        {failed ? (
          <div className="status" role="alert">
            <p className="status__text">{t('shell.Error')}</p>
            <button type="button" className="button" onClick={() => setTick((value) => value + 1)}>
              {t('shell.Retry')}
            </button>
          </div>
        ) : (
          <div className="status" role="status">
            <p className="status__text">{t('shell.Loading')}</p>
          </div>
        )}
      </div>
    );
  }

  const sqlite = status.database.provider === 'Sqlite';
  const rows: { label: string; value: ReactNode }[] = [
    { label: t('setup.AboutVersion'), value: <span className="mono">{status.version}</span> },
    { label: t('setup.AboutSystem'), value: t('setup.AboutSystemValue') },
    { label: t('setup.AboutCompany'), value: status.company.name ?? <span className="muted">{t('setup.AboutNoCompany')}</span> },
    {
      label: t('setup.AboutDatabase'),
      value: (
        <span className="about__value">
          <span>{sqlite ? t('setup.AboutSqlite') : t('setup.AboutSqlServer')}</span>
          {sqlite ? null : <span className="muted">{t('setup.AboutSqlServerBackup')}</span>}
        </span>
      ),
    },
  ];
  if (status.dataDirectory) {
    rows.push({ label: t('setup.AboutDataFolder'), value: <span className="mono about__path">{status.dataDirectory}</span> });
  }
  if (sqlite || status.lastBackup) {
    rows.push({
      label: t('setup.AboutLastBackup'),
      value: status.lastBackup ? (
        <span className="about__value">
          <span className="mono">{formatDateTime(status.lastBackup.at, culture)}</span>
          {status.lastBackup.path ? <span className="mono muted about__path">{status.lastBackup.path}</span> : null}
        </span>
      ) : (
        <span className="muted">{t('setup.AboutNoBackup')}</span>
      ),
    });
  }

  return (
    <div className="page about">
      {header}
      <section className="section">
        <dl className="about__list">
          {rows.map((row) => (
            <div key={row.label} className="about__row">
              <dt className="about__label">{row.label}</dt>
              <dd className="about__data">{row.value}</dd>
            </div>
          ))}
        </dl>
      </section>
      <section className="section" aria-label={t('setup.UpdateTitle')}>
        <h2 className="devices__section-title">{t('setup.UpdateTitle')}</h2>
        <p className="about__note">{t('setup.UpdateNote')}</p>
      </section>
    </div>
  );
}
