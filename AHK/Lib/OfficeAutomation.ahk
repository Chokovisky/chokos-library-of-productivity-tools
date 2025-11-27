; ==============================================================================
; ARQUIVO: Lib\OfficeAutomation.ahk
; DESCRIÇÃO: Automação COM Blindada (Ignora Zumbis)
; ==============================================================================

; #region Funções de Automação Office acessíveis via Worker
Excel_ExportToPDF() {
    ToolTip("🚀 Carregando abas...")
    SetTimer () => ToolTip(), -1000
    RunWorker("ExcelExportSelector") 
}

Word_ExportToPDF() {
    ToolTip("🚀 Gerando PDF do Word...")
    SetTimer () => ToolTip(), -1000
    RunWorker("WordExportToPDF")
}
; #endregion

; --- [CLASSE DE JOBS] ---
class WorkerJobs {
    
    ; #region jobs de geração de PDF Office

    ;; JOB 1: EXCEL
    static ExcelExportSelector() {
        try {
            xl := WorkerJobs.GetActiveExcel()
            
            try {
                wb := xl.ActiveWorkbook
            } catch {
                throw Error("Excel encontrado, mas nenhuma pasta de trabalho ativa (Barra Amarela ou Tela Inicial?).")
            }
            
            if !wb 
                throw Error("Nenhuma planilha ativa.")

            fullPath := wb.FullName
            if (fullPath = "") or !InStr(fullPath, "\") {
                MsgBox("Salve o arquivo Excel antes de continuar.", "Worker")
                ExitApp()
            }
            
            ; --- GUI ---
            SelectorGui := Gui(, "Exportar PDFs - " . wb.Name)
            SelectorGui.SetFont("s10", "Segoe UI")
            SelectorGui.Add("Text",, "Selecione as abas:")
            
            LV := SelectorGui.Add("ListView", "w350 r10 +Checked -Hdr", ["Nome da Aba"])
            for sheet in wb.Sheets {
                LV.Add("Check", sheet.Name)
            }
            
            btnExport := SelectorGui.Add("Button", "w100 Default y+10", "Exportar")
            btnExport.OnEvent("Click", (*) => WorkerJobs.ProcessExcelExport(wb, LV, SelectorGui))
            
            SelectorGui.OnEvent("Close", (*) => ExitApp())
            SelectorGui.Show()
            WinWaitClose(SelectorGui)
            
        } catch as err {
            MsgBox("Erro Excel: " . err.Message)
            ExitApp()
        }
    }
    
    ;; HELPER: PROCESSAR EXPORTAÇÃO EXCEL
    static ProcessExcelExport(wb, LV, GuiObj) {
        GuiObj.Hide()
        savedPath := wb.Path
        baseName := SubStr(wb.Name, 1, InStr(wb.Name, ".",, -1) - 1)
        count := 0
        BatchMode := 0 
        
        Loop LV.GetCount() {
            rowNumber := A_Index
            if (LV.GetNext(rowNumber - 1, "Checked") == rowNumber) {
                sheetName := LV.GetText(rowNumber)
                targetSheet := wb.Sheets(sheetName)
                targetPath := savedPath . "\" . baseName . " - " . sheetName . ".pdf"
                
                finalPath := FileUtils.ResolveConflict(targetPath, &BatchMode)
                
                if (finalPath = "") {
                    TrayTip "Pulado: " . sheetName, "Excel Worker"
                    continue
                }
                
                TrayTip "Salvando: " . sheetName, "Excel Worker"
                try {
                    targetSheet.ExportAsFixedFormat(0, finalPath, 0, True, False)
                    count++
                }
            }
        }
        
        GuiObj.Destroy()

        if (count > 0) {
            Result := MsgBox(count . " PDFs gerados com sucesso!`n`nDeseja abrir a pasta?", "Concluído", "YesNo Icon? Default1")
            if (Result = "Yes")
                Run 'explorer.exe "' . savedPath . '"'
        } else {
            MsgBox("Nenhuma aba exportada.", "Aviso")
        }
        ExitApp()
    }

    ;; JOB 2: WORD
    static WordExportToPDF() {
        try {
            ; Também usa a lógica inteligente pro Word, só pra garantir
            try {
                wd := ComObjActive("Word.Application") 
            } catch {
                throw Error("Nenhum Word aberto encontrado.")
            }
            
            try {
                doc := wd.ActiveDocument
            } catch {
                throw Error("Word aberto, mas nenhum documento ativo.")
            }
            
            fullPath := doc.FullName
            if (fullPath = "") or !InStr(fullPath, "\") {
                MsgBox("Salve o documento Word antes de continuar.", "Worker")
                ExitApp()
            }

            targetPath := SubStr(fullPath, 1, InStr(fullPath, ".",, -1) - 1) . ".pdf"
            BatchMode := 0 
            finalPath := FileUtils.ResolveConflict(targetPath, &BatchMode)
            
            if (finalPath = "") {
                ExitApp()
            }

            TrayTip "Gerando PDF...", "Word Worker"
            doc.ExportAsFixedFormat(finalPath, 17, false, 0, 0, 0, 0, 0, true, true, 0, true, true, false)
            
            Result := MsgBox("PDF Criado!`n" . finalPath . "`n`nDeseja abrir a pasta?", "Word Export", "YesNo Icon? Default1")
            if (Result = "Yes")
                Run 'explorer.exe /select,"' . finalPath . '"'
            
        } catch as err {
            MsgBox("Erro Word: " . err.Message)
        }
        ExitApp()
    }

    ;; HELPER: GET ACTIVE EXCEL (Ignora Zumbis)
    static GetActiveExcel() {
        ; 1. Tenta pegar a janela do Excel que está VISÍVEL e ATIVA
        ; ahk_class XLMAIN é a classe da janela do Excel
        hWnd := WinExist("ahk_class XLMAIN")
        
        if !hWnd {
            ; Se não achou janela, tenta o método antigo como fallback
            return ComObjActive("Excel.Application")
        }

        ; 2. Mágica DLL para extrair o Objeto COM direto da Janela (Ignora processo zumbi) - evita erros de "Elemento não encontrado"
        ; IID_IDispatch = {00020400-0000-0000-C000-000000000046}
        IID := Buffer(16)
        DllCall("ole32\CLSIDFromString", "WStr", "{00020400-0000-0000-C000-000000000046}", "Ptr", IID)
        
        ptr := 0
        ; OBJID_NATIVEOM = -16
        HRes := DllCall("oleacc\AccessibleObjectFromWindow", "Ptr", hWnd, "UInt", -16, "Ptr", IID, "Ptr*", &ptr)
        
        if (HRes != 0 || ptr = 0) {
             ; Se falhar, tenta o método padrão
             return ComObjActive("Excel.Application")
        }

        ; 3. Converte ponteiro para Objeto AHK
        obj := ComValue(9, ptr, 1)
        
        ; O objeto retornado é a Janela, precisamos subir para a Aplicação
        try {
            return obj.Application
        } catch {
            return ComObjActive("Excel.Application")
        }
    }
    ; #endregion
}
    
; ==============================================================================
; FIM DO ARQUIVO: Lib\OfficeAutomation.ahk
; ==============================================================================