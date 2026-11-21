# Vyxum.Desktop

The desktop window of Vyxum on a single PC: a WinForms shell around WebView2 that shows the web application. The product itself stays a web application; this window only removes the browser chrome (no address bar, own icon, optional full screen).

- Output: `Vyxum.exe` (assembly name `Vyxum`), `net10.0-windows`. It builds on Linux too (`EnableWindowsTargeting`) but runs on Windows 10/11 only.
- Address: `Urls` in `%ProgramData%\Vyxum\vyxum.json` (first entry; `*`, `+`, `0.0.0.0` and `[::]` hosts become `localhost`). `--url <address>` on the command line overrides it. Default `http://localhost:5000`.
- Full screen: F11 toggles. `Vyxum:Desktop:FullScreen` (`true`) in `vyxum.json` starts full screen, for touch terminals.
- Start: shows "Starting Vyxum..." and polls `GET {address}/health` every 500 ms. After 60 seconds it shows a message with a "Try again" button. If the server goes away later (for example it restarts), the waiting view returns by itself.
- Links that leave the application's origin open in the default browser.
- Without the WebView2 runtime (part of Windows 10/11) the address opens in the default browser and the window exits.
- A second launch brings the running window to the front.
- WebView2 data folder: `%LocalAppData%\Vyxum\WebView2`. Release builds turn off the context menu and developer tools.
- Texts are in `Resources/Strings.resx` (English) and `Resources/Strings.tr.resx` (Turkish), chosen by the Windows display language.
- `vyxum.ico` is a placeholder (plain square in the accent color).

Publish (used by the installer):

```
dotnet publish src/Desktop/Vyxum.Desktop -c Release -r win-x64 --self-contained -o <folder>
```

Tests of the address and health logic: `tests/Vyxum.Desktop.Tests` (runs on every platform).
