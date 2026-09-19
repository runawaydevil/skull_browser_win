# Skull Wins 0.01

Browser modal para Windows, autoral, por Pablo Murad.

---

## Contexto

O Skull Browser (`B:\NetShare\Projetos\skull browser\skull-browser`) é um fork do luakit:
núcleo C, WebKitGTK 4.1 sobre GTK 3, e cerca de 19 mil linhas de Lua em `lib/`
fazendo tudo que o usuário vê. O README declara o limite sem rodeios: Linux only,
e no Windows só roda sob WSL com WSLg.

Duas restrições do projeto atual são a razão deste aqui existir separado.

A primeira é a ADR `docs/decisoes/001-webkitgtk-api.md`, que decidiu ficar no
`webkit2gtk-4.1` e deixou uma consequência operacional: não adicionar código novo
em `extension/`. Aquelas 1.900 linhas de C rodam uma segunda VM Lua dentro do
processo de renderização do WebKit, usando uma API que a própria documentação do
upstream avisa que pode ser removida. É código com prazo de validade.

A segunda é geográfica. WebKitGTK não roda em Windows nativo, e migrar arrastaria
GTK 3 para GTK 4 junto.

O Skull Wins não é um port, nem compartilha código. É um projeto independente que
persegue o mesmo produto em outra plataforma: navegação modal no espírito do vim,
`gopher://` de primeira classe, e uma camada Lua grande que o usuário reescreve.
Versão 0.01, autoria de Pablo Murad, bilíngue pt-BR e inglês desde o primeiro commit.

**Diretório:** `B:\NetShare\Projetos\Skull Wins` (vazio hoje).

---

## Decisões já fechadas

| Decisão | Escolha | Por quê |
|---|---|---|
| Plataforma | Windows apenas | Nativo, sem WSL. Independente do fork Linux. |
| Motor | WebView2 (Chromium do Edge) | Já vem no Windows 11, atualizado pela Microsoft. Não herdamos manutenção de Chromium. |
| Host | C# / .NET 8 + WPF | WebView2 tem suporte de primeira classe no .NET. O valor do projeto está na camada Lua, não no host. |
| VM Lua | NLua (Lua 5.4 via KeraLua) | Lua de verdade, não dialeto. O `rc.lua` do usuário precisa ser Lua real. |
| Idiomas | pt-BR e inglês | Desde a 0.01, com detecção automática e override no `rc.lua`. |
| Processo web embutido | **Não existe** | O equivalente ao `extension/` do Skull Browser é JavaScript injetado. Resolve a ADR 001 por eliminação. |
| Licença | GPLv3 | Mesma do Skull Browser. Remove dúvida de proveniência e custa nada. |
| Nome | `skull.exe`, `skull://`, `%APPDATA%\skull` | Não conflita: o fork Linux não roda em Windows nativo. |
| Escopo 0.01 | Onze fases, sete módulos cortados | Ver seção de escopo. |

---

## Arquitetura

### Forma geral

```
┌─────────────────────────────────────────────────────────┐
│  SkullWins.App (WPF)                                    │
│  janela · abas · statusbar · cmdline · WebView2 hosts    │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────┴────────────────────────────────┐
│  SkullWins.Core                                          │
│  VM Lua · bindings · sinais · roteamento de teclado       │
└──┬──────────────────┬───────────────────┬────────────────┘
   │                  │                   │
┌──┴────────┐  ┌──────┴──────┐  ┌─────────┴────────┐
│ Protocols │  │   Storage   │  │   lua/  (~60 mód) │
│ gopher    │  │   SQLite    │  │  o projeto mora   │
└───────────┘  └─────────────┘  └───────────────────┘
```

`SkullWins.Core` não referencia `SkullWins.App`. Isso permite subir a VM Lua e
carregar os módulos num teste sem abrir janela, que é o que o `make run-tests` do
Skull Browser faz hoje com `skull.so`.

### Entrada de teclado

Este é o ponto mais delicado do projeto e a primeira coisa a provar.

O caminho óbvio não funciona. A documentação da Microsoft define acelerador assim:
uma tecla só conta se Ctrl ou Alt estiverem pressionados, ou se ela não mapear para
caractere. Escape é exceção permanente. Ou seja, `j`, `f` e `:` sozinhos **nunca**
chegam em `AcceleratorKeyPressed`. Navegação modal precisa exatamente dessas teclas.

O caminho que funciona é o do Vimium, que faz isso em Chromium há mais de dez anos:
capturar no JavaScript da página, antes dos handlers do site.

```
tecla
 └─> skull-input.js (keydown, capture: true, injetado em document-start)
      ├─ modo é normal?  preventDefault + stopPropagation + postMessage(host)
      └─ modo é insert?  deixa passar
           │
           └─> WebMessageReceived (C#)
                └─> Core.Input.Dispatch()
                     └─> Lua: modes.lua decide, binds.lua executa
```

O modo vive no host, mas a decisão em JS precisa ser síncrona. Resolve-se
espelhando: toda troca de modo empurra o novo estado para todos os frames via
`PostWebMessageAsString`, e o JS guarda numa variável local. O JS nunca pergunta,
só consulta o espelho.

Três mecanismos complementares:

- `AcceleratorKeyPressed` continua útil para o que ele de fato cobre: Escape,
  combinações com Ctrl e Alt, e teclas de função.
- `CoreWebView2Settings.AreBrowserAcceleratorKeysEnabled = false` desliga os atalhos
  nativos do Edge (Ctrl+P, Ctrl+F, Ctrl+Shift+I) para que possam ser rebindados.
- Quando o foco está na barra de comando ou na statusbar, o WPF trata as teclas
  direto. Nada disso passa pelo WebView2.

**Limitações conhecidas e aceitas.** `AddScriptToExecuteOnDocumentCreated` promete
injetar em todos os frames, mas o rastreador de bugs do WebView2 documenta que na
prática só cobre frames de mesma origem, e que iframes criados dinamicamente às
vezes ficam de fora. Mitigação: assinar `CoreWebView2.FrameCreated` e injetar por
frame via `CoreWebView2Frame`. Ainda assim, foco dentro de um iframe cross-origin
que escapou perde teclas. Widgets nativos (`<select>` aberto, visualizador de PDF do
Edge, diálogos do sistema) também não passam por JS. A saída para esses casos é
Escape, que sempre chega, devolvendo o foco ao host.

### Interceptação de requisições

Um mecanismo, três responsabilidades. No Skull Browser são três sistemas separados
(`register_scheme` em C, `chrome.lua`, e `adblock_wm.lua` dentro da extensão).

`CoreWebView2.WebResourceRequested`, com filtro, numa fila curta:

1. Esquema registrado em Lua casou? Lua devolve `{status, mime, body, headers}`. Para aqui.
2. Adblock bate na URL? Responde 204 vazio. Para aqui.
3. Passa adiante.

Serve `gopher://`, `skull://about`, `skull://history`, `skull://downloads`,
`skull://help`, `skull://settings` e o adblock inteiro.

**Restrição que muda o desenho.** A documentação é explícita: as registrações de
esquema são "valid and immutable throughout the lifetime of the associated WebView2s'
browser process", e ambientes que compartilham o processo precisam registrar
exatamente os mesmos esquemas, senão a criação do ambiente falha. O Lua ainda não
carregou nesse momento. Então:

- A lista de esquemas sai de um arquivo lido cru na partida (`config.json` ao lado
  do `rc.lua`, ou uma linha especial no próprio `rc.lua` lida por regex antes da VM).
- `skull`: `HasAuthorityComponent = true`, `TreatAsSecure = true` (para `localStorage`
  e contexto seguro nas páginas internas).
- `gopher`: `HasAuthorityComponent = true`, `TreatAsSecure = false`.
- `gemini`: registrado desde já, sem handler na 0.01. Registrar depois exigiria
  reiniciar o browser.

Registro de esquema deixa de ser dinâmico. É a única concessão real que o WebView2
impõe ao desenho.

Detalhe de implementação que evita um bug conhecido: o stream da resposta precisa ser
embrulhado numa classe que se descarta quando o WebView2 termina de ler, porque o
WebView2 não fecha o stream sozinho (issue 2513 do WebView2Feedback). A própria
documentação da Microsoft publica a classe `ManagedStream` para isso.

### Abas

Um `WebView2` por aba, todos compartilhando um `CoreWebView2Environment` único, com
`UserDataFolder` em `%APPDATA%\skull\profile`. Abas inativas ficam descarregadas do
visual tree mas vivas, como no `notebook` do GTK.

### Gopher

No Skull Browser, `lib/gopher.lua` faz socket com LuaSocket e monta HTML, tudo em
Lua. Aqui a divisão muda, e melhora:

- Rede em C# (`TcpClient` assíncrono). Não trava a janela, coisa que socket síncrono
  em Lua faz.
- Parse do menu (RFC 1436) em C#, com bytes gravados em arquivo como fixture. Testável
  sem rede e sem janela.
- **Renderização para HTML em Lua.** É o que o usuário vai querer mudar e tematizar.

Fronteira: `SkullWins.Protocols` devolve `IReadOnlyList<GopherItem>`, o Lua vira HTML.

### Disco

```
%APPDATA%\skull\
├── rc.lua           config do usuário
├── theme.lua        tema
├── config.json      esquemas + locale (lido antes da VM Lua)
├── history.db       SQLite
├── bookmarks.db     SQLite
├── downloads.db     SQLite
├── adblock\         listas baixadas
└── profile\         UserDataFolder do WebView2
```

Cópia de fábrica de `rc.lua`, `theme.lua` e `config.json` instalada ao lado do
`skull.exe`, usada quando não há nada em `%APPDATA%`. Mesmo esquema de fallback que
o `/usr/local/etc/xdg/skull/` do Skull Browser.

---

## Estrutura do repositório

```
SkullWins.sln
src/
├── SkullWins.App/           WPF. Entry point, MainWindow, TabStrip, StatusBar, CmdLine.
│   ├── Views/
│   ├── Input/               AcceleratorKeyRouter, WpfKeyRouter
│   └── WebViews/            WebViewHost, EnvironmentFactory, SchemeBootstrap
├── SkullWins.Core/          Sem WPF. Testável headless.
│   ├── Lua/                 LuaHost, bindings, marshalling
│   ├── Api/                 skull, window, webview, msg, timer, net, i18n
│   ├── Signals/             add_signal / emit_signal
│   └── Scheme/              pipeline de WebResourceRequested
├── SkullWins.Protocols/     Gopher (RFC 1436). Funções puras. Gemini depois.
├── SkullWins.Storage/       SQLite: history, bookmarks, downloads, quickmarks
└── SkullWins.Tests/         xUnit + runner Lua headless
lua/
├── init.lua
├── modes.lua  binds.lua  window.lua  webview.lua
├── i18n.lua
├── locale/en.lua  locale/pt_BR.lua
├── lousy/         signal, bind, mode, util, uri, theme
├── chrome/        about, help, history, bookmarks, downloads, settings, adblock
├── protocols/     gopher_render.lua
└── js/            skull-input.js, follow.js, select.js, formfiller.js
config/
├── rc.lua  theme.lua  config.json
docs/
├── decisoes/      ADRs, em português
└── README.pt-BR.md
build/
├── skull.iss      Inno Setup
└── icons/
.github/workflows/build.yml
README.md          inglês
AUTHORS
LICENSE
```

O diretório `lua/` é onde o projeto mora. `src/` deve ficar chato.

---

## Contrato da API Lua

O host expõe ao Lua uma superfície que espelha a do Skull Browser, com `skull` no
lugar de `luakit`. Isso não é nostalgia: é para que quem escreveu config para um
consiga ler a do outro.

```lua
skull.version, skull.locale, skull.config_dir, skull.data_dir, skull.install_dir
skull.register_scheme(name, handler)   -- handler(uri) -> {status, mime, body}
skull.spawn(cmd), skull.selection, skull.idle_add(fn)

window.new(uris) -> w
w.tabs, w:new_tab(uri), w:close_tab(), w:set_mode(name), w:notify(str), w:error(str)

view.uri, view.title, view.progress, view.is_loading, view.history
view:load_string(html, base_uri), view:eval_js(src, opts), view:reload(), view:go_back()
view:add_signal("load-status" | "navigation-request" | "key-press" | ...)

msg.info / msg.warn / msg.error / msg.verbose
timer{interval=ms, on_timeout=fn}
sqlite3{path=...}:exec(sql, params)
i18n.t(key, ...)   -- global também disponível como _()
```

**Regra de fronteira:** nada que dependa de WPF pode aparecer nessa superfície. Se um
binding precisa de `Dispatcher`, ele mora em `App` e conversa com `Core` por interface.

### Convenções herdadas que não podem mudar

Estas quatro não são detalhe de implementação. São o que faz sessenta módulos se
plugarem sem se conhecerem, e cada uma delas quebra dezenas de call sites em silêncio
se for "corrigida".

**1. Em `emit_signal`, o primeiro retorno não-nulo interrompe a cadeia** e seus valores
viram o retorno do `emit_signal`. É o mecanismo de veto: `adblock` bloqueia requisição
retornando `false` de `send-request`, `mime-type-decision` cancela download do mesmo
jeito. Handlers rodam em ordem de inserção, e a lista é clonada antes do despacho
porque handlers podem se remover durante ele.

**2. Em ações de bind, retornar `false` significa "não tratei, continue procurando".**
Qualquer outro retorno, incluindo `nil`, conta como tratado e para a busca. A
convenção é invertida em relação ao que a intuição sugere, e é proposital.

**3. Toda ação de bind é chamada com três argumentos:**
```lua
action.func(w, util.table.join(opts, args), opts)
```
O segundo é a fusão dos `opts` de declaração com os `args` da invocação (que sempre
trazem `object`, `binds`, `mods`, `key`, e para buffer ou comando também `buffer`,
`cmd`, `argument`). O terceiro são os `opts` crus.

**4. A cascata do tema resolve chaves que não existem no arquivo.** O `__index` de
`lousy.theme` remove tudo até o primeiro `_` e tenta de novo, recursivamente, até
cair em `fg`, `bg` ou `font`. Então `hint_overlay_selected_border` vira
`overlay_selected_border`, depois `selected_border`, depois `border`. Widgets pedem
chaves que ninguém definiu e contam com isso. Implementar a regra, não a lista.

Duas escolhas de desenho que seguem dessas convenções:

- `w.binds` é **reconstruído a cada troca de modo**, não consultado a cada tecla:
  `join(binds_do_modo, binds_do_modo_all)`. O modo `all` é um meta-modo cujos binds
  entram em todos os outros.
- O buffer de teclas (`gg`, `42gt`) usa casamento parcial: se nenhum bind pode ainda
  vir a casar, o buffer zera. O prefixo de contagem `%d*` é tratado à parte.

### Contrato de um modo

```lua
modes.new_mode("nome", "descrição", {
    binds = { ... },
    enter = function (w, ...) end,      -- args extras vêm de w:set_mode("x", a, b)
    leave = function (w) end,
    changed = function (w, text) end,   -- texto da barra mudou
    activate = function (w, text) end,  -- Enter; false suprime o histórico
    history = { maxlen = 50, items = {} },
    passthrough = false,                -- teclas vão para a página
    reset_on_focus = true,              -- sai do modo quando foca elemento não-editável
    reset_on_navigation = true,
})
```

Ordem na troca de modo: `leave` do antigo, grava `w.mode`, `update_binds`, `enter` do
novo, emite `mode-entered`.

### Páginas internas: a ponte Lua ⇄ JS

No Skull Browser, `chrome.add(nome, handler, on_first_visual, export_funcs)` faz cada
entrada de `export_funcs` virar uma **função global em JS que devolve Promise**,
injetada só naquele padrão de URI. O primeiro argumento do lado Lua é sempre a
webview, e o chamador em JS não passa isso.

```lua
chrome.add("downloads", html_handler, nil, {
    downloads_get_all = function (view, filter) ... end,
    download_cancel   = function (view, id) ... end,
})
```
```js
downloads_get_all(filter).then(render)
```

No WebView2 isso vira RPC sobre `WebMessageReceived`: um stub injetado guarda um mapa
de id para `resolve`/`reject`, manda `{id, fn, args}` para o host, e o host responde
`{id, ok, ret}`. Mesma forma, outro transporte. `AddHostObjectToScript` existe e
tentaria resolver isso sozinho, mas amarra a assinatura ao COM e não sobrevive a
funções definidas em Lua em runtime, então fica de fora.

O sentido inverso (Lua empurra para a página) continua sendo `view:eval_js(src)`, que
no WebView2 é `ExecuteScriptAsync`.

### Corrotinas: como o Lua continua parecendo síncrono

O WebView2 é assíncrono em tudo. `ExecuteScriptAsync`, `CallDevToolsProtocolMethodAsync`,
a leitura de um stream de resposta. O Lua dos sessenta módulos foi escrito assumindo
retorno imediato, e reescrever todos eles em estilo callback seria trocar o problema
de lugar.

O luakit já resolveu isso e o mecanismo se transplanta. `common/luayield.c` embrulha
callbacks do GLib em corrotina: `view:get_source()` e `luakit.website_data.fetch()`
parecem síncronos e não são. Em C#, o análogo é ponte `Task` para corrotina Lua:

```
Lua chama view:eval_js(src)
 └─ binding faz lua_yield
     └─ host dispara ExecuteScriptAsync
         └─ continuação no Dispatcher faz lua_resume com o resultado
```

Vale para `eval_js`, `get_source`, consulta de histórico, fetch de gopher e leitura de
`view.scroll`. O código Lua fica legível. A regra: **todo binding que no Skull Browser
era síncrono e aqui vira assíncrono passa por corrotina, não por callback.**

### Propriedades declarativas, e o buraco que o WebView2 deixa

O padrão mais rentável do Skull Browser é a tabela de propriedades de
`common/property.c`: um array de `{token, nome_gobject, tipo, gravável}` expõe **48
propriedades de `WebKitSettings`** a custo zero linhas por propriedade. `settings.lua`
e `domain_props.lua` são construídos inteiramente em cima disso.

Em C# o padrão se mantém (um dicionário de descritores sobre `CoreWebView2Settings`,
com reflexão ou geração de código). **O problema é o que tem do outro lado.**

`CoreWebView2Settings` expõe cerca de dezesseis propriedades: `IsScriptEnabled`,
`AreDevToolsEnabled`, `AreDefaultContextMenusEnabled`, `IsZoomControlEnabled`,
`IsBuiltInErrorPageEnabled`, `UserAgent`, `AreBrowserAcceleratorKeysEnabled`,
`IsPasswordAutosaveEnabled`, `IsGeneralAutofillEnabled`, `IsPinchZoomEnabled`,
`IsSwipeNavigationEnabled`, `IsWebMessageEnabled`, `AreDefaultScriptDialogsEnabled`,
`HiddenPdfToolbarItems`, e pouco mais.

Não existem: famílias de fonte, `default_font_size`, `minimum_font_size`,
`auto_load_images`, `default_charset`, `enable_webgl`, `enable_caret_browsing`,
`enable_spatial_navigation`, `enable_html5_local_storage`, `zoom_text_only`.

Três saídas, nesta ordem de preferência:

1. **Switch do Chromium** via `AdditionalBrowserArguments` no `CoreWebView2EnvironmentOptions`.
   Cobre `--blink-settings=defaultFontSize=16`, `--disable-webgl`, `--blink-settings=imagesEnabled=false`.
   Mesma restrição dos esquemas: vale para o processo inteiro e não muda em runtime.
   Vai no `config.json`, junto com a lista de esquemas.
2. **CSS ou JS injetado** para o que é de apresentação (tamanho mínimo de fonte,
   famílias). Funciona por aba e em runtime, que é o que `domain_props` precisa.
3. **Não existe.** Documentar em `skull://settings` que a chave não é suportada no
   Windows, em vez de aceitar em silêncio e não aplicar.

`settings.lua` precisa então de um campo a mais por chave: onde ela é aplicada
(propriedade nativa, switch de partida, injeção, ou indisponível). Sem isso, metade
das configurações do Skull Browser vira mentira silenciosa.

### Widgets: conjunto reduzido

No Skull Browser, o `rc.lua` monta a statusbar em Lua:

```lua
l.layout:pack(widgets.uri())
l.layout:pack(widgets.hist())
r.layout:pack(widgets.tabi())
```

Isso é parte de "você pode mudar tudo", e fica. Mas o `widget` do luakit registra
dezesseis tipos GTK, e portar todos para WPF é trabalho sem retorno.

O Skull Wins expõe **três**: `box`, `label`, `entry`, sobre `StackPanel`,
`TextBlock` e `TextBox`. Suficiente para compor statusbar e barra de comando a partir
do `rc.lua`, que é o caso de uso real. Tabstrip, notebook e janela são WPF nativo, não
componíveis em Lua na 0.01.

Os widgets indicadores (`uri`, `hist`, `progress`, `scroll`, `ssl`, `tabi`, `buf`)
seguem o protocolo de `lousy/widget/common.lua`: `add_widget` registra a instância,
`update_widgets_on_w` re-renderiza as de uma janela. Esse protocolo fica igual.

---

## Mapeamento: Skull Browser → Skull Wins

Os dez módulos `*_wm.lua` do Skull Browser rodam dentro do processo de renderização
do WebKit com acesso síncrono ao DOM. Não há equivalente no Chromium. Cada um vira
JavaScript injetado, assíncrono, conversando por `postMessage`.

| Skull Browser | Skull Wins | Observação |
|---|---|---|
| `extension/` (C, 1.900 linhas) | não existe | Resolve a ADR 001 por eliminação |
| `follow_wm.lua` | `lua/js/follow.js` | Hints de link. O de maior esforço. |
| `select_wm.lua` | `lua/js/select.js` | Seleção de texto por teclado |
| `formfiller_wm.lua` | `lua/js/formfiller.js` | Fica para depois da 0.01 |
| `adblock_wm.lua` | pipeline de `WebResourceRequested` | Bloqueio em rede, não em DOM. Melhor. |
| `error_page_wm.lua` | `NavigationCompleted` + `load_string` | |
| `styles.lua` + wm | `AddScriptToExecuteOnDocumentCreated` | Userstyles injetados |
| `image_css_wm.lua` | idem | |
| `referer_control_wm.lua` | `WebResourceRequested` | Reescreve header antes de sair |
| `scroll` via `WEBKIT_DOM_USE_UNSTABLE_API` | `eval_js` | Assíncrono. Statusbar aceita atraso. |
| `webview_wm.lua` | desnecessário | Existia porque WebKit não navegava `skull://` para `file://` |
| `chrome_wm.lua` | RPC sobre `WebMessageReceived` | Mesma forma de Promise, outro transporte |
| `lib/gopher.lua` (LuaSocket) | `Protocols/Gopher` + `gopher_render.lua` | Rede em C#, render em Lua |
| `clib/` + `widgets/` (C, ~7.800 linhas) | `SkullWins.Core/Api` | Superfície menor: 3 widgets, não 16 |
| `common/luayield.c` | ponte `Task` ↔ corrotina | Mantém o Lua com cara de síncrono |
| `common/property.c` (48 props) | descritores sobre `CoreWebView2Settings` | **Só ~16 existem.** Ver seção de propriedades. |
| IPC `AF_UNIX` + `luaserialize` | não existe | Sem segundo processo Lua, não há o que serializar |
| `unique.c` sobre `GtkApplication` | `Mutex` nomeado + named pipe | Necessário para "definir como padrão" funcionar |
| `luakit.selection` (primary/clipboard) | só `clipboard` | Windows não tem PRIMARY. `skull.selection.primary` devolve nil. |
| `lousy/` | `lua/lousy/` | Reescrito, mesma forma |
| `lousy.pickle` | mantido | Lua puro, portável. Formato de sessão, undoclose, histórico de comando. |
| `get_etc_hosts`, `term -e $EDITOR` | reescritos | As duas dependências POSIX explícitas da camada Lua |

---

## Bilíngue: pt-BR e inglês

O Skull Browser não tem nenhuma externalização de string: tudo é inglês embutido no
código. O Skull Wins nasce diferente, e isso é parte da identidade, não um extra.

**Mecanismo.** Chaves no código, tabelas por idioma, inglês como fallback.

```lua
-- lua/locale/pt_BR.lua
return {
  ["mode.insert"]        = "-- INSERÇÃO --",
  ["bookmark.added"]     = "Favorito adicionado: %s",
  ["download.finished"]  = "Download concluído: %s",
  ["error.host_not_found"] = "Servidor não encontrado: %s",
  ["gopher.type.dir"]    = "DIR ",
}
```

```lua
local _ = i18n.t
w:notify(_("bookmark.added", title))
```

Chave ausente no idioma ativo cai para o inglês. Ausente nos dois, devolve a própria
chave e registra um aviso em `skull://log`. O browser nunca quebra por falta de
tradução.

**Seleção do idioma**, nesta ordem:

1. `skull.locale = "pt_BR"` no `rc.lua`
2. `SKULL_LOCALE` no ambiente
3. `CultureInfo.CurrentUICulture` do Windows
4. inglês

**O caso difícil: as descrições de bind.** No Skull Browser, o campo `desc` de cada
bind não é comentário. Ele alimenta `skull://help` e `skull://binds`, que são gerados
a partir das tabelas de modo. São cerca de 250 binds e 120 comandos, e isso torna as
descrições o maior bloco de texto traduzível do projeto, estruturalmente grudado nas
tabelas de bind.

Solução: `desc` aceita chave em vez de literal, e a resolução acontece na hora de
renderizar, não na hora de declarar.

```lua
modes.add_binds("normal", {
    { "gg", "bind.scroll_top", function (w) w:scroll{ y = 0 } end },
})
```

`i18n.t("bind.scroll_top")` só é chamado quando `skull://binds` monta a página ou
quando a completação mostra a descrição. Quem escreve bind no próprio `rc.lua` passa
string literal normal: se a chave não existe no catálogo, o fallback devolve a própria
string, que é exatamente o comportamento desejado. Uma convenção serve os dois casos
sem `if`.

**Superfície coberta:** statusbar, notificações, nomes de modo, descrições de bind e
de comando, páginas de erro, todas as páginas `skull://`, e o instalador.

**O host tem strings também**, mas poucas: só as que aparecem antes da VM Lua subir
(WebView2 Runtime ausente, `rc.lua` com erro de sintaxe, perfil corrompido). Um
`Strings.cs` com dois dicionários resolve. `.resx` e assembly satélite para dez
frases é ferramenta demais.

**Documentação:** `README.md` em inglês, `docs/README.pt-BR.md` em português, ADRs em
português (como já é no Skull Browser).

**Teste:** um teste que varre `locale/en.lua` e `locale/pt_BR.lua` e falha se as
chaves divergirem. Sem isso, a tradução apodrece em duas semanas.

---

## Identidade e autoria

Versão **0.01**, autoria de **Pablo Murad**. Este é um projeto autoral, não um fork.

- `AUTHORS`: Pablo Murad, 2026. Sem linhagem luakit, porque não há código herdado.
- `skull://about`: nome, versão, autor, versão do WebView2 Runtime detectada em
  runtime, e o commit do build. Bilíngue.
- Ícone próprio, `.ico` multi-resolução gerado a partir do SVG.
- Página inicial própria e buscadores próprios, como no Skull Browser.
- Binário `skull.exe`, esquema `skull://`, config em `%APPDATA%\skull\`. Não conflita
  com o fork Linux, que não roda em Windows nativo.

**Licença: GPLv3**, decidido. O plano é escrever tudo do zero, mas reescrever sessenta
módulos passa por consultar os originais, e a linha entre consultar e adaptar é fina.
GPLv3 alinha com o Skull Browser, remove qualquer dúvida sobre proveniência, e não
custa nada num projeto que não vai ser vendido. `LICENSE` entra no primeiro commit, não
na Fase 10. Registrado como ADR 004.

---

## Riscos

| # | Risco | Impacto | Mitigação |
|---|---|---|---|
| 1 | Captura de teclado por JS não cobrir casos reais | Mata o produto | **Spike 0, antes de tudo.** Critério explícito abaixo. |
| 2 | Iframe cross-origin engole teclas | Irritação recorrente | `FrameCreated` + injeção por frame. Escape sempre volta ao host. |
| 3 | `WebResourceRequested` roda na thread de UI e é lento | Páginas pesadas travam | Filtro estreito, nunca `*` sem necessidade. Adblock decide com estrutura pré-compilada, não regex por requisição. |
| 4 | Esquemas imutáveis após criação do ambiente | Adicionar protocolo exige reiniciar | Registrar `skull`, `gopher`, `gemini` desde a 0.01. Documentar a limitação. |
| 5 | WebView2 Runtime ausente no Windows 10 | Não abre | Detectar na partida, mensagem bilíngue com link. Instalador com bootstrapper. |
| 6 | NLua depende de `lua54.dll` nativa | Falha de deploy | Fixar x64 e arm64 no `.csproj`, cobrir no smoke test do instalador. |
| 7 | Erro de Lua derrubar o browser | Perda de confiança | `pcall` em toda borda host→Lua. Ver seção de erros. |
| 8 | Escopo de sessenta módulos | Projeto nunca termina | Fases com critério de pronto. A 0.01 tem corte explícito. |
| 9 | Follow (hints de link) em JS | Subestimado | `select_wm` tem 630 linhas e faz travessia cross-frame, métricas de layout e mutação de DOM numa passada síncrona. Como JS injetado o algoritmo fica **mais** natural (`getClientRects` mais overlay), mas o retorno vira assíncrono e todo chamador em `follow.lua` muda de forma. Fase própria. |
| 10 | Metade das configurações não existir no WebView2 | Config vira mentira | `settings.lua` ganha campo de origem por chave: nativa, switch de partida, injeção, ou indisponível. `skull://settings` mostra qual é qual. |
| 11 | `AdditionalBrowserArguments` também é imutável | Trocar fonte exige reiniciar | Mesma restrição dos esquemas, mesmo arquivo (`config.json`), mesma nota na documentação. |
| 12 | Handler de esquema assíncrono dentro de evento síncrono | Gopher trava a UI ou não responde | `CoreWebView2Deferral`: pega o deferral, resolve em background, completa. É o equivalente exato do `request:finish()` do luakit, que também pode ser chamado depois. |

### Spike 0, critério de aceite

Meio dia. Janela WPF com um WebView2, VM Lua mínima, e `skull-input.js`. Passa se:

1. `j` rola a página e **não** digita "j" numa caixa de texto focada do site.
2. `i` entra em modo insert; a partir daí `j` digita "j" normalmente.
3. Escape volta ao modo normal, mesmo com foco dentro de um `<input>`.
4. `:` abre a barra de comando do WPF, com foco saindo do WebView2.
5. Ctrl+F do Edge não abre a busca nativa.
6. Funciona em github.com, gmail.com e uma página com iframe cross-origin, ou a
   falha do item 3 está documentada e é aceitável.

Se 1, 2 ou 3 falharem, o desenho muda antes de qualquer outra linha.

---

## Tratamento de erros

**Erro de Lua nunca derruba o browser.** Toda chamada host→Lua passa por `pcall`.
Falha vira notificação vermelha na statusbar, entrada em `skull://log` com traceback,
e o browser segue. O Skull Browser faz isso com `log_chrome.widget()` na statusbar,
e o padrão vale copiar.

**Erro no `rc.lua`.** Sintaxe quebrada na partida não pode deixar o usuário sem
browser. Carrega o `rc.lua` de fábrica, abre com aviso no topo dizendo qual linha
falhou, em pt-BR ou inglês. Um `skull.exe --check` valida e sai, como o `skull -k`.

**Erro de rede e de protocolo.** Página de erro própria, servida pelo mesmo pipeline
de `WebResourceRequested`. Categorias separadas: DNS, recusa de conexão, TLS,
timeout, gopher malformado. Mensagem específica por categoria, bilíngue. Nada de
"algo deu errado".

**Crash de aba.** `CoreWebView2.ProcessFailed` distingue renderer de browser process.
Renderer morto: aba mostra estado de crash com a URI e um botão de recarregar, as
outras abas seguem vivas. Browser process morto: todas as abas caem juntas, então
salva a sessão e recria o ambiente.

**WebView2 Runtime ausente.** Detectar com
`CoreWebView2Environment.GetAvailableBrowserVersionString()` antes de criar janela.
Ausente: diálogo bilíngue com link do Evergreen Bootstrapper. Windows 11 já traz.

**Corrupção de SQLite.** Abrir em modo WAL, e se falhar, renomear para `.corrupt` e
recriar vazio, avisando. Perder histórico é ruim; não abrir é pior.

---

## Testes

Meta de 80% nos projetos `Core`, `Protocols` e `Storage`. `App` fica de fora: é WPF,
e testar janela dá pouco retorno.

**Unitários (xUnit).**
- `Protocols.Gopher`: parser de RFC 1436 contra fixtures de bytes gravados de
  servidores reais (floodgap, entre outros). Casos sujos primeiro: linha sem tab,
  tipo desconhecido, CRLF faltando, UTF-8 inválido, linha `.` de fim.
- `Core.Scheme`: a fila de três passos, com handler fake.
- `Core.Lua`: marshalling nos dois sentidos, tabelas aninhadas, nil, números, e o
  comportamento de `pcall` na borda.
- `Storage`: migrações e as consultas de histórico e favoritos.
- `i18n`: paridade de chaves entre `en` e `pt_BR`, fallback, chave ausente.

**Lua headless.** Um runner que sobe a VM sem WPF e roda os testes dos módulos, com
um `webview` falso. Equivalente ao `make run-tests` do Skull Browser. Os módulos que
não tocam UI (`lousy.uri`, `lousy.util`, `gopher_render`, `completion`) ficam
cobertos de verdade.

**Fumaça manual, documentada em `docs/smoke.md`.** Uma lista curta que se roda antes
de cada release: abrir, navegar, `f` seguir link, `gopher://gopher.floodgap.com`,
`:open`, favoritar, baixar arquivo, trocar idioma, matar aba.

Não há teste E2E automatizado na 0.01. WebView2 não tem headless de verdade e montar
isso agora custa mais do que rende.

---

## Build, CI e distribuição

**Build:** `dotnet build`, `dotnet test`. Sem makefile.

**Versão:** derivada de `git describe --tags`, injetada como propriedade MSBuild.
O Skull Browser faz o mesmo com `build-utils/getversion.sh`, e a CI dele usa
`fetch-depth: 0` justamente por causa disso. Repetir esse cuidado.

**CI (`.github/workflows/build.yml`):** roda em `windows-latest`. Passos: restore,
build, test, e um job que publica o instalador em tag. A CI do Skull Browser roda em
`ubuntu-latest`; esta é o espelho dela.

**Instalador:** Inno Setup (`build/skull.iss`), bilíngue (o Inno tem suporte nativo a
múltiplos idiomas). Instala em `%LOCALAPPDATA%\Programs\Skull`, registra
`skull.exe` como opção de navegador padrão, copia os arquivos de fábrica, e chama o
bootstrapper do WebView2 se o runtime faltar.

**Publicação:** `dotnet publish -r win-x64 --self-contained false` por padrão, com
uma variante self-contained para quem não tem .NET 8. Duas arquiteturas: x64 e arm64.

---

## Fases

Cada fase tem critério de pronto. Nenhuma começa antes da anterior fechar.

**Fase 0 — Spike de teclado.** Meio dia. Critério: os seis itens do Spike 0 acima.
Nada mais é escrito até isso passar.

**Fase 1 — Esqueleto.** Solution, os cinco projetos, CI verde, `skull.exe` abrindo
uma janela com um WebView2 e uma statusbar. VM Lua sobe e roda um `rc.lua` que
imprime versão. Critério: `dotnet test` passa na CI e a janela abre.

**Fase 2 — Ponte Lua.** Sinais, `msg`, `timer`, `skull.*`, `window`, `webview`.
Marshalling coberto por teste. Critério: um `rc.lua` de vinte linhas consegue abrir
uma aba, navegar e imprimir o título.

**Fase 3 — Modal.** `lousy.bind`, `lousy.mode`, `modes.lua`, `binds.lua`,
`skull-input.js`. Modos normal, insert, command, passthrough. Barra de comando com
histórico. Critério: navegar um dia inteiro sem mouse, exceto seguir link.

**Fase 4 — Abas e sessão.** Múltiplas abas, tabstrip, `session.lua`, `undoclose.lua`,
restaurar ao abrir. Instância única por `Mutex` nomeado mais named pipe: abrir um link
com o browser já rodando manda a URI para a instância viva em vez de subir outra. Sem
isso, registrar como navegador padrão fica inutilizável. Critério: fechar com dez abas
e reabrir com as dez; clicar um link no Explorer abre aba na janela existente.

**Fase 5 — Esquemas e páginas internas.** Pipeline de `WebResourceRequested`,
`skull://about`, `skull://log`, `skull://help`. i18n ligado aqui, porque essas são as
primeiras páginas com texto. Critério: about e help legíveis nos dois idiomas.

**Fase 6 — Gopher.** `Protocols.Gopher` com testes, `gopher_render.lua`, tipos de
item, busca (tipo 7), download de binário (tipos 4, 5, 9). Critério: navegar
floodgap inteiro sem travar a janela.

**Fase 7 — Follow.** `follow.js`, hints de link por teclado, os modos `follow` e
`follow_selected`. A fase mais difícil. Critério: seguir links em github, wikipedia e
numa página com iframe.

**Fase 8 — Armazenamento.** Histórico, favoritos, downloads, quickmarks, com suas
páginas `skull://` e completação na barra de comando. Critério: `:open` completa a
partir do histórico.

**Fase 9 — Adblock.** Parser de EasyList, estrutura pré-compilada em memória,
bloqueio no pipeline, `skull://adblock` para ligar e desligar listas. Critério: uma
página de notícias grande carrega visivelmente mais limpa, e o tempo gasto no handler
fica abaixo de um milissegundo por requisição em média.

**Fase 10 — Acabamento 0.01.** Ícone, instalador, `README.md` e `README.pt-BR.md`,
`AUTHORS`, licença, `skull://settings`, tema, `docs/smoke.md`, ADRs escritas.
Critério: alguém instala num Windows limpo e usa sem ler o código.

---

## Escopo da 0.01

**Entra:** janela, abas, sessão, navegação modal com quatro modos, barra de comando
com completação, follow por hints, `gopher://`, páginas `skull://` (about, help, log,
history, bookmarks, downloads, settings, adblock), histórico, favoritos, downloads,
quickmarks, adblock, tema, pt-BR e inglês, instalador.

**Fica para depois:** formfiller, userstyles, noscript, proxy, tabgroups,
`gemini://` (esquema registrado, sem handler), modo privado, sincronização,
extensões, `skull://introspector`.

Esse corte deixa de fora sete módulos que o Skull Browser tem. É deliberado: são os
menos usados, e cada um deles custa uma fase inteira.

---

## Verificação de ponta a ponta

Depois da Fase 10, num Windows limpo (VM sem .NET e sem WebView2 Runtime):

1. Rodar o instalador. Escolher português. Confirmar que ele detecta a ausência do
   WebView2 Runtime e instala.
2. Abrir pelo menu Iniciar. A página inicial própria aparece.
3. `:open news.ycombinator.com`, navegar com `j` e `k`, seguir um link com `f`.
4. `gA` ou `:about`, conferir versão 0.01, autoria de Pablo Murad, e a versão do
   runtime detectada.
5. `:open gopher://gopher.floodgap.com`, navegar o menu, abrir um arquivo de texto,
   fazer uma busca (tipo 7).
6. Favoritar, ver em `skull://bookmarks`, reabrir pela completação de `:open`.
7. Baixar um arquivo, conferir em `skull://downloads`.
8. Fechar com várias abas, reabrir, conferir a sessão.
9. Trocar para inglês no `rc.lua`, reiniciar, conferir que toda a interface mudou.
10. Quebrar o `rc.lua` de propósito, reiniciar, conferir que o browser abre com o
    de fábrica e explica o erro.
11. `dotnet test` verde e cobertura acima de 80% em `Core`, `Protocols` e `Storage`.

---

## ADRs a escrever

Em `docs/decisoes/`, em português, no formato do Skull Browser (problema, por que
importa, decisão, consequência operacional, quando revisar).

- **001** WebView2 em vez de CEF ou Qt WebEngine
- **002** Captura de teclado em JavaScript, não em `AcceleratorKeyPressed`
- **003** NLua em vez de MoonSharp ou Lua-CSharp
- **004** Licença GPLv3
- **005** Esquemas e switches do Chromium registrados na partida, sem registro dinâmico
- **006** Corrotinas para async, não callbacks
- **007** Conjunto reduzido de widgets: três, não dezesseis
- **008** Configurações sem equivalente no WebView2: declarar indisponível, não fingir

---

## Tamanho do trabalho, para calibrar

O Skull Browser tem cerca de **14 mil linhas de C** (raiz 1.194, `clib/` 3.395,
`widgets/` 5.344, `common/` 3.821, `extension/` 2.559) e **19 mil linhas de Lua** em
`lib/`.

O Skull Wins corta o `extension/` inteiro (2.559 linhas de C mais os dez `*_wm.lua`,
que viram uns poucos arquivos JS) e reduz `widgets/` de dezesseis tipos para três. Em
compensação, ganha o que o GTK dava de graça e o WPF não dá, e ganha a camada de i18n
que não existe no original.

A camada Lua não encolhe: sessenta módulos continuam sendo sessenta módulos. É por isso
que o corte da 0.01 deixa sete de fora, e é por isso que as fases existem.

---

## Referências consultadas

- [Using local content in WebView2 apps](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content)
- [CoreWebView2CustomSchemeRegistration](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2customschemeregistration)
- [CoreWebView2Controller.AcceleratorKeyPressed](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2controller.acceleratorkeypressed)
- [CoreWebView2Settings.AreBrowserAcceleratorKeysEnabled](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.arebrowseracceleratorkeysenabled)
- [Using frames in WebView2 apps](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/frames)
- [WebView2Feedback #821, injeção em iframe cross-origin](https://github.com/MicrosoftEdge/WebView2Feedback/issues/821)
- [NLua](https://github.com/NLua/NLua)
- Skull Browser, `docs/decisoes/001-webkitgtk-api.md`
