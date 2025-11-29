# 🎡 Menu Radial ChokoLPT - Especificação

> Menu radial flexível que recebe configuração dinâmica

---

## Visão Geral

**Tech:** C# WPF .NET 10  
**Propósito:** Menu radial de ações rápidas, configurável por contexto/perfil

```
                    ╭─────────╮
               ╭────┤  Colar  ├────╮
              ╱     ╰─────────╯     ╲
        ╭────┤                       ├────╮
        │Undo│         ●───►        │Redo │
        ╰────┤     (cursor drag)    ├────╯
              ╲     ╭─────────╮     ╱
               ╰────┤ Copiar  ├────╯
                    ╰─────────╯
```

---

## Conceito Principal

Um único executável que:
1. Recebe lista de itens via **PostMessage** (rápido) ou **argumentos** (fallback)
2. Mostra menu radial com as opções recebidas
3. Usuário arrasta na direção da opção desejada
4. Retorna o **ID da ação** selecionada via stdout ou PostMessage de volta
5. AHK executa a ação correspondente

**Flexibilidade:** O mesmo .exe serve pra qualquer menu - clipboard, janelas, apps, o que quiser.

---

## Comunicação AHK ↔ C#

### Opção 1: PostMessage (Recomendado - Mais Rápido)

**AHK → C#:**
```
WM_COPYDATA com JSON:
{
  "items": [
    { "id": "copy", "label": "Copiar", "icon": "📋" },
    { "id": "paste", "label": "Colar", "icon": "📄" },
    { "id": "cut", "label": "Recortar", "icon": "✂️" }
  ],
  "title": "Clipboard",
  "hwnd_callback": 12345
}
```

**C# → AHK:**
```
WM_COPYDATA de volta para hwnd_callback:
{ "selected": "copy" }

Ou se cancelou:
{ "selected": null, "cancelled": true }
```

### Opção 2: Linha de Comando (Fallback)

**AHK chama:**
```
RadialMenu.exe --items "copy:Copiar:📋,paste:Colar:📄,cut:Recortar:✂️" --title "Clipboard"
```

**C# retorna via stdout:**
```
copy
```

Ou se cancelou:
```
CANCELLED
```

### Opção 3: Stdin JSON (Alternativa)

**AHK envia via stdin:**
```json
{"items":[{"id":"copy","label":"Copiar"},{"id":"paste","label":"Colar"}]}
```

**C# retorna via stdout:**
```
copy
```

---

## Estrutura do Item

| Campo | Tipo | Obrigatório | Descrição |
|-------|------|-------------|-----------|
| `id` | string | ✅ | Identificador retornado ao selecionar |
| `label` | string | ✅ | Texto exibido no menu |
| `icon` | string | ❌ | Emoji ou ícone (opcional) |
| `color` | string | ❌ | Cor específica do item (opcional) |

---

## Comportamentos Obrigatórios

### 1. Aparece na Posição do Mouse

O centro do menu radial aparece exatamente onde o cursor está.

---

### 2. Cursor Trava e Fica Invisível

Ao abrir:
- Cursor desaparece
- Mouse fica "travado" logicamente no centro
- Movimentos são relativos (delta), não absolutos

---

### 3. Seleção por Direção (Gesture)

- Usuário arrasta na direção do item desejado
- Item destaca visualmente quando direção aponta pra ele
- Soltar o botão (ou tecla) confirma a seleção
- Zona morta no centro = nenhuma seleção

---

### 4. Feedback Visual

- Item sob o cursor fica destacado
- Linha do centro até o item selecionado
- Animação suave de highlight

---

### 5. Cancelamento

- ESC cancela
- Clicar fora cancela
- Voltar pro centro e soltar = cancela (zona morta)

Ao cancelar, retorna indicador de cancelamento (não uma ação).

---

### 6. Não Roubar Foco

Assim como o Dashboard, não deve roubar foco da janela ativa.

---

### 7. Always On Top

Fica por cima de tudo enquanto aberto.

---

## Layout Dinâmico

O menu se adapta ao número de itens:

| Itens | Layout |
|-------|--------|
| 2 | Esquerda / Direita |
| 3 | Triângulo |
| 4 | Cruz (cima/baixo/esquerda/direita) |
| 5-6 | Hexágono |
| 7-8 | Octógono |
| 9+ | Círculo dividido igualmente |

Os itens são distribuídos uniformemente em círculo.

---

## Menus Predefinidos (Exemplos de Uso)

### Menu Clipboard
```json
{
  "items": [
    { "id": "copy", "label": "Copiar", "icon": "📋" },
    { "id": "paste", "label": "Colar", "icon": "📄" },
    { "id": "cut", "label": "Recortar", "icon": "✂️" },
    { "id": "paste_plain", "label": "Colar Sem Formatação", "icon": "📝" }
  ]
}
```

### Menu Janelas
```json
{
  "items": [
    { "id": "ontop", "label": "Always on Top", "icon": "📌" },
    { "id": "borderless", "label": "Sem Bordas", "icon": "🖼️" },
    { "id": "opacity", "label": "Opacidade", "icon": "👁️" },
    { "id": "minimize", "label": "Minimizar", "icon": "➖" },
    { "id": "close", "label": "Fechar", "icon": "❌" }
  ]
}
```

### Menu Apps Rápidos
```json
{
  "items": [
    { "id": "notion", "label": "Notion", "icon": "📓" },
    { "id": "obsidian", "label": "Obsidian", "icon": "💎" },
    { "id": "terminal", "label": "Terminal", "icon": "💻" },
    { "id": "explorer", "label": "Explorer", "icon": "📁" }
  ]
}
```

---

## Integração com hotkeys.json

Possibilidade de definir menus no JSON:

```json
{
  "radial_menus": {
    "clipboard": {
      "items": [
        { "id": "copy", "label": "Copiar", "action": "Send(^c)" },
        { "id": "paste", "label": "Colar", "action": "Send(^v)" }
      ]
    },
    "windows": {
      "items": [
        { "id": "ontop", "label": "No Topo", "action": "Win_ToggleOnTop" },
        { "id": "borderless", "label": "Sem Borda", "action": "Win_ToggleBorderless" }
      ]
    }
  },
  
  "hotkeys": [
    {
      "id": "radial_clipboard",
      "key": "CapsLock & c",
      "action": "RadialMenu.Show(clipboard)",
      "description": "Menu radial de clipboard"
    }
  ]
}
```

---

## Fluxo Completo

```
1. Usuário pressiona CapsLock + C
2. AHK detecta hotkey
3. AHK lê menu "clipboard" do JSON
4. AHK envia dados via PostMessage para RadialMenu.exe
5. Menu aparece na posição do mouse
6. Usuário arrasta pra direção "Colar"
7. Menu fecha e retorna "paste" via PostMessage
8. AHK recebe "paste"
9. AHK executa action correspondente: Send(^v)
```

---

## Argumentos de Linha de Comando

| Argumento | Descrição |
|-----------|-----------|
| `--items` | Lista de itens (formato: id:label:icon,id:label:icon) |
| `--title` | Título opcional do menu |
| `--stdin` | Ler configuração do stdin como JSON |
| `--hwnd` | HWND do AHK pra callback via PostMessage |
| `--x` | Posição X (default: posição do mouse) |
| `--y` | Posição Y (default: posição do mouse) |

---

## Estrutura de Projeto

```
CSharp/
├── ChokoLPT.Shared/                  # Biblioteca compartilhada
│   ├── ChokoLPT.Shared.csproj
│   ├── Helpers/
│   │   └── Win32.cs                  # P/Invoke (GetCursorPos, SetCursorPos, 
│   │                                 #   ShowCursor, MonitorFromPoint, 
│   │                                 #   GetMonitorInfo, FindWindow, PostMessage,
│   │                                 #   GetForegroundWindow, structs POINT/RECT/etc)
│   ├── Services/
│   │   ├── HotkeyLoader.cs           # Carrega hotkeys.json, cache
│   │   └── MessageService.cs         # PostMessage/WM_COPYDATA helpers
│   └── Models/
│       ├── HotkeyConfig.cs           # ProfilesConfig, HotkeyItem, etc
│       └── FlexibleBoolConverter.cs  # Converter tolerante para bool
│
├── RadialMenu/
│   ├── RadialMenu.csproj             # .NET 10, WPF, referencia Shared
│   ├── App.xaml                      # Recursos globais
│   ├── App.xaml.cs                   # Entry point, singleton, parse args
│   ├── MainWindow.xaml               # Layout do menu circular
│   ├── MainWindow.xaml.cs            # Lógica de seleção por gesture
│   ├── Models/
│   │   └── RadialMenuItem.cs         # Modelo específico do item radial
│   └── Helpers/
│       └── GeometryHelper.cs         # Cálculo de posições circulares
│
└── HKCheatsheetOverlay/              # Futuramente também referencia Shared
```

### Referência ao Shared

No `RadialMenu.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\ChokoLPT.Shared\ChokoLPT.Shared.csproj" />
</ItemGroup>
```

### O que vem do Shared vs Local

| Componente | Origem |
|------------|--------|
| Win32 P/Invoke | **Shared** |
| MessageService | **Shared** |
| HotkeyLoader | **Shared** |
| ProfilesConfig, HotkeyItem | **Shared** |
| FlexibleBoolConverter | **Shared** |
| RadialMenuItem | Local |
| GeometryHelper | Local |
| UI/Layout | Local |

---

## Build

### Primeiro: Criar o Shared (se ainda não existir)

```bash
cd CSharp
dotnet new classlib -n ChokoLPT.Shared -f net10.0-windows
```

### Depois: Build do RadialMenu

```bash
cd CSharp/RadialMenu
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

**Destino:** `AHK/Tools/RadialMenu/RadialMenu.exe`

### Ordem de Desenvolvimento

1. Criar `ChokoLPT.Shared` com Win32.cs e Models básicos
2. Copiar do HKCheatsheetOverlay o que for reutilizável
3. Criar `RadialMenu` referenciando Shared
4. Depois refatorar HKCheatsheetOverlay pra usar Shared também

---

## Checklist de Requisitos

### Arquitetura
- [ ] ChokoLPT.Shared criado
- [ ] Win32.cs no Shared (P/Invoke compartilhado)
- [ ] Models no Shared (reutilizáveis)
- [ ] MessageService no Shared
- [ ] RadialMenu referencia Shared

### Comportamento
- [ ] Aparece na posição do mouse
- [ ] Cursor trava e fica invisível
- [ ] Seleção por direção (arrastar)
- [ ] Zona morta no centro
- [ ] ESC cancela
- [ ] Não rouba foco
- [ ] Always on top

### Comunicação
- [ ] Recebe itens via PostMessage (WM_COPYDATA)
- [ ] Recebe itens via argumentos (fallback)
- [ ] Recebe itens via stdin JSON
- [ ] Retorna seleção via PostMessage
- [ ] Retorna seleção via stdout (fallback)
- [ ] Indica cancelamento claramente

### Visual
- [ ] Layout adapta ao número de itens
- [ ] Highlight do item selecionado
- [ ] Linha indicadora do centro ao item
- [ ] Suporta ícones (emoji)
- [ ] Dark theme

### Performance
- [ ] Abre instantaneamente
- [ ] Zero delay na resposta ao movimento
- [ ] Executável único

---

*Especificação v1.0 - Novembro 2024*
