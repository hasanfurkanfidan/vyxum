import { useId, useState } from 'react';
import { useT } from '../../i18n/I18nProvider';
import { ValidationError } from '../../lib/api';
import { selectDatabase, testDatabase, type DatabaseProvider, type DatabaseTestResult, type SetupStatus } from '../../lib/setup';
import { StepLayout, useSetupErrorKey } from './StepLayout';

export interface DatabaseStepProps {
  status: SetupStatus;
  /** Called after the choice was saved; `restartRequired` when the host restarts to use it. */
  onDone: (restartRequired: boolean) => void;
}

interface TestState {
  /** The connection string the result belongs to. */
  connectionString: string;
  result: DatabaseTestResult;
}

/** Step 1: where records are stored. SQLite on this computer is preselected; SQL Server is under "Advanced". */
export function DatabaseStep({ status, onDone }: DatabaseStepProps) {
  const t = useT();
  const id = useId();
  const errorKeyOf = useSetupErrorKey();
  const runningSqlServer = status.database.provider === 'SqlServer' && !status.restartPending;
  const [provider, setProvider] = useState<DatabaseProvider>(runningSqlServer ? 'SqlServer' : 'Sqlite');
  const [advancedOpen, setAdvancedOpen] = useState(runningSqlServer);
  const [connectionString, setConnectionString] = useState('');
  const [test, setTest] = useState<TestState | null>(null);
  const [testing, setTesting] = useState(false);
  const [busy, setBusy] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<readonly string[]>([]);
  const [errorKey, setErrorKey] = useState<string | null>(null);

  const trimmed = connectionString.trim();
  const testedOk = test !== null && test.result.ok && test.connectionString === trimmed;
  // SQL Server already runs and no new connection string was entered: nothing to change.
  const keepRunningSqlServer = provider === 'SqlServer' && runningSqlServer && trimmed === '';
  const canContinue = !busy && !testing && (provider === 'Sqlite' || testedOk || keepRunningSqlServer);

  const choose = (next: DatabaseProvider) => {
    setProvider(next);
    setErrorKey(null);
  };

  const runTest = async () => {
    if (testing) return;
    setTesting(true);
    setErrorKey(null);
    setFieldErrors([]);
    setTest(null);
    try {
      const result = await testDatabase({ provider: 'SqlServer', connectionString: trimmed });
      setTest({ connectionString: trimmed, result });
    } catch (error) {
      if (error instanceof ValidationError) setFieldErrors(error.forField('connectionString'));
      else setErrorKey(errorKeyOf(error));
    } finally {
      setTesting(false);
    }
  };

  const next = async () => {
    if (!canContinue) return;
    if (keepRunningSqlServer) {
      onDone(false);
      return;
    }
    setBusy(true);
    setErrorKey(null);
    setFieldErrors([]);
    try {
      const result = await selectDatabase(provider === 'Sqlite' ? { provider } : { provider, connectionString: trimmed });
      onDone(result.restartRequired);
    } catch (error) {
      if (error instanceof ValidationError) {
        const keys = error.forField('connectionString');
        if (keys.length > 0) setFieldErrors(keys);
        else setErrorKey(error.forField('provider')[0] ?? 'shell.Error');
      } else {
        setErrorKey(errorKeyOf(error));
      }
      setBusy(false);
    }
  };

  const fieldId = `${id}-connection`;
  const hintId = `${fieldId}-hint`;
  const errorId = `${fieldId}-error`;
  const advancedId = `${id}-advanced`;
  const testMessage = test && test.connectionString === trimmed ? test.result : null;

  return (
    <StepLayout
      title={t('setup.DatabaseTitle')}
      description={t('setup.DatabaseDescription')}
      errorKey={errorKey}
      actions={
        <button type="button" className="button button--primary wizard__button" disabled={!canContinue} onClick={() => void next()}>
          {t('setup.Next')}
        </button>
      }
    >
      <section className="section wizard__section" aria-label={t('setup.StepDatabase')}>
        <div className="choice-list">
          <label className={provider === 'Sqlite' ? 'choice choice--selected' : 'choice'}>
            <input
              className="choice__input"
              type="radio"
              name={`${id}-provider`}
              value="Sqlite"
              checked={provider === 'Sqlite'}
              onChange={() => choose('Sqlite')}
            />
            <span className="choice__text">
              <span className="choice__label">{t('setup.SqliteOption')}</span>
              <span className="choice__description">{t('setup.SqliteDescription')}</span>
            </span>
          </label>
        </div>

        <div className="advanced">
          <button
            type="button"
            className="advanced__toggle"
            aria-expanded={advancedOpen}
            aria-controls={advancedId}
            onClick={() => {
              // Closing the section goes back to the recommended choice, so no hidden option stays selected.
              if (advancedOpen) choose('Sqlite');
              setAdvancedOpen(!advancedOpen);
            }}
          >
            <span className="advanced__mark mono" aria-hidden="true">
              {advancedOpen ? '−' : '+'}
            </span>
            {t('setup.Advanced')}
          </button>

          {advancedOpen ? (
            <div className="advanced__body" id={advancedId}>
              <label className={provider === 'SqlServer' ? 'choice choice--selected' : 'choice'}>
                <input
                  className="choice__input"
                  type="radio"
                  name={`${id}-provider`}
                  value="SqlServer"
                  checked={provider === 'SqlServer'}
                  onChange={() => choose('SqlServer')}
                />
                <span className="choice__text">
                  <span className="choice__label">{t('setup.SqlServerOption')}</span>
                  <span className="choice__description">{t('setup.SqlServerDescription')}</span>
                </span>
              </label>

              {provider === 'SqlServer' ? (
                <div className="wizard__sqlserver">
                  <div className="field">
                    <label className="field__label" htmlFor={fieldId}>
                      {t('setup.ConnectionString')}
                    </label>
                    <input
                      id={fieldId}
                      className="input input--mono"
                      type="text"
                      spellCheck={false}
                      autoComplete="off"
                      value={connectionString}
                      aria-invalid={fieldErrors.length > 0}
                      aria-describedby={[hintId, fieldErrors.length > 0 ? errorId : ''].filter(Boolean).join(' ')}
                      onChange={(event) => {
                        setConnectionString(event.target.value);
                        setFieldErrors([]);
                      }}
                    />
                    <p className="field__hint mono" id={hintId}>
                      {t('setup.ConnectionStringHint')}
                    </p>
                    {fieldErrors.length > 0 ? (
                      <p className="field__error" id={errorId}>
                        {fieldErrors.map((key) => t(key)).join(' ')}
                      </p>
                    ) : null}
                  </div>
                  <div className="wizard__test">
                    <button
                      type="button"
                      className="button wizard__button"
                      disabled={testing || busy || trimmed === ''}
                      onClick={() => void runTest()}
                    >
                      {t('setup.TestConnection')}
                    </button>
                    <p
                      className={
                        testing || !testMessage ? 'wizard__test-result' : testMessage.ok ? 'wizard__test-result state--positive' : 'wizard__test-result state--warning'
                      }
                      role="status"
                    >
                      {testing
                        ? t('setup.Testing')
                        : testMessage
                          ? t(testMessage.code ?? (testMessage.ok ? 'setup.DbConnectionOk' : 'setup.DbUnknownError'))
                          : keepRunningSqlServer
                            ? null
                            : t('setup.TestRequired')}
                    </p>
                  </div>
                </div>
              ) : null}
            </div>
          ) : null}
        </div>
      </section>
    </StepLayout>
  );
}
