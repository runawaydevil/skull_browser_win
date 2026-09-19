using System.Security.Cryptography.X509Certificates;
using SkullWins.Core;
using Xunit;

namespace SkullWins.Tests;

/// <summary>
/// Making and reloading an identity.
///
/// The round trip is the one that earns its place. A certificate that comes
/// back from storage without a usable private key still loads, still has the
/// right subject, and still fails at the only moment that counts: the capsule
/// simply does not recognise you, and there is nothing in the logs.
/// </summary>
public class ClientCertificateTests
{
    [Fact]
    public void A_created_certificate_carries_its_name()
    {
        var cert = ClientCertificates.Load(ClientCertificates.Create("pablo"))!;
        Assert.Contains("CN=pablo", cert.Subject);
    }

    [Fact]
    public void A_created_certificate_has_a_usable_private_key()
    {
        // Without the key there is no identity, only a name.
        var cert = ClientCertificates.Load(ClientCertificates.Create("pablo"))!;

        Assert.True(cert.HasPrivateKey);
        using var key = cert.GetECDsaPrivateKey();
        Assert.NotNull(key);
    }

    [Fact]
    public void A_certificate_survives_a_round_trip_through_bytes()
    {
        // Exported, stored, read back. This is what the profile does every
        // time a request needs an identity.
        var bytes = ClientCertificates.Create("traveller");

        var first = ClientCertificates.Load(bytes)!;
        var second = ClientCertificates.Load(bytes)!;

        Assert.Equal(first.Thumbprint, second.Thumbprint);
        Assert.True(second.HasPrivateKey);
        using var key = second.GetECDsaPrivateKey();
        Assert.NotNull(key);
    }

    [Fact]
    public void Two_identities_are_not_the_same_identity()
    {
        var a = ClientCertificates.Load(ClientCertificates.Create("a"))!;
        var b = ClientCertificates.Load(ClientCertificates.Create("b"))!;
        Assert.NotEqual(a.Thumbprint, b.Thumbprint);
    }

    [Fact]
    public void An_identity_lasts_long_enough_to_be_worth_having()
    {
        // A lapsed identity costs someone their account on a capsule, with no
        // way to prove they were the previous holder.
        var cert = ClientCertificates.Load(ClientCertificates.Create("pablo"))!;

        Assert.True(cert.NotAfter > DateTime.Now.AddYears(ClientCertificates.ValidYears - 1));
        Assert.True(cert.NotBefore < DateTime.Now);
    }

    [Fact]
    public void Garbage_loads_as_nothing_rather_than_throwing()
    {
        // A corrupt identity must not stop a page from loading.
        Assert.Null(ClientCertificates.Load([1, 2, 3, 4]));
        Assert.Null(ClientCertificates.Load([]));
    }

    [Theory]
    [InlineData("pablo")]
    [InlineData("work-account")]
    [InlineData("test_1")]
    [InlineData("a.b")]
    public void Reasonable_names_are_allowed(string name)
        => Assert.True(ClientCertificates.IsValidName(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".hidden")]
    [InlineData("..")]
    [InlineData("has space")]
    [InlineData("slash/es")]
    [InlineData("back\\slash")]
    [InlineData("colon:name")]
    [InlineData("star*")]
    public void Names_that_would_break_a_path_are_refused(string name)
        => Assert.False(ClientCertificates.IsValidName(name));

    [Fact]
    public void A_very_long_name_is_refused()
        => Assert.False(ClientCertificates.IsValidName(new string('a', 49)));

    [Fact]
    public void Creating_with_a_bad_name_throws_rather_than_mangling_it()
        => Assert.Throws<ArgumentException>(() => ClientCertificates.Create("bad/name"));
}
