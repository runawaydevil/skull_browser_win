# Spike 0 - captura de teclado

Prova de conceito que roda antes de qualquer arquitetura definitiva. Existe para
responder uma pergunta: da para fazer navegacao modal sobre o WebView2?

O caminho obvio nao serve. A documentacao da Microsoft define acelerador como
tecla pressionada com Ctrl ou Alt, ou que nao mapeia para caractere. Ou seja
`j`, `f` e `:` sozinhos nunca chegam em `AcceleratorKeyPressed`. A saida e
capturar no JavaScript da pagina, na fase de captura, antes dos handlers do site.

## Rodar

    cd spike\KeyboardSpike
    dotnet run

Abre em `skull://spike/`, servido pelo proprio programa.

## Os seis criterios

Com a janela em foco, a statusbar mostra o modo a esquerda e a ultima tecla a
direita. O arquivo `%TEMP%\skull-spike.log` registra tudo.

| # | Fazer | Passa se |
|---|---|---|
| 1 | Clicar na caixa de texto, teclar `j` | Pagina rola, caixa continua vazia |
| 2 | Teclar `i`, voltar na caixa, teclar `j` | Digita "j", statusbar em INSERT |
| 3 | Com o cursor na caixa, teclar `Escape` | Volta para NORMAL |
| 4 | Teclar `:` | Barra de comando do WPF abre com foco |
| 5 | Teclar `Ctrl+F` | Busca do Edge nao abre; log diz que a tecla chegou |
| 6 | Olhar o log durante o carregamento | `injetado [child] https://example.com/` |

Extras: `z` deve aparecer como "nao ligada", porque a acao em `spike.lua`
devolve `false` de proposito, o que prova a convencao invertida do luakit. E `G`
deve pular para o fim, provando o caminho completo tecla, JS, C#, Lua, `eval_js`.

## Resultado ate agora

Passaram sem intervencao: registro do esquema, injecao no frame principal,
injecao no iframe cross-origin, carga da VM Lua.

Faltam 1 a 5. Precisam de digitacao real: o Windows bloqueia injecao de teclado
de outro processo na janela em foco, entao automatizar isso nao funcionou.

Se 1, 2 ou 3 falharem, o desenho do teclado muda antes de escrever mais alguma
coisa. Esse e o ponto de gastar meio dia aqui.
