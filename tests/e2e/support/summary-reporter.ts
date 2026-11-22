import type { FullResult, Reporter, Suite, TestCase, TestResult } from '@playwright/test/reporter';
import { copyFileSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';

interface Row {
  project: string;
  file: string;
  title: string;
  status: TestResult['status'] | 'not run';
  durationMs: number;
  shots: string[];
  /** Notes a scenario adds with test.info().annotations, such as the weight the real scale showed. */
  notes: string[];
  error?: string;
}

const statusText: Record<Row['status'], string> = {
  passed: '✅ geçti',
  failed: '❌ kaldı',
  timedOut: '❌ zaman aşımı',
  interrupted: '⚠️ yarıda kaldı',
  skipped: '⏭️ atlandı',
  'not run': '⏭️ çalışmadı',
};

/**
 * Writes the short report a person reads after a run: summary.md (Turkish, for the product owner) and summary.json
 * (for the session that ran the check). Screenshot paths are relative to the report folder.
 */
export default class SummaryReporter implements Reporter {
  private readonly outputFolder: string;
  private readonly rows = new Map<TestCase, Row>();
  private started = Date.now();

  constructor(options: { outputFolder?: string } = {}) {
    this.outputFolder = path.resolve(options.outputFolder ?? 'results');
  }

  onBegin(_config: unknown, suite: Suite): void {
    this.started = Date.now();
    for (const test of suite.allTests()) {
      this.rows.set(test, {
        project: test.parent.project()?.name ?? '',
        file: path.basename(test.location.file),
        title: test.titlePath().slice(3).join(' › '),
        status: 'not run',
        durationMs: 0,
        shots: [],
        notes: [],
      });
    }
  }

  onTestEnd(test: TestCase, result: TestResult): void {
    const row = this.rows.get(test);
    if (!row) return;
    row.status = result.status;
    row.notes = test.annotations.filter((a) => a.type !== 'skip' && a.description).map((a) => `${a.type}: ${a.description}`);
    row.durationMs = result.duration;
    // Screenshots get readable names: shots/<project>/<scenario>/NN-name.png (a failure screenshot comes last).
    const folder = path.join(this.outputFolder, 'shots', slug(row.project), slug(test.title));
    row.shots = result.attachments
      .filter((a) => a.contentType === 'image/png' && (a.path || a.body))
      .map((a, index) => {
        mkdirSync(folder, { recursive: true });
        const target = path.join(folder, `${String(index + 1).padStart(2, '0')}-${slug(a.name)}.png`);
        if (a.body) writeFileSync(target, a.body);
        else copyFileSync(a.path!, target);
        return path.relative(this.outputFolder, target).split(path.sep).join('/');
      });
    const error = result.errors[0];
    if (error) row.error = stripAnsi(error.message ?? error.value ?? '').split('\n').slice(0, 14).join('\n');
    if (result.status === 'skipped') {
      const reason = test.annotations.find((a) => a.type === 'skip')?.description;
      if (reason) row.error = reason;
    }
  }

  onEnd(result: FullResult): void {
    mkdirSync(this.outputFolder, { recursive: true });
    const rows = [...this.rows.values()];
    const failed = rows.filter((r) => r.status === 'failed' || r.status === 'timedOut' || r.status === 'interrupted');
    const passed = rows.filter((r) => r.status === 'passed');
    const real = rows.filter((r) => r.project === 'real-device');
    const fake = rows.filter((r) => r.project !== 'real-device');
    const minutes = ((Date.now() - this.started) / 60000).toFixed(1);
    const verdict = result.status === 'passed' && failed.length === 0 ? 'GEÇTİ' : 'KALDI';

    const lines: string[] = [];
    lines.push(`# Ekran denetimi: ${verdict}`, '');
    lines.push(`${new Date().toLocaleString('tr-TR')} · ${passed.length} geçti, ${failed.length} kaldı, ${rows.length - passed.length - failed.length} çalışmadı veya atlandı · ${minutes} dk`, '');
    if (failed.length > 0) {
      lines.push('## Kalanlar', '');
      for (const row of failed) {
        lines.push(`### ${row.title} (${row.project})`, '', '```', row.error ?? '', '```', '');
        for (const shot of row.shots.slice(-2)) lines.push(`![${row.title}](${shot})`, '');
      }
    }
    lines.push('## Sahte terazi ve deneme lisanslarıyla yapılanlar', '');
    lines.push(...table(fake), '');
    lines.push('## Gerçek cihazla yapılanlar', '');
    lines.push(
      real.length === 0 || real.every((r) => r.status === 'skipped' || r.status === 'not run')
        ? `Çalıştırılmadı.${real[0]?.error ? ` ${real[0].error}` : ''}`
        : table(real).join('\n'),
      '',
    );
    lines.push('## Sana kalan elle denemeler', '');
    lines.push(...manualChecks(), '');
    lines.push('Ayrıntılı rapor (her adımın görüntüsü, hata izi): `html/index.html`.', '');
    writeFileSync(path.join(this.outputFolder, 'summary.md'), lines.join('\n'), 'utf8');
    writeFileSync(
      path.join(this.outputFolder, 'summary.json'),
      JSON.stringify({ verdict, passed: passed.length, failed: failed.length, total: rows.length, minutes: Number(minutes), tests: rows }, null, 2),
      'utf8',
    );
  }

  printsToStdio(): boolean {
    return false;
  }
}

function table(rows: Row[]): string[] {
  const result = ['| Sonuç | Senaryo | Bölüm | Görüntü |', '|---|---|---|---|'];
  for (const row of rows) {
    const shots = row.shots.length === 0 ? '' : `[${row.shots.length} görüntü](${row.shots[0]})`;
    const notes = [...row.notes, ...(row.status === 'skipped' && row.error ? [row.error] : [])];
    const title = notes.length === 0 ? row.title : `${row.title}<br>${notes.join('<br>')}`;
    result.push(`| ${statusText[row.status]} | ${escape(title)} | ${row.project} | ${shots} |`);
  }
  return result;
}

/** The checks only a person can do, listed in manual-checks.md next to the scenarios. */
function manualChecks(): string[] {
  try {
    return readFileSync(path.resolve('manual-checks.md'), 'utf8').split('\n').filter((l) => l.startsWith('- '));
  } catch {
    return ['- (manual-checks.md bulunamadı)'];
  }
}

function slug(text: string): string {
  return text
    .toLowerCase()
    .replaceAll('ı', 'i')
    .normalize('NFKD')
    .replace(/[^\w\s-]/g, '')
    .trim()
    .replace(/[\s_]+/g, '-')
    .slice(0, 80);
}

function escape(text: string): string {
  return text.replaceAll('|', '\\|');
}

function stripAnsi(text: string): string {
  // eslint-disable-next-line no-control-regex
  return text.replace(/\u001b\[[0-9;]*m/g, '');
}
