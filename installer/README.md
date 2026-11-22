# Vyxum installer

`VyxumSetup-<version>.exe` is a single setup program built with [Inno Setup 6](https://jrsoftware.org/isinfo.php) (decision V-36). Inno Setup is only needed on the build machine and in CI; it is not part of the product.

## Build locally

Prerequisites (Windows x64):

- .NET 10 SDK (see `global.json`)
- Node.js 22
- Inno Setup 6 (`ISCC.exe` in `Program Files (x86)\Inno Setup 6` or on `PATH`)

```powershell
./installer/build-installer.ps1
```

Result: `artifacts/installer/VyxumSetup-<version>.exe`.

Options:

| Option | Meaning |
|---|---|
| `-Version 1.2.3` | Product version. Default: `<Version>` in `Directory.Build.props` (single version source). |
| `-OutputDir <path>` | Where the setup program is written. |
| `-SkipCompile` | Only publish the applications into `artifacts/publish`; do not run Inno Setup. |

The script builds the web UI (`npm ci`, `npm run build`), publishes Host, Agent and Desktop (Release, win-x64, self-contained, folder publish) into `artifacts/publish/{web,agent,desktop}`, checks the result (`wwwroot/index.html`, `WebView2Loader.dll`) and runs `ISCC`.

## What the installer does

- Installs to `Program Files\Vyxum` (`Web\`, `Agent\`, `Vyxum.exe`); needs administrator rights; Turkish and English (chosen from the Windows language).
- Creates `%ProgramData%\Vyxum\vyxum.json` only if it does not exist (default address `http://localhost:5000`), then runs `Vyxum.Host.exe --ensure-agent-key`, which adds a random agent key for the web application and the agent unless one is set.
- Registers the Windows services **Vyxum Web** and **Vyxum Agent** (automatic start), each running as its own virtual account (`NT SERVICE\Vyxum Web`, `NT SERVICE\Vyxum Agent`) instead of LocalSystem.
- Limits `%ProgramData%\Vyxum` to SYSTEM, administrators, the web service (change) and the agent service (read); it no longer inherits the ProgramData rights that let every user read it. Failure recovery: restart after 5 seconds, three times; also applied when the process exits with an error code on its own (the web host does this on purpose after the database is changed in the setup wizard).
- Creates desktop and start-menu shortcuts and offers to open Vyxum at the end.
- **Update:** run the newer setup. It stops both services, copies `vyxum.db` (and `-wal`/`-shm`) to `%ProgramData%\Vyxum\backups\vyxum-<date>-before-setup-<old version>.db`, replaces the files and starts the services. Existing settings in `vyxum.json` are never changed; only a missing agent key is added.
- **Uninstall:** stops and deletes the services and removes the program files. Data in `%ProgramData%\Vyxum` is kept unless the user answers "Yes" to the question asked at the end (default "No").

## Files

- `vyxum.iss` Inno Setup script (UTF-8 with BOM; keep it that way).
- `vyxum.default.json` default shared settings.
- `THIRD-PARTY-NOTICES.txt` licenses of bundled components.
