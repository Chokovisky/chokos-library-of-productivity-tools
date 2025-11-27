; ==============================================================================
; ARQUIVO: Config/Settings.ahk
; DESCRIÇÃO: Configurações Globais - Carrega JSONs UMA VEZ e define variáveis
; ==============================================================================

; #region CONFIGURAÇÕES DO AUTOHOTKEY
A_MaxHotkeysPerInterval := 255  ; Previne erro de scroll rápido
; #endregion

; #region FORÇAR ADMIN
if not A_IsAdmin {
    Run "*RunAs `"" A_ScriptFullPath "`""
    ExitApp
}
; #endregion

; #region PATHS GLOBAIS
Global APP_NAME := "ChokoLPT"
Global APP_DATA_PATH := EnvGet("LocalAppData") . "\" . APP_NAME
Global CONFIG_PATH := APP_DATA_PATH . "\config.json"
Global HOTKEYS_PATH := APP_DATA_PATH . "\hotkeys.json"
Global LOGS_PATH := APP_DATA_PATH . "\logs"
; #endregion

; #region INICIALIZAÇÃO DE DIRETÓRIOS
Settings_InitDirectories() {
    if !DirExist(APP_DATA_PATH)
        DirCreate(APP_DATA_PATH)
    if !DirExist(LOGS_PATH)
        DirCreate(LOGS_PATH)
}
Settings_InitDirectories()
; #endregion

; #region CARREGAR JSONs - CRÍTICO: CARREGAR APENAS UMA VEZ!
Settings_LoadJSON(path) {
    if !FileExist(path) {
        try FileAppend(
            FormatTime(, "yyyy-MM-dd HH:mm:ss") . " | ARQUIVO NÃO EXISTE: " . path . "`n",
            LOGS_PATH . "\settings_errors.log"
        )
        return Map()
    }
    try {
        ; Lê o arquivo
        txt := FileRead(path, "UTF-8-RAW")
        
        ; Remove BOM se existir
        txt := RegExReplace(txt, "^\xEF\xBB\xBF", "")
        if (Ord(SubStr(txt, 1, 1)) = 0xFEFF)
            txt := SubStr(txt, 2)
        
        ; Limpa whitespace inicial
        txt := LTrim(txt, " `t`n`r")
        
        ; USA JSON.parse() - keepbooltype=false (usa 1/0), as_map=true (retorna Map)
        result := JSON.parse(txt, false, true)
        return result
    } catch as err {
        try FileAppend(
            FormatTime(, "yyyy-MM-dd HH:mm:ss") . " | Erro ao carregar " . path . ": " . err.Message . "`n",
            LOGS_PATH . "\settings_errors.log"
        )
        return Map()
    }
}

Settings_SaveJSON(path, data) {
    try {
        if !IsObject(data)
            throw Error("Data is not an object")
        
        if Type(data) = "Map" && data.Count < 1
            throw Error("Map is empty - refusing to save")
        
        ; USA JSON.stringify() - expandlevel=10 (expande tudo), space="  " (2 espaços)
        jsonText := JSON.stringify(data, 10, "  ")
        
        if StrLen(jsonText) < 10
            throw Error("JSON output too small: " . jsonText)
        
        ; Backup antes de sobrescrever
        if FileExist(path) {
            backupPath := path . ".bak"
            try {
                if FileExist(backupPath)
                    FileDelete(backupPath)
                FileCopy(path, backupPath)
            }
        }
        
        if FileExist(path)
            FileDelete(path)
        FileAppend(jsonText, path, "UTF-8-RAW")
        return true
    } catch as err {
        try FileAppend(
            FormatTime(, "yyyy-MM-dd HH:mm:ss") . " | Erro ao salvar " . path . ": " . err.Message . "`n",
            LOGS_PATH . "\settings_errors.log"
        )
        return false
    }
}

; CRÍTICO: Carrega os JSONs UMA ÚNICA VEZ no início
Global ConfigData := Settings_LoadJSON(CONFIG_PATH)
Global HotkeyData := Settings_LoadJSON(HOTKEYS_PATH)
; #endregion

; #region ESTADO GLOBAL - Perfil Ativo
; IMPORTANTE:
;   - Para evitar estado "sujo" entre execuções, SEMPRE iniciamos em "Normal".
;   - Core_CycleProfile ainda persiste o perfil no JSON, mas o startup ignora isso.
;   - Isso garante que ao iniciar o script o perfil default seja sempre "Normal".
Global CurrentProfile := "Normal"
; #endregion

; #region FUNÇÕES DE ACESSO À CONFIG
Config_Get(key, defaultValue := "") {
    keys := StrSplit(key, ".")
    current := ConfigData
    
    for k in keys {
        if !IsObject(current) || !current.Has(k)
            return defaultValue
        current := current[k]
    }
    return current
}

Config_Set(key, value) {
    keys := StrSplit(key, ".")
    current := ConfigData
    
    for i, k in keys {
        if (i = keys.Length) {
            current[k] := value
        } else {
            if !current.Has(k)
                current[k] := Map()
            current := current[k]
        }
    }
    
    return Settings_SaveJSON(CONFIG_PATH, ConfigData)
}

Config_Reload() {
    Global ConfigData := Settings_LoadJSON(CONFIG_PATH)
    Global HotkeyData := Settings_LoadJSON(HOTKEYS_PATH)
    
    ; Atualiza perfil ativo
    Global CurrentProfile := (HotkeyData.Has("profiles") && HotkeyData["profiles"].Has("active"))
        ? HotkeyData["profiles"]["active"]
        : "Normal"
}
; #endregion

; #region PATHS DE APPS
GetAppPath(appName) {
    if ConfigData.Has("paths") && ConfigData["paths"].Has(appName)
        return ConfigData["paths"][appName]
    
    switch appName {
        case "notion":
            return "C:\Users\" . A_UserName . "\AppData\Local\Programs\Notion\Notion.exe"
        case "obsidian":
            return "C:\Users\" . A_UserName . "\AppData\Local\Obsidian\Obsidian.exe"
        default:
            return ""
    }
}

Global PATH_NOTION := GetAppPath("notion")
Global PATH_OBSIDIAN := GetAppPath("obsidian")
; #endregion

; ==============================================================================
; FIM DO ARQUIVO: Config/Settings.ahk
; ==============================================================================