; ==============================================================================
; ARQUIVO: Lib\WindowActions.ahk
; DESCRIÇÃO: Ações genéricas para Janelas (Topo, Sem Borda, Opacidade)
; VERSÃO: CORRIGIDA com KeyWait "e" em vez de KeyWait "CapsLock"
; ==============================================================================

;; Função para alternar "Sempre no Topo" da janela ativa
Win_ToggleOnTop() { 
    WinSetAlwaysOnTop -1, "A"
    ; Feedback visual simples (opcional)
    ToolTip("📌 Topo: " . (WinGetExStyle("A") & 0x8 ? "ON" : "OFF"))
    SetTimer () => ToolTip(), -1000
}

;; Função para alternar bordas da janela ativa
Win_ToggleBorderless() {
    WinSetStyle "^0xC40000", "A" 
    ToolTip("🖼️ Borda Alternada")
    SetTimer () => ToolTip(), -1000
}

;; Função para alternar a opacidade da janela ativa com um clique do meio entre 255 e 150
Cycle_Opacity() {
    current := WinGetTransparent("A")
    if (current = 255) 
        current := 150
    else if (current < 255) 
        current := 255
    WinSetTransparent(current, "A")
    ToolTip("💧 Opacidade: " . current)
}

;; Função para iniciar o scroll de opacidade na janela ativa enquanto CapsLock é segurado
;; CORREÇÃO CRÍTICA: Mudado KeyWait "CapsLock" para KeyWait "e"
Win_StartOpacityScroll() {
    ; 1. Feedback Visual
    ToolTip("💧 Scroll: Ajustar Opacidade")
    
    ; 2. Liga os atalhos do mouse dinamicamente
    ; Usamos uma "Fat Arrow Function" (() =>) para passar o parâmetro 10 ou -10
    Hotkey "*WheelUp",   (*) => Win_AdjustOpacity(10),  "On"
    Hotkey "*WheelDown", (*) => Win_AdjustOpacity(-10), "On"
    Hotkey "*MButton", (*) => Cycle_Opacity(), "On"

    ; 3. CORREÇÃO: Espera soltar a tecla E em vez de CapsLock
    ; Motivo: Quando chamado por "CapsLock & e", o KeyWait "CapsLock" pode retornar
    ; imediatamente porque o sistema já considera CapsLock "liberado" do contexto da hotkey
    KeyWait "e"
    
    ; 4. Limpeza (Desliga os atalhos e remove tooltip)
    Hotkey "*WheelUp", "Off"
    Hotkey "*WheelDown", "Off"
    Hotkey "*MButton", "Off"
    ToolTip()
}

;; Função auxiliar para ajustar a opacidade em passos
Win_AdjustOpacity(step) {
    try {
        current := WinGetTransparent("A")
        if (current = "") 
            current := 255
        
        new_val := current + step
        if (new_val > 255) 
            new_val := 255
        if (new_val < 20) 
            new_val := 20
            
        WinSetTransparent(new_val, "A")
        ToolTip("💧 Opacidade: " . new_val) ; Atualiza o tooltip com o valor
    } catch {
        WinSetTransparent(255, "A") ; Fallback para opacidade total se falhar
    }
}

; ==============================================================================
; FIM DO ARQUIVO: Lib\WindowActions.ahk
; ==============================================================================