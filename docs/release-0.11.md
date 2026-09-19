# Skull Wins 0.11

Second release. The key map now follows luakit, and a bug that quietly disabled
every shifted binding is fixed.


## The bug

Every key that needs shift was dead in 0.01. Not some of them: all of them.

A bind written as `J` was stored as `<shift-j>`. A press of J arrived from the
page as the character "J" with a shift modifier, and was turned into
`<shift-J>`. Those two strings are not equal, so the lookup missed, every time.

That took out J, K, H, L, G, O, T, F, R and, worst of all, `:` which is typed
as shift and semicolon. With the command bar unreachable there was no way to
type an address, in a browser with no address bar.

A single character now carries its shift in its own identity: a capital folds
to lowercase plus shift, a shifted symbol keeps the symbol and drops the shift.
Seventeen tests cover the round trip, one per key shape that used to break.


## Typing in a page

Focusing a text box now switches to insert mode on its own, the way luakit does
it. Before this, clicking a search field and typing fired key bindings instead
of writing text, and backspace appeared to do nothing. Escape returns to normal.


## Keys now follow luakit

The map was rewritten against luakit's own lib/binds.lua rather than invented.
Notable corrections:

    J K       next tab, previous tab     (they were reversed)
    d         close tab                  (it used to bookmark)
    r R       reload, reload skipping cache
    0 ^ $     top, far left, far right
    %         go to [count] percent
    < >       move the tab
    gy        duplicate the tab
    g0 g$     first tab, last tab
    ZZ ZQ     quit
    C-o C-i   back, forward
    Backspace back

New, with no luakit equivalent to copy:

    / ? n N   search the page
    y         copy the address
    zi zo zz  zoom
    gA gb gB gi   about, bookmarks, bookmark this, history

Counts work where luakit has them: 3j scrolls three lines, 2H goes back twice,
50% jumps to the middle of the page.


## Also

The window raises itself on startup instead of opening behind whatever was
already on screen.

The command bar takes the Win32 keyboard focus away from the rendering view
when it opens. WebView2 hosts its own child window and holds that focus, so
moving only WPF's logical focus was not enough.

Searching a page is in, driven by window.find: `/` and `?` to start, `n` and
`N` to move between matches.

Copying the address with `y` is in.


## Everything else

Unchanged from 0.01: one executable, no installer, about 60 MB, carrying its
own runtime. gopher, https, skull:// pages, history and bookmarks in SQLite,
English and Brazilian Portuguese.

Still missing: ad blocking, form filling, user stylesheets, proxies, tab
groups, private mode, session restore, gemini.

162 tests.


## Licence

GNU GPLv3.

Pablo Murad, https://pablomurad.com
https://github.com/runawaydevil/skull_browser_win
