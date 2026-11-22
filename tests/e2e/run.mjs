#!/usr/bin/env node
// Runs the end-to-end screen check in one command:
//   builds the web UI (if needed), the test server and the agent; starts the server on an empty database with a
//   simulated scale, starts the agent, drives the screens in a real browser, writes the report and stops everything.
//
//   node tests/e2e/run.mjs                     the whole check
//   node tests/e2e/run.mjs --grep license      only scenarios whose title matches
//   node tests/e2e/run.mjs --real-url http://localhost:5000
//                                              check the real scale of the installed Vyxum (read-only). Without the
//                                              option an installed Vyxum on localhost:5000 is used when it answers;
//                                              --real-url none turns this off.
//   node tests/e2e/run.mjs --no-build          skip the builds (after an unchanged earlier run)
//   node tests/e2e/run.mjs --headed            show the browser
//   Other options: --port 5390, --build-ui (rebuild the web UI), --host-project <csproj>, --config <file>, --out <dir>.
//   Anything after "--" goes to Playwright unchanged.
//
// The report is written to artifacts/e2e/<time>/summary.md (and html/index.html); the path is printed at the end.
import { spawn, spawnSync } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync, writeFileSync } from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
const isWindows = process.platform === 'win32';

const { options, passthrough } = parseArguments(process.argv.slice(2));
const port = Number(options.port ?? 5390);
const scalePort = port + 1;
const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 15);
const out = path.resolve(options.out ?? path.join(repo, 'artifacts', 'e2e', stamp));
const hostProject = path.resolve(options['host-project'] ?? path.join(repo, 'tests', 'Vyxum.E2E.Host', 'Vyxum.E2E.Host.csproj'));
const agentProject = path.join(repo, 'src', 'Agent', 'Vyxum.Agent', 'Vyxum.Agent.csproj');
const webDir = path.join(repo, 'src', 'Web', 'vyxum-web');
const config = path.resolve(options.config ?? path.join(here, 'playwright.config.ts'));
const url = `http://127.0.0.1:${port}`;

mkdirSync(path.join(out, 'logs'), { recursive: true });
const children = [];
let stopping = false;
process.on('SIGINT', () => void stopAll().then(() => process.exit(130)));

try {
  if (!options['no-build']) build();
  ensureNodeModules(here);
  await ensurePortFree(port);
  await ensurePortFree(scalePort);

  step(`Starting the test server on ${url} (empty database in ${path.join(out, 'data')})`);
  const host = start('host', 'dotnet', [outputDll(hostProject), '--port', String(port), '--data', path.join(out, 'data')], { stdin: 'pipe' });
  await waitForHealth(host);

  step('Starting the agent');
  start('agent', 'dotnet', [outputDll(agentProject), '--Vyxum:Agent:ServerUrl', url, '--Vyxum:Agent:AgentName', 'e2e'], {
    env: { DOTNET_ENVIRONMENT: 'Production' },
  });

  const realUrl = await findRealVyxum();
  step(realUrl ? `Real scale: the installed Vyxum at ${realUrl} (read-only)` : 'Real scale: no installed Vyxum found, skipped');

  step('Running the scenarios');
  const playwrightArgs = ['test', '--config', config, ...passthrough];
  if (options.grep) playwrightArgs.push('--grep', options.grep);
  if (options.project) playwrightArgs.push('--project', options.project);
  if (options.headed) playwrightArgs.push('--headed');
  // Playwright's own entry point, run by this Node: no shell, so arguments with spaces stay whole on Windows too.
  const result = spawnSync(process.execPath, [path.join(here, 'node_modules', '@playwright', 'test', 'cli.js'), ...playwrightArgs], {
    cwd: here,
    stdio: 'inherit',
    env: {
      ...process.env,
      VYXUM_E2E_URL: url,
      VYXUM_E2E_SCALE_PORT: String(scalePort),
      VYXUM_E2E_OUT: out,
      VYXUM_E2E_SHOTS: path.join(out, 'shots'),
      ...(realUrl ? { VYXUM_E2E_REAL_URL: realUrl } : {}),
    },
  });
  await stopAll();
  mkdirSync(path.join(repo, 'artifacts', 'e2e'), { recursive: true });
  writeFileSync(path.join(repo, 'artifacts', 'e2e', 'latest.txt'), out + '\n');
  console.log(`\nReport: ${path.join(out, 'summary.md')}\nDetails: ${path.join(out, 'html', 'index.html')}\nLogs: ${path.join(out, 'logs')}`);
  process.exit(result.status ?? 1);
} catch (error) {
  console.error(`\nThe check could not run: ${error instanceof Error ? error.message : error}`);
  console.error(`Logs: ${path.join(out, 'logs')}`);
  await stopAll();
  process.exit(2);
}

function parseArguments(args) {
  const options = {};
  const separator = args.indexOf('--');
  const own = separator === -1 ? args : args.slice(0, separator);
  const flags = new Set(['no-build', 'build-ui', 'headed']);
  for (let i = 0; i < own.length; i++) {
    const name = own[i].replace(/^--/, '');
    if (!own[i].startsWith('--')) throw new Error(`Unexpected argument '${own[i]}'.`);
    options[name] = flags.has(name) ? true : own[++i];
  }
  return { options, passthrough: separator === -1 ? [] : args.slice(separator + 1) };
}

function step(text) {
  console.log(`\n▶ ${text}`);
}

function run(command, args, cwd) {
  // npm is a .cmd script on Windows, which only starts through the shell; nothing passed to it contains spaces.
  const result = spawnSync(command, args, { cwd, stdio: 'inherit', shell: isWindows && command === 'npm' });
  if (result.status !== 0) throw new Error(`'${command} ${args.join(' ')}' failed in ${cwd}.`);
}

function build() {
  if (options['build-ui'] || !existsSync(path.join(repo, 'src', 'Host', 'Vyxum.Host', 'wwwroot', 'index.html'))) {
    step('Building the web UI');
    ensureNodeModules(webDir);
    run('npm', ['run', 'build'], webDir);
  }
  step('Building the test server and the agent');
  for (const project of [hostProject, agentProject]) {
    run('dotnet', ['build', project, '-p:SkipUiBuild=true', '-nologo', '-v', 'q', '-clp:ErrorsOnly'], repo);
  }
}

function ensureNodeModules(directory) {
  if (!existsSync(path.join(directory, 'node_modules'))) {
    step(`Installing packages in ${path.relative(repo, directory) || '.'}`);
    run('npm', ['ci', '--no-audit', '--no-fund'], directory);
  }
}

function outputDll(project) {
  const name = path.basename(project, '.csproj');
  const dll = path.join(path.dirname(project), 'bin', 'Debug', 'net10.0', `${name}.dll`);
  if (!existsSync(dll)) throw new Error(`${dll} does not exist. Run without --no-build.`);
  return dll;
}

function start(name, command, args, { stdin = 'ignore', env = {} } = {}) {
  const log = createWriteStream(path.join(out, 'logs', `${name}.log`));
  const child = spawn(command, args, { cwd: repo, stdio: [stdin, 'pipe', 'pipe'], env: { ...process.env, ...env } });
  child.stdout.pipe(log);
  child.stderr.pipe(log);
  child.on('exit', (code) => {
    if (!stopping) console.error(`The ${name} stopped unexpectedly (exit code ${code}). See logs/${name}.log.`);
  });
  children.push({ name, child });
  return child;
}

async function stopAll() {
  stopping = true;
  for (const { child } of children.reverse()) {
    if (child.exitCode !== null) continue;
    child.stdin?.end(); // the test server stops when its input closes
    const exited = new Promise((resolve) => child.once('exit', resolve));
    const timer = setTimeout(() => child.kill(), 5000);
    if (!child.stdin) child.kill();
    await exited;
    clearTimeout(timer);
  }
  children.length = 0;
}

async function waitForHealth(host) {
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    if (host.exitCode !== null) throw new Error('The test server stopped while starting. See logs/host.log.');
    try {
      const response = await fetch(`${url}/health`);
      if (response.ok) return;
    } catch {
      // not listening yet
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error('The test server did not answer /health within 90 seconds. See logs/host.log.');
}

/** The installed Vyxum whose real scale may be looked at: --real-url, or localhost:5000 when it answers. */
async function findRealVyxum() {
  const given = options['real-url'];
  if (given === 'none') return null;
  const candidate = (given ?? 'http://localhost:5000').replace(/\/$/, '');
  try {
    const response = await fetch(`${candidate}/health`, { signal: AbortSignal.timeout(2000) });
    if (response.ok) return candidate;
  } catch {
    // nothing answers there
  }
  if (given) throw new Error(`No Vyxum answers at ${given}/health.`);
  return null;
}

function ensurePortFree(portNumber) {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once('error', () =>
      reject(new Error(`Port ${portNumber} is in use. Stop the program using it or pass --port with another number.`)),
    );
    server.listen(portNumber, '127.0.0.1', () => server.close(() => resolve()));
  });
}
