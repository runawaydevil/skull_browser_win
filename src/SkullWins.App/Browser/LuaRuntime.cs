using System.IO;
using System.Text;
using NLua;
using SkullWins.Core;

namespace SkullWins.App.Browser;

/// <summary>
/// The embedded Lua VM and the surface it sees.
///
/// Everything crossing this boundary goes through a try/catch. A broken rc.lua
/// must degrade to a warning in the status bar and a line in skull://log, never
/// to a dead browser. That is the single rule this class exists to enforce.
///
/// The names mirror the Linux version with skull in place of luakit, so a config
/// written for one reads in the other even though no code is shared.
/// </summary>
public sealed class LuaRuntime : IDisposable
{
    private readonly Lua _lua = new();
    private readonly Locale _locale;
    private readonly Action<string> _log;

    public string? LastError { get; private set; }

    public LuaRuntime(Locale locale, Action<string> log)
    {
        _locale = locale;
        _log = log;
        _lua.State.Encoding = Encoding.UTF8;
    }

    /// <summary>Expose a host method to Lua under a global name.</summary>
    public void Register(string name, object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName)
            ?? throw new ArgumentException($"no such method: {methodName}", nameof(methodName));
        _lua.RegisterFunction(name, target, method);
    }

    public void SetGlobal(string name, object? value) => _lua[name] = value;

    public object? GetGlobal(string name) => _lua[name];

    /// <summary>
    /// Run a chunk. Returns null on success or the error text on failure; the
    /// caller decides whether that is fatal. Nothing here throws at the caller.
    /// </summary>
    public string? DoString(string chunk, string name = "chunk")
    {
        try
        {
            _lua.DoString(chunk, name);
            LastError = null;
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log("lua error in " + name + ": " + ex.Message);
            return ex.Message;
        }
    }

    public string? DoFile(string path)
    {
        if (!File.Exists(path)) { return "not found: " + path; }

        try
        {
            return DoString(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
        }
        catch (IOException ex)
        {
            LastError = ex.Message;
            return ex.Message;
        }
    }

    /// <summary>
    /// Call a Lua function by name. Missing function is not an error: a config
    /// is allowed to not define an optional hook.
    /// </summary>
    public object?[]? Call(string function, params object[] args)
    {
        try
        {
            if (_lua[function] is not LuaFunction fn) { return null; }
            return fn.Call(args);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log("lua error calling " + function + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>True when the named global is a callable function.</summary>
    public bool HasFunction(string name)
    {
        try { return _lua[name] is LuaFunction; }
        catch { return false; }
    }

    /// <summary>
    /// Read a flat string table, the shape a locale file returns. Anything that
    /// is not a string pair is skipped rather than failing the load.
    /// </summary>
    public Dictionary<string, string> ReadStringTable(string chunk, string name)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var returned = _lua.DoString("return (function() " + chunk + " end)()", name);
            if (returned is { Length: > 0 } && returned[0] is LuaTable lt)
            {
                foreach (var key in lt.Keys)
                {
                    if (key is string k && lt[k] is string v) { table[k] = v; }
                }
            }
        }
        catch (Exception ex)
        {
            _log("lua error reading table " + name + ": " + ex.Message);
        }

        return table;
    }

    /// <summary>Translate through the active catalogue. Exposed to Lua as i18n.</summary>
    public string Translate(string key) => _locale.Translate(key);

    public void Dispose() => _lua.Dispose();
}
