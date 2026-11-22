// Client for the license API (/api/license). Error codes of a rejected request are keys `license.Error.{code}`.
import { apiDelete, apiGet, apiPut, apiPostText, RuleError, type RequestOptions } from './api';

export type LicenseStatus = 'none' | 'invalid' | 'notYetValid' | 'valid' | 'grace' | 'readOnly';
export type LicenseType = 'trial' | 'subscription' | 'perpetual' | 'nfr';
export type LicenseModuleState = 'free' | 'licensed' | 'grace' | 'readOnly' | 'locked';

export interface LicenseModule {
  id: string;
  installed: boolean;
  licensed: boolean;
  license: LicenseModuleState;
}

/** The body of GET /api/license (and of a successful upload or station change). Dates are `yyyy-MM-dd`. */
export interface LicenseState {
  hasLicense: boolean;
  valid: boolean;
  status: LicenseStatus;
  error: string | null;
  licenseId: string | null;
  type: LicenseType | null;
  licensee: { name: string; taxNumber: string | null } | null;
  validFrom: string | null;
  validUntil: string | null;
  graceEndsOn: string | null;
  graceDaysLeft: number | null;
  clockBehind: boolean;
  /** True for about six hours after the clock jumped far ahead; it can be a false alarm, so show it quietly. */
  clockAhead: boolean;
  stationLimit: number;
  stations: string[];
  modules: LicenseModule[];
}

/** Error codes of the license API that have a message of their own. */
export const licenseErrorCodes = [
  'invalid_format',
  'unsupported_format',
  'unknown_key',
  'invalid_signature',
  'invalid_payload',
  'too_large',
  'no_license',
  'unknown_device',
  'station_limit_exceeded',
  'stations_too_long',
  'license_required',
  'license_read_only',
] as const;

/** Resource key of the message for an error code (a code without a message of its own gets the general one). */
export function licenseErrorKey(code: string | null | undefined): string {
  return code && (licenseErrorCodes as readonly string[]).includes(code) ? `license.Error.${code}` : 'license.Error.unknown';
}

/** Resource key of the message for a failed license request; a lost connection gets the general server message. */
export function licenseFailureKey(error: unknown): string {
  if (error instanceof RuleError) return licenseErrorKey(error.code);
  return error instanceof Error && error.name === 'NetworkError' ? 'shell.Error' : 'license.Error.unknown';
}

/** Whether a license that verifies is stored (valid, in its grace period or read-only). */
export function hasVerifiedLicense(state: Pick<LicenseState, 'status'>): boolean {
  return state.status === 'valid' || state.status === 'grace' || state.status === 'readOnly';
}

/** Licensed stations as "used / limit", for example "2 / 3". */
export function stationsText(used: number, limit: number): string {
  return `${used} / ${limit}`;
}

export function getLicense(options?: RequestOptions): Promise<LicenseState> {
  return apiGet<LicenseState>('/api/license', options);
}

/** Uploads the text of a license file; the response is the new state. */
export function uploadLicense(fileText: string): Promise<LicenseState> {
  return apiPostText<LicenseState>('/api/license', fileText);
}

export function deleteLicense(): Promise<void> {
  return apiDelete<void>('/api/license');
}

export function saveStations(deviceIds: readonly string[]): Promise<LicenseState> {
  return apiPut<LicenseState>('/api/license/stations', { deviceIds });
}

/** True when a license is stored that works now (valid, or in its grace period): replacing it needs a confirmation. */
export function isWorkingLicense(status: LicenseStatus): boolean {
  return status === 'valid' || status === 'grace';
}

/**
 * Resource key of the warning to show after an upload whose result is not a plain valid license (a file that has
 * ended, is in its grace period, or starts later), or null for a valid one.
 */
export function uploadWarningKey(status: LicenseStatus): string | null {
  switch (status) {
    case 'readOnly':
      return 'license.UploadedReadOnly';
    case 'notYetValid':
      return 'license.UploadedNotYetValid';
    case 'grace':
      return 'license.UploadedGrace';
    default:
      return null;
  }
}
