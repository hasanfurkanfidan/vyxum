import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { vyxumShared } from './vite-plugin-vyxum-shared';

// The ASP.NET Core host serves the API and SignalR hubs during development.
const backend = 'http://localhost:5000';

export default defineConfig(({ mode }) => ({
  plugins: [react(), vyxumShared()],
  build: {
    // The host serves the built files as static content. The folder is not committed.
    outDir: '../../Host/Vyxum.Host/wwwroot',
    emptyOutDir: true,
    // One local bundle read from disk, never over the internet. It includes all of React Router because module
    // screens share it (src/sdk/shared.ts), which puts it just over Vite's default 500 kB warning.
    chunkSizeWarningLimit: 700,
  },
  server: {
    // Tests load the paid sample module's hand-written screen from the repository's sample modules.
    ...(mode === 'test' ? { fs: { allow: ['.', '../../../tests/SampleModules/Vyxum.Samples.Paid/ui'] } } : {}),
    proxy: {
      '/api': { target: backend },
      '/hubs': { target: backend, ws: true },
      // Embedded screens of modules (served by the host under the license rules).
      '^/modules/': { target: backend },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
    unstubGlobals: true,
    // Newer Node versions define their own localStorage global (undefined without a storage file),
    // which hides the jsdom one. Turn it off so tests see the browser storage of jsdom.
    execArgv: ['--no-experimental-webstorage'],
  },
}));
