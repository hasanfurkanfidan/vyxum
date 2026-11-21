// Client for the first-run setup API (/api/setup/*). Messages are resource keys with the `setup.` prefix.
import { apiGet, apiPost, type RequestOptions } from './api';

export type DatabaseProvider = 'Sqlite' | 'SqlServer';

export interface SetupStatus {
  completed: boolean;
  version: string;
  database: { provider: DatabaseProvider; isDefault: boolean };
  company: { name: string | null };
  dataDirectory: string | null;
  lastBackup: { path: string | null; at: string } | null;
  /** A database change was saved but the host has not restarted with it yet. */
  restartPending: boolean;
}

export interface DatabaseChoice {
  provider: DatabaseProvider;
  connectionString?: string;
}

export interface DatabaseTestResult {
  ok: boolean;
  /** Resource key such as `setup.DbConnectionOk`, or null. */
  code: string | null;
  databaseExists: boolean | null;
}

export interface DatabaseSelectionResult {
  restartRequired: boolean;
}

export function getSetupStatus(options?: RequestOptions): Promise<SetupStatus> {
  return apiGet<SetupStatus>('/api/setup/status', options);
}

export function testDatabase(choice: DatabaseChoice): Promise<DatabaseTestResult> {
  return apiPost<DatabaseTestResult>('/api/setup/database/test', choice);
}

export function selectDatabase(choice: DatabaseChoice): Promise<DatabaseSelectionResult> {
  return apiPost<DatabaseSelectionResult>('/api/setup/database', choice);
}

export function saveCompany(name: string): Promise<{ name: string }> {
  return apiPost<{ name: string }>('/api/setup/company', { name });
}

export function completeSetup(): Promise<{ completed: boolean; completedAt: string }> {
  return apiPost<{ completed: boolean; completedAt: string }>('/api/setup/complete');
}

/** Error code the setup API answers with when the request does not come from this computer. */
export const localOnlyCode = 'setup.LocalOnly';
