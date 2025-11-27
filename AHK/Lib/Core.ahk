; ==============================================================================
; ARQUIVO: Lib/Core.ahk
; DESCRIÇÃO: Funções Centrais - CapsLock e Ciclo de Perfis
; VERSÃO: CORRIGIDA com melhor tratamento de erros
; ==============================================================================

; #region Inicialização do Core
Core_Initialize() {
    ; CRÍTICO: CapsLock deve SEMPRE estar desligada
    ; A hotkey CapsLock:: é registrada em Keybindings.ahk
    SetCapsLockState("AlwaysOff")
}
; #endregion

; #region Funções Centrais

; CRÍTICO: Esta função é chamada quando CapsLock é pressionado sozinho
Core_HandleCapsPress() {
    Send("{Esc}")
}

; Wrapper para uso dinâmico via JSON (action: "Core_HandleCapsHotkey")
; Mantém o nome antigo para compatibilidade e expõe um nome semântico para o configurador.
Core_HandleCapsHotkey() {
    Core_HandleCapsPress()
}

; CRÍTICO: Função de ciclo de perfis - EXTREMO CUIDADO aqui!
; CORREÇÃO: Adicionado tratamento de erro robusto e verificação de salvamento
Core_CycleProfile() {
    Global CurrentProfile, HotkeyData, HOTKEYS_PATH, LOGS_PATH
    
    ; Obtém lista de perfis disponíveis do JSON
    profiles := []
    if HotkeyData.Has("profiles") && HotkeyData["profiles"].Has("available") {
        profiles := HotkeyData["profiles"]["available"]
    } else {
        ; Fallback se JSON não tiver perfis definidos
        profiles := ["Normal", "Gamer"]
    }
    
    ; Encontra o índice do perfil atual
    currentIndex := 0
    for i, p in profiles {
        if (p = CurrentProfile)
            currentIndex := i
    }
    
    ; Calcula próximo perfil (circular)
    nextIndex := Mod(currentIndex, profiles.Length) + 1
    nextProfile := profiles[nextIndex]
    
    ; Atualiza variável global
    oldProfile := CurrentProfile
    CurrentProfile := nextProfile
    
    ; Salva no JSON com verificação
    saveSuccess := false
    if HotkeyData.Has("profiles") {
        HotkeyData["profiles"]["active"] := nextProfile
        
        ; Tenta salvar
        if Settings_SaveJSON(HOTKEYS_PATH, HotkeyData) {
            ; CORREÇÃO: Verifica se realmente salvou lendo o arquivo novamente
            try {
                testLoad := Settings_LoadJSON(HOTKEYS_PATH)
                if testLoad.Has("profiles") && testLoad["profiles"]["active"] = nextProfile {
                    saveSuccess := true
                }
            }
        }
    }
    
    ; Obtém metadados do perfil para feedback visual
    meta := Core_GetProfileMeta(nextProfile)
    
    ; Atualiza ícone da bandeja
    TraySetIcon("shell32.dll", meta.tray_icon_index)
    
    ; Recarrega hotkeys DINAMICAMENTE para aplicar o novo perfil sem precisar de Reload
    try {
        HotkeyLoader.Reload()
    } catch as err {
        try FileAppend(
            FormatTime(, "yyyy-MM-dd HH:mm:ss") . " | Erro ao recarregar hotkeys: " . err.Message . "`n",
            LOGS_PATH . "\profile_cycle.log"
        )
    }
    
    ; Feedback visual ao usuário com status de salvamento
    if saveSuccess {
        ToolTip(meta.icon . " Perfil: " . nextProfile . "`n`nHotkeys recarregadas automaticamente.")
    } else {
        ToolTip("❌ ERRO ao salvar perfil!`n`nPerfil mudou para: " . nextProfile . "`nMas NÃO foi salvo no JSON!`n`nVerifique permissões em:`n" . HOTKEYS_PATH)
    }
    SetTimer(() => ToolTip(), -4000)
    
    ; Log da mudança
    try {
        logMsg := FormatTime(, "yyyy-MM-dd HH:mm:ss") . " | Perfil: " . oldProfile . " → " . nextProfile
        logMsg .= " | Salvo: " . (saveSuccess ? "SIM" : "NÃO") . "`n"
        FileAppend(logMsg, LOGS_PATH . "\profile_cycle.log")
    }
}

; Obtém metadados do perfil (ícone, descrição, etc)
Core_GetProfileMeta(profile) {
    Global HotkeyData
    
    ; Valores padrão
    result := {
        icon: "📁",
        description: "",
        tray_icon_index: 44
    }
    
    ; Tenta obter do JSON
    if HotkeyData.Has("profiles") && HotkeyData["profiles"].Has("meta") {
        meta := HotkeyData["profiles"]["meta"]
        if meta.Has(profile) {
            profileMeta := meta[profile]
            if profileMeta.Has("icon")
                result.icon := profileMeta["icon"]
            if profileMeta.Has("description")
                result.description := profileMeta["description"]
            if profileMeta.Has("tray_icon_index")
                result.tray_icon_index := profileMeta["tray_icon_index"]
        }
    }
    
    return result
}

; #endregion

; ==============================================================================
; FIM DO ARQUIVO: Lib/Core.ahk
; ==============================================================================