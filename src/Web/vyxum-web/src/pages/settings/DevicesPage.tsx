import { Fragment, useCallback, useEffect, useState, type FormEvent } from 'react';
import { useCulture, useT } from '../../i18n/I18nProvider';
import { NetworkError, RuleError, ruleErrorKey, ValidationError } from '../../lib/api';
import { deleteDeviceConfig, listDeviceConfigs, toInput, updateDeviceConfig, type DeviceConfig } from '../../lib/devices';
import { formatDecimal, formatWeight } from '../../lib/format';
import { getDevices, type LiveDevice } from '../../lib/weighing';
import { ConnectionFields, draftOf, settingsOf, type ConnectionDraft } from '../../components/devices/ConnectionFields';
import {
  DeviceDetailsFields,
  detailsDraftOf,
  detailsOf,
  fieldErrorsOf,
  FormField,
  type DetailsDraft,
  type FieldErrors,
} from '../../components/devices/DeviceDetailsFields';
import { fill, useConnectionText } from '../../components/devices/deviceText';
import { ConnectionStatus, LiveWeightPanel } from '../../components/devices/LiveWeightPanel';
import { ScaleSetupFlow } from '../../components/devices/ScaleSetupFlow';
import { PageHeader } from '../../shell/PageHeader';
import '../../styles/devices.css';

/** The live status of the list is asked again this often. */
export const devicesPageTiming = { liveMs: 2000 };

function errorKeyOf(error: unknown): string {
  if (error instanceof RuleError) return ruleErrorKey(error);
  if (!(error instanceof NetworkError)) console.error('A device request failed.', error);
  return 'shell.Error';
}

type Mode = { kind: 'list' } | { kind: 'add' } | { kind: 'edit'; device: DeviceConfig } | { kind: 'delete'; device: DeviceConfig };

/** Settings > Devices: configured scales with their live state; add, edit and delete (decision V-39). */
export function DevicesPage() {
  const t = useT();
  const culture = useCulture();
  const describe = useConnectionText();
  const [devices, setDevices] = useState<DeviceConfig[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const [live, setLive] = useState<LiveDevice[]>([]);
  const [mode, setMode] = useState<Mode>({ kind: 'list' });
  const [pendingId, setPendingId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const reload = useCallback(() => setTick((value) => value + 1), []);

  useEffect(() => {
    const controller = new AbortController();
    listDeviceConfigs({ signal: controller.signal })
      .then((list) => {
        if (controller.signal.aborted) return;
        setDevices(list);
        setLoadError(null);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setLoadError(errorKeyOf(error));
      });
    return () => controller.abort();
  }, [tick]);

  // Live connection state and last weight of every device.
  useEffect(() => {
    let controller = new AbortController();
    const load = () => {
      controller.abort();
      controller = new AbortController();
      const current = controller;
      getDevices({ signal: current.signal })
        .then((list) => {
          if (!current.signal.aborted) setLive(list);
        })
        .catch((error: unknown) => {
          if (!current.signal.aborted && !(error instanceof NetworkError)) console.error('Loading the live devices failed.', error);
        });
    };
    load();
    const timer = setInterval(load, devicesPageTiming.liveMs);
    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, []);

  const closeAdd = async () => {
    if (pendingId) await deleteDeviceConfig(pendingId).catch((error: unknown) => console.error('Removing the unconfirmed scale failed.', error));
    setPendingId(null);
    setMode({ kind: 'list' });
    reload();
  };

  const header = (
    <PageHeader
      eyebrow={t('setup.Settings')}
      title={t('devices.Title')}
      actions={
        mode.kind === 'list' ? (
          <button type="button" className="button button--primary" onClick={() => setMode({ kind: 'add' })}>
            {t('devices.AddScale')}
          </button>
        ) : undefined
      }
    />
  );

  if (loadError && !devices) {
    return (
      <div className="page">
        {header}
        <div className="status" role="alert">
          <p className="status__text">{t(loadError)}</p>
          <button type="button" className="button" onClick={reload}>
            {t('shell.Retry')}
          </button>
        </div>
      </div>
    );
  }

  if (!devices) {
    return (
      <div className="page">
        {header}
        <div className="status" role="status">
          <p className="status__text">{t('shell.Loading')}</p>
        </div>
      </div>
    );
  }

  if (mode.kind === 'add') {
    return (
      <div className="page devices">
        {header}
        <ScaleSetupFlow
          existingIds={devices.map((d) => d.id)}
          onPendingChange={setPendingId}
          onConfirmed={() => {
            setPendingId(null);
            setMode({ kind: 'list' });
            setNotice('devices.Saved');
            reload();
          }}
        />
        <div className="devices__footer">
          <button type="button" className="button" onClick={() => void closeAdd()}>
            {t('devices.Cancel')}
          </button>
        </div>
      </div>
    );
  }

  if (mode.kind === 'edit') {
    return (
      <div className="page devices">
        {header}
        <DeviceEditor
          device={mode.device}
          onSaved={() => {
            setMode({ kind: 'list' });
            setNotice('devices.Saved');
            reload();
          }}
          onCancel={() => setMode({ kind: 'list' })}
        />
      </div>
    );
  }

  return (
    <div className="page devices">
      {header}
      {notice ? (
        <p className="devices__notice state--positive" role="status">
          {t(notice)}
        </p>
      ) : null}
      {devices.length === 0 ? (
        <div className="status">
          <p className="status__text">{t('devices.Empty')}</p>
        </div>
      ) : (
        <section className="section">
          <table className="table devices__table">
            <thead>
              <tr>
                <th scope="col">{t('devices.ColName')}</th>
                <th scope="col">{t('devices.ColStatus')}</th>
                <th scope="col" className="numeric">
                  {t('devices.ColLastWeight')}
                </th>
                <th scope="col">{t('devices.ColCapacity')}</th>
                <th scope="col">{t('devices.ColConnection')}</th>
                <th scope="col">
                  <span className="visually-hidden">{t('devices.ColActions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {devices.map((device) => {
                const state = live.find((d) => d.deviceId === device.id);
                const weight = state?.lastWeight;
                const unit = device.unit ?? '';
                const deleting = mode.kind === 'delete' && mode.device.id === device.id;
                return (
                  <Fragment key={device.id}>
                    <tr>
                      <td>
                        <span className="devices__name">{device.name}</span>
                        <span className="devices__id mono muted">{device.id}</span>
                      </td>
                      <td>
                        {device.isEnabled ? (
                          <ConnectionStatus state={state?.state ?? 'Disconnected'} lastError={state?.lastError} />
                        ) : (
                          <span className="muted">{t('devices.Disabled')}</span>
                        )}
                      </td>
                      <td className="numeric">
                        {weight && weight.value !== null ? `${formatWeight(weight.value, culture)}${weight.unit ? ` ${weight.unit}` : ''}` : null}
                      </td>
                      <td className="mono">
                        {device.capacity || device.division
                          ? fill(
                              t('devices.CapacityLine'),
                              device.capacity ? `${formatDecimal(device.capacity, culture)} ${unit}`.trim() : '–',
                              device.division ? `${formatDecimal(device.division, culture)} ${unit}`.trim() : '–',
                            )
                          : null}
                      </td>
                      <td className="mono devices__connection">{describe(device.transport, device.protocol)}</td>
                      <td>
                        <div className="devices__actions">
                        <button type="button" className="button button--quiet" onClick={() => setMode({ kind: 'edit', device })}>
                          {t('devices.Edit')}
                        </button>
                        <button
                          type="button"
                          className="button button--quiet"
                          aria-expanded={deleting}
                          onClick={() => setMode(deleting ? { kind: 'list' } : { kind: 'delete', device })}
                        >
                          {t('devices.Delete')}
                        </button>
                        </div>
                      </td>
                    </tr>
                    {deleting ? (
                      <DeleteRow
                        device={device}
                        onDeleted={() => {
                          setMode({ kind: 'list' });
                          setNotice(null);
                          reload();
                        }}
                        onCancel={() => setMode({ kind: 'list' })}
                      />
                    ) : null}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </section>
      )}
    </div>
  );
}

function DeleteRow({ device, onDeleted, onCancel }: { device: DeviceConfig; onDeleted: () => void; onCancel: () => void }) {
  const t = useT();
  const [busy, setBusy] = useState(false);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const remove = async () => {
    setBusy(true);
    setErrorKey(null);
    try {
      await deleteDeviceConfig(device.id);
      onDeleted();
    } catch (error) {
      setErrorKey(errorKeyOf(error));
      setBusy(false);
    }
  };
  return (
    <tr className="devices__dialog-row">
      <td colSpan={6}>
        <div className="devices__dialog" role="dialog" aria-label={t('devices.Delete')}>
          <p>{fill(t('devices.DeleteConfirm'), device.name)}</p>
          {errorKey ? (
            <p className="field__error" role="alert">
              {t(errorKey)}
            </p>
          ) : null}
          <div className="devices__dialog-actions">
            <button type="button" className="button button--warning" disabled={busy} onClick={() => void remove()}>
              {t('devices.Delete')}
            </button>
            <button type="button" className="button" disabled={busy} onClick={onCancel}>
              {t('devices.Cancel')}
            </button>
          </div>
        </div>
      </td>
    </tr>
  );
}

interface DeviceEditorProps {
  device: DeviceConfig;
  onSaved: (device: DeviceConfig) => void;
  onCancel: () => void;
}

/** Edit form: name, details, enabled and the connection settings, with the live weight as a test. */
function DeviceEditor({ device, onSaved, onCancel }: DeviceEditorProps) {
  const t = useT();
  const culture = useCulture();
  const [name, setName] = useState(device.name);
  const [enabled, setEnabled] = useState(device.isEnabled);
  const [details, setDetails] = useState<DetailsDraft>(() => detailsDraftOf(device, culture));
  const [connection, setConnection] = useState<ConnectionDraft>(() => draftOf(device.transport, device.protocol));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const formId = `device-${device.id}`;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setErrors({});
    setErrorKey(null);
    try {
      const settings = settingsOf(connection, device);
      const saved = await updateDeviceConfig(
        device.id,
        toInput(device, { name: name.trim(), isEnabled: enabled, ...detailsOf(details, culture), ...settings }),
      );
      onSaved(saved);
    } catch (error) {
      if (error instanceof ValidationError) {
        const byField = fieldErrorsOf(error);
        setErrors(byField);
        const known = ['name', 'capacity', 'division', 'unit', 'transport', 'transport.portName', 'transport.host', 'transport.port', 'protocol'];
        const other = Object.entries(byField).filter(([key]) => !known.includes(key));
        if (other.length > 0) setErrorKey(other[0]![1][0] ?? 'shell.Error');
      } else {
        setErrorKey(errorKeyOf(error));
      }
      setBusy(false);
    }
  };

  return (
    <form id={formId} className="devices__editor" onSubmit={(event) => void submit(event)} noValidate>
      <section className="section" aria-label={device.name}>
        <h2 className="devices__section-title">{device.name}</h2>
        <div className="device-form__grid">
          <FormField label={t('devices.FieldName')} errors={errors.name}>
            {(props) => (
              <input {...props} className="input" type="text" maxLength={200} value={name} onChange={(event) => setName(event.target.value)} />
            )}
          </FormField>
          <FormField label={t('devices.FieldId')}>
            {(props) => <input {...props} className="input input--mono" type="text" value={device.id} readOnly />}
          </FormField>
        </div>
        <DeviceDetailsFields draft={details} onChange={setDetails} errors={errors} />
        <label className="devices__check">
          <input type="checkbox" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} />
          <span>{t('devices.FieldEnabled')}</span>
        </label>
      </section>

      <section className="section" aria-label={t('devices.ConnectionSettings')}>
        <h2 className="devices__section-title">{t('devices.ConnectionSettings')}</h2>
        <ConnectionFields draft={connection} onChange={setConnection} errors={errors} />
      </section>

      <section className="section" aria-label={t('devices.LiveTest')}>
        <h2 className="devices__section-title">{t('devices.LiveTest')}</h2>
        <p className="scale-flow__hint">{t('devices.LiveHint')}</p>
        <LiveWeightPanel deviceId={device.id} />
      </section>

      {errorKey ? (
        <p className="field__error" role="alert">
          {t(errorKey)}
        </p>
      ) : null}
      <div className="devices__footer">
        <button type="submit" className="button button--primary" disabled={busy}>
          {t('devices.Save')}
        </button>
        <button type="button" className="button" disabled={busy} onClick={onCancel}>
          {t('devices.Cancel')}
        </button>
      </div>
    </form>
  );
}
