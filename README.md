# Skull Wins

A keyboard-driven browser for Windows. Modal navigation in the vim tradition,
`gopher://` as a first-class protocol, and a large Lua layer you can rewrite.

Version 0.01, by Pablo Murad. Windows only, and independent from
[Skull Browser](https://github.com/runawaydevil/skull-browser), which is Linux
only. No shared code.

## Status

Phase 0 of eleven: the keyboard spike. Nothing here is a browser yet.

What the spike already proves:

- A custom scheme (`skull://`) registered at environment creation serves pages.
- The input script reaches the top frame **and** cross-origin iframes.
- An embedded Lua VM (Lua 5.4 via NLua) loads and dispatches.

What it does not prove yet: that modal keys work under real typing. That needs
a human at the keyboard, see `spike/README.md`.

## Stack

    .NET 10 / WPF     host window, tabs, status bar
    WebView2          rendering (Chromium, shipped with Windows 11)
    NLua              Lua 5.4, the configuration and extension language

## Plan

`docs/plano-0.01.md` has the full design: architecture, the Lua API contract,
the conventions inherited from luakit that cannot change, i18n, risks, phases
and acceptance criteria. Written in Portuguese.

## License

GNU GPLv3.
