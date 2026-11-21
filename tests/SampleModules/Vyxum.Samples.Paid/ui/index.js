// Screens of the paid sample module: a hand-written ES module without a build step.
// React and @vyxum/sdk are bare specifiers; the web application maps them to its own copies (import map),
// so this file never brings its own React. A real module builds the same shape with Vite (see the module SDK notes).
import { createElement as h, useCallback, useEffect, useState } from 'react';
import { PageHeader, apiGet, apiPost, useModuleLicense, useT } from '@vyxum/sdk';

export const sdkVersion = 1;

const moduleId = 'samples.paid';

function EntriesPage() {
  const t = useT();
  const license = useModuleLicense(moduleId);
  const [entries, setEntries] = useState(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(() => {
    apiGet('/api/samples-paid/entries')
      .then((list) => {
        setEntries(list);
        setFailed(false);
      })
      .catch(() => setFailed(true));
  }, []);

  useEffect(load, [load]);

  const add = () => {
    apiPost('/api/samples-paid/entries', { text: new Date().toISOString() }).then(load, () => setFailed(true));
  };

  // A read-only module shows its data but offers no actions.
  const actions = license.canWrite
    ? h('button', { type: 'button', className: 'button button--primary', onClick: add }, t('samples-paid.Add'))
    : undefined;

  let body;
  if (failed) body = h('p', { role: 'alert' }, t('samples-paid.LoadFailed'));
  else if (entries === null) body = null;
  else if (entries.length === 0) body = h('p', null, t('samples-paid.Empty'));
  else body = h('ul', { 'data-testid': 'samples-paid-entries' }, entries.map((text, i) => h('li', { key: i }, text)));

  return h(
    'div',
    { className: 'page' },
    h(PageHeader, { title: t('samples-paid.Title'), eyebrow: t('samples-paid.Eyebrow'), actions }),
    body,
  );
}

/** Called once by the web application after it imported this file. */
export function register(host) {
  return {
    routes: [{ path: '', component: EntriesPage }],
    nav: [{ labelKey: 'samples-paid.Title', path: '' }],
  };
}
