Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!ifndef APP_DIR
  !define APP_DIR "..\artifacts\windows"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "..\artifacts\MicDenoiser-Setup-x64.exe"
!endif
!ifndef UNINSTALL_MANIFEST
  !define UNINSTALL_MANIFEST "..\artifacts\uninstall-files.nsh"
!endif
Name "MicDenoiser 2.4"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\MicDenoiser"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
VIProductVersion "2.4.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "MicDenoiser"
VIAddVersionKey /LANG=1033 "FileDescription" "MicDenoiser Windows x64 Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "2.4.0"
VIAddVersionKey /LANG=1033 "LegalCopyright" "MicDenoiser contributors; see bundled component licenses"
!define MUI_ABORTWARNING
!define MUI_ICON "..\MicDenoiser\Assets\MicDenoiser.ico"
!define MUI_UNICON "..\MicDenoiser\Assets\MicDenoiser.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\MicDenoiser.exe"
!define MUI_LANGDLL_REGISTRY_ROOT "HKCU"
!define MUI_LANGDLL_REGISTRY_KEY "Software\MicDenoiser"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "$(FinishInfo)"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Arabic"
!insertmacro MUI_RESERVEFILE_LANGDLL
LangString AppSection ${LANG_ENGLISH} "MicDenoiser application (required)"
LangString AppSection ${LANG_ARABIC} "برنامج MicDenoiser (مطلوب)"
LangString DesktopSection ${LANG_ENGLISH} "Desktop shortcut"
LangString DesktopSection ${LANG_ARABIC} "اختصار على سطح المكتب"
LangString Requirements ${LANG_ENGLISH} "MicDenoiser requires Windows 10/11 x64."
LangString Requirements ${LANG_ARABIC} "يتطلب MicDenoiser ويندوز 10 أو 11 بنظام x64."
LangString CloseApp ${LANG_ENGLISH} "Close MicDenoiser before installing or removing it."
LangString CloseApp ${LANG_ARABIC} "أغلق MicDenoiser قبل تثبيته أو إزالته."
LangString FinishInfo ${LANG_ENGLISH} "MicDenoiser is installed, including the .NET runtime, models and audio libraries. To use it in calling apps, install VB-Cable separately from https://vb-audio.com/Cable/ and select CABLE Output as the microphone."
LangString FinishInfo ${LANG_ARABIC} "تم تثبيت MicDenoiser مع ملفات .NET والنموذج ومكتبات الصوت. لاستخدامه في برامج المكالمات، ثبّت VB-Cable بشكل منفصل من https://vb-audio.com/Cable/ واختار CABLE Output كمايك."

Function .onInit
  SetShellVarContext current
  SetRegView 64
  !insertmacro MUI_LANGDLL_DISPLAY
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "$(Requirements)"
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "$(Requirements)"
    Abort
  ${EndIf}
  FindWindow $0 "" "MicDenoiser 2.0"
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.1"
  ${EndIf}
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.2"
  ${EndIf}
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.3"
  ${EndIf}
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.4"
  ${EndIf}
  ${If} $0 != 0
    MessageBox MB_OK|MB_ICONEXCLAMATION "$(CloseApp)"
    Abort
  ${EndIf}
FunctionEnd
Section "$(AppSection)" SEC_APP
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /r "${APP_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfFileExists "$APPDATA\MicDenoiser\settings.json" settings_present
    CreateDirectory "$APPDATA\MicDenoiser"
    FileOpen $1 "$APPDATA\MicDenoiser\settings.json" w
    ${If} $LANGUAGE == ${LANG_ARABIC}
      FileWrite $1 '{"Language":"ar"}'
    ${Else}
      FileWrite $1 '{"Language":"en"}'
    ${EndIf}
    FileClose $1
  settings_present:
  CreateDirectory "$SMPROGRAMS\MicDenoiser"
  CreateShortcut "$SMPROGRAMS\MicDenoiser\MicDenoiser.lnk" "$INSTDIR\MicDenoiser.exe"
  CreateShortcut "$SMPROGRAMS\MicDenoiser\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "DisplayName" "MicDenoiser 2.4"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "DisplayVersion" "2.4.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "DisplayIcon" "$INSTDIR\MicDenoiser.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "NoRepair" 1
SectionEnd
Section /o "$(DesktopSection)" SEC_DESKTOP
  CreateShortcut "$DESKTOP\MicDenoiser.lnk" "$INSTDIR\MicDenoiser.exe"
SectionEnd
Function un.onInit
  SetShellVarContext current
  SetRegView 64
  !insertmacro MUI_UNGETLANGUAGE
  FindWindow $0 "" "MicDenoiser 2.0"
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.1"
  ${EndIf}
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.2"
  ${EndIf}
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.3"
  ${EndIf}
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.4"
  ${EndIf}
  ${If} $0 != 0
    MessageBox MB_OK|MB_ICONEXCLAMATION "$(CloseApp)"
    Abort
  ${EndIf}
FunctionEnd
Section "Uninstall"
  !include "${UNINSTALL_MANIFEST}"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$DESKTOP\MicDenoiser.lnk"
  Delete "$SMPROGRAMS\MicDenoiser\MicDenoiser.lnk"
  Delete "$SMPROGRAMS\MicDenoiser\Uninstall.lnk"
  RMDir "$SMPROGRAMS\MicDenoiser"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MicDenoiser"
  DeleteRegKey HKCU "Software\MicDenoiser"
  RMDir "$INSTDIR"
  ; User settings in AppData are deliberately preserved. No recursive user-folder deletion.
SectionEnd
