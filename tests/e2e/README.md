# End-to-end screen check

[Türkçe](README.tr.md)

One command starts the real Vyxum on an empty database, connects the real agent to a simulated scale, uses the screens
in a real browser as a person would, checks the results and takes a screenshot of every step. At the end it writes a
short pass/fail report with the screenshots.

The UI tests in `src/Web/vyxum-web` (Vitest) check each screen against a fake API and stay the fast safety net. This
check adds what they cannot see: the built UI served by the host, the database, the agent and the live weights, the
license files, and how the screens look.

## Run it

Needs the .NET 10 SDK and Node.js 22, nothing else. On Windows the check uses Microsoft Edge, which is always
installed. On Linux and macOS run `npx playwright install chromium` once in this folder.

```bash
node tests/e2e/run.mjs
```

It builds the web UI (only if it was never built; `--build-ui` rebuilds it), the test server and the agent, then starts:

- the test server on `http://127.0.0.1:5390` with an empty data folder (`tests/Vyxum.E2E.Host`);
- a simulated MT-SICS scale on TCP port 5391, inside the test server;
- the agent, connected to the test server.

The ports of an installed Vyxum (5000) are not used. If 5390 is taken, pass `--port 5490`.

| Option | |
|---|---|
| `-- scenarios/01 scenarios/05` | Only these files (after `--`, as Playwright file filters). Include `01`: later scenarios need the finished wizard. |
| `--grep text` | Only scenarios whose title matches; projects others depend on (`setup`, `core`) still run in full. |
| `--no-build` | Skip the builds when nothing changed since the last run. |
| `--headed` | Show the browser. |
| `--real-url http://localhost:5000` | Where the installed Vyxum with the real scale is (see below). `none` turns it off. |
| `-- <args>` | Everything after `--` goes to Playwright as it is. |

The exit code is 0 when every scenario passed.

## The report

The run prints where it wrote the report, `artifacts/e2e/<time>/` (the newest path is also in
`artifacts/e2e/latest.txt`):

| File | |
|---|---|
| `summary.md` | The short report, in Turkish: verdict, failures with their screenshots, what was checked with the simulated scale, what was checked with the real scale, and the checks only a person can do (`manual-checks.md`). |
| `shots/<project>/<scenario>/NN-name.png` | Every screenshot, numbered in step order. |
| `html/index.html` | Playwright's report: every step, screenshot and, for a failure, a trace (`npx playwright show-trace <trace.zip>`). |
| `logs/host.log`, `logs/agent.log` | Output of the test server and the agent. |
| `summary.json` | The same results for a program. |

## After a coding task

The session that finished a task runs the check and reports to the product owner:

1. `node tests/e2e/run.mjs` (with the installed Vyxum running, the real scale is checked too).
2. Read `summary.md`. A failure caused by the task is fixed before the task is reported as done.
3. Send the verdict line, the failures and the screenshots of the screens the task changed. Say which checks used the
   simulated scale and which used the real one, and list the manual checks that are still open.
4. When the task changed or added a screen, it also adds or updates its scenario (below).

## Real scale

When an installed Vyxum answers on `http://localhost:5000` (or at `--real-url`), the `real-device` project checks it
**read-only**: the device list says the scale is connected, new readings arrive, and the weighing screen shows the live
weight. It clicks nothing and changes nothing there. Whether the number is the right weight needs a person with a known
weight; that and other hands-on checks are listed in `manual-checks.md` and copied into every report.

## What the test server adds

`tests/Vyxum.E2E.Host` runs the unchanged host (`Program`, middleware, web UI) with three test-only additions. It is
never part of a release.

- It also trusts a license signing key generated at start-up. The key lives only in memory; the production keys stay
  trusted too.
- `GET /e2e/license?kind=valid|perpetual|not-yet|grace|read-only|tampered|not-a-license&modules=a,b&stations=2`
  returns a license file signed with that key.
- `POST /e2e/scale?value=1250&unit=g&state=stable|motion|overload|underload` changes what the simulated scale shows;
  `silent=true` makes it stop answering (a pulled cable). `GET /e2e/scale` returns the commands it received, so a
  scenario can prove that no tare or reset command reached the scale.
- `/e2e/scale2` does the same for a second simulated scale on the port after it (`VYXUM_E2E_SCALE2_PORT`). Setup does
  not add it; a scenario that needs two scales at a station adds it as a network scale.

## Add a scenario

Scenarios are in `scenarios/`. All of them share one server, in this order (`playwright.config.ts`):

1. `setup`: `01-setup.spec.ts`, the wizard on the empty database. It adds the scale, the company and two products.
2. `core`: `02-…` to `09-…` in file order, in Turkish. Later files may use what earlier ones made (the weighing
   scenario weighs the product the products scenario added); say so in the scenario's comment.
3. `looks-…`: `looks.spec.ts` visits the main screens in English and on a 1280×800 touch panel. Add a new screen to its
   list.
4. `real-device`: `real-scale.spec.ts`, only with an installed Vyxum.

Write a scenario like this:

```ts
import { expectCleanScreen, exact, test, expect } from '../support/vyxum';

test('Products: a product can be deactivated', async ({ page, t, shot, scale, licenses }) => {
  await page.goto('/products');
  await page.getByRole('button', { name: exact(t('products.Deactivate')) }).click();
  await expect(page.getByText(t('products.Inactive'))).toBeVisible();
  await expectCleanScreen(page);
  await shot('urun-pasif');
});
```

- `t('key', ...values)` returns the text the screen shows, read from the server's own resource strings in the
  project's language. Never write interface text into a scenario; an unknown key fails the test.
- `shot('name')` takes a screenshot for the report. Take one after every step a person would look at. Names are short
  Turkish slugs: the report is read in Turkish.
- `scale.set({ value, unit, state })`, `scale.silent(true)`, `scale.status()` control the simulated scale; `scale2`
  controls the second one.
- `licenses.get('valid', { modules: ['samples.paid'] })` returns a file for `setInputFiles`.
- `expectCleanScreen(page)` fails on raw resource keys (a missing translation) and on sideways scrolling.
- Any script error or console error fails the scenario. When a screen logs an error on purpose (the weighing screen
  logs a refused save), push a pattern into `allowedBrowserErrors` before that step.
- Prefer roles and labels (`getByRole`, `getByLabel`) over CSS classes; they are what a person sees and they survive
  restyling.
- A check that needs hands (a real weight, a cable, a printed label) goes into `manual-checks.md`, in Turkish.

## Paid modules

The open core uses the sample paid module (`tests/SampleModules/Vyxum.Samples.Paid`) for the locked preview, the license
states and the read-only mode. The paid modules repository checks its own screens with the same tools:

1. A test server project there, for example `tests/Vyxum.Pro.E2E.Host`, referencing
   `../vyxum/tests/Vyxum.E2E.Host/Vyxum.E2E.Host.csproj` and the module projects, with
   `Main(args) => E2EServer.RunAsync(args, ["Vyxum.Modules.Labels"])`. Module-specific simulators (a fake printer)
   can be added there as more `/e2e/...` endpoints.
2. Its own `playwright.config.ts` and scenarios, importing `support/vyxum.ts` from this folder and using the same
   projects (`setup` can be copied; `core` holds the module's scenarios; licenses come from `licenses.get` with the
   module's id).
3. `node ../vyxum/tests/e2e/run.mjs --host-project tests/Vyxum.Pro.E2E.Host/Vyxum.Pro.E2E.Host.csproj --config tests/e2e/playwright.config.ts`
