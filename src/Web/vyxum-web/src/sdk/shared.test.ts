import * as React from 'react';
import * as ReactDOMClient from 'react-dom/client';
import * as JsxRuntime from 'react/jsx-runtime';
import * as ReactRouterDom from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { SDK_EXPORTS, SDK_VERSION, SHARED_GLOBAL, SHARED_MODULES, importMap, shimSource } from './contract';
import { sdk } from './index';
import { installShared } from './shared';

function dataUrl(source: string): string {
  return 'data:text/javascript;charset=utf-8,' + encodeURIComponent(source);
}

async function importShim(key: string, names: readonly string[]): Promise<Record<string, unknown>> {
  return (await import(/* @vite-ignore */ dataUrl(shimSource(key, names)))) as Record<string, unknown>;
}

describe('shared modules for module screens', () => {
  it('maps every shared specifier to a shim file', () => {
    expect(importMap().imports).toEqual({
      react: '/vyxum-shared/react.js',
      'react/jsx-runtime': '/vyxum-shared/react-jsx-runtime.js',
      'react/jsx-dev-runtime': '/vyxum-shared/react-jsx-dev-runtime.js',
      'react-dom': '/vyxum-shared/react-dom.js',
      'react-dom/client': '/vyxum-shared/react-dom-client.js',
      'react-router-dom': '/vyxum-shared/react-router-dom.js',
      'react-router': '/vyxum-shared/react-router-dom.js',
      '@vyxum/sdk': '/vyxum-shared/sdk.js',
    });
  });

  it('has an SDK object with exactly the documented names', () => {
    expect(Object.keys(sdk).sort()).toEqual([...SDK_EXPORTS].sort());
  });

  it('puts every shared module on the global', () => {
    installShared();
    const shared = (globalThis as Record<string, unknown>)[SHARED_GLOBAL] as Record<string, unknown>;
    expect(Object.keys(shared).sort()).toEqual(SHARED_MODULES.map((m) => m.key).sort());
  });

  it('gives a module the same React, React Router and SDK instances as the application', async () => {
    installShared();

    const react = await importShim('react', Object.keys(React));
    expect(react.useState).toBe(React.useState);
    expect(react.createElement).toBe(React.createElement);
    expect((react.default as typeof React).useEffect).toBe(React.useEffect);

    const dev = await importShim('react/jsx-dev-runtime', SHARED_MODULES.find((m) => m.key === 'react/jsx-dev-runtime')!.names!);
    expect(dev.jsxDEV).toBe(JsxRuntime.jsx);
    expect(dev.jsxs).toBe(JsxRuntime.jsxs);
    expect(dev.Fragment).toBe(JsxRuntime.Fragment);

    const client = await importShim('react-dom/client', Object.keys(ReactDOMClient));
    expect(client.createRoot).toBe(ReactDOMClient.createRoot);
    expect(client.hydrateRoot).toBe(ReactDOMClient.hydrateRoot);

    const router = await importShim('react-router-dom', Object.keys(ReactRouterDom));
    expect(router.useNavigate).toBe(ReactRouterDom.useNavigate);

    const sdkShim = await importShim('sdk', SDK_EXPORTS);
    expect(sdkShim.useT).toBe(sdk.useT);
    expect(sdkShim.sdkVersion).toBe(SDK_VERSION);
  });

  it('fails clearly when the application has not set the global', async () => {
    const source = shimSource('react', ['useState']).replace(SHARED_GLOBAL, '__missing');
    await expect(import(/* @vite-ignore */ dataUrl(source))).rejects.toThrow(/not available/);
  });
});
