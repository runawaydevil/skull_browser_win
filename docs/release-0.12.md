# Skull Wins 0.12

Fixes a flicker introduced in 0.11. If you are on 0.11, replace it.


## What was wrong

0.11 added auto-insert: focusing a text box switches to insert mode so that
typing in a page writes text instead of firing key bindings. The handler that
did it had two faults that fed each other.

`UseMode` had no guard against being asked for the mode it was already in, and
on every call it gave the keyboard back to the rendering view. So a page
reporting that a field took focus caused the browser to move focus, which reset
the page's own focus, which fired another focus event, which came back as
another request. A loop, running as fast as the page could report, and it
looked like the window shaking.

Measured at idle, the browser was burning CPU with nothing happening. It now
sits at zero.

Three changes close it:

    a mode change to the mode already active returns immediately
    the keyboard is only taken back when the command bar was holding it
    the page waits one turn of the event loop before reporting a field lost
      focus, so moving between two fields no longer flips modes twice


## Two more flicker sources, fixed while in there

WebView2 paints white between documents unless told otherwise. Against a dark
interface that is a white flash on every navigation. The engine background and
the area behind it are now the interface colour.

The tab strip was torn down and rebuilt whenever a page changed its title, and
a loading page changes its title several times. The labels are updated in place
now, and controls are only created or destroyed when the number of tabs
actually changes.

A background tab finishing a load also used to take the keyboard. Only the tab
in front can do that now.


## Everything else

Unchanged from 0.11: the luakit key map, the fix for shifted bindings, search
with / and ?, y to copy the address. One executable, no installer, about 60 MB.

162 tests.


## Licence

GNU GPLv3.

Pablo Murad, https://pablomurad.com
https://github.com/runawaydevil/skull_browser_win
