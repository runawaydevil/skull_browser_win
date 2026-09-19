using System.IO;
using System.Security.Cryptography.X509Certificates;
using SkullWins.Core;

namespace SkullWins.App;

/// <summary>
/// Client certificates, which are what gemini calls identities.
///
/// A capsule recognises you by the certificate you present, the way a server
/// recognises an SSH key. There is no registration and no password: the key
/// file is the credential.
///
/// The specification is explicit that a client must not generate one and use
/// it without the user being involved, so nothing here is ever called from a
/// response handler. It is only reached from a command the user typed.
/// </summary>
public static class Identities
{
    public static string PathFor(string name) => Path.Combine(Profile.IdentitiesDir, name + ".pfx");

    public static bool Exists(string name) => File.Exists(PathFor(name));

    public static IReadOnlyList<string> All()
    {
        try
        {
            if (!Directory.Exists(Profile.IdentitiesDir)) { return []; }

            return Directory.GetFiles(Profile.IdentitiesDir, "*.pfx")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException) { return []; }
    }

    /// <summary>
    /// Generate a self-signed certificate and write it to the profile.
    ///
    /// ECDSA on P-256: small, fast, and accepted everywhere in gemini space.
    /// The file carries no passphrase, which is what every gemini client does
    /// and what an unencrypted SSH key does. Whoever holds the file is the
    /// identity, and under a portable profile that file sits beside the
    /// executable, which is a choice worth knowing about.
    /// </summary>
    public static void Create(string name)
    {
        if (Exists(name)) { throw new IOException("an identity called " + name + " already exists"); }

        Directory.CreateDirectory(Profile.IdentitiesDir);
        File.WriteAllBytes(PathFor(name), ClientCertificates.Create(name));
    }

    /// <summary>
    /// Load an identity from the profile. See ClientCertificates.Load for why
    /// the storage flag it uses is not a detail.
    /// </summary>
    public static X509Certificate2? Load(string name)
    {
        try
        {
            var path = PathFor(name);
            return File.Exists(path) ? ClientCertificates.Load(File.ReadAllBytes(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static bool Delete(string name)
    {
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path)) { return false; }

            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
    }

    /// <summary>When it was made and when it lapses, for the identities page.</summary>
    public static (DateTime Created, DateTime Expires)? Describe(string name)
    {
        using var certificate = Load(name);
        if (certificate is null) { return null; }

        return (certificate.NotBefore, certificate.NotAfter);
    }
}
