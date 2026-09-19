using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SkullWins.Core;

/// <summary>
/// Making and loading the client certificates gemini calls identities.
///
/// Kept away from the filesystem so every part of it can be tested: the test
/// project does not reference the application, and a certificate that loses
/// its private key on the way back from disk fails in a way nothing else
/// notices until a capsule refuses to recognise you.
/// </summary>
public static class ClientCertificates
{
    /// <summary>
    /// Ten years. An identity is a long-lived name, not a session, and an
    /// expiry that lapses would cost someone their account on a capsule with
    /// no way to recover it.
    /// </summary>
    public const int ValidYears = 10;

    /// <summary>
    /// A name that is safe as a file name and readable as a subject. Anything
    /// else is refused rather than quietly rewritten, so an identity is always
    /// called what the user called it.
    /// </summary>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return false; }
        if (name.Length > 48) { return false; }
        if (name.StartsWith('.')) { return false; }

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '.')) { return false; }
        }

        return true;
    }

    /// <summary>
    /// A fresh self-signed certificate, as PKCS#12 bytes.
    ///
    /// ECDSA on P-256: small, quick, and accepted throughout gemini space.
    /// No passphrase, which is what every gemini client does and what an
    /// unencrypted SSH key does. Whoever holds these bytes is the identity.
    /// </summary>
    public static byte[] Create(string name)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException("invalid identity name: " + name, nameof(name));
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);

        // Backdated a day so a clock running slightly behind does not reject a
        // certificate that was made a moment ago.
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(ValidYears));

        return certificate.Export(X509ContentType.Pkcs12);
    }

    /// <summary>
    /// Load a certificate so it can be presented during a handshake.
    ///
    /// The storage flag is load-bearing. A PKCS#12 loaded with
    /// EphemeralKeySet does not work with SslStream on Windows: the handshake
    /// completes and the certificate is never sent, which looks exactly like a
    /// server ignoring it and gives nothing to debug. DefaultKeySet imports the
    /// key where the TLS stack can reach it.
    ///
    /// The cost of that is worth stating rather than discovering: loading an
    /// identity puts its key in a container on the machine, portable profile
    /// or not.
    ///
    /// Returns null rather than throwing, because a corrupt identity must not
    /// stop a page from loading; the request simply goes without one.
    /// </summary>
    public static X509Certificate2? Load(byte[] pkcs12)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(
                pkcs12, null, X509KeyStorageFlags.DefaultKeySet);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
