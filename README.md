# Skull Wins

A keyboard-driven web browser for Windows. Modal navigation in the vim
tradition, gopher as a first-class protocol, and configuration that is code
rather than a settings screen.

Version 0.20, by Pablo Murad. Windows only.

This is a separate program from Skull Browser, which runs on Linux. They share
a design and no code.


## What works

    starts on google      configurable, skull://newtab is still there
    modal navigation      normal, insert, command, passthrough
    tabs                  open, close, cycle, numbered in the status bar
    link hints            press f, type the label, go
    gopher://             menus, text, search, binary items
    gemini://             TLS, gemtext, redirects, input prompts, identities
    https and http        rendered by WebView2, the Edge engine
    skull:// pages        about, help, history, bookmarks, log, newtab
    about page            build, commit, engine, runtime, machine, profile
    history, bookmarks    SQLite, with search
    two languages         English and Brazilian Portuguese

Not in 0.20: ad blocking, form filling, user stylesheets, proxies, tab groups,
private mode, session restore.


## Requirements

    Windows 10 or 11
    WebView2 runtime   (already present on Windows 11)

The portable build carries its own .NET, so there is nothing else to install.


## Build

    dotnet build
    dotnet test

A single-file portable executable:

    dotnet publish src/SkullWins.App -c Release -r win-x64 --self-contained true ^
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
      -p:EnableCompressionInSingleFile=true -o dist


## Run

    skull
    skull gopher://gopher.floodgap.com
    skull --check              validate rc.lua and exit
    skull --init               write the configuration to disk
    skull --locale=pt_BR       force a language


## Keys

The map follows luakit. Where luakit binds a key, that key does the same thing
here, even where another one would have been nicer, because muscle memory is
the point of a modal browser. Keys luakit has no equivalent for are marked.

    motion
      j k h l       scroll down, up, left, right
      gg  G         top, bottom
      0  ^  $       top, far left, far right
      50%           go to 50 percent of the page
      C-d  C-u      half a screen down, up
      C-f  C-b      a full screen down, up
      space         a full screen down

    history
      H  L          back, forward
      C-o  C-i      back, forward
      Backspace     back
      r  R          reload, reload skipping the cache
      C-c           stop loading

    opening
      o  t          open an address, open it in a new tab
      O  T          the same, starting from the current address

    tabs
      J  K          next tab, previous tab
      gt  gT        next tab, previous tab
      g0  g$        first tab, last tab
      C-t  C-w      new tab, close tab
      d             close tab
      <  >          move this tab left, right
      gy            duplicate this tab

    finding
      f  F          label the links, follow one, or follow into a new tab
      /  ?          search the page forwards, backwards
      n  N          next match, previous match

    the rest
      y             copy the address                       (not in luakit: y yanks there too)
      gh  gH        start page, start page in a new tab
      zi zo zz      zoom in, out, reset                    (not in luakit)
      gA gb gB gi   about, bookmarks, bookmark this, history  (not in luakit)
      F1            help                                   (luakit binds this too)
      i             insert mode, keys go to the page
      C-z           passthrough, every key goes to the page
      :             command bar
      ZZ  ZQ        quit
      Escape        back to normal mode

Focusing a text box switches to insert mode on its own, the way luakit does it,
so clicking a search field and typing writes text instead of firing bindings.
Escape gets you back.

Commands: open, tabopen, quit, quitall, reload, back, forward, about, help,
history, bookmarks, bookmark, gopher, log. Most have short aliases; see
skull://help.


## Where it keeps things

One executable, no installer, nothing to unpack. Data goes in `skull-data`
beside the binary when that folder can be written to, so a copy on a stick
carries its history, its pinned certificates and its identities with it. When
it cannot, because the program sits under Program Files or on a locked stick,
it falls back to `%APPDATA%\skull`. `skull://about` says which one is in use.

    --portable        insist on beside the executable
    --no-portable     insist on %APPDATA%
    --profile=PATH    somewhere else

An existing `%APPDATA%` profile is copied across once, the first time a
portable copy runs, so upgrading does not look like losing your history.

**It never makes itself your default browser.** No protocol association, no
registry writes, nothing outside its own profile. Links clicked in other
programs will not open here, and that is on purpose.


## Gemini

Capsules are self-signed as a matter of course, so ordinary certificate
validation would refuse nearly all of them. Skull Wins pins instead: a host is
remembered the first time you see it, and a certificate that changes while the
pinned one is still valid is refused, because that is what an interception
looks like.

    :cert             what is pinned for this host
    :cert accept      trust a changed certificate, deliberately
    :cert forget      unpin, so the next visit pins afresh

An identity is a client certificate: it is how a capsule knows you between
visits, and gemini has nothing else resembling a login. Nothing is ever
generated for you, because the specification forbids it without your
involvement.

    :identity             list them
    :identity new NAME    make one
    :identity use NAME    attach it to this capsule and directory
    :identity drop        stop using it here
    :identity forget NAME delete it and its key

An identity covers the directory it was attached in and everything below, so
attaching while reading one post covers its siblings and nothing above them.

The key file has no passphrase, like an unencrypted SSH key: whoever holds the
file is you. Under a portable profile that file sits beside the executable.
Loading one also imports its key into a container on the machine you are
running on, which is what Windows requires for TLS to use it at all, so a
portable copy is not quite traceless.

Gopher has no identities. The protocol has no authentication to attach one to.


## Configuration

The browser runs from a copy built into the executable, so a fresh install
needs no files. To change anything:

    skull --init

That writes into %APPDATA%\skull. Files on disk win over the built-in copy,
one file at a time, so you can override a single locale string and leave the
rest alone.

    %APPDATA%\skull\
        rc.lua          your configuration, real Lua
        locale\         translation tables
        history.db      SQLite
        bookmarks.db    SQLite
        skull.log       what went wrong
        profile\        engine profile

A broken rc.lua does not stop the browser. It falls back to the built-in copy
and tells you which line failed.


## Design notes

The host is small: a WPF window, tabs, and the plumbing around WebView2.
Everything above that is meant to be readable and replaceable.

Keyboard capture happens in JavaScript injected into every page, not through
the WebView2 accelerator hook. That hook only fires when Ctrl or Alt is held,
or for keys that produce no character, so plain j, f and : never reach it.
See docs/adr/002-keyboard-capture.md.

Gopher parsing is a pure function over bytes with no sockets in it, which is
why it can be tested against recorded captures instead of a live server.

docs/design.md has the rest.


## Security

A page is never trusted. Navigation asked for by a page goes through a scheme
allow-list, control messages are only honoured from the tab in front, and no
control character reaches the gopher wire. docs/adr/010-untrusted-pages.md has
the reasoning and the exploit chain it closes.

rc.lua runs with full access to the host, like a shell profile. It is your own
file; do not run someone else's.


## License

GNU GPLv3. See LICENSE.
