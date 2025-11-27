; ==============================================================================
; ARQUIVO: Config/Keybindings.ahk
; DESCRIÇÃO: Loader Dinâmico de Hotkeys a partir do JSON
; ==============================================================================

; #region CLASSE HOTKEYLOADER - Processa e registra hotkeys do JSON
class HotkeyLoader {
    
    static RegisteredHotkeys := []
    
    ; Inicializa o loader e registra as hotkeys
    static Init() {
        Global HotkeyData
        
        if !IsObject(HotkeyData) || !HotkeyData.Count {
            ToolTip("⚠️ HotkeyData vazio! Verifique hotkeys.json")
            SetTimer(() => ToolTip(), -3000)
            return false
        }
        
        this.RegisterAll()
        this.LogRegisteredHotkeys()
        return true
    }
    
    ; Recarrega hotkeys (para mudança de perfil ou recarregar JSON)
    ; Agora RegisterAll() já faz UnregisterAll() internamente, então Reload é só um alias.
    static Reload() {
        this.RegisterAll()
    }
    
    ; CRÍTICO: Registra TODAS as hotkeys de TODOS os perfis, uma vez só.
    ; A ativação é controlada dinamicamente via ProfileAndContextMatch().
    static RegisterAll() {
        Global HotkeyData
        
        if !HotkeyData.Has("hotkeys")
            return

        ; Sempre limpa tudo antes de (re)registrar
        this.UnregisterAll()
        
        globals := []        ; hotkeys sem contexto
        contextItems := []   ; hotkeys com contexto

        for item in HotkeyData["hotkeys"] {
            ; Pula se desabilitada
            if item.Has("enabled") && !item["enabled"]
                continue

            ; ID usado para logging/UI e, se necessário, casos especiais
            id := item.Has("id") ? item["id"] : ""

            context := item.Has("context") ? item["context"] : ""
            if (context != "" && context != "null")
                contextItems.Push(item)
            else
                globals.Push(item)
        }

        ; 1º: registra as globais (sem contexto)
        for item in globals
            this.RegisterForAllProfiles(item)

        ; 2º: registra as com contexto (override das globais onde a janela bate)
        for item in contextItems
            this.RegisterForAllProfiles(item)
    }

    ; Helper: registra um item do JSON para todos os perfis definidos
    static RegisterForAllProfiles(item) {
        Global HotkeyData

        id      := item.Has("id") ? item["id"] : ""
        action  := item["action"]
        context := item.Has("context") ? item["context"] : ""

        ; Se não tiver lista de perfis, nada a fazer
        if !item.Has("profiles") {
            ; CASO ESPECIAL: cycle_profile deve existir em TODOS os perfis disponíveis
            if (id = "cycle_profile"
             && HotkeyData.Has("profiles")
             && HotkeyData["profiles"].Has("available"))
            {
                for _, profile in HotkeyData["profiles"]["available"] {
                    key := this.GetKeyForProfile(item, profile)
                    this.RegisterHotkeySmart(key, action, profile, context, id)
                }
            }
            return
        }

        ; CASO ESPECIAL: cycle_profile → sempre registrar em TODOS os perfis disponíveis,
        ; ignorando a lista local de profiles do item se existir.
        if (id = "cycle_profile"
         && HotkeyData.Has("profiles")
         && HotkeyData["profiles"].Has("available"))
        {
            for _, profile in HotkeyData["profiles"]["available"] {
                key := this.GetKeyForProfile(item, profile)
                this.RegisterHotkeySmart(key, action, profile, context, id)
            }
            return
        }

        ; Demais hotkeys: registrar apenas para os perfis declarados no item
        if !item.Has("profiles")
            return

        for _, profile in item["profiles"] {
            key := this.GetKeyForProfile(item, profile)
            this.RegisterHotkeySmart(key, action, profile, context, id)
        }
    }

    ; Registra uma hotkey com condição dinâmica de perfil + contexto
    static RegisterHotkeySmart(key, action, profile, contextName, id) {
        try {
            condFunc := (*) => HotkeyLoader.ProfileAndContextMatch(profile, contextName)
            HotIf(condFunc)
            Hotkey(key, (*) => this.ExecuteAction(action), "On")
            HotIf()

            this.RegisteredHotkeys.Push({
                key: key,
                context: contextName,
                id: id,
                profile: profile
            })
        } catch as err {
            try FileAppend(
                FormatTime(, "yyyy-MM-dd HH:mm:ss") . " | Erro ao registrar [" . id . "] " . key . ": " . err.Message . "`n",
                LOGS_PATH . "\hotkey_errors.log"
            )
        }
    }

    ; Mantém a função antiga disponível (não usada no fluxo JSON atual)
    static RegisterHotkey(key, action, context, id) {
        this.RegisterHotkeySmart(key, action, "", context, id)
    }
    
    ; Desregistra todas as hotkeys dinâmicas registradas pelo loader
    static UnregisterAll() {
        for item in this.RegisteredHotkeys {
            try {
                ; Desliga independente de perfil/contexto – vamos recriar tudo em RegisterAll()
                Hotkey(item.key, "Off")
            }
        }
        this.RegisteredHotkeys := []
    }
    
    ; CRÍTICO: Executa a ação da hotkey
    static ExecuteAction(action) {
        Global LOGS_PATH

        ; Ação vazia (bloquear tecla)
        if (action = "return")
            return
        
        ; Toggle CapsLock real
        if (action = "CapsLock") {
            SetCapsLockState(!GetKeyState("CapsLock", "T"))
            return
        }
        
        ; Tooltip de Esc cancelado
        if (action = "tooltip_esc_cancelado") {
            ToolTip("⛔ Esc Cancelado")
            SetTimer(() => ToolTip(), -1000)
            return
        }

        ; CASO ESPECIAL: Troca de perfil é crítica, chamamos direto
        ; + logging para confirmar se a hotkey dinâmica (^+!F12 do JSON) está disparando
        if (action = "Core_CycleProfile") {
            ToolTip("Core_CycleProfile acionado via JSON")
            SetTimer(() => ToolTip(), -800)
            try {
                FileAppend(
                    FormatTime(, "yyyy-MM-dd HH:mm:ss") . " | ExecuteAction: Core_CycleProfile acionado via JSON`n",
                    LOGS_PATH . "\profile_cycle.log"
                )
            }
            Core_CycleProfile()
            return
        }
        
        ; Send() direto
        if InStr(action, "Send(") {
            RegExMatch(action, "Send\(['\x22](.+?)['\x22]\)", &match)
            if match
                Send(match[1])
            return
        }
        
        ; Execução dinâmica genérica (mantida só para outros casos)
        try {
            if InStr(action, ".") {
                ; Método de classe (ex: DashboardUtils.Toggle)
                parts := StrSplit(action, ".")
                className := parts[1]
                methodName := parts[2]
                
                ; AHK v2: obter referência da classe e chamar o método
                classRef := %className%
                classRef.%methodName%()
                
            } else {
                ; Função global (exceto Core_CycleProfile, já tratada acima)
                funcRef := %action%
                funcRef()
            }
        } catch as err {
            ToolTip("❌ Erro: " . action . "`n" . err.Message)
            SetTimer(() => ToolTip(), -2000)
        }
    }
    
    ; Verifica se hotkey está no perfil
    static IsInProfile(item, profile) {
        if !item.Has("profiles")
            return false
        
        for p in item["profiles"] {
            if (p = profile)
                return true
        }
        return false
    }
    
    ; Obtém a tecla correta para o perfil
    static GetKeyForProfile(item, profile) {
        if item.Has("key_overrides") && item["key_overrides"].Has(profile)
            return item["key_overrides"][profile]
        return item["key"]
    }
    
    ; Obtém a condição WinActive para o contexto
    static GetContextCondition(contextName) {
        Global HotkeyData
        if !HotkeyData.Has("contexts")
            return ""
        if !HotkeyData["contexts"].Has(contextName)
            return ""
        return HotkeyData["contexts"][contextName]
    }
    
    ; Verifica se a janela ativa corresponde a uma condição
    static MatchesCondition(condition) {
        if (condition = "" || condition = "null")
            return true
        
        ; Suporta múltiplas condições com ||
        if InStr(condition, "||") {
            parts := StrSplit(condition, "||")
            for part in parts {
                part := Trim(part)
                if (part != "" && this.MatchesSingleCondition(part))
                    return true
            }
            return false
        }
        
        return this.MatchesSingleCondition(condition)
    }
    
    ; Verifica uma condição individual
    static MatchesSingleCondition(condition) {
        condition := Trim(condition)
        if InStr(condition, "ahk_exe") || InStr(condition, "ahk_class")
            return WinActive(condition)
        return WinActive(condition)
    }

    ; Combina perfil + contexto em uma única condição para o HotIf dinâmico
    static ProfileAndContextMatch(profile, contextName) {
        Global CurrentProfile

        ; 1. Perfil
        if (profile != "" && profile != CurrentProfile)
            return false

        ; 2. Contexto (se vazio/null, trata como global para aquele perfil)
        if (contextName = "" || contextName = "null")
            return true

        condition := HotkeyLoader.GetContextCondition(contextName)
        if (condition = "")
            return true  ; contexto não mapeado no JSON => assume global

        return HotkeyLoader.MatchesCondition(condition)
    }
    
    ; Loga todas as hotkeys registradas para debug
    static LogRegisteredHotkeys() {
        Global APP_DATA_PATH
        logPath := APP_DATA_PATH . "\hotkeys_registered.log"
        
        try {
            logContent := "=== HOTKEYS REGISTRADAS ===" . "`n"
            logContent .= "Timestamp: " . FormatTime(, "yyyy-MM-dd HH:mm:ss") . "`n"
            logContent .= "Total: " . this.RegisteredHotkeys.Length . " hotkeys" . "`n`n"
            
            if (this.RegisteredHotkeys.Length = 0) {
                logContent .= "⚠️ NENHUMA HOTKEY REGISTRADA!`n"
            } else {
                for item in this.RegisteredHotkeys {
                    logContent .= "ID: " . item.id . "`n"
                    logContent .= "  Key: " . item.key . "`n"
                    logContent .= "  Context: " . (item.context != "" ? item.context : "(global)") . "`n"
                    logContent .= "`n"
                }
            }
            
            if FileExist(logPath)
                FileDelete(logPath)
            FileAppend(logContent, logPath, "UTF-8")
        } catch as err {
            ToolTip("❌ Erro ao criar log: " . err.Message)
            SetTimer(() => ToolTip(), -2000)
        }
    }
    
    ; Getters para UI/Dashboard
    static GetHotkeysForProfile(profile := "") {
        Global CurrentProfile, HotkeyData
        
        if (profile = "")
            profile := CurrentProfile
        
        result := []
        
        if !HotkeyData.Has("hotkeys")
            return result
        
        for item in HotkeyData["hotkeys"] {
            if !item.Has("enabled") || item["enabled"] {
                if this.IsInProfile(item, profile) {
                    result.Push({
                        id: item["id"],
                        key: this.GetKeyForProfile(item, profile),
                        description: item["description"],
                        category: item.Has("category") ? item["category"] : "",
                        context: item.Has("context") ? item["context"] : ""
                    })
                }
            }
        }
        
        return result
    }
}
; #endregion

; ==============================================================================
; HOTKEYS ESTÁTICAS
; ==============================================================================
; Hotkeys globais que não vêm do JSON (comportamentos especiais / emergenciais)
;
; - F2 + Scroll: navega contextos do HKCheatsheetOverlay mesmo sem foco na janela
;   Implementação:
;     F2 & WheelUp   → CycleContext(-1)
;     F2 & WheelDown → CycleContext(+1)
;   No C#, isso cai em MainWindow.WndProc(App.WM_SHOW_OVERLAY) com wParam = ±1.
; ==============================================================================

; F2 + Scroll → navegar contextos no overlay (global)
F2 & WheelUp::  HKCheatsheetOverlayUtils.CycleContext(-1)
F2 & WheelDown::HKCheatsheetOverlayUtils.CycleContext(1)

; ==============================================================================
; INICIALIZAÇÃO - Registra hotkeys dinâmicas do JSON
; ==============================================================================

HotkeyLoader.Init()

; ==============================================================================
; FIM DO ARQUIVO: Config/Keybindings.ahk
; ==============================================================================