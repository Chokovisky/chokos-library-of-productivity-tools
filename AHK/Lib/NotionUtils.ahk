; ==============================================================================
; Lib/NotionUtils.ahk
; DESCRIÇÃO: Utilitários específicos para o Notion (atalhos, automações)
; ==============================================================================

;; Função para garantir que o comando vá apenas para o Notion
Notion_ToggleAllToggles() {
    if WinActive("ahk_exe Notion.exe") {
        Send("^!t") ; Envia Ctrl + Alt + T (Nativo do Notion para Expand/Collapse All)
    } else {
        ; Opcional: Traz o Notion para frente e executa
        try {
            WinActivate("ahk_exe Notion.exe")
            WinWaitActive("ahk_exe Notion.exe",, 1)
            Send("^!t")
        }
    }
}

;; Placeholder futura função de "Registrar Sessão de Estudo"
; Notion_LogStudySession() {
;     ; Aqui entra o script futuro para logar sessões de estudo no Notion
; }

;; Placeholder futura função de "Inserção Rápida"
; Notion_QuickInsert() {
;     ; Aqui entra o script futuro para inserção rápida no Notion
; }   

; ==============================================================================
; FIM DO ARQUIVO: Lib/NotionUtils.ahk
; ==============================================================================