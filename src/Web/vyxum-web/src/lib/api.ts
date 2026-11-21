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

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE';

export interface RequestOptions {
  signal?: AbortSignal;
  headers?: Record<string, string>;
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

function toError(status: number, body: unknown): ApiError {
  if (isValidationProblem(body)) return new ValidationError(status, body);
  if (isCodedError(body)) return new RuleError(status, body);
  return new ApiError(`Request failed with status ${status}.`, status, body);
}

async function send(method: HttpMethod, path: string, body: unknown, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };
  const init: RequestInit = { method, headers, credentials: 'same-origin' };
  if (options.signal) init.signal = options.signal;
  if (body !== undefined) {
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
    throw toError(response.status, await readBody(response));
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

export function apiPut<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
  return request<T>('PUT', path, body, options);
}

export function apiDelete<T>(path: string, options?: RequestOptions): Promise<T> {
  return request<T>('DELETE', path, undefined, options);
}

/** GET that returns the raw body as a Blob, for file downloads such as the Excel export. */
export async function apiBlob(path: string, options: RequestOptions = {}): Promise<Blob> {
  const response = await send('GET', path, undefined, { ...options, headers: { Accept: '*/*', ...options.headers } });
  return response.blob();
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
}

export interface UiModules {
  modules: UiModule[];
  capabilities: string[];
}

export function getUiStrings(options?: RequestOptions): Promise<UiStrings> {
  return apiGet<UiStrings>('/api/ui/strings', options);
}

export function getUiModules(options?: RequestOptions): Promise<UiModules> {
  return apiGet<UiModules>('/api/ui/modules', options);
}
