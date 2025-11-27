; ==============================================================================
; ARQUIVO: Lib\FileUtils.ahk (Corrigido)
; DESCRIÇÃO: Utilitários para manipulação de arquivos (conflitos, nomes únicos, etc.)
; ==============================================================================

class FileUtils {
    ;; Pega um caminho único, adicionando (1), (2), etc. se necessário
    static GetUniquePath(fullPath) {
        if !FileExist(fullPath)
            return fullPath
        SplitPath fullPath, &name, &dir, &ext, &nameNoExt
        i := 1
        Loop {
            newPath := dir . "\" . nameNoExt . " (" . i . ")." . ext
            if !FileExist(newPath)
                return newPath
            i++
        }
    }
    ;; Resolve conflitos de arquivo com GUI, retorna o caminho final a ser usado
    static ResolveConflict(fullPath, &BatchMode) {
        if !FileExist(fullPath)
            return fullPath

        if IsSet(BatchMode) {
            switch BatchMode {
                case 1: 
                    try FileDelete(fullPath)
                    return fullPath
                case 2: 
                    return this.GetUniquePath(fullPath)
                case 3: 
                    return ""
            }
        } else {
            BatchMode := 0
        }

        Result := this.ShowConflictGui(fullPath, &isChecked)
        
        if (isChecked)
            BatchMode := Result
        
        switch Result {
            case 1: 
                try FileDelete(fullPath)
                return fullPath
            case 2: 
                return this.GetUniquePath(fullPath)
            case 3: 
                return ""
            default: 
                return ""
        }
    }

    ;; GUI para resolver conflitos de arquivos
    static ShowConflictGui(fullPath, &isChecked) {
        SplitPath fullPath, &fileName
        
        ConfGui := Gui("+AlwaysOnTop +Owner", "Conflito de Arquivo")
        ConfGui.SetFont("s9", "Segoe UI")
        
        ConfGui.Add("Text", "w300", "O arquivo já existe:")
        
        ; FIX: Definir Negrito ANTES de adicionar o texto
        ConfGui.SetFont("bold")
        ConfGui.Add("Text", "w300 +Wrap cRed", fileName) ; Removemos 'bold' daqui
        ConfGui.SetFont("norm") ; Resetamos para normal para os próximos controles
        
        ConfGui.Add("Text", "w300", "O que deseja fazer?")
        
        btnOver := ConfGui.Add("Button", "w300 h30", "Sobrescrever (Substituir)")
        btnNew  := ConfGui.Add("Button", "w300 h30 Default", "Criar Novo (Salvar com índice)")
        btnSkip := ConfGui.Add("Button", "w300 h30", "Pular este arquivo")
        
        cbBatch := ConfGui.Add("Checkbox", "y+15", "Fazer isso para os próximos conflitos")
        
        UserChoice := 0 
        
        btnOver.OnEvent("Click", (*) => (UserChoice := 1, ConfGui.Hide()))
        btnNew.OnEvent("Click",  (*) => (UserChoice := 2, ConfGui.Hide()))
        btnSkip.OnEvent("Click", (*) => (UserChoice := 3, ConfGui.Hide()))
        ConfGui.OnEvent("Close", (*) => (UserChoice := 3, ConfGui.Destroy()))
        
        ConfGui.Show()
        WinWaitClose(ConfGui)
        
        isChecked := cbBatch.Value
        return UserChoice
    }
}

; ==============================================================================
; FIM DO ARQUIVO: Lib\FileUtils.ahk
; ==============================================================================