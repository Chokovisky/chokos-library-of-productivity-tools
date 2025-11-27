; ==============================================================================
; ARQUIVO: Lib\ScreenLock.ahk
; DESCRIÇÃO: Trava o input e tela até senha correta ser digitada
; ==============================================================================

Global UNLOCK_PASSWORD := "1234" ; <--- SUA SENHA AQUI

;; Função principal para travar a tela
System_CustomLock() {
    ; 1. Cria a GUI de bloqueio (Preta, tela cheia), ainda a customizar
    LockGui := Gui("+AlwaysOnTop -Caption +ToolWindow +OwnDialogs")
    LockGui.BackColor := "000000"
    LockGui.SetFont("s20 cRed", "Consolas")
    
    ; Texto centralizado
    LockGui.Add("Text", "x0 y0 w" A_ScreenWidth " h" A_ScreenHeight " Center VCenter", "SYSTEM LOCKED`nDigite a senha")
    LockGui.Show("x0 y0 w" A_ScreenWidth " h" A_ScreenHeight)
    
    ; 2. Loop de Segurança
    Loop {
        ; --- LÓGICA V2 (InputHook) ---
        ; L4 = Limite de 4 digitos
        ; T10 = Timeout de 10 segundos
        ; {Enter}{Esc} = Teclas que encerram a captura
        IH := InputHook("L4 T10", "{Enter}{Esc}")
        IH.Start()
        IH.Wait() ; Espera o usuário terminar de digitar
        
        ; Checa se a senha bate (IH.Input contém o que foi digitado)
        if (IH.Input = UNLOCK_PASSWORD) {
            LockGui.Destroy()
            ToolTip("SISTEMA DESBLOQUEADO")
            SetTimer () => ToolTip(), -2000
            Break ; Sai do loop e libera o PC
        }
        
        ; Se errou, o loop roda de novo e o InputHook reseta
    }
}

; ==============================================================================
; FIM DO ARQUIVO: Lib\ScreenLock.ahk
; ==============================================================================