# Skull Wins 0.01

First release. A keyboard-driven browser for Windows, with gopher as a
first-class protocol.

This is version 0.01, not 1.0. It works, it is not finished, and the list of
what is missing is longer than the list of what is here.


## Getting it

Download skull.exe and run it. There is no installer and nothing to unpack: it
is one file, about 60 MB, and it carries its own .NET runtime.

Windows 10 or 11. Windows 11 already has the WebView2 runtime; on Windows 10
the browser will tell you if it is missing and where to get it.


## First minute

The browser opens on Google with no address bar, because there is no address
bar. To go somewhere:

    o                 opens the command bar with "open " already typed
    google.com        type where you want to go
    Enter

The status bar at the bottom tells you this when there is nothing else to show.

Then:

    j k           scroll down, up
    f             label every link, type a label to follow it
    t             new tab
    gt            next tab
    i             insert mode, keys go to the page again
    Escape        back to normal mode
    :             command bar
    gH            help, with the full key list


## What works

Modal navigation with four modes. Tabs. Link hints. Command bar with a handful
of commands. gopher:// menus, text files, type 7 search and binary items. Any
ordinary https site, rendered by the Edge engine. History and bookmarks in
SQLite. Internal pages under skull://. English and Brazilian Portuguese,
picked from Windows or forced with --locale.


## What does not

No ad blocking. No form filling. No user stylesheets. No proxy settings. No
tab groups. No private mode. No session restore: close the browser and the
tabs are gone. The gemini scheme is registered but nothing answers it.

It does not register itself as your default browser, because there is no
installer to do that. Links clicked in other programs will not open here.

Link hints only see the top document. Links inside a cross-origin iframe are
not labelled.


## Configuration

    skull --init

Writes the configuration into %APPDATA%\skull. Edit rc.lua from there; it is
real Lua and it runs at startup. A broken rc.lua does not stop the browser, it
falls back to the built-in copy and says which line failed. Check it without
opening a window with skull --check.


## Reporting a problem

    skull --sysinfo

prints the build, the engine version and the machine, which is the same thing
skull://about shows. Paste that into the report.


## Security

A review before this release found that the keyboard bridge, which injects a
script into every page, could be spoken to by the page itself. Combined with
the gopher implementation that allowed a visited website to make the browser
write arbitrary bytes to any TCP port reachable from the machine, with no
click. It is fixed, and docs/adr/010-untrusted-pages.md explains what was
wrong and what now stops it.

rc.lua runs with full access to the host, like a shell profile. It is your own
file; do not run someone else's.


## Known rough edges

The window does not remember its size or position.

Downloads use whatever the engine does by default. There is no download
manager and no skull://downloads page.

Zoom is per tab and resets when the tab navigates.

A page can still forge keystrokes, because the capture script lives inside the
page and cannot be told apart from a copy of it. Forged keys cannot navigate
anywhere dangerous and tabs are capped at 50, so the worst case is a nuisance.


## Licence

GNU GPLv3.

Pablo Murad, https://pablomurad.com
https://github.com/runawaydevil/skull_browser_win


## Checksum

    sha256  01f22b7ae195e063ac2539d59cb57027247f2bd6a72e0ddd9e4735822ca42688
            skull-0.01-win-x64.exe
