import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Link, Navigate, Route, Routes, matchPath, useLocation, useParams } from 'react-router-dom';
import { useT } from '../i18n/I18nProvider';
import { LockedPreview } from './LockedPreview';
import { useModules } from './ModulesProvider';
import { useModuleUi } from './ModuleUiProvider';
import { routes } from './navigation';
import '../styles/license.css';

/** Placeholder of a module screen whose module has no license; links to the license settings. */
export function ModuleLicenseRequired() {
  const t = useT();
  return (
    <section className="license-placeholder" aria-labelledby="license-placeholder-title">
      <h1 id="license-placeholder-title" className="license-placeholder__title">
        {t('shell.ModuleLicenseRequired')}
      </h1>
      <p className="license-placeholder__text">{t('shell.ModuleLicenseRequiredHint')}</p>
      <Link className="button button--primary license-placeholder__link" to={routes.settingsLicense}>
        {t('shell.ModuleLicenseSettings')}
      </Link>
    </section>
  );
}

function ModuleProblem({ textKey }: { textKey: string }) {
  const t = useT();
  return (
    <div className="status" role="alert">
      <p className="status__text">{t(textKey)}</p>
    </div>
  );
}

interface BoundaryState {
  failed: boolean;
}

/** Keeps an error in a module's screen inside that screen; the shell and the menu keep working. */
export class ModuleErrorBoundary extends Component<{ moduleId: string; children: ReactNode }, BoundaryState> {
  override state: BoundaryState = { failed: false };

  static getDerivedStateFromError(): BoundaryState {
    return { failed: true };
  }

  override componentDidCatch(error: unknown, info: ErrorInfo) {
    console.error(`A screen of module '${this.props.moduleId}' failed.`, error, info.componentStack);
  }

  override render() {
    return this.state.failed ? <ModuleProblem textKey="shell.ModuleScreenFailed" /> : this.props.children;
  }
}

/** Route `/m/:moduleId/*`: the screens a module registered, or why they are not available. */
export function ModuleScreen() {
  const { moduleId = '' } = useParams();
  const { modules, hasCapability } = useModules();
  const moduleUi = useModuleUi();
  const module = modules.find((m) => m.id === moduleId);

  if (!module) return <Navigate to={routes.weighing} replace />;
  if (module.license === 'locked') return module.preview ? <LockedPreview module={module} /> : <ModuleLicenseRequired />;
  if (!module.ui) return <Navigate to={routes.weighing} replace />;

  const result = moduleUi.get(moduleId);
  if (!result) return <div className="status" role="status" />;
  if (result.status !== 'loaded') {
    return <ModuleProblem textKey={result.status === 'incompatible' ? 'shell.ModuleIncompatible' : 'shell.ModuleLoadFailed'} />;
  }

  return (
    <Routes>
      {result.routes
        .filter((route) => route.capability === undefined || hasCapability(route.capability))
        .map((route) => {
          const Screen = route.component;
          const element = (
            <ModuleErrorBoundary moduleId={moduleId}>{Screen ? <Screen /> : route.element}</ModuleErrorBoundary>
          );
          return route.path === '' ? (
            <Route key="" index element={element} />
          ) : (
            <Route key={route.path} path={route.path} element={element} />
          );
        })}
      <Route path="*" element={<Navigate to={routes.weighing} replace />} />
    </Routes>
  );
}

/**
 * Outside the shell: a screen a module shows on the whole window (`pages`, SDK 2) at `/m/{moduleId}/{path}`, for
 * example a sign-in screen; every other address goes to `fallback`, the shell with its screens. It reads the address
 * itself instead of being a `/m/:moduleId/*` route, so the shell's routes in `fallback` stay relative to the root.
 */
export function ModulePage({ fallback }: { fallback: ReactNode }) {
  const { pathname } = useLocation();
  const match = matchPath({ path: routes.module, end: false }, pathname);
  const moduleId = match?.params.moduleId ?? '';
  const rest = match?.params['*'] ?? '';
  const { modules, hasCapability } = useModules();
  const moduleUi = useModuleUi();
  const module = modules.find((m) => m.id === moduleId);
  const result = moduleUi.get(moduleId);
  if (!module || module.license === 'locked' || result?.status !== 'loaded') return fallback;

  const relative = '/' + rest.replace(/\/+$/, '');
  const page = result.pages.find(
    (p) => (p.capability === undefined || hasCapability(p.capability)) && matchPath({ path: '/' + p.path, end: true }, relative) !== null,
  );
  if (!page) return fallback;
  const Screen = page.component;
  return (
    <main className="module-page">
      <ModuleErrorBoundary moduleId={moduleId}>{Screen ? <Screen /> : page.element}</ModuleErrorBoundary>
    </main>
  );
}
