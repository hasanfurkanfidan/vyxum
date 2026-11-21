import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { LicenseUpload } from '../../components/license/LicenseUpload';
import { UploadResult } from '../../components/license/UploadResult';
import { useCulture, useT } from '../../i18n/I18nProvider';
import type { UiModule } from '../../lib/api';
import { NetworkError } from '../../lib/api';
import { formatDateOnly } from '../../lib/format';
import {
  deleteLicense,
  getLicense,
  hasVerifiedLicense,
  licenseErrorKey,
  licenseFailureKey,
  stationsText,
  type LicenseModuleState,
  type LicenseState,
} from '../../lib/license';
import { getSetupStatus, type SetupStatus } from '../../lib/setup';
import { useModules } from '../../shell/ModulesProvider';
import { PageHeader } from '../../shell/PageHeader';
import { StationsSection } from './StationsSection';
import '../../styles/license.css';

/** Replaces `{0}`, `{1}`, ... in a translated text. */
function fill(text: string, ...values: (string | number)[]): string {
  return values.reduce<string>((result, value, index) => result.replace(`{${index}}`, String(value)), text);
}

/** What the license looks like once it is removed; the next fetch replaces it with the server's answer. */
function noLicenseState(current: LicenseState): LicenseState {
  return {
    ...current,
    hasLicense: false,
    valid: false,
    status: 'none',
    error: null,
    licenseId: null,
    type: null,
    licensee: null,
    validFrom: null,
    validUntil: null,
    graceEndsOn: null,
    graceDaysLeft: null,
    stationLimit: 0,
    stations: [],
    modules: [],
  };
}

interface ModuleRow {
  id: string;
  /** Display name from the module itself; null shows the id. */
  name: string | null;
  provides: readonly string[];
  requires: readonly string[];
  optional: readonly string[];
  paid: boolean;
  installed: boolean;
  state: LicenseModuleState;
}

/** The installed modules, then the modules a license names that are not installed here. */
function moduleRows(modules: readonly UiModule[], license: LicenseState | null): ModuleRow[] {
  const rows: ModuleRow[] = modules.map((m) => ({
    id: m.id,
    name: m.name ?? null,
    provides: m.provides ?? [],
    requires: m.requires ?? [],
    optional: m.optional ?? [],
    paid: m.requiresLicense === true,
    installed: true,
    state: m.license ?? 'free',
  }));
  const installed = new Set(modules.map((m) => m.id));
  for (const m of license?.modules ?? []) {
    if (!installed.has(m.id)) rows.push({ id: m.id, name: null, provides: [], requires: [], optional: [], paid: true, installed: false, state: m.license });
  }
  return rows;
}

/** Settings > Modules and license (design source: BModuller). */
export function ModulesLicensePage() {
  const t = useT();
  const culture = useCulture();
  const { modules, reload } = useModules();
  const [license, setLicense] = useState<LicenseState | null>(null);
  const [setup, setSetup] = useState<SetupStatus | null>(null);
  const [failed, setFailed] = useState(false);
  const [tick, setTick] = useState(0);
  const [message, setMessage] = useState<{ kind: 'uploaded'; state: LicenseState } | { kind: 'removed' } | null>(null);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [removeError, setRemoveError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setFailed(false);
    Promise.all([getLicense({ signal: controller.signal }), getSetupStatus({ signal: controller.signal })])
      .then(([state, status]) => {
        if (controller.signal.aborted) return;
        setLicense(state);
        setSetup(status);
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        if (!(error instanceof NetworkError)) console.error('Loading the license failed.', error);
        setFailed(true);
      });
    return () => controller.abort();
  }, [tick]);

  const rows = useMemo(() => moduleRows(modules, license), [modules, license]);

  const uploaded = useCallback((state: LicenseState) => {
    setLicense(state);
    setConfirmRemove(false);
    setMessage({ kind: 'uploaded', state });
  }, []);

  const remove = async () => {
    setRemoving(true);
    setRemoveError(null);
    try {
      await deleteLicense();
    } catch (error) {
      setRemoveError(licenseFailureKey(error));
      setRemoving(false);
      return;
    }
    // The license is gone; what follows only refreshes the screen, so a failure there is not an error of the removal.
    setLicense((current) => (current ? noLicenseState(current) : current));
    setConfirmRemove(false);
    setMessage({ kind: 'removed' });
    setRemoving(false);
    try {
      setLicense(await getLicense());
    } catch (error) {
      console.error('Reloading the license after the removal failed.', error);
    }
    await reload();
  };

  const header = <PageHeader eyebrow={t('setup.Settings')} title={t('license.Title')} />;

  if (!license) {
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

  const verified = hasVerifiedLicense(license);
  const opened = license.modules.filter((m) => m.licensed && m.installed).length;
  const sqlite = setup?.database.provider === 'Sqlite';

  const licenseRows: { label: string; value: ReactNode }[] = [];
  if (license.licensee) {
    licenseRows.push({ label: t('license.Licensee'), value: license.licensee.name });
    if (license.type) licenseRows.push({ label: t('license.Type'), value: t(typeKey(license.type)) });
    if (license.status === 'notYetValid' && license.validFrom) {
      licenseRows.push({ label: t('license.ValidFrom'), value: formatDateOnly(license.validFrom, culture) });
    }
    licenseRows.push({
      label: t('license.Validity'),
      value: license.validUntil ? formatDateOnly(license.validUntil, culture) : t('license.Perpetual'),
    });
    licenseRows.push({ label: t('license.Stations'), value: stationsText(license.stations.length, license.stationLimit) });
    licenseRows.push({ label: t('license.OpenedModules'), value: String(opened) });
    if (license.licenseId) licenseRows.push({ label: t('license.LicenseId'), value: license.licenseId });
  }

  return (
    <div className="page license-page">
      {header}
      <p className="license-page__intro">{t('license.Description')}</p>

      <div className="license-page__columns">
        <section className="license-block" aria-label={t('license.SectionLicense')}>
          <div className="license-block__head">
            <h2 className="license-block__title">{t('license.SectionLicense')}</h2>
            <span className={`license-block__status ${license.status === 'valid' ? 'state--positive' : license.status === 'none' ? 'muted' : 'state--warning'}`}>
              {statusText(license, t)}
            </span>
          </div>

          {license.status === 'none' ? <p className="license-block__text">{t('license.NoLicenseText')}</p> : null}
          {licenseRows.length > 0 ? (
            <dl className="kv">
              {licenseRows.map((row) => (
                <div key={row.label} className="kv__row">
                  <dt className="kv__label">{row.label}</dt>
                  <dd className="kv__value">{row.value}</dd>
                </div>
              ))}
            </dl>
          ) : null}
          {license.clockBehind ? <p className="license-block__notice">{t('license.ClockBehind')}</p> : null}
          {message?.kind === 'uploaded' ? <UploadResult state={message.state} /> : null}
          {message?.kind === 'removed' ? (
            <p className="license-block__ok" role="status">
              {t('license.Removed')}
            </p>
          ) : null}

          <div className="license-block__actions">
            <LicenseUpload onUploaded={uploaded} />
            {license.hasLicense && !confirmRemove ? (
              <button type="button" className="button button--warning license-upload__button" onClick={() => setConfirmRemove(true)}>
                {t('license.Remove')}
              </button>
            ) : null}
          </div>

          {confirmRemove ? (
            <div className="license-dialog" role="dialog" aria-label={t('license.Remove')}>
              <p>{t('license.RemoveConfirm')}</p>
              {removeError ? (
                <p className="field__error" role="alert">
                  {t(removeError)}
                </p>
              ) : null}
              <div className="license-dialog__actions">
                <button type="button" className="button button--warning" disabled={removing} onClick={() => void remove()}>
                  {t('license.RemoveYes')}
                </button>
                <button type="button" className="button" disabled={removing} onClick={() => setConfirmRemove(false)}>
                  {t('license.Cancel')}
                </button>
              </div>
            </div>
          ) : null}
        </section>

        <section className="license-block" aria-label={t('license.SectionSystem')}>
          <h2 className="license-block__title">{t('license.SectionSystem')}</h2>
          {setup ? (
            <dl className="kv">
              <div className="kv__row">
                <dt className="kv__label">{t('setup.AboutVersion')}</dt>
                <dd className="kv__value">{setup.version}</dd>
              </div>
              <div className="kv__row">
                <dt className="kv__label">{t('setup.AboutDatabase')}</dt>
                <dd className="kv__value">{sqlite ? t('setup.AboutSqlite') : t('setup.AboutSqlServer')}</dd>
              </div>
              {setup.dataDirectory ? (
                <div className="kv__row">
                  <dt className="kv__label">{t('setup.AboutDataFolder')}</dt>
                  <dd className="kv__value">{setup.dataDirectory}</dd>
                </div>
              ) : null}
            </dl>
          ) : null}
        </section>
      </div>

      <section className="license-modules" aria-label={t('license.SectionModules')}>
        <table className="table">
          <thead>
            <tr>
              <th scope="col">{t('license.ColModule')}</th>
              <th scope="col">{t('license.ColKind')}</th>
              <th scope="col">{t('license.ColProvides')}</th>
              <th scope="col">{t('license.ColNeeds')}</th>
              <th scope="col">{t('license.ColStatus')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const status = moduleStatus(row);
              return (
                <tr key={row.id}>
                  <td className="license-modules__name">{row.name ?? row.id}</td>
                  <td className="license-modules__kind">{row.paid ? t('license.KindPaid') : t('license.KindOpen')}</td>
                  <td className="license-modules__caps">{row.provides.join(', ')}</td>
                  <td className="license-modules__caps license-modules__caps--muted">
                    {needsText(row, t)}
                  </td>
                  <td>
                    <span className={`chip ${status.className}`}>{t(status.textKey)}</span>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </section>

      {verified && license.stationLimit > 0 ? <StationsSection license={license} onSaved={setLicense} /> : null}
    </div>
  );
}

function typeKey(type: string): string {
  switch (type) {
    case 'trial':
      return 'license.TypeTrial';
    case 'subscription':
      return 'license.TypeSubscription';
    case 'perpetual':
      return 'license.TypePerpetual';
    default:
      return 'license.TypeNfr';
  }
}

/** The status line of the license block: a word, with the reason when the stored file does not verify. */
function statusText(license: LicenseState, t: (key: string) => string): string {
  switch (license.status) {
    case 'valid':
      return t('license.StatusValid');
    case 'invalid':
      return fill(t('license.StatusInvalid'), t(licenseErrorKey(license.error)));
    case 'notYetValid':
      return t('license.StatusNotYetValid');
    case 'grace':
      return fill(t('license.StatusGrace'), license.graceDaysLeft ?? 0);
    case 'readOnly':
      return t('license.StatusReadOnly');
    default:
      return t('license.StatusNone');
  }
}

function moduleStatus(row: ModuleRow): { textKey: string; className: string } {
  if (!row.installed) return { textKey: 'license.ModuleNotInstalled', className: '' };
  switch (row.state) {
    case 'locked':
      return { textKey: 'license.ModuleLocked', className: '' };
    case 'grace':
      return { textKey: 'license.ModuleGrace', className: 'chip--warning' };
    case 'readOnly':
      return { textKey: 'license.ModuleReadOnly', className: 'chip--warning' };
    default:
      return { textKey: 'license.ModuleOpen', className: 'chip--on' };
  }
}

/** Required capabilities, then the optional ones with a prefix word. */
function needsText(row: ModuleRow, t: (key: string) => string): string {
  const parts = [...row.requires];
  if (row.optional.length > 0) parts.push(fill(t('license.NeedsOptional'), row.optional.join(', ')));
  return parts.join('; ');
}
