; ==============================================================================
; ARQUIVO: Lib\TerminalUtils.ahk
; DESCRIÇÃO: Quake Terminal para Windows Terminal (AHK v2)
;            - Cria 1x a janela quake nativa via wt -w _quake
;            - Toggle depois é só WinHide/WinShow (não abre nova aba)
;            - AlwaysOnTop + Borderless hard apply
;            - Bleed mínimo para colar no topo (TopBleed=1)
; ==============================================================================

class TerminalUtils {

    ; =========================
    ; CONFIG
    ; =========================
    static TerminalExe   := "wt.exe"                       ; executável do terminal
    static QuakeName     := "_quake"                       ; nome da janela quake nativa
    static WT_ExeName    := "WindowsTerminal.exe"
    static WT_Class      := "CASCADIA_HOSTING_WINDOW_CLASS"

    static HeightFrac    := 0.50                           ; 40% da altura da tela
    static TopBleed      := 1                              ; cola no topo (1px funciona bem pra você)
    static SideBleed     := 0                              ; laterais sem bleed, ajuste se precisar
    static AlwaysOnTop   := true
    static Borderless    := true

    ; =========================
    ; ESTADO
    ; =========================
    static hwnd  := 0
    static shown := false
    static busy  := false

    ; =========================
    ; API PÚBLICA
    ; =========================
    static ToggleQuake() {
        if (this.busy)
            return
        this.busy := true

        SetWinDelay -1
        DetectHiddenWindows true

        ; 1) tenta usar hwnd salvo, senão acha
        if !(this.hwnd && WinExist("ahk_id " this.hwnd)) {
            this.hwnd := this.FindTerminalWindow()
            this.shown := false
        }

        ; 2) se não existe, cria UMA vez e já mostra certo
        if !this.hwnd {
            if !this.CreateQuakeOnce() {
                this.busy := false
                return
            }
            this.ShowQuake()
            this.shown := true
            this.busy := false
            return
        }

        ; 3) toggle instantâneo por estado interno
        if (this.shown) {
            this.HideQuake()
            this.shown := false
        } else {
            this.ShowQuake()
            this.shown := true
        }

        this.busy := false
    }

    ; Se você quiser ajustar config por fora:
    static SetHeightFrac(frac) => this.HeightFrac := frac
    static SetBleed(topBleed := 1, sideBleed := 0) {
        this.TopBleed := topBleed
        this.SideBleed := sideBleed
    }
    static SetBorderless(on := true) => this.Borderless := on
    static SetAlwaysOnTop(on := true) => this.AlwaysOnTop := on

    ; =========================
    ; CORE
    ; =========================
    static CreateQuakeOnce() {
        pre := WinGetList("ahk_class " this.WT_Class " ahk_exe " this.WT_ExeName)

        Run this.TerminalExe " -w " this.QuakeName
        if !WinWait("ahk_class " this.WT_Class " ahk_exe " this.WT_ExeName, , 3)
            return false

        post := WinGetList("ahk_class " this.WT_Class " ahk_exe " this.WT_ExeName)
        this.hwnd := this.PickNewWindow(pre, post)

        if !this.hwnd
            this.hwnd := this.FindTerminalWindow()

        return !!this.hwnd
    }

    static FindTerminalWindow() {
        list := WinGetList("ahk_class " this.WT_Class " ahk_exe " this.WT_ExeName)
        return list.Length ? list[1] : 0
    }

    static PickNewWindow(pre, post) {
        preSet := Map()
        for h in pre
            preSet[h] := true

        for h in post
            if !preSet.Has(h)
                return h

        return 0
    }

    static ShowQuake() {
        if !this.hwnd
            return

        WinShow    "ahk_id " this.hwnd
        WinRestore "ahk_id " this.hwnd

        if (this.Borderless)
            this.ForceBorderless(this.hwnd)

        this.PositionQuake(this.hwnd, this.HeightFrac, this.TopBleed, this.SideBleed)

        if (this.AlwaysOnTop)
            this.ForceTopMost(this.hwnd)

        WinActivate "ahk_id " this.hwnd
    }

    static HideQuake() {
        if !this.hwnd
            return
        WinHide "ahk_id " this.hwnd
    }

    static PositionQuake(hwnd, frac, topBleed, sideBleed) {
        ; pega área real do monitor primário
        MonitorGet(1, &L, &T, &R, &B)
        W := (R - L)
        H := (B - T)

        targetH := Round(H * frac)

        ; bleed negativo pra colar no topo/laterais
        x := L - sideBleed
        y := T - topBleed
        w := W + (2 * sideBleed)
        h := targetH + topBleed

        WinMove x, y, w, h, "ahk_id " hwnd
    }

    ; =========================
    ; BORDERLESS + TOPMOST
    ; =========================
    static ForceBorderless(hwnd) {
        ; estilos alvo (janela clássica)
        WS_CAPTION    := 0x00C00000
        WS_THICKFRAME := 0x00040000
        WS_BORDER     := 0x00800000
        WS_DLGFRAME   := 0x00400000
        WS_SYSMENU    := 0x00080000
        GWL_STYLE := -16

        style := DllCall("GetWindowLongPtr", "ptr", hwnd, "int", GWL_STYLE, "ptr")
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_BORDER | WS_DLGFRAME | WS_SYSMENU)
        DllCall("SetWindowLongPtr", "ptr", hwnd, "int", GWL_STYLE, "ptr", style)

        ; força o Windows a recalcular moldura
        SWP_NOMOVE := 0x0002
        SWP_NOSIZE := 0x0001
        SWP_NOZORDER := 0x0004
        SWP_FRAMECHANGED := 0x0020

        DllCall("SetWindowPos"
            , "ptr", hwnd
            , "ptr", 0
            , "int", 0, "int", 0, "int", 0, "int", 0
            , "uint", SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_FRAMECHANGED)
    }

    static ForceTopMost(hwnd) {
        WinSetAlwaysOnTop 1, "ahk_id " hwnd

        ; reforço via SetWindowPos
        HWND_TOPMOST := -1
        SWP_NOMOVE := 0x0002
        SWP_NOSIZE := 0x0001
        SWP_SHOWWINDOW := 0x0040

        DllCall("SetWindowPos"
            , "ptr", hwnd
            , "ptr", HWND_TOPMOST
            , "int", 0, "int", 0, "int", 0, "int", 0
            , "uint", SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW)
    }
}

; ==============================================================================
; Uso (exemplo):
;   #Include Lib\TerminalUtils.ahk
;   #`::TerminalUtils.ToggleQuake()
; ==============================================================================
