; ==============================================================================
; ARQUIVO: Lib\AppActions.ahk
; DESCRIÇÃO: Ações genéricas para Apps (Ciclo de Case, Pesquisa Google, Abrir/Focar Apps)
; ==============================================================================

;; Função para ciclar caixa alta/baixa/título (VERSÃO ROBUSTA)
App_CycleTextCase() {
    ; 1. BACKUP: Salva o clipboard binário
    SavedClip := ClipboardAll() 
    A_Clipboard := "" 
    
    ; 2. INPUT: Copia seleção (com retry)
    SendInput "^c"  ; ← SendInput é mais confiável
    
    ; Timeout maior para apps lentos
    if !ClipWait(1.0) {  ; ← Aumentado de 0.5 para 1.0
        A_Clipboard := SavedClip
        ToolTip("❌ Nenhum texto selecionado")
        SetTimer(() => ToolTip(), -1500)
        return
    }
    
    ; 3. LÓGICA DE TRANSFORMAÇÃO
    Txt := A_Clipboard
    
    ; Verifica se pegou algo válido
    if (Txt == "") {
        A_Clipboard := SavedClip
        return
    }
    
    if (Txt == StrLower(Txt))
        NewTxt := StrUpper(Txt)
    else if (Txt == StrUpper(Txt))
        NewTxt := StrTitle(Txt)
    else
        NewTxt := StrLower(Txt)
    
    ; 4. OUTPUT (PASTE com verificação)
    A_Clipboard := NewTxt
    
    ; CRÍTICO: Aguarda o clipboard ser atualizado
    ClipWait(0.5)
    
    ; Delay antes do paste (apps lentos precisam disso)
    Sleep 100  ; ← Aumentado de 50 para 100
    
    SendInput "^v"  ; ← SendInput mais rápido
    
    ; delay de segurança físico
    Sleep 100  ; ← Aumentado para apps lentos
    
    ; 5. RE-SELEÇÃO (A CORREÇÃO MATEMÁTICA)
    Len := StrLen(StrReplace(NewTxt, "`r", ""))
    SendInput "+{Left " . Len . "}"
    
    ; 6. RESTAURAÇÃO (com delay)
    Sleep 50  ; ← Aguarda reseleção terminar
    A_Clipboard := SavedClip
}

;; Pesquisa seleção de texto no Google
App_GoogleSearchSelection() {
    OldClip := A_Clipboard
    A_Clipboard := ""
    Send("^c")
    if ClipWait(0.5)
        Run("https://www.google.com/search?q=" . A_Clipboard)
    else
        Run("https://www.google.com")
    Sleep 100
    A_Clipboard := OldClip
}

;; Abre Apps de forma inteligente (Foca se aberto, Abre se fechado)
App_OpenOrFocus(exeName, fullPath := "") {
    ; Se já existe janela do exe, apenas ativa
    if WinExist("ahk_exe " . exeName) {
        WinActivate
        return
    }

    ; Se não existe janela e temos caminho completo, tenta abrir
    if (fullPath != "") {
        try {
            Run(fullPath)
        } catch Error as e {
            ToolTip("⚠ Falha ao abrir '" . exeName . "': " . e.Message)
            SetTimer(() => ToolTip(), -1500)
        }
    } else {
        ; Sem caminho completo: avisa em vez de explodir
        ToolTip("⚠ App_OpenOrFocus sem fullPath para '" . exeName . "'")
        SetTimer(() => ToolTip(), -1500)
    }
}

; Wrappers específicos para o RadialMenu (sem parâmetros na action string)
Radial_OpenNotion() {
    ; Abre ou foca Notion
    App_OpenOrFocus("Notion.exe")
}

Radial_OpenObsidian() {
    ; Abre ou foca Obsidian
    App_OpenOrFocus("Obsidian.exe")
}

Radial_OpenTerminal() {
    ; Placeholder: tenta abrir Windows Terminal
    ; Ajusta para o terminal que você realmente usa se precisar
    App_OpenOrFocus("wt.exe")
}

Radial_OpenExplorer() {
    ; Abre ou foca o Explorer
    App_OpenOrFocus("explorer.exe")
}

; ==============================================================================
; FIM DO ARQUIVO: Lib\AppActions.ahk
; ==============================================================================
