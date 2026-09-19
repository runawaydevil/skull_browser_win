// skull-input.js - captura de teclado do Skull Wins.
//
// Roda em document-start em todo frame alcancavel. Espelha o modo atual que o
// host empurra, porque a decisao de engolir ou nao a tecla precisa ser sincrona
// e o modo mora do outro lado da ponte.
//
// A captura acontece na fase de captura do window, antes de qualquer handler da
// pagina. E assim que o Vimium ganha de sites que sequestram teclado.

(function () {
    "use strict";

    if (window.__skull_input__) { return; }
    window.__skull_input__ = true;

    var bridge = window.chrome && window.chrome.webview;
    if (!bridge) { return; }   // frame sem ponte: nao ha o que fazer

    var mode = "normal";

    // ev.key do navegador para os nomes que o luakit usa, para que as tabelas
    // de bind do Skull Browser continuem legiveis aqui.
    var KEYMAP = {
        "Enter": "Return",
        "Backspace": "BackSpace",
        "PageUp": "Page_Up",
        "PageDown": "Page_Down",
        "ArrowUp": "Up",
        "ArrowDown": "Down",
        "ArrowLeft": "Left",
        "ArrowRight": "Right",
        " ": "space"
    };

    var BARE_MODIFIERS = {
        "Control": 1, "Alt": 1, "Shift": 1, "Meta": 1,
        "CapsLock": 1, "NumLock": 1, "ScrollLock": 1,
        "AltGraph": 1, "Dead": 1
    };

    bridge.addEventListener("message", function (ev) {
        var m = ev.data;
        if (m && m.type === "mode") { mode = m.mode; }
    });

    window.addEventListener("keydown", function (ev) {
        if (BARE_MODIFIERS[ev.key]) { return; }

        // Em insert e passthrough a pagina manda. Escape e a unica saida, e por
        // isso e a unica tecla que continua sendo interceptada.
        if (mode === "insert" || mode === "passthrough") {
            if (ev.key !== "Escape") { return; }
        }

        var mods = [];
        if (ev.ctrlKey) { mods.push("control"); }
        if (ev.altKey) { mods.push("mod1"); }
        if (ev.shiftKey) { mods.push("shift"); }

        ev.preventDefault();
        ev.stopPropagation();

        bridge.postMessage({
            type: "key",
            key: KEYMAP[ev.key] || ev.key,
            mods: mods,
            frame: (window.top === window) ? "top" : "child"
        });
    }, true);

    // Diagnostico: diz ao host que este frame foi alcancado pela injecao.
    bridge.postMessage({
        type: "hello",
        url: String(location.href).slice(0, 200),
        frame: (window.top === window) ? "top" : "child"
    });
})();
