# 002 — Captura de teclado em JavaScript, não em AcceleratorKeyPressed

**Data:** 2026-09-19
**Situação:** aceita

## O problema

Navegação modal exige que `j`, `f` e `:` cheguem ao browser antes da página.
A API que o WebView2 oferece para isso é `CoreWebView2Controller.AcceleratorKeyPressed`,
e ela não serve.

A documentação da Microsoft define acelerador assim:

> A key is considered an accelerator if either Ctrl or Alt is currently being
> held, or if the pressed key does not map to a character.

Escape é exceção permanente e sempre conta. Fora isso, toda tecla que produz
caractere, pressionada sem Ctrl ou Alt, nunca dispara o evento. São exatamente as
teclas de que a navegação modal depende.

## Decisão

Capturar no JavaScript da página, em `keydown` na fase de captura, injetado em
`document-start` por `AddScriptToExecuteOnDocumentCreated`. É o que o Vimium faz
em Chromium há mais de dez anos.

O modo vive no host, mas a decisão de engolir a tecla precisa ser síncrona no JS.
Resolve-se espelhando: toda troca de modo empurra o estado novo para todos os
frames por `PostWebMessageAsJson`, e o JS guarda numa variável local. O JS nunca
pergunta, só consulta o espelho.

Três mecanismos complementares:

- `AcceleratorKeyPressed` continua servindo para o que de fato cobre: Escape,
  combinações com Ctrl e Alt, teclas de função.
- `AreBrowserAcceleratorKeysEnabled = false` desliga os atalhos nativos do Edge
  para que Ctrl+F e companhia possam ser religados no Lua.
- Foco na barra de comando ou na statusbar: o WPF trata direto, sem passar pelo
  WebView2.

## Consequência operacional

**O foco do teclado precisa estar no controle WebView2, não na janela WPF.**
Sem `Web.Focus()` explícito, o foco fica no `Window`, a página nunca vê um
`keydown`, e a captura nunca roda. Descoberto na prática: o spike ficava mudo.

Teclas digitadas em widgets nativos (`<select>` aberto, visualizador de PDF do
Edge, diálogos do sistema) não passam por JavaScript e estão perdidas. A saída é
Escape, que sempre chega ao host, devolvendo o foco.

## O que mudou em relação ao esperado

O plano previa que a injeção não alcançaria iframes cross-origin, com base na
issue 821 do WebView2Feedback. **Na prática alcança.** Com o runtime 153.0.4234.48
o script chegou a um iframe de `https://example.com` dentro de uma página
`skull://`, e o handshake de diagnóstico voltou. A issue é de 2022 e pelo visto
foi resolvida nesse meio tempo.

A mitigação por `FrameCreated` mais `CoreWebView2Frame` fica de pé mesmo assim,
porque é barata e cobre iframes criados dinamicamente.

## Quando revisar

- Se a Microsoft expuser um gancho de teclado no host que cubra teclas comuns
- Se algum site conseguir furar a captura na fase de captura
- Se a injeção em iframe cross-origin regredir em algum runtime futuro
