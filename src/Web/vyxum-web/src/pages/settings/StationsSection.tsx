import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../../i18n/I18nProvider';
import { NetworkError } from '../../lib/api';
import { listDeviceConfigs, type DeviceConfig } from '../../lib/devices';
import { licenseFailureKey, saveStations, stationsText, type LicenseState } from '../../lib/license';
import { routes } from '../../shell/navigation';

export interface StationsSectionProps {
  license: LicenseState;
  /** Called with the new license state after the choice was saved. */
  onSaved: (state: LicenseState) => void;
}

/** Choice of the licensed stations: a station is a device (decision V-49), at most as many as the license allows. */
export function StationsSection({ license, onSaved }: StationsSectionProps) {
  const t = useT();
  const [devices, setDevices] = useState<DeviceConfig[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [chosen, setChosen] = useState<ReadonlySet<string>>(() => new Set(license.stations));
  const [busy, setBusy] = useState(false);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    listDeviceConfigs({ signal: controller.signal })
      .then((list) => {
        if (!controller.signal.aborted) setDevices(list);
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        if (!(error instanceof NetworkError)) console.error('Loading the devices failed.', error);
        setFailed(true);
      });
    return () => controller.abort();
  }, []);

  const toggle = (id: string, on: boolean) => {
    setSaved(false);
    setErrorKey(null);
    setChosen((current) => {
      const next = new Set(current);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });
  };

  const save = async () => {
    setBusy(true);
    setErrorKey(null);
    setSaved(false);
    try {
      const state = await saveStations([...chosen]);
      onSaved(state);
      setSaved(true);
    } catch (error) {
      setErrorKey(licenseFailureKey(error));
    }
    setBusy(false);
  };

  const over = chosen.size > license.stationLimit;

  return (
    <section className="section" aria-label={t('license.StationsTitle')}>
      <div className="license-block__head">
        <h2 className="license-block__title">{t('license.StationsTitle')}</h2>
        <span className={over ? 'stations__count stations__count--over' : 'stations__count'} aria-label={t('license.Stations')}>
          {stationsText(chosen.size, license.stationLimit)}
        </span>
      </div>
      <p className="license-block__text">{t('license.StationsHint')}</p>

      {failed ? (
        <p className="field__error" role="alert">
          {t('shell.Error')}
        </p>
      ) : devices === null ? null : devices.length === 0 ? (
        <p className="license-block__text">
          {t('license.StationsNoDevices')} <Link to={routes.settingsDevices}>{t('devices.Title')}</Link>
        </p>
      ) : (
        <ul className="stations__list" aria-label={t('license.StationsDevices')}>
          {devices.map((device) => (
            <li key={device.id} className="stations__item">
              <label className="stations__check">
                <input type="checkbox" checked={chosen.has(device.id)} onChange={(event) => toggle(device.id, event.target.checked)} />
                <span>{device.name}</span>
                {device.isEnabled ? null : <span className="muted">{t('license.StationsDisabled')}</span>}
              </label>
            </li>
          ))}
        </ul>
      )}

      {errorKey ? (
        <p className="field__error" role="alert">
          {t(errorKey)}
        </p>
      ) : null}
      {saved ? (
        <p className="license-block__ok" role="status">
          {t('license.StationsSaved')}
        </p>
      ) : null}
      {devices && devices.length > 0 ? (
        <div className="license-block__actions">
          <button type="button" className="button button--primary" disabled={busy} onClick={() => void save()}>
            {t('license.StationsSave')}
          </button>
        </div>
      ) : null}
    </section>
  );
}
