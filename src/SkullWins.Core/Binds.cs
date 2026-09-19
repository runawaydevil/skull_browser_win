namespace SkullWins.Core;

/// <summary>A key binding: a normalised trigger, a description key, an action.</summary>
public sealed record Bind(string Trigger, string Description, Func<BindContext, bool> Action);

/// <summary>What an action is handed when it fires.</summary>
public sealed record BindContext(string Key, string Modifiers, string Buffer, int Count);

/// <summary>
/// Trigger normalisation and key dispatch, following luakit's lousy.bind.
///
/// The convention that matters, and that reads backwards until you know why:
/// an action returning false means "I did not handle this, keep looking".
/// Anything else, including no return at all, counts as handled and stops the
/// search. It is deliberate: it lets a bind conditionally decline without
/// needing a separate predicate.
///
/// Triggers are normalised when they are added, not when a key arrives, so the
/// hot path is a dictionary lookup rather than string surgery per keystroke.
/// </summary>
public static class Triggers
{
    /// <summary>
    /// Canonical form of a trigger.
    ///
    ///   "j"            -> "j"
    ///   "J"            -> "&lt;shift-j&gt;"     uppercase implies shift
    ///   "&lt;Control-c&gt;"  -> "&lt;control-c&gt;"   modifiers lowercased and sorted
    ///   "&lt;C-A-x&gt;"     -> "&lt;control-mod1-x&gt;"
    ///
    /// Sorting the modifiers means &lt;control-shift-a&gt; and &lt;shift-control-a&gt;
    /// are the same bind, which is what a user expects.
    /// </summary>
    public static string Normalise(string trigger)
    {
        if (string.IsNullOrEmpty(trigger)) { return trigger; }

        if (trigger.Length == 1)
        {
            return char.IsUpper(trigger[0])
                ? "<shift-" + char.ToLowerInvariant(trigger[0]) + ">"
                : trigger;
        }

        if (!(trigger.StartsWith('<') && trigger.EndsWith('>')))
        {
            // A multi-character trigger with no angle brackets is a buffer
            // sequence such as "gg" or a named key such as "Escape".
            return trigger;
        }

        var inner = trigger[1..^1];
        var parts = inner.Split('-');
        if (parts.Length == 1) { return "<" + parts[0].ToLowerInvariant() + ">"; }

        var key = parts[^1];
        var mods = new List<string>();

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var m = ExpandModifier(parts[i]);
            if (m is not null && !mods.Contains(m)) { mods.Add(m); }
        }

        // An uppercase final key carries an implicit shift.
        if (key.Length == 1 && char.IsUpper(key[0]))
        {
            if (!mods.Contains("shift")) { mods.Add("shift"); }
            key = char.ToLowerInvariant(key[0]).ToString();
        }

        mods.Sort(StringComparer.Ordinal);
        return "<" + string.Join('-', mods) + "-" + key + ">";
    }

    private static string? ExpandModifier(string raw) => raw.ToLowerInvariant() switch
    {
        "c" or "ctrl" or "control" => "control",
        "s" or "shift" => "shift",
        "a" or "alt" or "mod1" => "mod1",
        "m" or "meta" or "super" or "mod4" => "mod4",
        "lock" => "lock",
        "mod2" => "mod2",
        "mod3" => "mod3",
        "mod5" => "mod5",
        _ => null,
    };

    /// <summary>
    /// Build the trigger string for a key that just arrived, given the already
    /// sorted modifier string the input layer produced.
    /// </summary>
    public static string FromKey(string key, string modifiers)
    {
        var mods = modifiers.Length == 0
            ? new List<string>()
            : modifiers.Split('-').ToList();

        // A single printable character already carries the shift in its own
        // identity. The browser reports shift+j as "J" and shift+; as ":", so
        // keeping shift in the trigger as well would double-count it and the
        // lookup would miss every capital and every shifted symbol. Letters
        // fold down to lowercase plus shift; everything else drops shift and
        // keeps the character it produced.
        if (key.Length == 1)
        {
            if (char.IsUpper(key[0]))
            {
                key = char.ToLowerInvariant(key[0]).ToString();
                if (!mods.Contains("shift")) { mods.Add("shift"); }
            }
            else if (!char.IsLetter(key[0]))
            {
                mods.Remove("shift");
            }
        }

        if (mods.Count == 0) { return key; }

        mods.Sort(StringComparer.Ordinal);
        return "<" + string.Join('-', mods) + "-" + key + ">";
    }
}

/// <summary>
/// A per-mode bind table plus the pending key buffer that makes sequences like
/// "gg" and counted motions like "42gt" work.
/// </summary>
public sealed class BindTable
{
    private readonly Dictionary<string, Bind> _binds = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    public IReadOnlyList<Bind> All => _order.Select(t => _binds[t]).ToList();

    public int Count => _binds.Count;

    /// <summary>Adding a trigger that already exists replaces it.</summary>
    public void Add(string trigger, string description, Func<BindContext, bool> action)
    {
        var key = Triggers.Normalise(trigger);
        if (!_binds.ContainsKey(key)) { _order.Add(key); }
        _binds[key] = new Bind(key, description, action);
    }

    public bool Remove(string trigger)
    {
        var key = Triggers.Normalise(trigger);
        _order.Remove(key);
        return _binds.Remove(key);
    }

    public Bind? Find(string trigger)
        => _binds.TryGetValue(Triggers.Normalise(trigger), out var b) ? b : null;

    /// <summary>
    /// True when some bind could still match if more characters arrive. Used to
    /// decide whether to keep the pending buffer or drop it. "g" is a partial
    /// match while "gg" exists; "q" is not, so the buffer clears immediately.
    /// </summary>
    public bool HasPartialMatch(string buffer)
    {
        foreach (var trigger in _order)
        {
            if (trigger.Length > buffer.Length
                && trigger.StartsWith(buffer, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Merge another table over this one. Used to fold the "all" meta-mode into
    /// every mode; the other table's binds lose to this one on conflict, which
    /// is what lets a mode override a global key.
    /// </summary>
    public BindTable MergedWith(BindTable other)
    {
        var merged = new BindTable();
        foreach (var b in other.All) { merged.Add(b.Trigger, b.Description, b.Action); }
        foreach (var b in All) { merged.Add(b.Trigger, b.Description, b.Action); }
        return merged;
    }
}

/// <summary>Result of feeding a key to the dispatcher.</summary>
public sealed record DispatchResult(bool Handled, string Buffer);

public static class Dispatcher
{
    /// <summary>
    /// Feed one key press into a bind table.
    ///
    /// Direct triggers (anything with a modifier, or a named key) are matched on
    /// their own. Bare printable characters accumulate into the buffer so that
    /// sequences and counts work, and the buffer is dropped as soon as nothing
    /// can match it.
    /// </summary>
    public static DispatchResult Feed(
        BindTable table, string key, string modifiers, string buffer, bool bufferEnabled)
    {
        var direct = Triggers.FromKey(key, modifiers);

        var bind = table.Find(direct);
        if (bind is not null)
        {
            var ctx = new BindContext(key, modifiers, buffer, ParseCount(buffer));
            // false means "not handled", so the search continues.
            if (bind.Action(ctx)) { return new DispatchResult(true, ""); }
        }

        var isBareChar = modifiers.Length == 0 && key.Length == 1;
        if (!bufferEnabled || !isBareChar)
        {
            return new DispatchResult(false, bufferEnabled ? buffer : "");
        }

        var next = buffer + key;

        var seqBind = table.Find(next);
        if (seqBind is not null)
        {
            var ctx = new BindContext(key, modifiers, next, ParseCount(next));
            if (seqBind.Action(ctx)) { return new DispatchResult(true, ""); }
        }

        // A counted motion such as 42gt: strip the leading digits and retry.
        var stripped = StripCount(next);
        if (stripped != next)
        {
            var countBind = table.Find(stripped);
            if (countBind is not null)
            {
                var ctx = new BindContext(key, modifiers, next, ParseCount(next));
                if (countBind.Action(ctx)) { return new DispatchResult(true, ""); }
            }
        }

        // Keep the buffer only while something could still match it. Digits are
        // always kept, because they might be the count of a motion still coming.
        var keep = table.HasPartialMatch(next)
                   || table.HasPartialMatch(stripped)
                   || next.All(char.IsDigit);

        return new DispatchResult(false, keep ? next : "");
    }

    private static string StripCount(string buffer)
    {
        var i = 0;
        while (i < buffer.Length && char.IsDigit(buffer[i])) { i++; }
        return buffer[i..];
    }

    private static int ParseCount(string buffer)
    {
        var digits = new string(buffer.TakeWhile(char.IsDigit).ToArray());
        return digits.Length > 0 && int.TryParse(digits, out var n) ? n : 1;
    }
}
