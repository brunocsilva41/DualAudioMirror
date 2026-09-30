#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif

#ifndef ArtifactsDir
  #define ArtifactsDir "..\artifacts"
#endif

#if FileExists(CompilerPath + "Languages\BrazilianPortuguese.isl")
  #define PtBrMessages "compiler:Languages\BrazilianPortuguese.isl"
#else
  #define PtBrMessages "compiler:Default.isl"
#endif

[Setup]
AppId={{A7C3D9F1-4B2E-4E7A-9C11-DAM20260001}}
AppName=DualAudioMirror
AppVersion={#MyAppVersion}
AppVerName=DualAudioMirror {#MyAppVersion}
AppPublisher=Bruno Silva
AppPublisherURL=https://github.com/brunocsilva41/DualAudioMirror
AppSupportURL=https://github.com/brunocsilva41/DualAudioMirror
AppUpdatesURL=https://github.com/brunocsilva41/DualAudioMirror
VersionInfoProductVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName=DualAudioMirror
VersionInfoDescription=Instalador do DualAudioMirror
DefaultDirName={autopf}\DualAudioMirror
DefaultGroupName=DualAudioMirror
DisableProgramGroupPage=yes
DisableReadyMemo=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
WizardImageFile=..\assets\wizard-image.bmp
WizardSmallImageFile=..\assets\wizard-small.bmp
SetupIconFile=..\assets\icon.ico
LicenseFile=..\LICENSE
InfoBeforeFile=..\docs\installer-intro.txt
UninstallDisplayIcon={app}\DualAudioMirror.exe
CloseApplications=yes
RestartApplications=no
Compression=lzma2
SolidCompression=yes
OutputDir={#ArtifactsDir}
OutputBaseFilename=DualAudioMirror-Setup-{#MyAppVersion}

[Languages]
Name: "portuguesebr"; MessagesFile: "{#PtBrMessages}"

[Types]
Name: "custom"; Description: "Instalação personalizada"; Flags: iscustom

[Components]
Name: "app"; Description: "Programa DualAudioMirror"; Types: custom; Flags: fixed

[CustomMessages]
TaskGroupShortcuts=Atalhos:
TaskDesktopIcon=Criar atalho na &Área de Trabalho
TaskStartMenu=Criar atalho no &Menu Iniciar
TaskGroupStartup=Inicialização com o Windows:
TaskStartup=Iniciar o DualAudioMirror quando o Windows &iniciar
TaskGroupFinish=Ao concluir:
TaskRunApp=Executar o DualAudioMirror ao &final da instalação
UninstallProgram=Desinstalar o %1
LaunchProgram=Iniciar o %1
CreateDesktopIcon=Criar um atalho na &Área de Trabalho

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:TaskGroupShortcuts}"
Name: "startmenu"; Description: "{cm:TaskStartMenu}"; GroupDescription: "{cm:TaskGroupShortcuts}"
Name: "startup"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:TaskGroupStartup}"; Flags: unchecked
Name: "runapp"; Description: "{cm:TaskRunApp}"; GroupDescription: "{cm:TaskGroupFinish}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\DualAudioMirror"; Filename: "{app}\DualAudioMirror.exe"; WorkingDir: "{app}"; IconIndex: 0; Comment: "DualAudioMirror"; Tasks: "startmenu"
Name: "{autodesktop}\DualAudioMirror"; Filename: "{app}\DualAudioMirror.exe"; WorkingDir: "{app}"; IconIndex: 0; Comment: "DualAudioMirror"; Tasks: "desktopicon"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DualAudioMirror"; ValueData: """{app}\DualAudioMirror.exe"""; Flags: uninsdeletevalue; Tasks: "startup"

[Run]
Filename: "{app}\DualAudioMirror.exe"; Description: "{cm:LaunchProgram,DualAudioMirror}"; Flags: nowait postinstall skipifsilent; Tasks: "runapp"

[Code]
const
  NL = #13#10;
  VbCablePageUrl = 'https://vb-audio.com/Cable/';
  VbCableDownloadUrl = 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip';

var
  DepsPage: TInputOptionWizardPage;
  DepsStatusLabel: TLabel;
  VbCableDetected: Boolean;
  VbCableInstalled: Boolean;

function URLDownloadToFile(caller: Cardinal; url: string; fileName: string; reserved: Cardinal; callback: Cardinal): Cardinal;
  external 'URLDownloadToFileW@urlmon.dll stdcall';

function DetectVBCableFiles: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_win10.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_win7.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_vista.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_2003.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64arm_win10.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable_win7.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable_xp.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable_2003.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbc_wdm.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbcable64.sys'));
  if (not Result) and IsWin64 then
    Result := DirExists(ExpandConstant('{commonpf64}\VB\CABLE'));
end;

function DetectVBCablePnp: Boolean;
var
  OutFile: String;
  Cmd: String;
  Lines: TArrayOfString;
  RC: Integer;
begin
  Result := False;
  OutFile := ExpandConstant('{tmp}\dam-vbcable-pnp.txt');
  if FileExists(OutFile) then
    DeleteFile(OutFile);
  Cmd := '-NoProfile -NonInteractive -Command "$c=@(Get-PnpDevice | Where-Object { $_.FriendlyName -like ''*CABLE*'' -and $_.Status -eq ''OK'' }).Count; if($c -gt 0){Set-Content -Path ''' + OutFile + ''' -Value ''1'' -Encoding ASCII}else{Set-Content -Path ''' + OutFile + ''' -Value ''0'' -Encoding ASCII}"';
  if Exec('powershell.exe', Cmd, '', SW_HIDE, ewWaitUntilTerminated, RC) then
  begin
    if RC = 0 then
    begin
      if LoadStringsFromFile(OutFile, Lines) then
      begin
        if GetArrayLength(Lines) > 0 then
          Result := Trim(Lines[0]) = '1';
      end;
    end;
  end;
end;

function DetectVBCable: Boolean;
begin
  Result := DetectVBCableFiles;
  if not Result then
    Result := DetectVBCablePnp;
end;

function FindVbSetup(const Dir: String): String;
var
  FindRec: TFindRec;
begin
  Result := '';
  if FileExists(Dir + '\VBCABLE_Setup_x64.exe') then
  begin
    Result := Dir + '\VBCABLE_Setup_x64.exe';
    exit;
  end;
  if FindFirst(Dir + '\*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
          (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Result := FindVbSetup(Dir + '\' + FindRec.Name);
          if Result <> '' then
            exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure OpenVbCableSite;
var
  ErrCode: Integer;
begin
  ShellExec('open', VbCablePageUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
end;

procedure InstallVbCable;
var
  ZipFile: String;
  ExtractDir: String;
  SetupFile: String;
  RC: Integer;
  ErrCode: Integer;
  Started: Boolean;
  Extracted: Boolean;
begin
  ZipFile := ExpandConstant('{tmp}\vbcable.zip');
  ExtractDir := ExpandConstant('{tmp}\vbcable');

  if URLDownloadToFile(0, VbCableDownloadUrl, ZipFile, 0, 0) <> 0 then
  begin
    if MsgBox('Não foi possível baixar o pacote do VB-Cable automaticamente.' + NL + NL +
      'Abrir a página oficial para você baixar manualmente?', mbConfirmation, MB_YESNO) = IDYES then
      OpenVbCableSite;
    exit;
  end;

  if DirExists(ExtractDir) then
    DelTree(ExtractDir, True, True, True);

  Extracted := Exec('powershell.exe',
    '-NoProfile -NonInteractive -Command "Expand-Archive -Force -Path ''' + ZipFile +
    ''' -DestinationPath ''' + ExtractDir + '''"', '', SW_HIDE, ewWaitUntilTerminated, RC);
  if (not Extracted) or (RC <> 0) then
  begin
    if MsgBox('O pacote do VB-Cable foi baixado, mas não pôde ser extraído.' + NL + NL +
      'Abrir a página oficial para você instalar manualmente?', mbConfirmation, MB_YESNO) = IDYES then
      OpenVbCableSite;
    exit;
  end;

  SetupFile := FindVbSetup(ExtractDir);
  if SetupFile = '' then
  begin
    if MsgBox('O instalador do VB-Cable não foi encontrado dentro do pacote baixado.' + NL + NL +
      'Abrir a página oficial para você instalar manualmente?', mbConfirmation, MB_YESNO) = IDYES then
      OpenVbCableSite;
    exit;
  end;

  Started := ShellExec('runas', SetupFile, '-i -h', ExtractDir, SW_SHOWNORMAL, ewWaitUntilTerminated, ErrCode);
  if not Started then
  begin
    if MsgBox('Não foi possível iniciar o instalador do VB-Cable.' + NL + NL +
      'Abrir a página oficial para você instalar manualmente?', mbConfirmation, MB_YESNO) = IDYES then
      OpenVbCableSite;
    exit;
  end;

  VbCableInstalled := DetectVBCable;
  if VbCableInstalled then
  begin
    if MsgBox('O driver do VB-Cable foi instalado.' + NL + NL +
      'É necessário reiniciar o computador para que o cabo virtual fique disponível.' + NL + NL +
      'Deseja reiniciar agora?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', ExpandConstant('{sys}\shutdown.exe'), '/r /t 10', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
  end
  else
    MsgBox('O instalador do VB-Cable terminou, mas o cabo virtual ainda não foi encontrado.' + NL + NL +
      'Reinicie o computador e, se o modo sincronizado continuar indisponível, instale o VB-Cable em:' +
      NL + VbCablePageUrl, mbInformation, MB_OK);
end;

procedure RefreshDepsStatus;
begin
  if DepsStatusLabel = nil then
    exit;
  if VbCableDetected then
  begin
    DepsStatusLabel.Caption := 'Status: VB-Cable detectado';
    DepsStatusLabel.Font.Color := clGreen;
  end
  else
  begin
    DepsStatusLabel.Caption := 'Status: VB-Cable NÃO encontrado';
    DepsStatusLabel.Font.Color := clRed;
  end;
end;

procedure InitializeWizard;
begin
  VbCableDetected := False;
  VbCableInstalled := False;
  if not WizardSilent then
    VbCableDetected := DetectVBCable;

  DepsPage := CreateInputOptionPage(wpInfoBefore,
    'Dependências — VB-Cable',
    'O modo sincronizado usa o VB-Cable como fonte de áudio virtual.',
    'No modo sincronizado o áudio passa pelo cabo virtual VB-Cable e todos os aparelhos marcam juntos, sem diferença de tempo. Confira se o cabo virtual já está instalado neste computador.',
    True, False);
  DepsPage.Add('Baixar e instalar o VB-Cable agora');
  DepsPage.Add('Eu mesmo baixo depois — abrir o site oficial');

  DepsStatusLabel := TLabel.Create(DepsPage);
  DepsStatusLabel.Parent := DepsPage.Surface;
  DepsStatusLabel.Left := DepsPage.CheckListBox.Left;
  DepsStatusLabel.Top := DepsPage.CheckListBox.Top + DepsPage.CheckListBox.Height + ScaleY(14);
  DepsStatusLabel.AutoSize := True;
  DepsStatusLabel.Font.Style := [fsBold];

  RefreshDepsStatus;
  if VbCableDetected then
    DepsPage.SelectedValueIndex := 1
  else
    DepsPage.SelectedValueIndex := 0;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Txt: String;
begin
  if DepsPage <> nil then
    if CurPageID = DepsPage.ID then
      RefreshDepsStatus;

  if CurPageID = wpFinished then
  begin
    Txt := 'Instalação concluída.' + NL + NL +
      'Configurações e logs: ' + ExpandConstant('{localappdata}\DualAudioMirror') + NL +
      'Site e documentação: https://github.com/brunocsilva41/DualAudioMirror';
    if VbCableInstalled then
      Txt := Txt + NL + NL +
        'O VB-Cable foi instalado: reinicie o computador para usar o modo sincronizado.'
    else if not VbCableDetected then
      Txt := Txt + NL + NL +
        'Lembrete: instale o VB-Cable (https://vb-audio.com/Cable/) para liberar o modo sincronizado.';
    WizardForm.FinishedLabel.Caption := Txt;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if (not WizardSilent) and (not VbCableDetected) then
      if DepsPage <> nil then
      begin
        if DepsPage.Values[0] then
          InstallVbCable
        else if DepsPage.Values[1] then
          OpenVbCableSite;
      end;
  end;
end;
