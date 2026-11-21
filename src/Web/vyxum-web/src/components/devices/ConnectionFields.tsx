import { useId, type ReactNode } from 'react';
import { useT } from '../../i18n/I18nProvider';
import {
  baudRates,
  frameOf,
  frameText,
  protocolFor,
  protocolTypes,
  serialFrames,
  type ProtocolSettings,
  type TransportSettings,
} from '../../lib/devices';

/** Connection settings as edited on screen. Numbers stay text until saved so the server can reject bad input. */
export interface ConnectionDraft {
  transportType: string;
  portName: string;
  baudRate: string;
  frame: string;
  host: string;
  tcpPort: string;
  protocolType: string;
}

export const emptyConnection: ConnectionDraft = {
  transportType: 'serial',
  portName: '',
  baudRate: '9600',
  frame: '8N1',
  host: '',
  tcpPort: '',
  protocolType: 'mt-sics',
};

export function draftOf(transport: TransportSettings, protocol: ProtocolSettings): ConnectionDraft {
  return {
    transportType: String(transport.type),
    portName: transport.portName ?? '',
    baudRate: String(transport.baudRate ?? 9600),
    frame: frameText(transport),
    host: transport.host ?? '',
    tcpPort: transport.port !== undefined ? String(transport.port) : '',
    protocolType: protocol.type,
  };
}

function toNumber(text: string): number | string {
  const trimmed = text.trim();
  return /^\d+$/.test(trimmed) ? Number(trimmed) : trimmed;
}

/**
 * Transport and protocol settings from the draft. Fields of the original settings that the screen
 * does not show (timeouts, poll interval, simulator settings) are kept when the type is unchanged.
 */
export function settingsOf(
  draft: ConnectionDraft,
  original?: { transport: TransportSettings; protocol: ProtocolSettings },
): { transport: TransportSettings; protocol: ProtocolSettings } {
  const sameTransport = original && String(original.transport.type) === draft.transportType;
  let transport: TransportSettings;
  if (draft.transportType === 'serial') {
    const frame = frameOf(draft.frame);
    const base: TransportSettings = sameTransport ? { ...original.transport } : { type: 'serial', handshake: 'None' };
    delete base.host;
    delete base.port;
    transport = {
      ...base,
      type: 'serial',
      portName: draft.portName.trim(),
      baudRate: toNumber(draft.baudRate) as number,
      dataBits: frame.dataBits,
      parity: frame.parity,
      stopBits: frame.stopBits,
    };
  } else if (draft.transportType === 'tcp') {
    const base: TransportSettings = sameTransport ? { ...original.transport } : { type: 'tcp' };
    transport = { ...base, type: 'tcp', host: draft.host.trim(), port: toNumber(draft.tcpPort) as number };
  } else {
    transport = sameTransport ? original.transport : { type: draft.transportType };
  }

  const protocol =
    original && original.protocol.type === draft.protocolType ? original.protocol : protocolFor(draft.protocolType);
  return { transport, protocol };
}

export interface ConnectionFieldsProps {
  draft: ConnectionDraft;
  onChange: (draft: ConnectionDraft) => void;
  /** Ports found by the last scan, offered as suggestions for the port name. */
  knownPorts?: readonly string[];
  /** Error keys by field: `transport`, `transport.portName`, `transport.host`, `transport.port`, `protocol`. */
  errors?: Readonly<Record<string, readonly string[]>>;
  /** Shows the simulator choice (only for a device that already uses it). */
  allowSimulator?: boolean;
}

/** Connection type, serial or network settings and protocol. */
export function ConnectionFields({ draft, onChange, knownPorts = [], errors = {}, allowSimulator = false }: ConnectionFieldsProps) {
  const t = useT();
  const id = useId();
  const set = (change: Partial<ConnectionDraft>) => onChange({ ...draft, ...change });

  const field = (name: string, label: string, control: (props: { id: string; invalid: boolean; describedBy?: string }) => ReactNode, wide = false) => {
    const fieldId = `${id}-${name.replace('.', '-')}`;
    const keys = errors[name] ?? [];
    const errorId = `${fieldId}-error`;
    return (
      <div className={`field${wide ? ' device-form__wide' : ''}`}>
        <label className="field__label" htmlFor={fieldId}>
          {label}
        </label>
        {control({ id: fieldId, invalid: keys.length > 0, ...(keys.length > 0 ? { describedBy: errorId } : {}) })}
        {keys.length > 0 ? (
          <p className="field__error" id={errorId}>
            {keys.map((key) => t(key)).join(' ')}
          </p>
        ) : null}
      </div>
    );
  };

  const knownProtocol = protocolTypes.some((p) => p.type === draft.protocolType);
  const portListId = `${id}-ports`;

  return (
    <div className="device-form__grid">
      {field('transport', t('devices.FieldTransport'), ({ id: fieldId, invalid, describedBy }) => (
        <select
          id={fieldId}
          className="input"
          value={draft.transportType}
          aria-invalid={invalid}
          aria-describedby={describedBy}
          onChange={(event) => set({ transportType: event.target.value })}
        >
          <option value="serial">{t('devices.TransportSerial')}</option>
          <option value="tcp">{t('devices.TransportTcp')}</option>
          {allowSimulator || draft.transportType === 'simulator' ? (
            <option value="simulator">{t('devices.TransportSimulator')}</option>
          ) : null}
        </select>
      ))}

      {field('protocol', t('devices.FieldProtocol'), ({ id: fieldId, invalid, describedBy }) => (
        <select
          id={fieldId}
          className="input"
          value={draft.protocolType}
          aria-invalid={invalid}
          aria-describedby={describedBy}
          onChange={(event) => set({ protocolType: event.target.value })}
        >
          {protocolTypes.map((p) => (
            <option key={p.type} value={p.type}>
              {t(p.labelKey)}
            </option>
          ))}
          {knownProtocol ? null : <option value={draft.protocolType}>{draft.protocolType}</option>}
        </select>
      ))}

      {draft.transportType === 'serial' ? (
        <>
          {field('transport.portName', t('devices.FieldPortName'), ({ id: fieldId, invalid, describedBy }) => (
            <>
              <input
                id={fieldId}
                className="input input--mono"
                type="text"
                list={knownPorts.length > 0 ? portListId : undefined}
                value={draft.portName}
                aria-invalid={invalid}
                aria-describedby={describedBy}
                autoComplete="off"
                spellCheck={false}
                onChange={(event) => set({ portName: event.target.value })}
              />
              {knownPorts.length > 0 ? (
                <datalist id={portListId}>
                  {knownPorts.map((port) => (
                    <option key={port} value={port} />
                  ))}
                </datalist>
              ) : null}
            </>
          ))}
          {field('transport.baudRate', t('devices.FieldBaudRate'), ({ id: fieldId }) => (
            <select id={fieldId} className="input input--mono" value={draft.baudRate} onChange={(event) => set({ baudRate: event.target.value })}>
              {baudRates.map((rate) => (
                <option key={rate} value={String(rate)}>
                  {rate}
                </option>
              ))}
              {baudRates.some((rate) => String(rate) === draft.baudRate) ? null : <option value={draft.baudRate}>{draft.baudRate}</option>}
            </select>
          ))}
          {field('transport.frame', t('devices.FieldFrame'), ({ id: fieldId }) => (
            <select id={fieldId} className="input input--mono" value={draft.frame} onChange={(event) => set({ frame: event.target.value })}>
              {serialFrames.map((frame) => {
                const text = frameText(frame);
                return (
                  <option key={text} value={text}>
                    {text}
                  </option>
                );
              })}
              {serialFrames.some((frame) => frameText(frame) === draft.frame) ? null : <option value={draft.frame}>{draft.frame}</option>}
            </select>
          ))}
        </>
      ) : null}

      {draft.transportType === 'tcp' ? (
        <>
          {field('transport.host', t('devices.FieldHost'), ({ id: fieldId, invalid, describedBy }) => (
            <input
              id={fieldId}
              className="input input--mono"
              type="text"
              value={draft.host}
              aria-invalid={invalid}
              aria-describedby={describedBy}
              autoComplete="off"
              spellCheck={false}
              onChange={(event) => set({ host: event.target.value })}
            />
          ))}
          {field('transport.port', t('devices.FieldTcpPort'), ({ id: fieldId, invalid, describedBy }) => (
            <input
              id={fieldId}
              className="input input--mono"
              type="text"
              inputMode="numeric"
              value={draft.tcpPort}
              aria-invalid={invalid}
              aria-describedby={describedBy}
              autoComplete="off"
              onChange={(event) => set({ tcpPort: event.target.value })}
            />
          ))}
        </>
      ) : null}
    </div>
  );
}
