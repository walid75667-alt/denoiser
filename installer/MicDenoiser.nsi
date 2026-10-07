Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "nsDialogs.nsh"
Var PurposeDialog
Var CallsRadio
Var StudioRadio
Var UsagePurpose
Var InitialPreset
Var InitialSimple
!ifndef APP_DIR
  !define APP_DIR "..\artifacts\windows"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "..\artifacts\MicDenoiser-Setup-x64.exe"
!endif
!ifndef UNINSTALL_MANIFEST
  !define UNINSTALL_MANIFEST "..\artifacts\uninstall-files.nsh"
!endif
Name "MicDenoiser 2.6"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\MicDenoiser"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
VIProductVersion "2.6.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "MicDenoiser"
VIAddVersionKey /LANG=1033 "FileDescription" "MicDenoiser Windows x64 Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "2.6.0"
VIAddVersionKey /LANG=1033 "LegalCopyright" "MicDenoiser contributors; see bundled component licenses"
!define MUI_ABORTWARNING
!define MUI_ICON "..\MicDenoiser\Assets\MicDenoiser.ico"
!define MUI_UNICON "..\MicDenoiser\Assets\MicDenoiser.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\MicDenoiser.exe"
!define MUI_LANGDLL_REGISTRY_ROOT "HKCU"
!define MUI_LANGDLL_REGISTRY_KEY "Software\MicDenoiser"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!insertmacro MUI_PAGE_WELCOME
Page custom PurposePage PurposeLeave
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
LangString FinishInfo ${LANG_ARABIC} "تم تثبيت MicDenoiser مع ملفات .NET والنموذج ومكتبات الصوت. لاستخدامه في برامج المكالمات، ثبّت VB-Cable بشكل منفصل من https://vb-audio.com/Cable/ واختر CABLE Output كميكروفون."

LangString PurposeTitle ${LANG_ENGLISH} "Choose your purpose"
LangString PurposeTitle ${LANG_ARABIC} "اختر الغرض"
LangString PurposeHint ${LANG_ENGLISH} "This selects the starting workspace and presets for a new installation. Existing settings are preserved."
LangString PurposeHint ${LANG_ARABIC} "يحدد مساحة العمل والإعدادات الأولية للتثبيت الجديد. تُحفظ الإعدادات الحالية."
LangString CallsLabel ${LANG_ENGLISH} "Calls and streaming"
LangString CallsLabel ${LANG_ARABIC} "مكالمات وبث"
LangString StudioLabel ${LANG_ENGLISH} "Singing and narration"
LangString StudioLabel ${LANG_ARABIC} "غناء وتعليق صوتي"
Function PurposePage
  !insertmacro MUI_HEADER_TEXT "$(PurposeTitle)" "$(PurposeHint)"
  nsDialogs::Create 1018
  Pop $PurposeDialog
  ${If} $PurposeDialog == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 44u "$(PurposeHint)"
  Pop $0
  ${NSD_CreateRadioButton} 0 60u 100% 20u "$(CallsLabel)"
  Pop $CallsRadio
  ${NSD_CreateRadioButton} 0 90u 100% 20u "$(StudioLabel)"
  Pop $StudioRadio
  ${If} $UsagePurpose == "studio"
    ${NSD_Check} $StudioRadio
  ${Else}
    ${NSD_Check} $CallsRadio
  ${EndIf}
  nsDialogs::Show
FunctionEnd
Function PurposeLeave
  ${NSD_GetState} $StudioRadio $0
  ${If} $0 == ${BST_CHECKED}
    StrCpy $UsagePurpose "studio"
    StrCpy $InitialPreset "voiceover"
    StrCpy $InitialSimple "false"
  ${Else}
    StrCpy $UsagePurpose "calls"
    StrCpy $InitialPreset "calls"
    StrCpy $InitialSimple "true"
  ${EndIf}
FunctionEnd
Function .onInit
  StrCpy $UsagePurpose "calls"
  StrCpy $InitialPreset "calls"
  StrCpy $InitialSimple "true"
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
  FindWindow $0 "" "MicDenoiser"
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.5"
  ${EndIf}
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.0"
  ${EndIf}
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
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.6"
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
      FileWrite $1 '{"Language":"ar","UsagePurpose":"$UsagePurpose","Preset":"$InitialPreset","SimpleMode":$InitialSimple}'
    ${Else}
      FileWrite $1 '{"Language":"en","UsagePurpose":"$UsagePurpose","Preset":"$InitialPreset","SimpleMode":$InitialSimple}'
    ${EndIf}
    FileClose $1
  settings_present:
  CreateDirectory "$SMPROGRAMS\MicDenoiser"
  CreateShortcut "$SMPROGRAMS\MicDenoiser\MicDenoiser.lnk" "$INSTDIR\MicDenoiser.exe"
  CreateShortcut "$SMPROGRAMS\MicDenoiser\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "DisplayName" "MicDenoiser 2.6"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicDenoiser" "DisplayVersion" "2.6.0"
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
  FindWindow $0 "" "MicDenoiser"
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.5"
  ${EndIf}
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.0"
  ${EndIf}
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
  ${If} $0 == 0
    FindWindow $0 "" "MicDenoiser 2.6"
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
