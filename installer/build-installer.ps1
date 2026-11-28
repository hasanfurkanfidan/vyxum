<#
.SYNOPSIS
  Builds the Vyxum setup program (artifacts/installer/VyxumSetup-<version>.exe).

.PARAMETER Version
  Product version. Defaults to <Version> in Directory.Build.props (the single version source).

.PARAMETER OutputDir
  Where the setup program is written. Default: artifacts/installer.

.PARAMETER SkipCompile
  Only publish the applications; do not run Inno Setup.

.PARAMETER ModulesRepo
  A checkout of the paid modules repository. Every src/*/Vyxum.Modules.*/Vyxum.Modules.*.csproj in it is published
  into web/modules/<name>, where the host loads it (decision V-59). Default: ../vyxum-pro next to this repository,
  when it exists.

.PARAMETER CoreOnly
  Build the setup program without the paid modules, even when ../vyxum-pro exists.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDir,
    [switch]$SkipCompile,
    [string]$ModulesRepo,
    [switch]$CoreOnly
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

function Invoke-Step([string]$title, [scriptblock]$action) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { Fail "$title failed (exit code $LASTEXITCODE)." }
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$webUi = Join-Path $repo 'src\Web\vyxum-web'
$publishRoot = Join-Path $repo 'artifacts\publish'
if (-not $OutputDir) { $OutputDir = Join-Path $repo 'artifacts\installer' }

# --- Version ---------------------------------------------------------------
if (-not $Version) {
    $props = [xml](Get-Content -Raw (Join-Path $repo 'Directory.Build.props'))
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
}
if (-not $Version) { Fail 'No version given and no <Version> found in Directory.Build.props.' }
if ($Version -notmatch '^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$') { Fail "Invalid version '$Version'." }
Write-Host "Version: $Version"

# --- Tools -----------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail '.NET 10 SDK (dotnet) was not found on PATH.' }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { Fail 'Node.js 22 (npm) was not found on PATH.' }

# --- Web UI ----------------------------------------------------------------
Push-Location $webUi
try {
    Invoke-Step 'npm ci' { npm ci }
    Invoke-Step 'npm run build' { npm run build }
}
finally { Pop-Location }

# --- Publish ---------------------------------------------------------------
if (Test-Path $publishRoot) { Remove-Item -Recurse -Force $publishRoot }

$projects = @(
    @{ Name = 'web';     Path = 'src\Host\Vyxum.Host\Vyxum.Host.csproj' },
    @{ Name = 'agent';   Path = 'src\Agent\Vyxum.Agent\Vyxum.Agent.csproj' },
    @{ Name = 'desktop'; Path = 'src\Desktop\Vyxum.Desktop\Vyxum.Desktop.csproj' }
)
foreach ($p in $projects) {
    $out = Join-Path $publishRoot $p.Name
    $proj = Join-Path $repo $p.Path
    # The UI was built above; SkipUiBuild stops the Host from building it a second time.
    Invoke-Step "dotnet publish $($p.Name)" {
        dotnet publish $proj -c Release -r win-x64 --self-contained true -o $out "-p:Version=$Version" -p:SkipUiBuild=true
    }
}

# --- Paid modules (decision V-59) ------------------------------------------------
# Each module is published next to the host as web/modules/<name>. Files the host already has are removed from the
# module folder: the host always loads its own copy of a shared assembly, so a second copy would only add size.
if (-not $CoreOnly -and -not $ModulesRepo) {
    $sibling = Join-Path (Split-Path $repo -Parent) 'vyxum-pro'
    if (Test-Path $sibling) { $ModulesRepo = $sibling }
}
$moduleNames = @()
if (-not $CoreOnly -and $ModulesRepo) {
    $ModulesRepo = (Resolve-Path $ModulesRepo).Path
    $moduleProjects = Get-ChildItem (Join-Path $ModulesRepo 'src') -Recurse -Filter 'Vyxum.Modules.*.csproj' |
        Where-Object { $_.Directory.Name -eq $_.BaseName -and $_.FullName -notmatch '\\(bin|obj)\\' } |
        Sort-Object BaseName
    if (-not $moduleProjects) { Fail "No module projects were found under $ModulesRepo\src." }
    $webDir = Join-Path $publishRoot 'web'
    $hostFiles = @{}
    Get-ChildItem $webDir -Recurse -File | ForEach-Object { $hostFiles[$_.FullName.Substring($webDir.Length + 1)] = $true }
    foreach ($m in $moduleProjects) {
        $out = Join-Path $webDir "modules\$($m.BaseName)"
        Invoke-Step "dotnet publish $($m.BaseName)" {
            dotnet publish $m.FullName -c Release -r win-x64 --self-contained false -o $out "-p:Version=$Version"
        }
        Get-ChildItem $out -Recurse -File | Where-Object { $hostFiles.ContainsKey($_.FullName.Substring($out.Length + 1)) } |
            Remove-Item -Force
        Get-ChildItem $out -Recurse -File -Filter '*.runtimeconfig.json' | Remove-Item -Force
        Get-ChildItem $out -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending |
            Where-Object { -not (Get-ChildItem $_.FullName -Force) } | Remove-Item -Force
        $moduleNames += $m.BaseName
    }
    Write-Host "Paid modules from ${ModulesRepo}: $($moduleNames -join ', ')"
}
else {
    Write-Host 'Paid modules: none (core only).' -ForegroundColor Yellow
}

# Third-party notices: the core's, plus the paid modules repository's when its modules are included.
$notices = Get-Content -Raw (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt')
if ($moduleNames) {
    $proNotices = Join-Path $ModulesRepo 'THIRD-PARTY-NOTICES.txt'
    if (-not (Test-Path $proNotices)) { Fail "Missing ${proNotices}: the paid modules' third-party licenses must ship with them." }
    $notices = $notices.TrimEnd() + "`r`n`r`n" + (Get-Content -Raw $proNotices)
}
Set-Content -Path (Join-Path $publishRoot 'THIRD-PARTY-NOTICES.txt') -Value $notices -Encoding utf8

# --- Verify ----------------------------------------------------------------
$checks = @(
    @{ File = 'web\wwwroot\index.html';      Hint = 'the React UI was not built into the web output' },
    @{ File = 'web\Vyxum.Host.exe';          Hint = 'host executable missing' },
    @{ File = 'agent\Vyxum.Agent.exe';       Hint = 'agent executable missing' },
    @{ File = 'desktop\Vyxum.exe';           Hint = 'desktop executable missing' },
    @{ File = 'desktop\WebView2Loader.dll';  Hint = 'WebView2 loader missing; the desktop window cannot start' }
)
foreach ($name in $moduleNames) {
    $checks += @{ File = "web\modules\$name\$name.dll"; Hint = 'paid module missing from the modules folder' }
}
foreach ($c in $checks) {
    if (-not (Test-Path (Join-Path $publishRoot $c.File))) { Fail "Missing $($c.File): $($c.Hint)." }
}

foreach ($p in $projects) {
    $dir = Join-Path $publishRoot $p.Name
    $mb = [math]::Round(((Get-ChildItem $dir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Write-Host ("  {0,-8} {1} MB" -f $p.Name, $mb)
}

if ($SkipCompile) {
    Write-Host 'SkipCompile set: publish finished, setup program not built.' -ForegroundColor Yellow
    exit 0
}

# --- Inno Setup ------------------------------------------------------------
$iscc = $null
$cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($cmd) { $iscc = $cmd.Source }
if (-not $iscc) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $iscc) { Fail 'Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isinfo.php or use -SkipCompile.' }

New-Item -ItemType Directory -Force $OutputDir | Out-Null
Invoke-Step 'ISCC' {
    & $iscc "/DAppVersion=$Version" "/DPublishDir=$publishRoot" "/DRepoDir=$repo" "/O$OutputDir" (Join-Path $PSScriptRoot 'vyxum.iss')
}

$setup = Join-Path $OutputDir "VyxumSetup-$Version.exe"
if (-not (Test-Path $setup)) { Fail "Expected output not found: $setup" }
Write-Host ("Done: {0} ({1} MB)" -f $setup, [math]::Round((Get-Item $setup).Length / 1MB, 1)) -ForegroundColor Green
