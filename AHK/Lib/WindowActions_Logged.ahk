; ==============================================================================
; ARQUIVO: Lib\WindowActions.ahk (VERSÃO COM LOGS)
; DESCRIÇÃO: Ações genéricas para Janelas com logging extensivo
; ==============================================================================

; Configuração de logs
Global WINDOW_ACTIONS_LOG := EnvGet("LocalAppData") . "\ChokoLPT\logs\window_actions.log"

WindowActions_LogMsg(msg) {
    Global WINDOW_ACTIONS_LOG
    try {
        timestamp := FormatTime(, "HH:mm:ss.") . A_MSec
        FileAppend(timestamp . " | " . msg . "`n", WINDOW_ACTIONS_LOG)
    }
}

;; Função para alternar "Sempre no Topo" da janela ativa
Win_ToggleOnTop() {
    WindowActions_LogMsg(">>> Win_ToggleOnTop() CHAMADA")
    try {
        WinSetAlwaysOnTop -1, "A"
        state := (WinGetExStyle("A") & 0x8 ? "ON" : "OFF")
        WindowActions_LogMsg("Estado: " . state)
        ToolTip("📌 Topo: " . state)
        SetTimer () => ToolTip(), -1000
        WindowActions_LogMsg("✓ Win_ToggleOnTop() SUCESSO")
    } catch as err {
        WindowActions_LogMsg("❌ ERRO: " . err.Message)
    }
}

;; Função para alternar bordas da janela ativa
Win_ToggleBorderless() {
    WindowActions_LogMsg(">>> Win_ToggleBorderless() CHAMADA")
    try {
        WinSetStyle "^0xC40000", "A"
        ToolTip("🖼️ Borda Alternada")
        SetTimer () => ToolTip(), -1000
        WindowActions_LogMsg("✓ Win_ToggleBorderless() SUCESSO")
    } catch as err {
        WindowActions_LogMsg("❌ ERRO: " . err.Message)
    }
}

;; Função para alternar a opacidade da janela ativa com um clique do meio entre 255 e 150
Cycle_Opacity() {
    WindowActions_LogMsg(">>> Cycle_Opacity() CHAMADA")
    try {
        current := WinGetTransparent("A")
        WindowActions_LogMsg("Opacidade atual: " . (current = "" ? "255" : current))
        
        if (current = 255) 
            current := 150
        else if (current < 255) 
            current := 255
        
        WindowActions_LogMsg("Nova opacidade: " . current)
        WinSetTransparent(current, "A")
        ToolTip("💧 Opacidade: " . current)
        WindowActions_LogMsg("✓ Cycle_Opacity() SUCESSO")
    } catch as err {
        WindowActions_LogMsg("❌ ERRO: " . err.Message)
    }
}

;; Função para iniciar o scroll de opacidade na janela ativa enquanto CapsLock é segurado
Win_StartOpacityScroll() {
    WindowActions_LogMsg("========================================")
    WindowActions_LogMsg(">>> Win_StartOpacityScroll() CHAMADA")
    WindowActions_LogMsg("Timestamp: " . A_TickCount)
    
    ; 1. Feedback Visual
    ToolTip("💧 Scroll: Ajustar Opacidade")
    WindowActions_LogMsg("Tooltip exibido")
    
    ; 2. Liga os atalhos do mouse dinamicamente
    try {
        WindowActions_LogMsg("Registrando hotkeys dinâmicas...")
        WindowActions_LogMsg("  - Tentando *WheelUp...")
        Hotkey "*WheelUp", (*) => Win_AdjustOpacity(10), "On"
        WindowActions_LogMsg("  ✓ *WheelUp registrado")
        
        WindowActions_LogMsg("  - Tentando *WheelDown...")
        Hotkey "*WheelDown", (*) => Win_AdjustOpacity(-10), "On"
        WindowActions_LogMsg("  ✓ *WheelDown registrado")
        
        WindowActions_LogMsg("  - Tentando *MButton...")
        Hotkey "*MButton", (*) => Cycle_Opacity(), "On"
        WindowActions_LogMsg("  ✓ *MButton registrado")
        
        WindowActions_LogMsg("✓ Todas as hotkeys registradas com sucesso")
    } catch as err {
        WindowActions_LogMsg("❌ ERRO ao registrar hotkeys: " . err.Message)
        WindowActions_LogMsg("Stack: " . err.Stack)
        ToolTip("❌ ERRO: " . err.Message)
        SetTimer(() => ToolTip(), -3000)
        return
    }

    ; 3. Trava a execução aqui até você SOLTAR o CapsLock
    WindowActions_LogMsg("Aguardando soltar CapsLock...")
    WindowActions_LogMsg("Timestamp antes KeyWait: " . A_TickCount)
    
    KeyWait "CapsLock"
    
    WindowActions_LogMsg("Timestamp após KeyWait: " . A_TickCount)
    WindowActions_LogMsg("CapsLock solta detectada")
    
    ; 4. Limpeza (Desliga os atalhos e remove tooltip)
    try {
        WindowActions_LogMsg("Desligando hotkeys...")
        Hotkey "*WheelUp", "Off"
        WindowActions_LogMsg("  ✓ *WheelUp desligado")
        Hotkey "*WheelDown", "Off"
        WindowActions_LogMsg("  ✓ *WheelDown desligado")
        Hotkey "*MButton", "Off"
        WindowActions_LogMsg("  ✓ *MButton desligado")
    } catch as err {
        WindowActions_LogMsg("❌ ERRO ao desligar hotkeys: " . err.Message)
    }
    
    ToolTip()
    WindowActions_LogMsg("<<< Win_StartOpacityScroll() FINALIZADA")
    WindowActions_LogMsg("========================================")
    WindowActions_LogMsg("")
}

;; Função auxiliar para ajustar a opacidade em passos
Win_AdjustOpacity(step) {
    WindowActions_LogMsg(">>> Win_AdjustOpacity(" . step . ") CHAMADA")
    try {
        current := WinGetTransparent("A")
        WindowActions_LogMsg("Opacidade atual: " . (current = "" ? "255 (padrão)" : current))
        
        if (current = "") 
            current := 255
        
        new_val := current + step
        if (new_val > 255) 
            new_val := 255
        if (new_val < 20) 
            new_val := 20
        
        WindowActions_LogMsg("Calculado: " . current . " + " . step . " = " . new_val)
        WinSetTransparent(new_val, "A")
        ToolTip("💧 Opacidade: " . new_val)
        WindowActions_LogMsg("✓ Opacidade aplicada: " . new_val)
    } catch as err {
        WindowActions_LogMsg("❌ ERRO: " . err.Message)
        WinSetTransparent(255, "A")
    }
    WindowActions_LogMsg("<<< Win_AdjustOpacity() FINALIZADA")
}

; ==============================================================================
; FIM DO ARQUIVO: Lib\WindowActions.ahk
; ==============================================================================