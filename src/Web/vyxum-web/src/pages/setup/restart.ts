// Waiting for the host to come back after a database change.
import { getSetupStatus, type SetupStatus } from '../../lib/setup';

/** Timing of the restart wait. Tests shorten it. */
export const restartTiming = {
  /** Time between two status requests. */
  pollMs: 1000,
  /** After this long with `restartPending` still true the host did not restart by itself (not a service). */
  manualAfterMs: 20000,
};

export interface RestartWatch {
  stop: () => void;
}

/**
 * Asks for the setup status every `pollMs` until the host answers with no pending restart.
 * Failed requests are expected while the host is down and are ignored.
 * `onManual` is called once when the host keeps reporting a pending restart for `manualAfterMs`.
 */
export function watchRestart(onBack: (status: SetupStatus) => void, onManual: () => void): RestartWatch {
  const started = Date.now();
  let stopped = false;
  let manual = false;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const tick = async () => {
    if (stopped) return;
    try {
      const status = await getSetupStatus();
      if (stopped) return;
      if (!status.restartPending) {
        stopped = true;
        onBack(status);
        return;
      }
      if (!manual && Date.now() - started >= restartTiming.manualAfterMs) {
        manual = true;
        onManual();
      }
    } catch {
      // The host is restarting.
    }
    if (!stopped) timer = setTimeout(() => void tick(), restartTiming.pollMs);
  };

  timer = setTimeout(() => void tick(), restartTiming.pollMs);
  return {
    stop: () => {
      stopped = true;
      if (timer !== undefined) clearTimeout(timer);
    },
  };
}
