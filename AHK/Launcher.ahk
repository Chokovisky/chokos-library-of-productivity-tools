; ==============================================================================
; ARQUIVO: Launcher.ahk
; DESCRIÇÃO: Script Principal - Gerenciamento de Bibliotecas e Inicialização
; ==============================================================================

; #region Habilitações Básicas
#Requires AutoHotkey v2.0 
#SingleInstance Force
#UseHook true 
Persistent
SetWorkingDir A_ScriptDir
; #endregion

; ==============================================================================
; ORDEM DE CARREGAMENTO (CRÍTICO!)
; ==============================================================================
; 1. Jxon (parser JSON) - PRECISA SER PRIMEIRO
; 2. Settings (carrega JSONs UMA VEZ, define globals)
; 3. Core (usa globals do Settings)
; 4. Demais libs (usam globals)
; 5. Keybindings (registra hotkeys do JSON)
; ==============================================================================

; 1. Parser JSON (dependência de tudo)
#Include "Lib\Jxon.ahk"

; 2. Configurações Globais (carrega JSONs UMA VEZ)
#Include "Config\Settings.ahk"

; 3. Core do Sistema
#Include "Lib\Core.ahk"

; 4. Bibliotecas de Sistema
#Include "Lib\DelegateWorker.ahk"
#Include "Lib\FileUtils.ahk"
#Include "Lib\OfficeAutomation.ahk"
#Include "Lib\TaskbarBetterAutoHider.ahk"

; 5. Bibliotecas de Ação
#Include "Lib\AppActions.ahk"
#Include "Lib\WindowActions.ahk"
#Include "Lib\ScreenLock.ahk"
#Include "Lib\NotionUtils.ahk"
#Include "Lib\TerminalUtils.ahk"
#Include "Lib\DashboardUtils.ahk"

; 6. UI
#Include "Lib\HKlistUI.ahk"
#Include "Lib\RadialMenuUtils.ahk"

; 7. Keybindings (registra hotkeys do HotkeyData global)
#Include "Config\Keybindings.ahk"

; ==============================================================================
; AUTO-START
; ==============================================================================

; Inicializa Core (aplica config do CapsLock)
Core_Initialize()
 
; Inicia TaskbarManager
TaskbarManager.Start()
 
; Pré-aquece HKCheatsheetOverlay em background após um pequeno delay
SetTimer(() => HKCheatsheetOverlayUtils.Warmup(), -5000)

; Pré-aquece RadialMenu em background para evitar coldstart
SetTimer(() => Run(A_ScriptDir . "\Tools\RadialMenu\RadialMenu.exe --background",, "Hide"), -6000)

; Hotkey de desenvolvimento/debug
^F5::Reload  ; Ctrl + F5 para recarregar

; ==============================================================================
; FIM DO ARQUIVO: Launcher.ahk
; ==============================================================================