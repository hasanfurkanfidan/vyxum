// Fills the shared global that the shim files (import map targets) read, so that module screens use the
// application's own React, React Router and SDK. main.tsx imports this first; modules are imported only later,
// after /api/ui/modules answered, so the global is always set before any module code runs.
import * as React from 'react';
import * as ReactDOM from 'react-dom';
import * as JsxRuntime from 'react/jsx-runtime';
import * as ReactRouterDom from 'react-router-dom';
import { SHARED_GLOBAL } from './contract';
import { sdk } from './index';

export type SharedModules = Record<string, unknown>;

export function sharedModules(): SharedModules {
  return {
    react: React,
    'react/jsx-runtime': JsxRuntime,
    'react-dom': ReactDOM,
    'react-router-dom': ReactRouterDom,
    sdk,
  };
}

export function installShared(): void {
  (globalThis as Record<string, unknown>)[SHARED_GLOBAL] = sharedModules();
}
