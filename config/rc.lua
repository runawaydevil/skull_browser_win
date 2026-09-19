-- rc.lua - your configuration.
--
-- This file is real Lua and runs at startup. A syntax error here does not stop
-- the browser: it falls back to the built-in copy and reports the line that
-- failed. Run "skull --check" to validate without opening a window.
--
-- This file lands in %APPDATA%\skull when you run "skull --init". Until then
-- the browser uses its built-in copy and this file is only documentation.

-- Force a language. Without this, the browser follows Windows and falls back
-- to English. Available: "en", "pt_BR".
-- skull.locale = "pt_BR"

-- The search engine used when what you typed is not an address.
-- skull.search = "https://duckduckgo.com/?q="

-- The page a new tab opens on.
-- skull.newtab = "skull://newtab"
