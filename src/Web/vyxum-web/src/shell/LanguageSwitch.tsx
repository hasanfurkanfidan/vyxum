import { useI18n } from '../i18n/I18nProvider';
import { supportedCultures, type Culture } from '../lib/culture';

const labelKeys: Record<Culture, string> = {
  tr: 'shell.Turkish',
  en: 'shell.English',
};

export function LanguageSwitch() {
  const { t, culture, setCulture, status } = useI18n();

  return (
    <div className="language" role="group" aria-labelledby="language-heading">
      <span id="language-heading" className="sidebar__heading">
        {t('shell.Language')}
      </span>
      <div className="language__options">
        {supportedCultures.map((option) => {
          const active = option === culture;
          return (
            <button
              key={option}
              type="button"
              lang={option}
              className={active ? 'language__option language__option--active' : 'language__option'}
              aria-pressed={active}
              disabled={status === 'loading'}
              onClick={() => {
                if (!active) void setCulture(option);
              }}
            >
              {t(labelKeys[option])}
            </button>
          );
        })}
      </div>
    </div>
  );
}
