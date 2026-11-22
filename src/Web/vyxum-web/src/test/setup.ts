import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterEach } from 'vitest';

// The app loads several lists on start; give slow machines time before an async query gives up.
configure({ asyncUtilTimeout: 4000 });

afterEach(() => {
  cleanup();
  // Expire every cookie and reset the document language between tests.
  for (const part of document.cookie.split(';')) {
    const name = part.split('=')[0]?.trim();
    if (name) document.cookie = `${name}=; path=/; max-age=0`;
  }
  document.documentElement.lang = 'tr';
  window.sessionStorage.clear();
});
