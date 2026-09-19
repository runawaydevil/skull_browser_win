namespace SkullWins.Core;

/// <summary>
/// The signal bus, ported in shape from luakit's lousy.signal.
///
/// Two rules here are load-bearing and must not be "improved":
///
///   1. Handlers run in the order they were added.
///   2. The first handler returning a non-null value stops the chain, and its
///      value becomes the result of Emit.
///
/// Rule 2 is the veto mechanism. A navigation handler returning false blocks
/// the navigation; a mime handler returning false cancels the download. Dozens
/// of call sites depend on it, and breaking it fails silently, which is the
/// worst way to break anything.
///
/// The handler list is copied before dispatch because a handler is allowed to
/// add or remove handlers while the chain is running.
/// </summary>
public sealed class Signals
{
    private readonly Dictionary<string, List<Func<object?[], object?>>> _handlers = new();

    /// <summary>
    /// Signal names allow word characters, underscore, dash and colon, so
    /// "property::uri" and "load-status" are both legal.
    /// </summary>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name)) { return false; }
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or ':')) { return false; }
        }
        return true;
    }

    public void Add(string name, Func<object?[], object?> handler)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException($"invalid signal name: {name}", nameof(name));
        }

        if (!_handlers.TryGetValue(name, out var list))
        {
            list = new List<Func<object?[], object?>>();
            _handlers[name] = list;
        }
        list.Add(handler);
    }

    public bool Remove(string name, Func<object?[], object?> handler)
        => _handlers.TryGetValue(name, out var list) && list.Remove(handler);

    public void RemoveAll(string name) => _handlers.Remove(name);

    public int Count(string name)
        => _handlers.TryGetValue(name, out var list) ? list.Count : 0;

    /// <summary>
    /// Run the chain. Returns the first non-null handler result, or null when
    /// every handler declined.
    /// </summary>
    public object? Emit(string name, params object?[] args)
    {
        if (!_handlers.TryGetValue(name, out var list) || list.Count == 0)
        {
            return null;
        }

        // Copy first: a handler may mutate the list while we walk it.
        foreach (var handler in list.ToArray())
        {
            var result = handler(args);
            if (result is not null) { return result; }
        }

        return null;
    }
}
