# 001 - WebView2 rather than CEF or Qt WebEngine

Date: 2026-09-19
Status: accepted


## Problem

The browser needs a rendering engine. Skull Browser uses WebKitGTK, which does
not run on Windows, so this project had to choose its own.


## Decision

WebView2, the Chromium engine behind Edge.

It ships with Windows 11 and Microsoft keeps it updated, which means this
project does not inherit the job of shipping Chromium security fixes. That job
is the real cost of an embedded engine, and it never ends.

It also provides what the design needs: WebResourceRequested for custom
schemes and request interception, AddScriptToExecuteOnDocumentCreated for the
keyboard layer, and CallDevToolsProtocolMethodAsync for testing.


## Rejected

CEF gives full control and a consistent engine everywhere, at the price of a
200 MB payload and owning the update treadmill. Worth it for a
cross-platform browser; this one is Windows only.

Qt WebEngine drags in Qt and its licensing, for no benefit here.


## Consequences

Windows only, by construction. Not a limitation of this decision so much as
the premise of the project.

The settings surface is much smaller than WebKit's. See ADR 008.

Custom schemes are frozen when the browser process starts. See ADR 005.
