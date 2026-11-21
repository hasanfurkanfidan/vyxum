// The UI language is remembered in the standard ASP.NET Core culture cookie,
// so the host picks the same language for /api/ui/strings and for exported files.

export const supportedCultures = ['tr', 'en'] as const;
export type Culture = (typeof supportedCultures)[number];
export const defaultCulture: Culture = 'tr';

export const cultureCookieName = '.AspNetCore.Culture';
const oneYearSeconds = 365 * 24 * 60 * 60;

export function isSupportedCulture(value: string): value is Culture {
  return (supportedCultures as readonly string[]).includes(value);
}

/** Maps a server culture name ("tr", "tr-TR", "en-US") to a supported UI culture. */
export function toCulture(value: string | null | undefined): Culture {
  const neutral = (value ?? '').toLowerCase().split('-')[0] ?? '';
  return isSupportedCulture(neutral) ? neutral : defaultCulture;
}

/** Cookie value in the format of CookieRequestCultureProvider: `c=tr|uic=tr`. */
export function cultureCookieValue(culture: Culture): string {
  return `c=${culture}|uic=${culture}`;
}

export function writeCultureCookie(culture: Culture): void {
  const value = encodeURIComponent(cultureCookieValue(culture));
  document.cookie = `${cultureCookieName}=${value}; path=/; max-age=${oneYearSeconds}; samesite=lax`;
}

/** Reads the UI culture from the cookie, or undefined when the cookie is missing. */
export function readCultureCookie(): Culture | undefined {
  for (const part of document.cookie.split(';')) {
    const [name, ...rest] = part.trim().split('=');
    if (name !== cultureCookieName) continue;
    const value = decodeURIComponent(rest.join('='));
    const uic = /(?:^|\|)uic=([^|]+)/.exec(value)?.[1];
    return uic ? toCulture(uic) : undefined;
  }
  return undefined;
}
