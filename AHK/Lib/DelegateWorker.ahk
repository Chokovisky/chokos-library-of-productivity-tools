; ==============================================================================
; ARQUIVO: Lib\DelegateWorker.ahk
; DESCRIÇÃO: Despacha o Worker como USUÁRIO COMUM (bypass no Admin)
; ==============================================================================

;; --- FUNÇÃO PRINCIPAL DE DELEGAÇÃO DO WORKER ---
RunWorker(JobName) {
    WorkerPath := A_ScriptDir . "\Worker.ahk" ; Caminho do Worker
    
    if !FileExist(WorkerPath) {
        MsgBox("CRÍTICO: Worker.ahk não encontrado!`n" . WorkerPath)
        return
    }

    Args := '"' . WorkerPath . '" "' . JobName . '"'
    
    try {
        ShellRun(A_AhkPath, Args)
    } catch as err {
        MsgBox("Erro ShellRun: " . err.Message)
    }
}

; --- FUNÇÃO DE ENGENHARIA (DE-ELEVATION v2) ---
ShellRun(pTarget, pArgs := "", pDir := "", pVerb := "open") {
    try {
        ; CORREÇÃO AQUI: v2 usa ComObject, não ComObjCreate
        shell := ComObject("Shell.Application")
        
        ; FindWindowSW(0, 0, 8, 0, 1) -> 8=SWC_DESKTOP, 1=SWFO_NEEDDISPATCH
        ; Pega o objeto do Desktop (que roda como Usuário)
        desktop := shell.Windows.FindWindowSW(0, 0, 8, 0, 1)
        
        ; Executa através do Desktop, herdando permissão de Usuário
        desktop.Document.Application.ShellExecute(pTarget, pArgs, pDir, pVerb, 1)
    } 
    catch {
        ; Fallback
        Run '"' . pTarget . '" ' . pArgs, pDir
    }
}
; ==============================================================================
; FIM DO ARQUIVO: Lib\DelegateWorker.ahk
; ==============================================================================