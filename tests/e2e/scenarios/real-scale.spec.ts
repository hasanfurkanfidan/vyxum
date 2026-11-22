import { test, expect } from '../support/vyxum';

/**
 * The real scale of the Vyxum installed on this computer (the Windows services, usually http://localhost:5000).
 * Read-only: it reads the device list and looks at the weighing screen; it clicks nothing and changes nothing.
 * Whether the number is the right weight still needs a person with a known weight (see manual-checks.md).
 */
const realUrl = process.env.VYXUM_E2E_REAL_URL;

interface Device {
  deviceId: string;
  name: string | null;
  state: string;
  lastError: string | null;
  lastWeight: { value: number; unit: string; state: string } | null;
  lastSeen: string | null;
}

async function devices(): Promise<Device[]> {
  const response = await fetch(`${realUrl}/api/devices`);
  expect(response.ok, `GET ${realUrl}/api/devices answered ${response.status}`).toBe(true);
  return (await response.json()) as Device[];
}

test.describe('Real scale (installed Vyxum, read-only)', () => {
  test.skip(!realUrl, 'Yüklü bir Vyxum bulunamadı (VYXUM_E2E_REAL_URL yok). Gerçek teraziyle denemek için --real-url http://localhost:5000 ver.');

  test('The real scale is connected and sends weights', async () => {
    const first = await devices();
    expect(first.length, 'The installed Vyxum lists no device').toBeGreaterThan(0);
    const connected = first.filter((d) => d.state === 'Connected');
    expect(connected.map((d) => d.deviceId), `Devices and states: ${first.map((d) => `${d.deviceId}=${d.state}${d.lastError ? ` (${d.lastError})` : ''}`).join(', ')}`).not.toEqual([]);

    // Fresh readings keep arriving: lastSeen moves forward within a few seconds.
    await new Promise((resolve) => setTimeout(resolve, 3000));
    const second = await devices();
    for (const device of connected) {
      const later = second.find((d) => d.deviceId === device.deviceId);
      expect(later?.lastWeight, `${device.deviceId} has no weight`).toBeTruthy();
      expect(Date.parse(later!.lastSeen ?? '') > Date.parse(device.lastSeen ?? ''), `${device.deviceId} sent no new reading in 3 seconds`).toBe(true);
      test.info().annotations.push({
        type: 'gerçek terazi',
        description: `${device.name ?? device.deviceId}: ${later!.lastWeight!.value} ${later!.lastWeight!.unit} (${later!.lastWeight!.state})`,
      });
    }
  });

  test('The weighing screen of the installed Vyxum shows the live weight', async ({ page, t, shot }) => {
    await page.goto(`${realUrl}/`);
    // The page moves to the wizard by itself when setup is not finished.
    const live = page.getByRole('region', { name: t('weighing.LiveWeight') });
    await Promise.race([page.waitForURL(/\/setup/, { timeout: 15_000 }), live.waitFor({ timeout: 15_000 })]);
    if (page.url().includes('/setup')) {
      await shot('kurulum-tamamlanmamis');
      test.skip(true, 'Yüklü Vyxum’un kurulum sihirbazı tamamlanmamış; ekrana dokunulmadı.');
    }
    await expect(page.getByText(t('weighing.DeviceConnected'), { exact: false }).first()).toBeVisible({ timeout: 15_000 });
    await expect(live).toContainText(/\d/);
    await shot('gercek-terazi-tartim-ekrani');
  });
});
