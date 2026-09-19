# 002 - Keyboard capture in JavaScript, not AcceleratorKeyPressed

Date: 2026-09-19
Status: accepted


## Problem

Modal navigation needs j, f and : to reach the browser before the page sees
them. WebView2 offers CoreWebView2Controller.AcceleratorKeyPressed for exactly
this kind of interception, and it does not work for our case.

Microsoft's definition:

> A key is considered an accelerator if either Ctrl or Alt is currently being
> held, or if the pressed key does not map to a character.

Escape is a permanent exception and always counts. Every other key that
produces a character, pressed without Ctrl or Alt, never raises the event.
Those are precisely the keys modal navigation is built on.


## Decision

Capture in the page: a keydown listener in the capture phase, injected at
document-start through AddScriptToExecuteOnDocumentCreated. This is what
Vimium has done in Chromium for over a decade, and it beats sites that install
their own keyboard handlers because the capture phase runs first.

The mode lives in the host, but the decision to swallow a key has to be
synchronous inside the page. So the host mirrors it: every mode change is
pushed to all frames with PostWebMessageAsJson, and the script keeps a local
copy. The script never asks, it only reads the mirror.

Three mechanisms cover the rest:

- AcceleratorKeyPressed still handles what it actually covers: Escape, Ctrl
  and Alt combinations, function keys.
- AreBrowserAcceleratorKeysEnabled = false turns off Edge's own shortcuts so
  Ctrl+F and friends can be rebound.
- When focus is in the command bar, WPF handles keys directly and nothing goes
  through WebView2 at all.


## Consequences

Keyboard focus must be on the WebView2 control, not merely on the WPF window.
Without an explicit Focus() call the window holds focus, the page never sees a
keydown, and the capture never runs. This was not theoretical: the prototype
was silent until that call was added.

Keys typed into native widgets are lost: an open select dropdown, the Edge PDF
viewer, system dialogs. The way out is Escape, which always reaches the host
and returns focus.


## Verification

A self test drives the six acceptance criteria through the DevTools protocol.
Input.dispatchKeyEvent produces trusted events that travel the whole Chromium
input pipeline, so the capture listener and preventDefault behave exactly as
they do under a real keypress. Results:

    pass  normal mode: j scrolls and does not type into a focused input
    pass  insert mode: j types into the page
    pass  escape leaves insert with the caret inside an input
    pass  ctrl+f reaches the bind instead of the Edge find bar
    pass  script injected into a cross-origin iframe

What the self test does not cover is the OS to WebView2 leg. That leg was never
in doubt.


## What the design got wrong

The plan expected injection to miss cross-origin iframes, based on
WebView2Feedback issue 821. It does not. On runtime 153.0.4234.48 the script
reached an https://example.com iframe inside a skull:// page and the diagnostic
handshake came back. The issue is from 2022 and appears to have been fixed.

The FrameCreated plus CoreWebView2Frame mitigation stays anyway. It is cheap
and it covers frames created after load.


## Revisit when

- Microsoft exposes a host-side keyboard hook that sees ordinary keys
- A site is found that defeats capture-phase interception
- Cross-origin iframe injection regresses in a future runtime
