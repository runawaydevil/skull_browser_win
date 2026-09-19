-- spike.lua - a camada Lua do spike de teclado.
--
-- Pequena de proposito, mas com a forma definitiva: tabela de binds por modo,
-- e a convencao invertida do luakit preservada, ou seja, uma acao que devolve
-- false significa "nao tratei, continue procurando".

local mode = "normal"

local function trig(key, mods)
    if mods == "" then return key end
    return "<" .. mods .. "-" .. key .. ">"
end

local binds = {}

binds.all = {
    ["Escape"] = function ()
        set_mode("normal")
        return true
    end,
}

binds.normal = {
    ["j"] = function () eval_js("window.scrollBy(0, 60)")  return true end,
    ["k"] = function () eval_js("window.scrollBy(0, -60)") return true end,
    ["d"] = function () eval_js("window.scrollBy(0, window.innerHeight / 2)")  return true end,
    ["u"] = function () eval_js("window.scrollBy(0, -window.innerHeight / 2)") return true end,
    ["G"] = function () eval_js("window.scrollTo(0, document.body.scrollHeight)") return true end,
    ["i"] = function () set_mode("insert")  return true end,
    [":"] = function () set_mode("command") return true end,
    ["r"] = function () reload() return true end,

    -- Prova o criterio 5: se o Ctrl+F do Edge estivesse ativo, a busca nativa
    -- abriria e esta acao nunca rodaria.
    ["<control-f>"] = function ()
        notify("Ctrl+F chegou no Lua. A busca nativa do Edge esta desligada.")
        return true
    end,

    -- Prova a convencao invertida: devolver false nao consome a tecla, entao o
    -- despacho segue e o fallback registra a tecla nao ligada.
    ["z"] = function ()
        notify("z: acao devolveu false, logo nao tratou")
        return false
    end,
}

binds.insert = {}
binds.command = {}

function on_mode_changed(m)
    mode = m
end

function on_key(key, mods)
    local t = trig(key, mods)

    local set = binds[mode]
    if set and set[t] then
        if set[t]() ~= false then return true end
    end

    if binds.all[t] then
        if binds.all[t]() ~= false then return true end
    end

    return false
end
