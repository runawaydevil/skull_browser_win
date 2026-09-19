# 010 - A page is never trusted, so its messages must be harmless

Date: 2026-09-19
Status: accepted


## Problem

Keyboard capture works by injecting a script into every page and having it
post messages back to the host. That design is sound (see ADR 002), but it has
a consequence that was missed until a review before 0.01.

A web message carries no proof of who sent it. The injected script and a copy
of it written by a hostile site look identical on the wire. So any page the
user visits can post any message the real script posts.

Three of those messages did real work:

    key           dispatch a key binding
    follow        navigate to a URL
    mode-request  change the browser's mode

Combined with the gopher implementation, that was a chain. A gopher URL names
a host, a port and the exact bytes to send. The selector arrives percent
decoded, so CR and LF could hide inside it as %0d%0a and appear only after
decoding. `BuildRequest` wrote the selector straight to the socket.

The result: a page could post

    {type:'follow', uri:'gopher://10.0.0.1:6379/_%0d%0aSET key value%0d%0a'}

and the browser would open a TCP connection to any host and port reachable
from the machine and write attacker-chosen bytes into it. No click. This is the
long-known gopher smuggling technique, reachable here from any visited site.

Separately, a gopher item of type `h` carries a `URL:` field that was returned
verbatim for any scheme, so a gopher server could hand back
`URL:javascript:...` and get script execution on click.


## Decision

Do not try to authenticate the channel. It cannot be done: the script runs in
the page, and anything it knows the page knows. Make every message the channel
can carry harmless instead, with independent checks so that no single mistake
reopens the chain.

**Navigation asked for by a page goes through an allow-list.** http, https,
gopher and skull. Not file:, not javascript:, not data:. The command bar keeps
the full resolver, because a person typed into it.

**Control messages only come from the tab in front.** A background tab has no
business driving the browser.

**follow is only honoured while follow mode is running**, which only happens
after the user pressed f. mode-request can only return to normal, and only
from follow mode, so a page cannot push the browser into a mode of its choice.

**Control characters never reach the gopher wire.** `BuildRequest` strips them.
Only the framing it adds itself may contain CR, LF or tab.

**Gopher refuses well-known non-gopher ports**, the same list browsers keep for
http: 22, 25, 6379, 11211, 3306 and the rest. Port 70 and unusual gopher ports
such as 7070 stay open.

**A gopher `URL:` link is only rendered when its scheme is http, https or
gopher.** Anything else becomes inert text.


## What is still true

A page can still forge key messages, and nothing can stop that while the
capture script lives in the page. So the browser is built to survive it: forged
keys can no longer navigate anywhere dangerous, and tabs are capped at 50 so a
runaway page annoys rather than exhausts.

rc.lua runs with full host privileges. That is the same trust model as a shell
profile and it is the user's own file, but it is worth saying out loud.


## Tests

`tests/SkullWins.Tests/SecurityTests.cs` has one test per link of the chain,
including the percent-encoded CRLF payload in its real shape. They exist so
that a later change cannot quietly reopen this.


## Revisit when

- WebView2 offers a way to distinguish the injected script from page script
- A new message type is added to the bridge, which needs the same analysis
- Someone wants file: navigation from a page, which they should not get
