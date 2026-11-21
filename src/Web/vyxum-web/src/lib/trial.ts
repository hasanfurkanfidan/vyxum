// The text of a trial license request, ready to copy and send to Vyxum or the dealer.

export interface TrialRequestInput {
  /** Company name from the setup wizard, or null. */
  company: string | null;
  moduleName: string;
  moduleId: string;
  /** Product version, or null when it could not be read. */
  version: string | null;
  /** Id of the license stored now, or null when there is none. */
  licenseId: string | null;
  date: Date;
}

/** `yyyy-MM-dd` in local time. */
export function isoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** The request text; `t` resolves the label keys, so it comes out in the current language. Lines without a value are left out. */
export function buildTrialRequest(input: TrialRequestInput, t: (key: string) => string): string {
  const rows: [string, string | null][] = [
    [t('license.TrialCompany'), input.company],
    [t('license.TrialModule'), `${input.moduleName} (${input.moduleId})`],
    [t('license.TrialVersion'), input.version],
    [t('license.TrialLicenseId'), input.licenseId],
    [t('license.TrialDate'), isoDate(input.date)],
  ];
  return [t('license.TrialHeading'), ...rows.filter(([, value]) => value).map(([label, value]) => `${label}: ${value}`)].join('\n');
}

/** Copies text with the clipboard API; false when the browser refuses (the caller then selects the text for the user). */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Fall through to the selection fallback.
  }
  return false;
}
