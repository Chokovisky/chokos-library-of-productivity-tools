; ==============================================================================
; ARQUIVO: Lib\HKlistUI.ahk
; DESCRIÇÃO: Dashboard V14 - Layout Fixo com Conectores Visuais
; ==============================================================================

class HKlistUI {
    ; ============================================================
    ; CONFIGURAÇÕES & ESTADO
    ; ============================================================
    static MainGUI := ""
    static IsVisible := false
    static CurrentContextIndex := 0
    
    ; --- Scroll Control (Freio Independente) ---
    static ScrollOffset := 0
    static MaxScrollOffset := 0
    static LastScrollTime := 0
    static LastCycleTime := 0
    static ScrollDelay := 150
    
    ; --- Dados ---
    static DB := Map()
    static ContextList := []
    static GameContextList := []
    static ConflictMap := Map()
    
    ; --- Layout ---
    static WIDTH := 1400
    static HEIGHT := 750
    static COL1_W := 200
    static COL2_W := 550
    static COL3_W := 550
    static LINE_H := 36
    
    ; NOVO: Layout fixo para alinhamento
    static HOTKEY_AREA_W := 280   ; largura fixa da área de badges
    static SEP_W := 30
    static SEP_GAP := 6           ; espaço entre sep e descrição
    
    ; --- Cores ---
    static BG_COLOR := "101010"
    static DIMMED_COLOR := "505050"
    
    ; Controles
    static RenderedControls := []
    
    ; ============================================================
    ; 1. CONTROLE GERAL
    ; ============================================================
    static ToggleHUD(ForceOpen := false) {
        if (this.IsVisible && !ForceOpen) {
            this.Hide()
        } else {
            this.Show()
        }
    }
    
    static Show() {
        this.BuildDatabase()
        this.DetectContext()
        
        if (!this.MainGUI)
            this.CreateGUI()
        
        this.RenderAll()
        
        X := (A_ScreenWidth - this.WIDTH) / 2
        this.MainGUI.Show("NoActivate x" . X . " y80 w" . this.WIDTH . " h" . this.HEIGHT)
        
        this.EnableScrollHooks()
        this.IsVisible := true
    }
    
    static Hide() {
        if (this.MainGUI)
            this.MainGUI.Hide()
        
        this.DisableScrollHooks()
        this.IsVisible := false
    }
    
    static RefreshIfVisible() {
        if (this.IsVisible) {
            this.BuildDatabase()
            this.RenderAll()
        }
    }
    
    ; ============================================================
    ; 2. PARSER COM TAGS (Core/Normal/Gamer) + CONTEXTOS/JOGOS
    ; ============================================================
    static BuildDatabase() {
        KeyFile := A_ScriptDir . "\Config\Keybindings.ahk"
        if !FileExist(KeyFile)
            return
        
        FileContent := FileRead(KeyFile)
        
        this.DB := Map()
        this.ContextList := []
        this.GameContextList := []
        this.ConflictMap := Map()
        
        this.DB["Core"] := []
        this.DB["Normal"] := []
        this.DB["Gamer"] := []
        
        CurrentTag := "Core"
        PendingDesc := ""
        InGamerBlock := false
        
        Loop Parse, FileContent, "`n", "`r" {
            Line := Trim(A_LoopField)
            
            if RegExMatch(Line, "^;[^;]")
                continue
            
            if InStr(Line, "#HotIf") {
                if InStr(Line, 'CurrentProfile = "Normal"') {
                    CurrentTag := "Normal"
                    InGamerBlock := false
                }
                else if InStr(Line, 'CurrentProfile = "Gamer"') {
                    CurrentTag := "Gamer"
                    InGamerBlock := true
                }
                else if RegExMatch(Line, 'i)WinActive\("ahk_exe\s+(.*?)\.exe"\)', &AppMatch) {
                    AppName := StrTitle(AppMatch[1])
                    CurrentTag := AppName
                    
                    if !this.DB.Has(AppName) {
                        this.DB[AppName] := []
                        
                        if (InGamerBlock) {
                            this.GameContextList.Push(AppName)
                        } else {
                            this.ContextList.Push(AppName)
                        }
                    }
                }
                else {
                    CurrentTag := "Core"
                    InGamerBlock := false
                }
                
                PendingDesc := ""
                continue
            }
            
            if RegExMatch(Line, "^\s*;;\s*(.*)", &m) {
                PendingDesc := m[1]
                continue
            }
            
            if RegExMatch(Line, "^\s*(.*?)::", &m) {
                Key := m[1]
                Desc := ""
                
                if RegExMatch(Line, ";;\s*(.*)$", &mDesc)
                    Desc := mDesc[1]
                else if (PendingDesc != "")
                    Desc := PendingDesc
                
                if (Desc != "") {
                    Key := StrUpper(StrReplace(StrReplace(StrReplace(StrReplace(
                        Key, "CapsLock &", "CAPS "), "^", "CTRL "), "!", "ALT "), "+", "SHIFT "))
                    Key := StrReplace(Key, "<", "")
                    Key := StrReplace(Key, ">", "")
                    Key := StrReplace(Key, "#", "WIN ")
                    Key := StrReplace(Key, "  ", " ")
                    
                    Obj := {Key: Key, Desc: Desc, Tag: CurrentTag}
                    this.DB[CurrentTag].Push(Obj)
                    
                    if (CurrentTag != "Core" && CurrentTag != "Normal" && CurrentTag != "Gamer")
                        this.ConflictMap[Key] := true
                }
                
                PendingDesc := ""
            }
        }
    }
    
    ; ============================================================
    ; 3. GUI & RENDERIZAÇÃO
    ; ============================================================
    static CreateGUI() {
        if this.MainGUI
            this.MainGUI.Destroy()
        
        this.MainGUI := Gui("+AlwaysOnTop +ToolWindow -Caption +Border +E0x08000000 +0x02000000", "HK Dashboard")
        this.MainGUI.BackColor := this.BG_COLOR
        this.MainGUI.SetFont("s10", "Segoe UI")
        this.MainGUI.OnEvent("Escape", (*) => this.Hide())
        
        ; Headers
        this.MainGUI.SetFont("s12 Bold c" . this.DIMMED_COLOR)
        this.SidebarTitle := this.MainGUI.Add("Text", "x20 y15 w" . this.COL1_W, "NAVEGAÇÃO (F2)")
        
        this.MainGUI.SetFont("s14 Bold cFFD700")
        this.GlobalTitle := this.MainGUI.Add("Text", "x" . (this.COL1_W + 40) . " y15 w" . this.COL2_W, "GLOBAIS")
        
        this.MainGUI.SetFont("s14 Bold c00FF00")
        this.ContextTitle := this.MainGUI.Add("Text", "x" . (this.COL1_W + this.COL2_W + 60) . " y15 w" . this.COL3_W, "CONTEXTO")
        
        ; Divisórias
        this.MainGUI.Add("Text", "x" . (this.COL1_W + 20) . " y10 h" . this.HEIGHT . " w1 Background333333")
        this.MainGUI.Add("Text", "x" . (this.COL1_W + this.COL2_W + 40) . " y10 h" . this.HEIGHT . " w1 Background333333")
        
        this.ScrollOffset := 0
        this.RenderedControls := []
    }
    
    static RenderAll() {
        for ctrl in this.RenderedControls {
            try ctrl.Visible := false
        }
        this.RenderedControls := []
        
        this.RenderSidebar()
        this.RenderGlobals()
        this.RenderContext()
    }
    
    ; Renderização da parte de hotkeys globais (gerais do perfil ativo)
    static RenderGlobals() {
        X_Start := this.COL1_W + 30
        Y_Start := 60
        Y := Y_Start - (this.ScrollOffset * this.LINE_H)
        
        MyProfile := IsSet(CurrentProfile) ? CurrentProfile : "Normal"
        this.GlobalTitle.Text := "GLOBAIS (" . StrUpper(MyProfile) . " + SCROLL F1)"
        
        MasterList := []
        
        if this.DB.Has("Core")
            for item in this.DB["Core"]
                MasterList.Push(item)
        
        if (MyProfile = "Normal" && this.DB.Has("Normal"))
            for item in this.DB["Normal"]
                MasterList.Push(item)
        
        if (MyProfile = "Gamer" && this.DB.Has("Gamer"))
            for item in this.DB["Gamer"]
                MasterList.Push(item)
        
        VisibleCount := 0
        
        for item in MasterList {
            if (MyProfile = "Gamer" && InStr(item.Key, "WIN"))
                continue
            
            if (Y < Y_Start - this.LINE_H) {
                Y += this.LINE_H
                VisibleCount++
                continue
            }
            if (Y > this.HEIGHT)
                break
            
            isConflict := this.ConflictMap.Has(item.Key)
            DescX := X_Start + this.HOTKEY_AREA_W + this.SEP_GAP
            
            ; 1. Badges
            EndBadgeX := this.RenderHotkeyBadge(item.Key, isConflict, X_Start, Y, false)
            
            ; 2. Conector PRIMEIRO (fonte menor + Y ajustado)
            lineColor := isConflict ? "2A2A2A" : "666666"
            lineY := Y + 12  ; ← AJUSTADO mais pra cima
            
            if (EndBadgeX < DescX - 4) {
                ConnectorWidth := DescX - EndBadgeX - 4
                
                if (ConnectorWidth > 10) {
                    NumChars := Floor(ConnectorWidth / 6.5)  ; ← Ajustado pra fonte menor
                    LineStr := ""
                    Loop NumChars {
                        LineStr .= "─"
                    }
                    
                    this.MainGUI.SetFont("s9 c" . lineColor, "Consolas")  ; ← FONTE MENOR (s9)
                    line := this.MainGUI.Add("Text",
                        "x" . (EndBadgeX + 2) . " y" . lineY .
                        " w" . ConnectorWidth . " h12 Background" . this.BG_COLOR,  ; ← Altura fixa
                        LineStr)
                    this.RenderedControls.Push(line)
                }
            }
            
            ; 3. Descrição POR CIMA (renderiza DEPOIS)
            ColorDesc := isConflict ? "666666" : "FFFFFF"
            BgDesc := isConflict ? "Transparent" : "1A1A1A"
            Style := isConflict ? "Strike" : "Norm"
            
            this.MainGUI.SetFont("s11 c" . ColorDesc . " " . Style, "Segoe UI")
            d := this.MainGUI.Add("Text",
                "x" . DescX . " y" . (Y + 7) .
                " w" . (this.COL2_W - (DescX - X_Start)) . " h20 Background" . BgDesc,
                "  " . item.Desc)
            this.RenderedControls.Push(d)
            
            Y += this.LINE_H
            VisibleCount++
        }
        
        VisibleH := this.HEIGHT - 80
        TotalH := VisibleCount * this.LINE_H
        this.MaxScrollOffset := Max(0, Ceil((TotalH - VisibleH) / this.LINE_H))
    }
    ; Renderização da parte de hotkeys do contexto ativo
    static RenderContext() {
        X_Start := this.COL1_W + this.COL2_W + 50
        Y := 60
        
        MyProfile := IsSet(CurrentProfile) ? CurrentProfile : "Normal"
        
        if (this.CurrentContextIndex = 0) {
            this.ContextTitle.Text := "DESKTOP"
            this.ContextTitle.Opt("c505050")
            List := []
        } else {
            if (MyProfile = "Gamer") {
                CtxName := this.GameContextList[this.CurrentContextIndex]
            } else {
                CtxName := this.ContextList[this.CurrentContextIndex]
            }
            
            this.ContextTitle.Text := StrUpper(CtxName)
            this.ContextTitle.Opt("c00FF00")
            List := this.DB.Has(CtxName) ? this.DB[CtxName] : []
        }
        
        for item in List {
            DescX := X_Start + this.HOTKEY_AREA_W + this.SEP_GAP
            
            EndBadgeX := this.RenderHotkeyBadge(item.Key, false, X_Start, Y, true)
            
            ; 1. Conector PRIMEIRO (fonte menor)
            lineColor := "00AA66"
            lineY := Y + 12  ; ← AJUSTADO
            
            if (EndBadgeX < DescX - 4) {
                ConnectorWidth := DescX - EndBadgeX - 4
                
                if (ConnectorWidth > 10) {
                    NumChars := Floor(ConnectorWidth / 6.5)
                    LineStr := ""
                    Loop NumChars {
                        LineStr .= "─"
                    }
                    
                    this.MainGUI.SetFont("s9 c" . lineColor, "Consolas")  ; ← s9
                    line := this.MainGUI.Add("Text",
                        "x" . (EndBadgeX + 2) . " y" . lineY .
                        " w" . ConnectorWidth . " h12 Background" . this.BG_COLOR,
                        LineStr)
                    this.RenderedControls.Push(line)
                }
            }
            
            ; 2. Descrição POR CIMA
            this.MainGUI.SetFont("s11 cFFFFFF Norm", "Segoe UI")
            d := this.MainGUI.Add("Text",
                "x" . DescX . " y" . (Y + 7) .
                " w" . (this.COL3_W - (DescX - X_Start)) . " h20 Background0F2818",
                "  " . item.Desc)
            this.RenderedControls.Push(d)
            
            Y += this.LINE_H
        }
    }
    ; Renderização da barra lateral de contextos/jogos
    static RenderSidebar() {
        Y := 60
        X := 20
        
        MyProfile := IsSet(CurrentProfile) ? CurrentProfile : "Normal"
        
        ModeIcon := (MyProfile = "Gamer") ? "🎮" : "💼"
        this.SidebarTitle.Text := ModeIcon . " " . StrUpper(MyProfile)
        this.SidebarTitle.Opt("c" . ((MyProfile = "Gamer") ? "FF6B35" : this.DIMMED_COLOR))
        
        this.AddSidebarItem("Global / Desktop", 0, X, Y)
        Y += this.LINE_H
        
        if (MyProfile = "Gamer") {
            for index, name in this.GameContextList {
                this.AddSidebarItem(name, index, X, Y)
                Y += this.LINE_H
            }
        } else {
            for index, name in this.ContextList {
                this.AddSidebarItem(name, index, X, Y)
                Y += this.LINE_H
            }
        }
    }
    static AddSidebarItem(Name, Index, X, Y) {
        IsActive := (this.CurrentContextIndex = Index)
        Color := IsActive ? "00FFFF" : "808080"
        Prefix := IsActive ? "█ " : Index . ". "
        Font := IsActive ? "Bold" : "Norm"
        
        this.MainGUI.SetFont("s11 c" . Color . " " . Font)
        ctrl := this.MainGUI.Add("Text", "x" . X . " y" . Y . " w" . this.COL1_W . " h" . this.LINE_H, Prefix . Name)
        this.RenderedControls.Push(ctrl)
    }
    
    ; Renderiza hotkeys em badges com conectores até a descrição
    static RenderHotkeyBadge(HotkeyStr, IsConflict, X, Y, IsContext) {
        Keys := StrSplit(HotkeyStr, " ")
        CurrentX := X
        BadgeH := 24
    
        if (IsConflict) {
            BgColor := "1A1A1A"
            TextColor := "555555"
        } else if (IsContext) {
            BgColor := "0A2818"
            TextColor := "00FF88"
        } else {
            BgColor := "1F1C08"
            TextColor := "FFD700"
        }
        
        for index, key in Keys {
            if (key = "")
                continue
            
            KeyWidth := Max(40, (StrLen(key) * 10) + 16)
            
            this.MainGUI.SetFont("s11 Bold c" . TextColor, "Consolas")
            badge := this.MainGUI.Add("Text",
                "x" . CurrentX . " y" . (Y + 5) .
                " w" . KeyWidth . " h" . BadgeH . " Center Border Background" . BgColor,
                key)
            
            this.RenderedControls.Push(badge)
            
            CurrentX += KeyWidth + 14
            
            if (index < Keys.Length) {
                this.MainGUI.SetFont("s13 Bold c888888", "Segoe UI")
                plus := this.MainGUI.Add("Text",
                    "x" . (CurrentX - 12) . " y" . (Y + 7) .
                    " w10 h20 Center Background" . this.BG_COLOR,
                    "+")
                this.RenderedControls.Push(plus)
            }
        }
    
        return CurrentX
    }
    
    ; ============================================================
    ; INPUT & SCROLL
    ; ============================================================
    ; Detecta o contexto ativo baseado na janela atual
    static DetectContext() {
        try {
            ActiveExe := WinGetProcessName("A")
            ActiveTitle := StrTitle(StrReplace(ActiveExe, ".exe", ""))
            
            MyProfile := IsSet(CurrentProfile) ? CurrentProfile : "Normal"
            SearchList := (MyProfile = "Gamer") ? this.GameContextList : this.ContextList
            
            for i, name in SearchList {
                if InStr(ActiveTitle, name) || InStr(name, ActiveTitle) {
                    this.CurrentContextIndex := i
                    return
                }
            }
        }
        
        this.CurrentContextIndex := 0
    }
    
    ; Navegação por scroll (F1 = globais, F2 = contexto)
    static EnableScrollHooks() {
        Hotkey "F1 & WheelUp", (*) => this.ScrollGlobals(-1), "On"
        Hotkey "F1 & WheelDown", (*) => this.ScrollGlobals(1), "On"
        Hotkey "F2 & WheelUp", (*) => this.CycleContext(-1), "On"
        Hotkey "F2 & WheelDown", (*) => this.CycleContext(1), "On"
    }
    
    static DisableScrollHooks() {
        try {
            Hotkey "F1 & WheelUp", "Off"
            Hotkey "F1 & WheelDown", "Off"
            Hotkey "F2 & WheelUp", "Off"
            Hotkey "F2 & WheelDown", "Off"
        }
    }
    
    static ScrollGlobals(Dir) {
        if (A_TickCount - this.LastScrollTime < this.ScrollDelay)
            return
        
        this.LastScrollTime := A_TickCount
        
        NewOffset := this.ScrollOffset + Dir
        
        if (NewOffset < 0)
            NewOffset := 0
        if (NewOffset > this.MaxScrollOffset)
            NewOffset := this.MaxScrollOffset
        
        if (NewOffset != this.ScrollOffset) {
            this.ScrollOffset := NewOffset
            this.RenderAll()
            DllCall("User32\RedrawWindow", "Ptr", this.MainGUI.Hwnd, "Ptr", 0, "Ptr", 0, "UInt", 5)
        }
    }
    
    static CycleContext(Dir) {
        if (A_TickCount - this.LastCycleTime < this.ScrollDelay)
            return
        
        this.LastCycleTime := A_TickCount
        
        MyProfile := IsSet(CurrentProfile) ? CurrentProfile : "Normal"
        MaxIdx := (MyProfile = "Gamer") ? this.GameContextList.Length : this.ContextList.Length
        
        this.CurrentContextIndex += Dir
        
        if (this.CurrentContextIndex < 0)
            this.CurrentContextIndex := MaxIdx
        if (this.CurrentContextIndex > MaxIdx)
            this.CurrentContextIndex := 0
        
        this.RenderAll()
    }
}
; ==============================================================================
; FIM DO ARQUIVO: Lib\HKlistUI.ahk
; ==============================================================================