import { describe, expect, it, vi } from 'vitest';
import { installFakeApi, jsonResponse } from '../test/fakeApi';
import { ApiError, NetworkError, RuleError, ValidationError, apiGet, apiPost, getUiModules, withQuery } from './api';

describe('api client', () => {
  it('returns the parsed JSON body', async () => {
    installFakeApi({ modules: ['weighing'], capabilities: ['weighing.records'] });
    await expect(getUiModules()).resolves.toEqual({
      modules: [{ id: 'weighing', version: '1.0.0', name: 'Tartım' }],
      capabilities: ['weighing.records'],
    });
  });

  it('sends JSON bodies', async () => {
    const fetchMock = installFakeApi({
      routes: { 'POST /api/things': async (_url, init) => jsonResponse({ echo: JSON.parse(String(init?.body)) }, 201) },
    });
    await expect(apiPost('/api/things', { name: 'x' })).resolves.toEqual({ echo: { name: 'x' } });
    const init = fetchMock.mock.calls[0]?.[1];
    expect(init?.headers).toMatchObject({ 'Content-Type': 'application/json' });
  });

  it('returns undefined for 204', async () => {
    installFakeApi({ routes: { 'POST /api/empty': () => new Response(null, { status: 204 }) } });
    await expect(apiPost('/api/empty')).resolves.toBeUndefined();
  });

  it('turns a ValidationProblem into a ValidationError', async () => {
    installFakeApi({
      routes: {
        'POST /api/products': () =>
          jsonResponse(
            {
              type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
              title: 'One or more validation errors occurred.',
              status: 400,
              errors: { Code: ['products.CodeRequired'], name: ['products.NameTooLong'] },
            },
            400,
          ),
      },
    });

    const error = await apiPost('/api/products', {}).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ValidationError);
    const validation = error as ValidationError;
    expect(validation.status).toBe(400);
    expect(validation.forField('code')).toEqual(['products.CodeRequired']);
    expect(validation.forField('Name')).toEqual(['products.NameTooLong']);
    expect(validation.forField('price')).toEqual([]);
  });

  it('turns a coded rule error into a RuleError', async () => {
    installFakeApi({
      routes: { 'POST /api/weighing/records': () => jsonResponse({ code: 'weighing.NotStable' }, 422) },
    });
    const error = await apiPost('/api/weighing/records', {}).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(RuleError);
    expect((error as RuleError).code).toBe('weighing.NotStable');
    expect((error as RuleError).status).toBe(422);
  });

  it('turns other failures into an ApiError with the status', async () => {
    installFakeApi();
    const error = await apiGet('/api/missing').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).not.toBeInstanceOf(ValidationError);
    expect((error as ApiError).status).toBe(404);
  });

  it('turns a network failure into a NetworkError', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))));
    const error = await apiGet('/api/ui/modules').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(NetworkError);
    expect((error as NetworkError).status).toBe(0);
  });

  it('builds query strings without empty values', () => {
    expect(withQuery('/api/products', { search: 'kaju', includeInactive: false, skip: 0, take: undefined, lot: '' })).toBe(
      '/api/products?search=kaju&includeInactive=false&skip=0',
    );
    expect(withQuery('/api/products', {})).toBe('/api/products');
  });
});
