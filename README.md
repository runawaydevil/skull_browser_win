# Skull Wins

A keyboard-driven web browser for Windows. Modal navigation in the vim
tradition, gopher as a first-class protocol, and configuration that is code
rather than a settings screen.

Version 0.01, by Pablo Murad. Windows only.

This is a separate program from Skull Browser, which runs on Linux. They share
a design and no code.


## What works

    starts on google      configurable, skull://newtab is still there
    modal navigation      normal, insert, command, passthrough
    tabs                  open, close, cycle, numbered in the status bar
    link hints            press f, type the label, go
    gopher://             menus, text, search, binary items
    https and http        rendered by WebView2, the Edge engine
    skull:// pages        about, help, history, bookmarks, log, newtab
    about page            build, commit, engine, runtime, machine, profile
    history, bookmarks    SQLite, with search
    two languages         English and Brazilian Portuguese

Not in 0.01: ad blocking, form filling, user stylesheets, proxies, tab groups,
private mode, gemini. The gemini scheme is registered but nothing answers it.


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

    j k h l       scroll
    gg  G         top, bottom
    C-d  C-u      half a screen
    space         a full screen
    H  L          back, forward
    r             reload
    f  F          follow a link, follow into a new tab
    t  C-w        new tab, close tab
    gt  gT        next tab, previous tab
    d             bookmark the current page
    i             insert mode, keys go to the page
    C-z           passthrough mode, every key goes to the page
    :             command bar
    Escape        back to normal mode
    gA gH gh gb   about, help, history, bookmarks

Commands: open, tabopen, quit, quitall, reload, back, forward, about, help,
history, bookmarks, bookmark, gopher, log. Most have short aliases; see
skull://help.


## What about reports

skull://about, or press gA, prints the exact binary you are running and what
it is running on: version and codename, build date and commit, whether the
build is a single file or a folder, the WebView2 and .NET versions, the
operating system and its build number, processors and memory, the profile
path, and how many pages and bookmarks are stored.

A bug report with that page in it names the exact binary.


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


## License

GNU GPLv3. See LICENSE.
