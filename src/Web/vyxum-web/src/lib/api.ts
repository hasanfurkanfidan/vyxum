// Small typed client for the Vyxum HTTP API.
// Every helper throws an ApiError (or a subclass) for non-success responses,
// so screens can branch on the error type instead of parsing bodies.

/** Any non-success response, or a failure to reach the server (status 0). */
export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(message: string, status: number, body: unknown = undefined) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

/** The server could not be reached (offline, host stopped, request aborted by the network). */
export class NetworkError extends ApiError {
  constructor(cause: unknown) {
    super('The server could not be reached.', 0, undefined);
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

/**
 * 400 ValidationProblem. Field keys and messages are resource keys
 * (for example `products.CodeRequired`); the screen translates them with `t`.
 */
export class ValidationError extends ApiError {
  readonly title: string | undefined;
  readonly errors: Readonly<Record<string, readonly string[]>>;

  constructor(status: number, body: ValidationProblem) {
    super(body.title ?? 'Validation failed.', status, body);
    this.name = 'ValidationError';
    this.title = body.title;
    this.errors = body.errors;
  }

  /** Error keys for one field. The lookup ignores the case of the first letter. */
  forField(field: string): readonly string[] {
    const direct = this.errors[field];
    if (direct) return direct;
    const match = Object.keys(this.errors).find((k) => k.toLowerCase() === field.toLowerCase());
    return match ? (this.errors[match] ?? []) : [];
  }
}

/** A business rule rejected the request (for example 422 `{ code: "weighing.NotStable" }`). */
export class RuleError extends ApiError {
  readonly code: string;

  constructor(status: number, body: CodedError) {
    super(`Request rejected: ${body.code}`, status, body);
    this.name = 'RuleError';
    this.code = body.code;
  }
}

/** Codes of a 403 that means the license does not allow the request (not yet valid, locked, or read-only). */
export const licenseDeniedCodes = ['license_required', 'license_read_only'] as const;

/**
 * 403 `license_required` or `license_read_only`: the license state changed under the screen (the grace period ended,
 * or another station changed the license). The client reloads the module list quietly; the screen shows `messageKey`.
 */
export class LicenseError extends RuleError {
  /** Resource key of the translated message. */
  readonly messageKey: string;

  constructor(status: number, body: CodedError) {
    super(status, body);
    this.name = 'LicenseError';
    this.messageKey = `license.Error.${body.code}`;
  }
}

/** Resource key to show for a rejected request: the license message for a LicenseError, otherwise the error code. */
export function ruleErrorKey(error: RuleError): string {
  return error instanceof LicenseError ? error.messageKey : error.code;
}

type LicenseDeniedListener = (error: LicenseError) => void;
const licenseDeniedListeners = new Set<LicenseDeniedListener>();

/** Calls `listener` for every response that says the license denies the request; returns the way to stop. */
export function onLicenseDenied(listener: LicenseDeniedListener): () => void {
  licenseDeniedListeners.add(listener);
  return () => licenseDeniedListeners.delete(listener);
}

export interface ValidationProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors: Record<string, string[]>;
}

export interface CodedError {
  code: string;
}

export function isValidationProblem(body: unknown): body is ValidationProblem {
  if (typeof body !== 'object' || body === null) return false;
  const errors = (body as { errors?: unknown }).errors;
  return typeof errors === 'object' && errors !== null && !Array.isArray(errors);
}

export function isCodedError(body: unknown): body is CodedError {
  return typeof body === 'object' && body !== null && typeof (body as { code?: unknown }).code === 'string';
}

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH';

/** Header every state-changing request carries; the server rejects such requests without it (CSRF protection). */
export const REQUEST_HEADER = 'X-Vyxum-Request';

export interface RequestOptions {
  signal?: AbortSignal;
  headers?: Record<string, string>;
  /** Sends this text as the body as it is (UTF-8 text), instead of a JSON-encoded body. */
  rawBody?: string;
}

async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204) return undefined;
  const text = await response.text();
  if (text.length === 0) return undefined;
  const type = response.headers.get('content-type') ?? '';
  if (type.includes('json')) {
    try {
      return JSON.parse(text) as unknown;
    } catch {
      return text;
    }
  }
  return text;
}

function isLicenseDenied(status: number, body: unknown): body is CodedError {
  return status === 403 && isCodedError(body) && (licenseDeniedCodes as readonly string[]).includes(body.code);
}

function toError(status: number, body: unknown): ApiError {
  if (isLicenseDenied(status, body)) return new LicenseError(status, body);
  if (isValidationProblem(body)) return new ValidationError(status, body);
  if (isCodedError(body)) return new RuleError(status, body);
  return new ApiError(`Request failed with status ${status}.`, status, body);
}

async function send(method: HttpMethod, path: string, body: unknown, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };
  if (method !== 'GET') headers[REQUEST_HEADER] = '1';
  const init: RequestInit = { method, headers, credentials: 'same-origin' };
  if (options.signal) init.signal = options.signal;
  if (options.rawBody !== undefined) {
    headers['Content-Type'] = 'text/plain; charset=utf-8';
    init.body = options.rawBody;
  } else if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(body);
  }

  let response: Response;
  try {
    response = await fetch(path, init);
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new NetworkError(error);
  }

  if (!response.ok) {
    const error = toError(response.status, await readBody(response));
    if (error instanceof LicenseError) for (const listener of licenseDeniedListeners) listener(error);
    throw error;
  }
  return response;
}

/** Sends a request and returns the parsed JSON body (undefined for 204 or an empty body). */
export async function request<T>(method: HttpMethod, path: string, body?: unknown, options: RequestOptions = {}): Promise<T> {
  const response = await send(method, path, body, options);
  return (await readBody(response)) as T;
}

export function apiGet<T>(path: string, options?: RequestOptions): Promise<T> {
  return request<T>('GET', path, undefined, options);
}

export function apiPost<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
  return request<T>('POST', path, body, options);
}

/** POST with a text body that is sent unchanged, for example the text of an uploaded file. */
export function apiPostText<T>(path: string, text: string, options: RequestOptions = {}): Promise<T> {
  return request<T>('POST', path, undefined, { ...options, rawBody: text });
}

export function apiPut<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
  return request<T>('PUT', path, body, options);
}

export function apiDelete<T>(path: string, options?: RequestOptions): Promise<T> {
  return request<T>('DELETE', path, undefined, options);
}

/** GET that returns the raw body text, for JSON the caller parses itself (for example to keep decimals). */
export async function apiText(path: string, options: RequestOptions = {}): Promise<string> {
  const response = await send('GET', path, undefined, options);
  return response.text();
}

/** GET that returns the raw body as a Blob, for file downloads such as the Excel export. */
export async function apiBlob(path: string, options: RequestOptions = {}): Promise<Blob> {
  return (await apiFile(path, options)).blob;
}

/** File name from a Content-Disposition header (`filename*=UTF-8''...` wins over `filename="..."`), or undefined. */
export function fileNameFromDisposition(header: string | null): string | undefined {
  if (!header) return undefined;
  const encoded = /filename\*\s*=\s*(?:[\w-]+)?'[^']*'([^;]+)/i.exec(header);
  if (encoded?.[1]) {
    try {
      return decodeURIComponent(encoded[1].trim());
    } catch {
      // Fall through to the plain form.
    }
  }
  const plain = /filename\s*=\s*(?:"([^"]*)"|([^;]+))/i.exec(header);
  const name = (plain?.[1] ?? plain?.[2])?.trim();
  return name ? name : undefined;
}

export interface DownloadedFile {
  blob: Blob;
  /** Name from Content-Disposition, if the server sent one. */
  fileName: string | undefined;
}

/** GET that returns a file with the name the server suggested. */
export async function apiFile(path: string, options: RequestOptions = {}): Promise<DownloadedFile> {
  const response = await send('GET', path, undefined, { ...options, headers: { Accept: '*/*', ...options.headers } });
  return { blob: await response.blob(), fileName: fileNameFromDisposition(response.headers.get('content-disposition')) };
}

/** Builds `path?a=1&b=2`, skipping undefined, null and empty values. */
export function withQuery(path: string, query: Record<string, string | number | boolean | null | undefined>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue;
    params.append(key, String(value));
  }
  const text = params.toString();
  return text.length > 0 ? `${path}?${text}` : path;
}

// Platform UI endpoints

export interface UiStrings {
  culture: string;
  strings: Record<string, string>;
}

export interface UiModule {
  id: string;
  version: string;
  /** Whether the module works only with a license. Older hosts leave it out. */
  requiresLicense?: boolean;
  /** Display name in the current language; null or absent when the module declares none (show the id). */
  name?: string | null;
  /** Capability ids the module provides, needs, and uses when available. Older hosts leave them out. */
  provides?: string[];
  requires?: string[];
  optional?: string[];
  /** `free`, `licensed`, `grace`, `readOnly` or `locked`. Older hosts leave it out (free). */
  license?: 'free' | 'licensed' | 'grace' | 'readOnly' | 'locked';
  /** The module's screens, when it has some and may load them (not locked); otherwise null or absent. */
  ui?: UiModuleScreens | null;
  /** Static sample of a paid module's screen, offered locked or not; absent when the module declares none. */
  preview?: UiModulePreview | null;
}

export interface UiModulePreview {
  /** Two short lines (separated by a line break) saying what the module does, in the current language. */
  description: string;
  /** URL of the sample image, served without a license. */
  imageUrl: string;
}

/** Where to send a trial license request; both parts are null when the installation has no contact configured. */
export interface UiContact {
  email: string | null;
  url: string | null;
}

export interface UiModuleScreens {
  /** URL of the entry ES module, e.g. `/modules/samples.paid/index.js`. */
  entry: string;
  /** SDK version the screens were built against. */
  sdkVersion: number;
}

/** Overall license state in the module list. */
export interface UiLicense {
  /** `none`, `invalid`, `notYetValid`, `valid`, `grace` or `readOnly`. */
  status: 'none' | 'invalid' | 'notYetValid' | 'valid' | 'grace' | 'readOnly';
  /** Name of the licensee of a license that verifies, or null. */
  licensee: string | null;
  /** In the grace period: days left, counting today. */
  graceDaysLeft: number | null;
}

export interface UiModules {
  modules: UiModule[];
  capabilities: string[];
  /** Older hosts leave it out (no license). */
  license?: UiLicense;
  /** Company name entered in the setup wizard, or null. Older hosts leave it out. */
  companyName?: string | null;
  /** Contact for license requests (configuration `Vyxum:Contact`). Older hosts leave it out. */
  contact?: UiContact;
}

export function getUiStrings(options?: RequestOptions): Promise<UiStrings> {
  return apiGet<UiStrings>('/api/ui/strings', options);
}

export function getUiModules(options?: RequestOptions): Promise<UiModules> {
  return apiGet<UiModules>('/api/ui/modules', options);
}
