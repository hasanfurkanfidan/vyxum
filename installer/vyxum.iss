; Vyxum setup program (Inno Setup 6). Built by installer\build-installer.ps1, which passes
; AppVersion, PublishDir and RepoDir. Keep this file saved as UTF-8 with BOM (Turkish text).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef RepoDir
  #define RepoDir ".."
#endif
#ifndef PublishDir
  #define PublishDir RepoDir + "\artifacts\publish"
#endif

#define AppIdGuid "{6430d8d8-6570-4484-895b-838265c32d00}"
#define WebService "Vyxum Web"
#define AgentService "Vyxum Agent"

[Setup]
; Fixed forever: the same id lets a newer setup recognise an installed Vyxum and update it.
AppId={{6430d8d8-6570-4484-895b-838265c32d00}
AppName=Vyxum
AppVersion={#AppVersion}
AppVerName=Vyxum {#AppVersion}
AppPublisher=Vyxum
DefaultDirName={autopf}\Vyxum
DefaultGroupName=Vyxum
OutputBaseFilename=VyxumSetup-{#AppVersion}
VersionInfoVersion={#AppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; Shortest possible wizard: no welcome, ready or start-menu pages; the folder page is skipped on updates.
DisableWelcomePage=yes
DisableReadyPage=yes
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyMemo=yes
ShowLanguageDialog=auto
UsePreviousLanguage=no
CloseApplications=yes
RestartApplications=no
SetupIconFile={#RepoDir}\src\Desktop\Vyxum.Desktop\vyxum.ico
UninstallDisplayIcon={app}\Vyxum.exe
UninstallDisplayName=Vyxum
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
tr.LaunchVyxum=Vyxum'u aç
en.LaunchVyxum=Open Vyxum
tr.ServicesStopFailed=Vyxum servisleri durdurulamadı. Lütfen bilgisayarı yeniden başlatıp kurulumu tekrar çalıştırın.
en.ServicesStopFailed=The Vyxum services could not be stopped. Please restart the computer and run the setup again.
tr.DeleteDataQuestion=Vyxum kaldırıldı.%n%nVeritabanı, ayarlar ve yedekler de silinsin mi?%n(%1)%n%nBu işlem geri alınamaz. "Hayır" derseniz verileriniz korunur ve Vyxum'u sonradan yeniden kurabilirsiniz.
en.DeleteDataQuestion=Vyxum has been removed.%n%nDo you also want to delete the database, settings and backups?%n(%1)%n%nThis cannot be undone. If you choose "No", your data is kept and you can reinstall Vyxum later.
tr.ServiceWebDescription=Vyxum web uygulaması (tartım, kayıt ve ürün yönetimi).
en.ServiceWebDescription=Vyxum web application (weighing, records and product management).
tr.ServiceAgentDescription=Vyxum cihaz bağlantısı (terazi ve yazıcı iletişimi).
en.ServiceAgentDescription=Vyxum device connection (scale and printer communication).
tr.ServiceFailed=Vyxum servisi oluşturulamadı veya başlatılamadı: %1%n%nKurulum tamamlandı; servisi Windows Hizmetler ekranından başlatabilirsiniz.
en.ServiceFailed=The Vyxum service could not be created or started: %1%n%nSetup has finished; you can start the service from the Windows Services screen.

[Dirs]
Name: "{commonappdata}\Vyxum"; Flags: uninsneveruninstall

[InstallDelete]
; Remove files of the previous version so no stale assemblies are left behind. Data in %ProgramData%\Vyxum is never touched.
Type: filesandordirs; Name: "{app}\Web"
Type: filesandordirs; Name: "{app}\Agent"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: filesandordirs; Name: "{app}\runtimes"
Type: filesandordirs; Name: "{app}\tr"
Type: filesandordirs; Name: "{app}\en"

[Files]
Source: "{#PublishDir}\web\*"; DestDir: "{app}\Web"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\desktop\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoDir}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#SourcePath}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
; Shared settings file: written once, never overwritten on update and never removed by the uninstaller.
Source: "{#SourcePath}\vyxum.default.json"; DestDir: "{commonappdata}\Vyxum"; DestName: "vyxum.json"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{autodesktop}\Vyxum"; Filename: "{app}\Vyxum.exe"
Name: "{autoprograms}\Vyxum"; Filename: "{app}\Vyxum.exe"

[Run]
Filename: "{app}\Vyxum.exe"; Description: "{cm:LaunchVyxum}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  WebServiceName = '{#WebService}';
  AgentServiceName = '{#AgentService}';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdGuid}_is1';

function RunSc(const Params: String): Integer;
var
  ResultCode: Integer;
begin
  if Exec(ExpandConstant('{sys}\sc.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode
  else
    Result := -1;
end;

function ServiceExists(const Name: String): Boolean;
begin
  Result := RunSc('query "' + Name + '"') = 0;
end;

function IsServiceStopped(const Name: String): Boolean;
var
  TmpFile: String;
  Output: AnsiString;
  ResultCode: Integer;
begin
  Result := True;
  TmpFile := ExpandConstant('{tmp}\vyxum-sc.txt');
  DeleteFile(TmpFile);
  if Exec(ExpandConstant('{cmd}'), '/C sc query "' + Name + '" > "' + TmpFile + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    if LoadStringFromFile(TmpFile, Output) then
      Result := (Pos('STOPPED', Output) > 0) or (Pos('1060', Output) > 0);
end;

{ Stops a service and waits until it has stopped (up to 30 seconds). A missing service counts as stopped. }
function StopServiceAndWait(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := True;
  if not ServiceExists(Name) then Exit;
  RunSc('stop "' + Name + '"');
  for I := 1 to 60 do
  begin
    if IsServiceStopped(Name) then Exit;
    Sleep(500);
  end;
  Result := IsServiceStopped(Name);
end;

procedure BackupDatabase(const OldVersion: String);
var
  DataDir, BackupDir, Stamp, Target, Ext: String;
  Exts: array[0..2] of String;
  I: Integer;
begin
  DataDir := ExpandConstant('{commonappdata}\Vyxum');
  if not FileExists(DataDir + '\vyxum.db') then Exit;
  BackupDir := DataDir + '\backups';
  ForceDirectories(BackupDir);
  Stamp := GetDateTimeString('yyyymmdd-hhnnss', #0, #0);
  Exts[0] := '.db';
  Exts[1] := '.db-wal';
  Exts[2] := '.db-shm';
  for I := 0 to 2 do
  begin
    Ext := Exts[I];
    if FileExists(DataDir + '\vyxum' + Ext) then
    begin
      Target := BackupDir + '\vyxum-' + Stamp + '-before-setup-' + OldVersion + Ext;
      if not CopyFile(DataDir + '\vyxum' + Ext, Target, False) then
        Log('Backup of vyxum' + Ext + ' failed.');
    end;
  end;
end;

{ Runs before files are copied: stops the services of an installed version and backs up the database. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  OldVersion: String;
begin
  Result := '';
  if not StopServiceAndWait(WebServiceName) or not StopServiceAndWait(AgentServiceName) then
  begin
    Result := ExpandConstant('{cm:ServicesStopFailed}');
    Exit;
  end;
  if not RegQueryStringValue(HKLM, UninstallKey, 'DisplayVersion', OldVersion) then
    OldVersion := 'first-install';
  BackupDatabase(OldVersion);
end;

procedure ConfigureService(const Name, Exe, Description: String);
var
  Quoted: String;
begin
  Quoted := '"\"' + Exe + '\""';
  if ServiceExists(Name) then
    { Already installed: only refresh the binary path, keep everything else. }
    RunSc('config "' + Name + '" binPath= ' + Quoted + ' start= auto')
  else if RunSc('create "' + Name + '" binPath= ' + Quoted + ' start= auto DisplayName= "' + Name + '"') <> 0 then
  begin
    MsgBox(FmtMessage(CustomMessage('ServiceFailed'), [Name]), mbError, MB_OK);
    Exit;
  end;
  RunSc('description "' + Name + '" "' + Description + '"');
  { Restart after 5 s, three times, counter resets after a day. The failure flag makes Windows apply
    this also when the process exits with a non-zero code on its own (the Web host does that on purpose
    to restart itself after the database was changed in the setup wizard). }
  RunSc('failure "' + Name + '" reset= 86400 actions= restart/5000/restart/5000/restart/5000');
  RunSc('failureflag "' + Name + '" 1');
end;

{ Gives the installation its own agent key in vyxum.json (web and agent) unless it has one. Without a key, any
  program on this computer, a web page in the browser included, could report weights. An existing key is kept. }
procedure EnsureAgentKey;
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{app}\Web\Vyxum.Host.exe'), '--ensure-agent-key', ExpandConstant('{app}\Web'), SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Log('Writing the agent key failed with code ' + IntToStr(ResultCode) + '.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    EnsureAgentKey;
    ConfigureService(WebServiceName, ExpandConstant('{app}\Web\Vyxum.Host.exe'), ExpandConstant('{cm:ServiceWebDescription}'));
    ConfigureService(AgentServiceName, ExpandConstant('{app}\Agent\Vyxum.Agent.exe'), ExpandConstant('{cm:ServiceAgentDescription}'));
    RunSc('start "' + WebServiceName + '"');
    RunSc('start "' + AgentServiceName + '"');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Services first, so no file is in use while the uninstaller removes the program folder. }
    StopServiceAndWait(AgentServiceName);
    StopServiceAndWait(WebServiceName);
    RunSc('delete "' + AgentServiceName + '"');
    RunSc('delete "' + WebServiceName + '"');
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{commonappdata}\Vyxum');
    if DirExists(DataDir) then
      { Default button is "No": silent uninstalls and a stray Enter keep the data. }
      if SuppressibleMsgBox(FmtMessage(CustomMessage('DeleteDataQuestion'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
