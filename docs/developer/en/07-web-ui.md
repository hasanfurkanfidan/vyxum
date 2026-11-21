# 7. Web UI

[Index](../README.md) · Previous: [6. Devices and the Agent](06-devices-and-agent.md) · Next: [8. Installer and releases](08-release.md)

## Basics

The screens are a **React 19 + TypeScript + Vite** application in `src/Web/vyxum-web`. It is an ordinary single-page app: `npm run build` produces static files in `src/Host/Vyxum.Host/wwwroot` and the ASP.NET Core host serves them. The customer never installs Node.js. The folder `wwwroot` is not committed.

The host sends unknown paths that are not `/api`, `/hubs` or `/health` to `index.html`, so client-side routes (for example `/records`) survive a browser refresh.

Commands (run in `src/Web/vyxum-web`, Node.js 22):

| Command | What it does |
|---|---|
| `npm ci` | Install exact package versions from `package-lock.json`. |
| `npm run dev` | Vite dev server with live reload (usually http://localhost:5173), proxying `/api` and `/hubs` to the host at `http://localhost:5000`. |
| `npm run typecheck` | TypeScript check. |
| `npm test` | Vitest (jsdom) with Testing Library. |
| `npm run build` | Type check, then build into the host's `wwwroot`. |

CI runs `typecheck`, `test` and `build`.

## Folder guide (`src/`)

| Folder | Contents |
|---|---|
| `main.tsx`, `App.tsx` | Start-up, providers, routes. |
| `shell/` | The frame around screens: sidebar (`Sidebar.tsx`), menu definition (`navigation.ts`), setup gate, modules provider, language switch. |
| `pages/` | One file per screen: weighing, records, products; `settings/` (devices, about); `setup/` (first-run wizard). |
| `components/devices/` | Parts shared by the wizard and the Settings → Devices screen. |
| `lib/` | Plain TypeScript: API calls (`api.ts`, `weighing.ts`, `products.ts`, `devices.ts`, `setup.ts`), number/date formatting, decimal input. No React here. |
| `i18n/` | Loads texts and gives screens a `t(key)` function. |
| `styles/` | CSS files per area, with design tokens in `tokens.css`. |
| `test/` | Test helpers: a fake API, a fake `EventSource`, the hard-coded-text checker. |

## No text in code

Every sentence the user sees comes from the server. Each module (and the host, and the platform's setup/device parts) has `Strings.resx` (English) and `Strings.tr.resx` (Turkish). The platform merges them and serves them at `GET /api/ui/strings` as `Prefix.Key` (for example `products.Title`; the host's own prefix is `shell`). In a component you write `t('products.Title')`, never the words. The UI is Turkish by default, and the user switches language from the menu; the choice is remembered in a cookie.

Two tests protect this: `noHardcodedText.test.ts` fails if a `.tsx` file contains literal text between tags, and `ResourceFileTests` (architecture tests) fails if a Turkish file lacks a key or has an empty value. A screen with text in only one language is **not finished**.

To add a text: add the key to `Strings.resx` **and** `Strings.tr.resx` of the owning project, rebuild the host, use `t('<prefix>.<Key>')`. In development the console warns about a missing key.

## Offline

Fonts (Instrument Sans, JetBrains Mono) come from `@fontsource` packages and are bundled in the build. Do not add CDN links, remote fonts or analytics. Check the license of any new npm package (MIT, BSD, Apache-2.0 compatible only).

## Which menu entries appear

`shell/navigation.ts` defines the menu. An entry that names a `module` (for example `weighing`) is shown only if that module is loaded; the list comes from `GET /api/ui/modules`. Platform screens (settings) always appear. Features of optional modules are tied to capabilities, e.g. the operator column only when `identity.users` exists.

**Not built yet:** a way for a separately developed module to bring its own screens into this app. `navGroups` has an empty `modules` group reserved for it, and the architecture decision for it belongs to a later phase. Before building a module UI, decide how its JavaScript reaches `wwwroot` and how routes are registered, and document the decision in [Chapter 10](10-decisions.md).

## Add a screen (for a built-in module)

1. Add the API calls in `lib/<name>.ts` and a page in `pages/`.
2. Add a route in `App.tsx` and a path in `routes` in `shell/navigation.ts`, and a menu item with its `module`.
3. Add texts in both `.resx` files.
4. Write a test next to the page (see `ProductsPage.test.tsx`; `test/fakeApi.ts` fakes the server).
5. Run `npm run typecheck && npm test && npm run build`.

## Live weight

The weighing screen follows a device with `EventSource` (Server-Sent Events) on `/api/devices/{id}/live` (`components/devices/useLiveDevice.ts`). The browser reconnects by itself. The server sends the current state at once and then every change.
