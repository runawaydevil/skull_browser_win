# 005 - Schemes are fixed at startup

Date: 2026-09-19
Status: accepted


## Problem

Skull Browser registers URI schemes at any time, from Lua. gopher.lua calls
register_scheme when the module loads. WebView2 does not allow that.


## What the documentation says

CoreWebView2CustomSchemeRegistration registrations are

> valid and immutable throughout the lifetime of the associated WebView2s'
> browser process

and any environment sharing that browser process must be created with
identical registrations, or creation fails outright.


## Decision

Every scheme the browser will ever understand is declared once, before the
environment is created and therefore before Lua has run:

    skull    secure context, has authority     internal pages
    gopher   plain, has authority              the protocol
    gemini   plain, has authority              registered, no handler yet

gemini is registered in 0.01 with nothing behind it, precisely because adding
it later would otherwise require a restart.

The same constraint applies to Chromium command-line switches passed through
AdditionalBrowserArguments, which is where settings without a WebView2
property have to go. Those live in the same startup path.


## Consequences

Scheme registration is no longer dynamic, and a user cannot add a protocol
from rc.lua. This is the single real concession WebView2 forces on the design.
