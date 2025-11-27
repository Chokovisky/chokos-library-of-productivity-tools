; ==============================================================================
; ARQUIVO: Lib\DashboardUtils.ahk
; DESCRIÇÃO: Utilitários para invocar o HKCheatsheetOverlay (C# WPF)
; ==============================================================================

; Classe nova principal para o overlay
class HKCheatsheetOverlayUtils {
    
    ; Caminho do executável do Overlay (mantido para futuro uso self-contained)
    static DashboardExe := A_ScriptDir . "\Tools\HKCheatsheetOverlay\HKCheatsheetOverlay.exe"
    
    ; Caminho do DLL (usado em dev via 'dotnet HKCheatsheetOverlay.dll ...')
    static DllPath := A_ScriptDir . "\..\CSharp\HKCheatsheetOverlay\bin\Release\net10.0-windows\win-x64\HKCheatsheetOverlay.dll"
    
    ; Título da janela do Overlay (deve bater com o Title do MainWindow.xaml C#)
    static WindowTitle := "ChokoLPT - HK Cheatsheet Overlay"

    ; Caminho de log (usa mesma pasta de logs do app principal)
    static LogPath := APP_DATA_PATH . "\logs\hkcheatsheet_overlay_ahk.log"

    ; Mensagem customizada usada pelo App.xaml.cs (WM_SHOW_OVERLAY)
    ; Deve bater com App.WM_SHOW_OVERLAY em C# (HKCheatsheetOverlay.App.WM_SHOW_OVERLAY)
    static WM_SHOW_OVERLAY := 0x8002
    
    ; ============================================================
    ; LOGGING
    ; ============================================================
    static Log(msg) {
        try {
            timestamp := FormatTime(, "yyyy-MM-dd HH:mm:ss.") . A_MSec
            FileAppend(timestamp . " | " . msg . "`n", this.LogPath)
        } catch {
            ; Não deixa logging quebrar o fluxo principal
        }
    }
    
    ; ============================================================
    ; TOGGLE PRINCIPAL
    ; ============================================================
    
    ;; Alterna visibilidade do Overlay
    static Toggle() {
        ; Novo fluxo de alta performance:
        ; - NÃO cria processo novo a cada toggle.
        ; - Envia WM_SHOW_OVERLAY direto para a janela residente do overlay.
        ; - Se a janela não existir (processo morreu), chama Warmup() para recriar
        ;   o processo em background e tenta enviar de novo.
        this.Log("Toggle() chamado - tentando enviar WM_SHOW_OVERLAY para overlay residente.")

        if this.SendToggleMessage() {
            return
        }

        this.Log("Toggle(): overlay não respondeu; tentando Warmup() para recriar processo.")
        this.Warmup()
        Sleep(150)

        if !this.SendToggleMessage() {
            this.Log("Toggle(): ainda não foi possível contatar overlay após Warmup().")
        }
    }

    ;; Envia WM_SHOW_OVERLAY para a janela do overlay (instância residente)
    static SendToggleMessage() {
        try {
            hwnd := WinExist(this.WindowTitle)
            if !hwnd {
                this.Log("SendToggleMessage(): janela não encontrado para '" . this.WindowTitle . "'.")
                return false
            }

            this.Log("SendToggleMessage(): hwnd encontrado=" . hwnd . " - enviando WM_SHOW_OVERLAY (wParam=0 para Toggle).")
            DllCall("PostMessage", "Ptr", hwnd, "UInt", this.WM_SHOW_OVERLAY, "Ptr", 0, "Ptr", 0)
            return true
        } catch as err {
            this.Log("SendToggleMessage(): EXCEÇÃO: " . err.Message)
            return false
        }
    }

    ;; Solicita ao overlay que faça CycleContext(direction) via WM_SHOW_OVERLAY
    ;; direction = +1 → próximo contexto (scroll para baixo)
    ;; direction = -1 → contexto anterior (scroll para cima)
    static CycleContext(direction) {
        this.Log("CycleContext(" . direction . ") chamado - tentando enviar WM_SHOW_OVERLAY com wParam=" . direction . ".")

        try {
            hwnd := WinExist(this.WindowTitle)
            if !hwnd {
                this.Log("CycleContext(): janela não encontrada para '" . this.WindowTitle . "'.")
                return
            }

            DllCall("PostMessage", "Ptr", hwnd, "UInt", this.WM_SHOW_OVERLAY, "Ptr", direction, "Ptr", 0)
        } catch as err {
            this.Log("CycleContext(): EXCEÇÃO: " . err.Message)
        }
    }
    
    ;; Abre o Overlay via EXE (mantido para cenários especiais / debug)
    static Show() {
        this.Log("Show() chamado.")
        this.Log("DashboardExe='" . this.DashboardExe . "'")
        
        ; Produção: rodar SEMPRE o WinExe publicado (WPF, sem console).
        if !FileExist(this.DashboardExe) {
            msg := "HKCheatsheetOverlay.exe não encontrado: " . this.DashboardExe
            this.Log("ERRO: " . msg)
            ToolTip("❌ " . msg)
            SetTimer(() => ToolTip(), -3000)
            return
        }
        
        ; Coleta argumentos
        args := this.BuildArgs()
        this.Log("Argumentos construídos: " . args)
        
        try {
            cmd := '"' . this.DashboardExe . '" ' . args
            this.Log("Executando Run() com EXE: " . cmd)
            ; HKCheatsheetOverlay.exe é WinExe WPF, não abre console.
            Run(cmd)
            this.Log("Run() retornou sem exceção (processo deve ter sido iniciado).")
        } catch as err {
            this.Log("EXCEÇÃO em Show(): " . err.Message)
            ToolTip("❌ Erro ao abrir HKCheatsheetOverlay: " . err.Message)
            SetTimer(() => ToolTip(), -3000)
        }
    }

    ;; Pré-aquece o Overlay em background no startup (primeira chamada rápida)
    static Warmup() {
        this.Log("Warmup() chamado.")
        this.Log("DashboardExe='" . this.DashboardExe . "'")

        if !FileExist(this.DashboardExe) {
            msg := "HKCheatsheetOverlay.exe não encontrado para Warmup: " . this.DashboardExe
            this.Log("ERRO: " . msg)
            return
        }

        ; Se já existe janela/processo, nada a fazer
        if WinExist(this.WindowTitle) {
            this.Log("Warmup(): janela já existe, ignorando.")
            return
        }

        try {
            cmd := '"' . this.DashboardExe . '" --background'
            this.Log("Warmup(): executando " . cmd)
            ; WinExe WPF, sem console. Inicia em modo escondido, só criando MainWindow.
            Run(cmd)
            this.Log("Warmup(): processo iniciado para pré-aquecer overlay.")
        } catch as err {
            this.Log("Warmup(): EXCEÇÃO ao iniciar overlay em background: " . err.Message)
        }
    }
    
    ; ============================================================
    ; HELPERS
    ; ============================================================
    
    ;; Constrói argumentos de linha de comando
    static BuildArgs() {
        args := ""
        
        ; Perfil ativo
        Global CurrentProfile
        if IsSet(CurrentProfile) && CurrentProfile != "" {
            args .= '--profile "' . CurrentProfile . '" '
            this.Log("BuildArgs(): CurrentProfile='" . CurrentProfile . "'")
        } else {
            this.Log("BuildArgs(): CurrentProfile não definido ou vazio")
        }
        
        ; Exe da janela ativa (para detectar contexto)
        try {
            activeExe := WinGetProcessName("A")
            if (activeExe != "") {
                args .= '--exe "' . activeExe . '" '
                this.Log("BuildArgs(): activeExe='" . activeExe . "'")
            } else {
                this.Log("BuildArgs(): activeExe vazio")
            }
        } catch as err {
            this.Log("BuildArgs(): EXCEÇÃO ao obter WinGetProcessName: " . err.Message)
        }
        
        ; Posição do mouse (para centralizar no monitor correto)
        try {
            CoordMode("Mouse", "Screen")
            MouseGetPos(&mx, &my)
            args .= '--x ' . mx . ' --y ' . my
            this.Log("BuildArgs(): Mouse pos x=" . mx . ", y=" . my)
        } catch as err {
            this.Log("BuildArgs(): EXCEÇÃO em MouseGetPos: " . err.Message)
        }
        
        return args
    }
    
    ;; Define caminho customizado do Overlay
    static SetPath(path) {
        this.Log("SetPath() chamado. Novo DashboardExe='" . path . "'")
        this.DashboardExe := path
    }
}

; --------------------------------------------------------------------------
; Compatibilidade: manter o nome antigo DashboardUtils para JSONs existentes.
; Qualquer "DashboardUtils.Toggle" no hotkeys.json continua funcionando.
; --------------------------------------------------------------------------
class DashboardUtils {
    static Toggle()    => HKCheatsheetOverlayUtils.Toggle()
    static Show()      => HKCheatsheetOverlayUtils.Show()
    static SetPath(p)  => HKCheatsheetOverlayUtils.SetPath(p)
}

; ==============================================================================
; FIM DO ARQUIVO: Lib\DashboardUtils.ahk
; ==============================================================================