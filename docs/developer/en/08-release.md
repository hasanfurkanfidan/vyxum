# 8. Installer and releases

[Index](../README.md) · Previous: [7. Web UI](07-web-ui.md) · Next: [9. Troubleshooting](09-troubleshooting.md)

## What the customer gets

One file, `VyxumSetup-<version>.exe`, built with **Inno Setup 6** (it is only a build tool; it is not part of the product). It installs three published, self-contained (no .NET needed on the customer's PC) programs for Windows 10/11 x64. The end-user steps are in [docs/INSTALL.md](../../INSTALL.md) (Turkish: [docs/KURULUM.md](../../KURULUM.md)).

## Files

| File | Role |
|---|---|
| `installer/build-installer.ps1` | Builds the UI, publishes the three programs, checks the output, runs the Inno Setup compiler. |
| `installer/vyxum.iss` | The Inno Setup script (UTF-8 **with BOM**; keep it so). Texts in Turkish and English; the language follows Windows. |
| `installer/vyxum.default.json` | Default `vyxum.json`: `{"Urls":"http://localhost:5000","Vyxum":{"Agent":{"ServerUrl":"http://localhost:5000"}}}`. |
| `installer/THIRD-PARTY-NOTICES.txt` | Licenses of bundled components. Update it when you add a package that ships to customers. |
| `installer/README.md` | Short reference. |

## Build the installer locally (Windows)

Prerequisites: .NET 10 SDK, Node.js 22, Inno Setup 6 (`ISCC.exe` on `PATH` or in `Program Files (x86)\Inno Setup 6`).

```powershell
./installer/build-installer.ps1
```

Output: `artifacts/installer/VyxumSetup-<version>.exe`. Options: `-Version 1.2.3`, `-OutputDir <path>`, `-SkipCompile` (only publish into `artifacts/publish/{web,agent,desktop}`).

What the script does, in order: reads the version (from `-Version`, else `<Version>` in `Directory.Build.props`); `npm ci` + `npm run build`; `dotnet publish` of Host, Agent and Desktop (Release, `win-x64`, self-contained, with `-p:SkipUiBuild=true` because the UI is already built); checks that `web\wwwroot\index.html`, `Vyxum.Host.exe`, `Vyxum.Agent.exe`, `Vyxum.exe` and `WebView2Loader.dll` exist; runs `ISCC`.

CI builds the installer on `windows-latest` for every push and pull request and uploads it as the artifact `VyxumSetup` (job `installer` in `.github/workflows/ci.yml`).

## What the installer does on the customer's PC

- Copies to `Program Files\Vyxum` (`Web\`, `Agent\`, `Vyxum.exe`). Needs administrator rights.
- Creates `%ProgramData%\Vyxum\vyxum.json` only if missing (`onlyifdoesntexist`) and never removes it on uninstall (`uninsneveruninstall`).
- Registers two Windows services with `sc.exe`: **Vyxum Web** (`Web\Vyxum.Host.exe`) and **Vyxum Agent** (`Agent\Vyxum.Agent.exe`), automatic start, recovery "restart after 5 s" three times, and the failure flag so that a deliberate exit with an error code also restarts (the host does that after the setup wizard changes the database).
- Creates desktop and start-menu shortcuts.
- **Update:** running a newer setup stops both services, copies the SQLite database (and `-wal`/`-shm`) to `%ProgramData%\Vyxum\backups\vyxum-<date>-before-setup-<old version>.db`, replaces the files and starts the services. `vyxum.json` is untouched. The new host then applies pending migrations at start ([Chapter 5](05-database.md)), after taking its own backup.
- **Uninstall:** stops and deletes the services and removes program files; asks whether to delete the data (default No).

## How to release a new version

There is no automatic release pipeline in this repository yet; a release is done by hand, in this order:

1. Make sure `main` is green in CI (build, tests on SQLite **and** SQL Server, web checks, installer job).
2. **Version:** change `<Version>` in `Directory.Build.props` (the only place; assemblies, the About screen and the installer take it from there) in its own commit, for example `Release 0.2.0`.
3. **Migrations check:** every model change since the last release has migrations for both providers (the tests enforce it). Never edit a migration that shipped.
4. **Upgrade test:** install the *previous* release's setup on a clean Windows machine (a VM), create some products and weighings, then run the *new* setup over it. Check: services running, data still there, a backup file exists in `backups\`.
5. Build the installer (CI artifact or the script) and test a **fresh install** as well, including the setup wizard and a simulated or real scale.
6. Update `installer/THIRD-PARTY-NOTICES.txt` if packages changed.
7. Tag the commit (`git tag v0.2.0`) and push the tag; attach the setup file to a GitHub release.
8. Update the end-user guides if screens or steps changed (both languages).

**Code signing:** this repository has no signing setup, and signing keys or certificates must never be committed. An unsigned setup makes Windows SmartScreen show a warning. When signing is added, keep the certificate outside the repository (CI secret or a hardware token) and document the step here.
