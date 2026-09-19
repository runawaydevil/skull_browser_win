namespace SkullWins.Core;

/// <summary>How the profile location was arrived at, for the about page.</summary>
public enum RootKind
{
    /// <summary>Beside the executable. The browser travels with its data.</summary>
    Portable,

    /// <summary>In the user's roaming profile.</summary>
    Roaming,

    /// <summary>Somewhere the user named.</summary>
    Explicit,
}

public sealed record ProfileChoice(string Path, RootKind Kind, string Reason)
{
    public bool IsPortable => Kind == RootKind.Portable;
}

/// <summary>
/// Decides where the profile lives.
///
/// Kept here, away from the filesystem, because the interesting part is the
/// decision and not the I/O. The caller supplies the candidate paths and a
/// predicate that answers whether a directory can be written to, so every
/// branch is reachable in a test without a stick, a read-only share or an
/// install under Program Files.
/// </summary>
public static class ProfileRoot
{
    /// <summary>What the user asked for on the command line.</summary>
    public sealed record Request(
        string? ExplicitPath = null,
        bool ForcePortable = false,
        bool ForceRoaming = false);

    /// <summary>
    /// Beside the executable when that can be written to, so a copy carried on
    /// a stick carries its history, its pinned certificates and its identities
    /// with it. The roaming profile when it cannot, because a copy installed
    /// under Program Files still has to open.
    ///
    /// Throws only when the user forced a location that cannot be used. An
    /// automatic choice never fails; it falls back.
    /// </summary>
    public static ProfileChoice Decide(
        Request request,
        string? besideExecutable,
        string roaming,
        Func<string, bool> canWrite)
    {
        if (!string.IsNullOrWhiteSpace(request.ExplicitPath))
        {
            return new ProfileChoice(request.ExplicitPath, RootKind.Explicit, "--profile");
        }

        if (request.ForceRoaming)
        {
            return new ProfileChoice(roaming, RootKind.Roaming, "--no-portable");
        }

        if (request.ForcePortable)
        {
            // Asked for by name, so a failure is reported rather than quietly
            // turned into something else the user did not ask for.
            if (string.IsNullOrEmpty(besideExecutable))
            {
                throw new InvalidOperationException(
                    "--portable: cannot determine the executable's directory");
            }

            if (!canWrite(besideExecutable))
            {
                throw new InvalidOperationException(
                    "--portable: cannot write to " + besideExecutable);
            }

            return new ProfileChoice(besideExecutable, RootKind.Portable, "--portable");
        }

        if (!string.IsNullOrEmpty(besideExecutable) && canWrite(besideExecutable))
        {
            return new ProfileChoice(besideExecutable, RootKind.Portable, "writable");
        }

        return new ProfileChoice(
            roaming,
            RootKind.Roaming,
            string.IsNullOrEmpty(besideExecutable) ? "no executable directory" : "not writable");
    }
}
