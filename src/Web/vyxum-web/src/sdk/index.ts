// @vyxum/sdk: the stable surface the web application offers to separately built module screens.
// Modules import it as a bare specifier; the import map points it at a shim that re-exports this object.
// Only add to it (and raise SDK_VERSION); never remove or change what is here.
import { useT, useCulture } from '../i18n/I18nProvider';
import {
  ApiError,
  NetworkError,
  RuleError,
  ValidationError,
  apiBlob,
  apiDelete,
  apiFile,
  apiGet,
  apiPost,
  apiPut,
  apiText,
  request,
  withQuery,
} from '../lib/api';
import { formatDate, formatDateTime, formatDecimal, formatMoney, formatTime, formatWeight } from '../lib/format';
import { useModules } from '../shell/ModulesProvider';
import { PageHeader } from '../shell/PageHeader';
import { routes } from '../shell/navigation';
import { SDK_VERSION } from './contract';
import { useModuleLicense } from './license';

export { useModuleLicense, type ModuleLicenseInfo, type ModuleLicenseState } from './license';

/** The SDK object, exposed to modules as `@vyxum/sdk`. */
export const sdk = {
  sdkVersion: SDK_VERSION,
  useT,
  useCulture,
  request,
  apiGet,
  apiPost,
  apiPut,
  apiDelete,
  apiText,
  apiBlob,
  apiFile,
  withQuery,
  ApiError,
  NetworkError,
  ValidationError,
  RuleError,
  formatDecimal,
  formatWeight,
  formatMoney,
  formatDate,
  formatTime,
  formatDateTime,
  PageHeader,
  useModules,
  useModuleLicense,
  /** Address of the settings page where the user loads a license file. */
  licenseSettingsPath: routes.settingsLicense,
} as const;

