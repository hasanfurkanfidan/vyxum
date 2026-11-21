import type { ReactNode } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { I18nProvider } from './i18n/I18nProvider';
import { ProductsPage } from './pages/ProductsPage';
import { RecordsPage } from './pages/RecordsPage';
import { WeighingPage } from './pages/WeighingPage';
import type { ModuleImporter } from './shell/moduleLoader';
import { ModuleScreen } from './shell/ModuleScreen';
import { ModuleUiProvider } from './shell/ModuleUiProvider';
import { ModulesProvider, useModules } from './shell/ModulesProvider';
import { routes } from './shell/navigation';
import { SetupGate } from './shell/SetupGate';
import { SetupProvider } from './shell/SetupProvider';
import { Shell } from './shell/Shell';
import { SetupWizard } from './pages/setup/SetupWizard';
import { AboutPage } from './pages/settings/AboutPage';
import { DevicesPage } from './pages/settings/DevicesPage';

/**
 * Strings, module list, module screens and setup status. The router is supplied by the caller (BrowserRouter or
 * MemoryRouter). Tests pass `moduleImporter` instead of importing module screens from the server.
 */
export function AppProviders({ children, moduleImporter }: { children: ReactNode; moduleImporter?: ModuleImporter }) {
  return (
    <I18nProvider>
      <ModulesProvider>
        <ModuleUiProvider {...(moduleImporter ? { importer: moduleImporter } : {})}>
          <SetupProvider>{children}</SetupProvider>
        </ModuleUiProvider>
      </ModulesProvider>
    </I18nProvider>
  );
}

function Home() {
  const { hasModule } = useModules();
  if (hasModule('weighing')) return <WeighingPage />;
  if (hasModule('products')) return <Navigate to={routes.products} replace />;
  return null;
}

/** Renders the screen only when its module is loaded; otherwise goes back to the start page. */
function RequireModule({ module, children }: { module: string; children: ReactNode }) {
  const { hasModule } = useModules();
  return hasModule(module) ? children : <Navigate to={routes.weighing} replace />;
}

export function App() {
  return (
    <SetupGate>
      <Routes>
        <Route path={routes.setup} element={<SetupWizard />} />
        <Route path={routes.setupTurkish} element={<Navigate to={routes.setup} replace />} />
        <Route path="*" element={<MainScreens />} />
      </Routes>
    </SetupGate>
  );
}

/** Screens inside the shell with the menu. */
function MainScreens() {
  return (
    <Shell>
      <Routes>
        <Route path={routes.weighing} element={<Home />} />
        <Route
          path={routes.records}
          element={
            <RequireModule module="weighing">
              <RecordsPage />
            </RequireModule>
          }
        />
        <Route
          path={routes.products}
          element={
            <RequireModule module="products">
              <ProductsPage />
            </RequireModule>
          }
        />
        <Route path={routes.settingsDevices} element={<DevicesPage />} />
        <Route path={routes.settingsAbout} element={<AboutPage />} />
        <Route path={routes.module} element={<ModuleScreen />} />
        <Route path="*" element={<Navigate to={routes.weighing} replace />} />
      </Routes>
    </Shell>
  );
}
