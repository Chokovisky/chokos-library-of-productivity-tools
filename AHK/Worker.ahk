; ==============================================================================
; ARQUIVO: Worker.ahk
; DESCRIÇÃO: Dispatcher de Jobs em Modo Usuário Comum (versão dinâmica)
; ==============================================================================

#Requires AutoHotkey v2.0
#SingleInstance Off

#Include "Lib\DelegateWorker.ahk"
#Include "Lib\OfficeAutomation.ahk"
#Include "Lib\FileUtils.ahk"

if A_Args.Length < 1 {
    MsgBox "Worker: Sem argumentos."
    ExitApp
}

MethodName := A_Args[1]

try {
    if WorkerJobs.HasMethod(MethodName) {
        WorkerJobs.%MethodName%()
    } else {
        MsgBox "Worker Error: Método '" . MethodName . "' desconhecido."
    }
} catch as err {
    MsgBox "Worker Crash: " . err.Message
}

; Se o Job for rápido (Word), ele termina. Se tiver GUI (Excel), ele espera.
; Para Jobs sem GUI, adicione ExitApp() no final da função deles na Classe.

; ==============================================================================
; FIM DO ARQUIVO: Worker.ahk
; ==============================================================================