using SkullWins.Storage;
using Xunit;

namespace SkullWins.Tests;

/// <summary>
/// Trust on first use.
///
/// This is the part of gemini that can fail without a symptom. A validation
/// callback that simply returns true gives a browser with TLS that protects
/// nothing, and everything still works, and nobody notices. So the test that
/// matters here is not that a good certificate passes. It is that a changed
/// one is refused.
/// </summary>
public class TrustStoreTests
{
    private const string Host = "example.org";
    private const int Port = 1965;
    private const string CertA = "AA:BB:CC";
    private const string CertB = "DD:EE:FF";

    private const long Now = 1_000_000;
    private const long NextYear = Now + 31_536_000;
    private const long LastYear = Now - 31_536_000;

    [Fact]
    public void An_unknown_host_is_pinned_on_sight()
    {
        using var t = TrustStore.InMemory();
        Assert.Equal(TrustVerdict.Pinned, t.Check(Host, Port, CertA, NextYear, Now));
        Assert.Equal(CertA, t.Find(Host, Port)!.Fingerprint);
    }

    [Fact]
    public void The_same_certificate_is_recognised()
    {
        using var t = TrustStore.InMemory();
        t.Check(Host, Port, CertA, NextYear, Now);
        Assert.Equal(TrustVerdict.Known, t.Check(Host, Port, CertA, NextYear, Now + 60));
    }

    [Fact]
    public void A_changed_certificate_before_expiry_is_refused()
    {
        // The test this whole feature exists for. If this ever passes as
        // Known or Rotated, the browser has no certificate pinning at all and
        // there is no other way to find out.
        using var t = TrustStore.InMemory();
        t.Check(Host, Port, CertA, NextYear, Now);

        Assert.Equal(TrustVerdict.Changed, t.Check(Host, Port, CertB, NextYear, Now + 60));

        // And the pin is not quietly overwritten by the attempt.
        Assert.Equal(CertA, t.Find(Host, Port)!.Fingerprint);
    }

    [Fact]
    public void A_changed_certificate_after_expiry_is_ordinary_rotation()
    {
        using var t = TrustStore.InMemory();
        t.Check(Host, Port, CertA, notAfter: LastYear, now: LastYear - 100);

        Assert.Equal(TrustVerdict.Rotated, t.Check(Host, Port, CertB, NextYear, Now));
        Assert.Equal(CertB, t.Find(Host, Port)!.Fingerprint);
    }

    [Fact]
    public void Fingerprint_comparison_ignores_case()
    {
        using var t = TrustStore.InMemory();
        t.Check(Host, Port, "aa:bb:cc", NextYear, Now);
        Assert.Equal(TrustVerdict.Known, t.Check(Host, Port, "AA:BB:CC", NextYear, Now));
    }

    [Fact]
    public void A_different_port_on_the_same_host_is_a_different_pin()
    {
        using var t = TrustStore.InMemory();
        t.Check(Host, 1965, CertA, NextYear, Now);
        Assert.Equal(TrustVerdict.Pinned, t.Check(Host, 1966, CertB, NextYear, Now));
    }

    [Fact]
    public void A_different_host_is_a_different_pin()
    {
        using var t = TrustStore.InMemory();
        t.Check("a.test", Port, CertA, NextYear, Now);
        Assert.Equal(TrustVerdict.Pinned, t.Check("b.test", Port, CertB, NextYear, Now));
    }

    [Fact]
    public void Accepting_a_change_is_deliberate_and_replaces_the_pin()
    {
        // Only ever reached because the user ran the command after being shown
        // what changed.
        using var t = TrustStore.InMemory();
        t.Check(Host, Port, CertA, NextYear, Now);
        Assert.Equal(TrustVerdict.Changed, t.Check(Host, Port, CertB, NextYear, Now));

        t.Accept(Host, Port, CertB, NextYear, Now);

        Assert.Equal(TrustVerdict.Known, t.Check(Host, Port, CertB, NextYear, Now));
    }

    [Fact]
    public void Accepting_an_unknown_host_simply_pins_it()
    {
        using var t = TrustStore.InMemory();
        t.Accept(Host, Port, CertA, NextYear, Now);
        Assert.NotNull(t.Find(Host, Port));
    }

    [Fact]
    public void Forgetting_a_host_makes_it_unknown_again()
    {
        using var t = TrustStore.InMemory();
        t.Check(Host, Port, CertA, NextYear, Now);

        Assert.True(t.Forget(Host, Port));
        Assert.False(t.Forget(Host, Port));
        Assert.Equal(TrustVerdict.Pinned, t.Check(Host, Port, CertB, NextYear, Now));
    }

    [Fact]
    public void First_seen_survives_a_rotation_but_last_seen_moves()
    {
        using var t = TrustStore.InMemory();
        t.Check(Host, Port, CertA, LastYear, LastYear - 100);
        var before = t.Find(Host, Port)!;

        t.Check(Host, Port, CertB, NextYear, Now);
        var after = t.Find(Host, Port)!;

        Assert.Equal(before.FirstSeen, after.FirstSeen);
        Assert.True(after.LastSeen > before.LastSeen);
    }

    [Fact]
    public void All_lists_every_pin()
    {
        using var t = TrustStore.InMemory();
        t.Check("b.test", Port, CertA, NextYear, Now);
        t.Check("a.test", Port, CertB, NextYear, Now);

        var all = t.All();
        Assert.Equal(2, all.Count);
        Assert.Equal("a.test", all[0].Host);
    }
}
