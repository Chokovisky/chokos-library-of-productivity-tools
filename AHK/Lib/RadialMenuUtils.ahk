; ==============================================================================
; ARQUIVO: Lib\RadialMenuUtils.ahk
; DESCRIÇÃO: Integração com RadialMenu.exe contextual baseada em hotkeys.json
; ==============================================================================
;
; Nota: HotkeyData, CurrentProfile e HotkeyLoader são globais fornecidos por
; Settings/HotkeyLoader. As inicializações abaixo servem apenas para:
;   - satisfazer analisadores estáticos (#Warn, VSCode), e
;   - garantir defaults seguros caso ainda não tenham sido definidos.
;
if !IsSet(HotkeyData)
    HotkeyData := Map()
if !IsSet(CurrentProfile)
    CurrentProfile := "Normal"

class RadialMenuUtils {
    static ExePath          := A_ScriptDir . "\Tools\RadialMenu\RadialMenu.exe"
    static _onMsgRegistered := false
    static _callbackResult  := ""
    static _callbackReady   := false
    static _callbackTag     := 0x524D5253  ; 'RMRS'
    
    ;; Mostra menu radial baseado em perfil e contexto ativo
    static ShowContextual() {
        ; 1. Detectar contexto ativo
        activeContext := this._DetectContext()
        
        ; 2. Fazer lookup no HotkeyData["radial_menus"]
        menuData := this._GetMenuForContext(activeContext)
        
        ; AHK v2: objetos JSON vêm como Map/Array; usar .Has() e .Length (propriedade)
        if (!IsObject(menuData) || !menuData.Has("items") || menuData["items"].Length = 0) {
            ; Sem menu pra esse contexto
            return
        }
        
        ; 3. Montar lista de items pra passar pro exe (fallback CLI)
        itemsArg := this._BuildItemsArg(menuData["items"])
        
        ; 4. Chamar RadialMenu residente passando também a lista de items (para JSON/WM_COPYDATA)
        result := this._CallRadialMenu(itemsArg, menuData["items"])
        
        ; 5. Se cancelou, sai
        if (result = "" || result = "CANCELLED")
            return
        
        ; 6. Buscar action correspondente e executar
        this._ExecuteSelection(result, menuData["items"])
    }
    
    ;; Detecta qual contexto está ativo baseado na janela em foco
    static _DetectContext() {
        try {
            exe := WinGetProcessName("A")
        } catch {
            return "global"
        }
        
        ; Verifica nos contextos definidos em HotkeyData["contexts"]
        if (IsObject(HotkeyData) && HotkeyData.Has("contexts")) {
            for contextName, condition in HotkeyData["contexts"] {
                ; Heurística simples: se a condição contém o exe, consideramos match
                if InStr(condition, exe)
                    return contextName
            }
        }
        
        return "global"
    }
    
    ;; Busca menu para o perfil e contexto atual
    static _GetMenuForContext(context) {
        if (!IsObject(HotkeyData) || !HotkeyData.Has("radial_menus"))
            return ""
        
        menus := HotkeyData["radial_menus"]
        profile := CurrentProfile  ; global definido em Settings/HotkeyLoader
        
        ; Tenta perfil atual + contexto
        if (menus.Has(profile) && menus[profile].Has(context))
            return menus[profile][context]
        
        ; Fallback: perfil atual + global
        if (menus.Has(profile) && menus[profile].Has("global"))
            return menus[profile]["global"]
        
        ; Fallback: Normal + contexto
        if (menus.Has("Normal") && menus["Normal"].Has(context))
            return menus["Normal"][context]
        
        ; Fallback: Normal + global
        if (menus.Has("Normal") && menus["Normal"].Has("global"))
            return menus["Normal"]["global"]
        
        return ""
    }
    
    ;; Monta string de items pro argumento --items (fallback CLI)
    static _BuildItemsArg(items) {
        ; Formato: id:label:icon,id:label:icon
        parts := []
        for idx, item in items {
            if (!IsObject(item))
                continue
            ; Map em AHK v2 usa .Has() para checar chave
            id    := item.Has("id")    ? item["id"]    : ""
            label := item.Has("label") ? item["label"] : ""
            icon  := item.Has("icon")  ? item["icon"]  : ""
            parts.Push(id . ":" . label . ":" . icon)
        }
        return this._Join(parts, ",")
    }
     
    ;; Chama o RadialMenu residente via WM_COPYDATA.
    ;; Para o fluxo contextual, NÃO usamos mais o fallback via Exec/StdOut,
    ;; justamente para evitar coldstart duplo e bug de abrir outro exe.
    static _CallRadialMenu(itemsArg, items := "") {
        if (IsObject(items))
            return this._CallRadialMenuJson(items)

        ; Sem items em formato estruturado, não tentamos CLI aqui.
        ; O caller (ShowContextual) trata result="" como "cancelado/sem ação".
        return ""
    }

    ;; Caminho rápido: JSON + WM_COPYDATA para instância residente (warmup).
    static _CallRadialMenuJson(items) {
        if !FileExist(this.ExePath)
            return ""
 
        ; 1) Garante que há uma instância residente em background (incluindo janelas ocultas)
        prevDH := A_DetectHiddenWindows
        DetectHiddenWindows true
        hwnd := 0
        try {
            hwnd := WinExist("ahk_exe RadialMenu.exe")
            if (!hwnd) {
                Run(this.ExePath . " --background",, "Hide")
                Sleep 500
                hwnd := WinExist("ahk_exe RadialMenu.exe")
            }
        } finally {
            DetectHiddenWindows prevDH
        }
        if (!hwnd) {
            ; Falha explícita: não cai para CLI/coldstart.
            ; Log em arquivo para inspeção posterior.
            try {
                FileAppend(
                    "[" A_Now "] RadialMenuUtils: falha ao localizar instância residente de RadialMenu.exe após warmup.`n",
                    A_ScriptDir . "\RadialMenu_WM_COPYDATA.log",
                    "UTF-8"
                )
            }
            catch {
            }
            return ""
        }
 
        ; 2) Garante callback global para receber o resultado via WM_COPYDATA
        this._EnsureCallback()
 
        ; 3) Monta JSON no formato esperado pelo C#:
        ;    { "hwnd_callback": 12345, "items": [ { "id": "...", "label": "...", "icon": "..." }, ... ] }
        jsonItems := []
        for idx, item in items {
            if (!IsObject(item))
                continue
            id    := item.Has("id")    ? item["id"]    : ""
            label := item.Has("label") ? item["label"] : id
            icon  := item.Has("icon")  ? item["icon"]  : ""
            color := item.Has("color") ? item["color"] : ""
 
            obj := Map()
            obj["id"]    := id
            obj["label"] := label
            if (icon    != "")
                obj["icon"]  := icon
            if (color   != "")
                obj["color"] := color
            jsonItems.Push(obj)
        }
 
        ; 3b) Construção manual de JSON (sem depender de Jxon_Dump).
        ; Supõe que id/label/icon/color não contenham aspas duplas.
        json := '{"hwnd_callback":' . A_ScriptHwnd . ',"items":['
        first := true
        for _, it in jsonItems {
            if (!first)
                json .= ','
            first := false
            id    := it["id"]
            label := it["label"]
            icon  := it.Has("icon")  ? it["icon"]  : ""
            color := it.Has("color") ? it["color"] : ""
            json .= '{"id":"' . id . '","label":"' . label . '"'
            if (icon != "")
                json .= ',"icon":"' . icon . '"'
            if (color != "")
                json .= ',"color":"' . color . '"'
            json .= '}'
        }
        json .= ']}'
 
        ; 4) Envia JSON via WM_COPYDATA para o RadialMenu residente
        if !this._SendCopyData(hwnd, json)
            return ""
 
        ; 5) Aguarda retorno via WM_COPYDATA (callback)
        this._callbackResult := ""
        this._callbackReady  := false
 
        timeoutMs := 1500
        start := A_TickCount
        while (!this._callbackReady && A_TickCount - start < timeoutMs)
            Sleep 10
 
        if (!this._callbackReady)
            return ""
 
        result := this._callbackResult
        this._callbackResult := ""
        this._callbackReady  := false
        return result
    }

    ;; Fallback antigo: Exec + StdOut.
    static _CallRadialMenuExec(itemsArg) {
        if !FileExist(this.ExePath)
            return ""
        
        ; AutoHotkey v2: usar ComObject em vez de ComObjCreate
        try {
            shell := ComObject("WScript.Shell")
        } catch {
            ; Se COM falhar por algum motivo, apenas executa sem capturar stdout
            Run(this.ExePath . " --items " . Chr(34) . itemsArg . Chr(34))
            return ""
        }
        
        ; Monta comando com aspas usando Chr(34) para evitar problemas de escape
        quote := Chr(34)
        cmd   := quote . this.ExePath . quote . " --items " . quote . itemsArg . quote
        exec  := shell.Exec(cmd)
        
        ; Espera terminar
        while (exec.Status = 0)
            Sleep 10
        
        return Trim(exec.StdOut.ReadAll(), "`r`n `t")
    }

    ;; Garante registro único do handler WM_COPYDATA para respostas do RadialMenu
    static _EnsureCallback() {
        ; Em AHK v2, IsSet exige variável simples, então usamos apenas o flag estático.
        if (!this._onMsgRegistered) {
            OnMessage(0x4A, ObjBindMethod(this, "_OnCopyData"))
            this._onMsgRegistered := true
            this._callbackResult  := ""
            this._callbackReady   := false
            ; _callbackTag já inicializado como estático na classe
        }
    }

    ;; Envia WM_COPYDATA com JSON (UTF-8) para o RadialMenu residente
    static _SendCopyData(hwnd, json) {
        if (!hwnd)
            return false
 
        ; Prepara buffer UTF-8
        bufSize := StrPut(json, "UTF-8") - 1
        buf := Buffer(bufSize)
        StrPut(json, buf, "UTF-8")
 
        ; COPYDATASTRUCT (dwData, cbData, lpData)
        ; Layout oficial (x64):
        ;   dwData : ULONG_PTR (8 bytes)
        ;   cbData : DWORD     (4 bytes)
        ;   padding: 4 bytes
        ;   lpData : PVOID     (8 bytes)
        ; Total: 24 bytes => A_PtrSize*2 + 8
        cds := Buffer(A_PtrSize * 2 + 8, 0)
        NumPut("ptr", this._callbackTag, cds, 0)
        NumPut("int", bufSize,           cds, A_PtrSize)
        NumPut("ptr", buf.Ptr,           cds, A_PtrSize + 8)
 
        ; Não dependemos do valor de retorno de SendMessage para decidir sucesso,
        ; pois o WndProc do C# sempre retorna 0. Se a DllCall não explodir, consideramos ok.
        DllCall("User32.dll\SendMessageW"
            , "ptr", hwnd
            , "uint", 0x4A       ; WM_COPYDATA
            , "ptr", 0
            , "ptr", cds.Ptr
            , "ptr")
        return true
    }

    ;; Handler global para WM_COPYDATA com resultado do RadialMenu
    static _OnCopyData(wParam, lParam, msg, hwnd) {
        try {
            ; COPYDATASTRUCT layout alinhado com C# (dwData, cbData, padding, lpData)
            dwData := NumGet(lParam, 0,           "ptr")
            cbData := NumGet(lParam, A_PtrSize,   "int")
            pData  := NumGet(lParam, A_PtrSize+8, "ptr")
 
            if (dwData != this._callbackTag || cbData <= 0 || !pData)
                return 0
 
            json := StrGet(pData, cbData, "UTF-8")
            if (json = "")
                return 0
 
            ; Espera JSON no formato: { "selected": "id" } ou { "selected": null, "cancelled": true }
            ; Usamos Jxon_Load se disponível; fallback tosco se não.
            selected := ""
            try {
                if IsSet(Jxon_Load) {
                    obj := Jxon_Load(json)
                    if (IsObject(obj) && obj.Has("selected")) {
                        val := obj["selected"]
                        if (Type(val) = "String")
                            selected := val
                        else
                            selected := ""
                    }
                }
            } catch {
            }
            if (selected = "") {
                ; Considera cancelado
                this._callbackResult := ""
            } else {
                this._callbackResult := selected
            }
            this._callbackReady := true
        } catch {
        }
        return 0
    }
    
    ;; Executa action do item selecionado
    static _ExecuteSelection(selectedId, items) {
        if (selectedId = "")
            return
        
        for idx, item in items {
            if (!IsObject(item))
                continue
            if (item.Has("id") && item["id"] = selectedId) {
                if (item.Has("action") && item["action"] != "") {
                    action := item["action"]
                    ; Usa o mesmo mecanismo de execução de ações do sistema
                    HotkeyLoader.ExecuteAction(action)
                }
                return
            }
        }
    }
    
    ;; Helper: junta array com separador
    static _Join(arr, sep) {
        result := ""
        for i, v in arr {
            if (i > 1)
                result .= sep
            result .= v
        }
        return result
    }
}

; Função global pra chamar via hotkey
RadialMenu_ShowContextual() {
    RadialMenuUtils.ShowContextual()
}

; ==============================================================================
; FIM DO ARQUIVO: Lib\RadialMenuUtils.ahk
; ==============================================================================