// Shares the application's React, React Router and module SDK with separately built module screens.
// - Injects the import map (bare specifier -> /vyxum-shared/*.js) at the top of index.html.
// - Emits the shim files into the build and serves them from the dev server.
// The shims re-export the objects the application puts on a global (src/sdk/shared.ts), so a module never
// brings its own React. Export names of the npm packages are read from the installed packages at build time.
import type { Plugin } from 'vite';
import { SDK_EXPORTS, SHARED_MODULES, SHARED_PATH, importMap, shimSource } from './src/sdk/contract';

async function exportNames(packageName: string): Promise<string[]> {
  const namespace = (await import(/* @vite-ignore */ packageName)) as Record<string, unknown>;
  const names = new Set(Object.keys(namespace));
  // CommonJS packages (React) carry their exports on the default object.
  const fallback = namespace.default;
  if (fallback !== null && typeof fallback === 'object') for (const name of Object.keys(fallback)) names.add(name);
  return [...names];
}

async function shimFiles(): Promise<Map<string, string>> {
  const files = new Map<string, string>();
  for (const shared of SHARED_MODULES) {
    const names = shared.packageName ? await exportNames(shared.packageName) : [...SDK_EXPORTS];
    files.set(shared.file, shimSource(shared.key, names));
  }
  return files;
}

export function vyxumShared(): Plugin {
  let files: Promise<Map<string, string>> | undefined;
  const getFiles = () => (files ??= shimFiles());

  return {
    name: 'vyxum-shared',
    transformIndexHtml() {
      return [
        { tag: 'script', attrs: { type: 'importmap' }, children: JSON.stringify(importMap(), null, 2), injectTo: 'head-prepend' },
      ];
    },
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const url = (req as { url?: string }).url?.split('?')[0] ?? '';
        if (!url.startsWith(SHARED_PATH)) {
          next();
          return;
        }
        getFiles().then(
          (map) => {
            const source = map.get(url.slice(SHARED_PATH.length));
            if (source === undefined) {
              res.statusCode = 404;
              res.end();
              return;
            }
            res.setHeader('Content-Type', 'text/javascript; charset=utf-8');
            res.setHeader('Cache-Control', 'no-cache');
            res.end(source);
          },
          (error: unknown) => next(error),
        );
      });
    },
    async generateBundle() {
      for (const [file, source] of await getFiles()) {
        this.emitFile({ type: 'asset', fileName: SHARED_PATH.slice(1) + file, source });
      }
    },
  };
}
