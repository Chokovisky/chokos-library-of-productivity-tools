; ==============================================================================
; ARQUIVO: Lib\TaskbarManager.ahk
; DESCRIÇÃO: AutoHide Inteligente com "Smooth Reload" (CORRIGIDO)
; ==============================================================================

class TaskbarManager {
    static CHECK_INTERVAL := 100 ; Intervalo de checagem em ms
    static MOUSE_THRESHOLD := 2 ; Pixels de tolerancia do bottom edge para ativar a taskbar
    static IsActive := false ; Estado do gerenciador
    static BarIsVisible := true ; Estado de visibilidade da taskbar
    static hMainBar := 0 ; Handle da taskbar principal
    
    ; ============================================================
    ; 1. CONTROLE
    ; ============================================================
    static Toggle() { ; Função para alternar o estado do TaskbarManager
        if (this.IsActive) {
            this.Stop()
            ToolTip("Taskbar: PADRÃO")
        } else {
            this.Start()
            ToolTip("Taskbar: CUSTOM")
        }
        SetTimer () => ToolTip(), -1500
    }

    static Start() {
        if (this.IsActive)
            return

        OnExit(ObjBindMethod(this, "HandleExit"))
        
        DetectHiddenWindows(true)
        this.hMainBar := WinExist("ahk_class Shell_TrayWnd")
        if !this.hMainBar {
            WinWait("ahk_class Shell_TrayWnd",, 2)
            this.hMainBar := WinExist("ahk_class Shell_TrayWnd")
        }

        ; Detecção de estado anterior
        try {
            Style := WinGetStyle(this.hMainBar)
            IsVis := (Style & 0x10000000)
            
            if (!IsVis) {
                this.BarIsVisible := false
                this.IsActive := true
                this.SetNativeAutoHide(true)
            } 
            else {
                this.SetNativeAutoHide(true)
                Sleep(100)
                this.HideVisual()
                this.BarIsVisible := false
                this.IsActive := true
            }
        } catch {
            ; Fallback se falhar
            this.SetNativeAutoHide(true)
            Sleep(100)
            this.HideVisual()
            this.BarIsVisible := false
            this.IsActive := true
        }
        
        SetTimer () => this.MonitorLoop(), this.CHECK_INTERVAL
    }

    static Stop() {
        SetTimer () => this.MonitorLoop(), 0
        this.RestoreDefaults()
        this.IsActive := false
    }

    static HandleExit(ExitReason, ExitCode) {
        if (ExitReason = "Reload")
            return
        this.RestoreDefaults()
    }

    ; ============================================================
    ; 2. LOOP DE MONITORAMENTO
    ; ============================================================
    static MonitorLoop() {
        if (!this.IsActive)
            return

        CoordMode "Mouse", "Screen"
        MouseGetPos(&mX, &mY)
        MonitorGet(1, &L, &T, &R, &B)
        
        mouseAtBottom := (mY >= B - this.MOUSE_THRESHOLD)
        
        mouseOver := false
        if (this.BarIsVisible && this.hMainBar) {
            try {
                if WinExist(this.hMainBar) {
                    WinGetPos(&tbX, &tbY, &tbW, &tbH, this.hMainBar)
                    if (mX >= tbX && mX <= tbX + tbW && mY >= tbY && mY <= tbY + tbH)
                        mouseOver := true
                }
            }
        }

        startOpen := WinActive("ahk_class Windows.UI.Core.CoreWindow") 
                  || GetKeyState("LWin", "P") 
                  || GetKeyState("RWin", "P")

        shouldShow := mouseAtBottom || mouseOver || startOpen

        if (shouldShow && !this.BarIsVisible) {
            this.ShowVisual()
            this.BarIsVisible := true
        } 
        else if (!shouldShow && this.BarIsVisible) {
            if (mY < B - 50) {
                this.HideVisual()
                this.BarIsVisible := false
            }
        }
    }

    ; ============================================================
    ; 3. VISUALIZAÇÃO (CORRIGIDA)
    ; ============================================================
    static HideVisual() {
        DetectHiddenWindows(false)
        
        if (this.hMainBar && WinExist(this.hMainBar)) {
            try WinHide(this.hMainBar)
        }
        
        try {
            ids := WinGetList("ahk_class Shell_SecondaryTrayWnd")
            for id in ids {
                if WinExist(id)
                    WinHide(id)
            }
        }
    }

    static ShowVisual() {
        DetectHiddenWindows(true)
        
        ; CORRIGIDO: Usa handle + verificação + delay
        if (this.hMainBar && WinExist(this.hMainBar)) {
            try {
                WinShow(this.hMainBar)
                Sleep(10)  ; ← Delay para garantir que Show terminou
                if WinExist(this.hMainBar)
                    WinSetAlwaysOnTop(1, this.hMainBar)
            }
        }
        
        ; Barras secundárias
        try {
            ids := WinGetList("ahk_class Shell_SecondaryTrayWnd")
            for id in ids {
                if WinExist(id) {
                    try {
                        WinShow(id)
                        Sleep(10)
                        if WinExist(id)
                            WinSetAlwaysOnTop(1, id)
                    }
                }
            }
        }
        
        DetectHiddenWindows(false)
    }

    static SetNativeAutoHide(enable) {
        if !this.hMainBar
            return
            
        state := enable ? 1 : 2
        APPBARDATA := Buffer(48, 0)
        
        try {
            if WinExist(this.hMainBar) {
                NumPut("UInt", 48, APPBARDATA, 0)
                NumPut("Ptr", this.hMainBar, APPBARDATA, 8)
                offsetState := (A_PtrSize == 8) ? 40 : 32
                NumPut("UInt", state, APPBARDATA, offsetState)
                DllCall("Shell32\SHAppBarMessage", "UInt", 10, "Ptr", APPBARDATA)
            }
        }
    }

    static RestoreDefaults() {
        this.ShowVisual()
        Sleep(50)
        this.SetNativeAutoHide(false)
        
        ; REMOVE AlwaysOnTop ao restaurar
        if (this.hMainBar && WinExist(this.hMainBar)) {
            try WinSetAlwaysOnTop(0, this.hMainBar)
        }
        
        try {
            ids := WinGetList("ahk_class Shell_SecondaryTrayWnd")
            for id in ids {
                if WinExist(id)
                    WinSetAlwaysOnTop(0, id)
            }
        }
    }
}
; ==============================================================================
; FIM DO ARQUIVO: Lib\TaskbarManager.ahk
; ==============================================================================