import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// The ASP.NET Core host serves the API and SignalR hubs during development.
const backend = 'http://localhost:5000';

export default defineConfig({
  plugins: [react()],
  build: {
    // The host serves the built files as static content. The folder is not committed.
    outDir: '../../Host/Vyxum.Host/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': { target: backend },
      '/hubs': { target: backend, ws: true },
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
});
