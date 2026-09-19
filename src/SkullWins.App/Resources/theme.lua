-- theme.lua - colours and fonts.
--
-- Lookup walks up a cascade: a missing key drops everything up to the first
-- underscore and tries again, down to fg, bg or font. So tab_selected_fg falls
-- back to selected_fg, then to fg. Change a handful of roots and the whole
-- interface follows.

return {
    font = "12px Consolas, monospace",
    fg   = "#d8d8d8",
    bg   = "#0d0d0d",

    selected_fg = "#9fd18a",
    selected_bg = "#222222",

    sbar_fg = "#b8b8b8",
    sbar_bg = "#161616",

    ibar_fg = "#e8e8e8",
    ibar_bg = "#0d0d0d",

    error_fg = "#d08a8a",
    hint_fg  = "#0d0d0d",
    hint_bg  = "#9fd18a",
}
