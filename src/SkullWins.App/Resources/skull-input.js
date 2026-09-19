// skull-input.js - keyboard capture for Skull Wins.
//
// Runs at document-start in every reachable frame. It mirrors the mode the
// host pushes down, because deciding whether to swallow a key has to be
// synchronous and the mode lives on the other side of the bridge.
//
// Capture happens in the window capture phase, ahead of any handler the page
// installed. That is how Vimium beats sites that hijack the keyboard.
//
// AcceleratorKeyPressed cannot do this job: WebView2 only calls it when Ctrl or
// Alt is held, or for keys that produce no character. Plain j, f and : never
// reach it, and those are exactly the keys modal navigation needs.

(function () {
    "use strict";

    if (window.__skull_input__) { return; }
    window.__skull_input__ = true;

    var bridge = window.chrome && window.chrome.webview;
    if (!bridge) { return; }   // no bridge in this frame, nothing to do

    var mode = "normal";

    // Map browser ev.key onto the names luakit uses, so bind tables written
    // for the Linux version still read correctly here.
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

        // In insert and passthrough the page owns the keyboard. Escape is the
        // only way out, so it stays intercepted and nothing else does.
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

    // Diagnostic: tell the host this frame was reached by the injection.
    bridge.postMessage({
        type: "hello",
        url: String(location.href).slice(0, 200),
        frame: (window.top === window) ? "top" : "child"
    });
})();
