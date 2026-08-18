using FluentAssertions;
using SoccerAi.Api.Configuration;
using SoccerAi.Api.Security;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// Admin keys can be supplied raw or pre-hashed, from configuration or the
/// environment, and every source is additive.
///
/// The environment used to be read only when configuration carried no hashes,
/// so ADMIN_API_KEY_HASH did nothing on any deployment that shipped one in
/// appsettings.json. Setting it appeared to work and silently didn't, which is
/// the failure these tests exist to prevent.
/// </summary>
public class AdminApiKeyRegistryTests
{
    private const string ConfiguredGuid = "3f2a9c1e-7b4d-4e8a-9f01-2c6d5b8e4a71";

    private static AdminApiKeyRegistry Build(
        string[]? hashes = null, string[]? keys = null,
        string? rawKeyEnv = null, string? hashEnv = null) =>
        new(new AdminApiKeyOptions { ApiKeyHashes = hashes ?? [], ApiKeys = keys ?? [] },
            rawKeyEnv, hashEnv);

    private static string HashOf(string key) =>
        AdminApiKeyRegistry.ComputeHash(AdminApiKeyRegistry.Canonicalize(key));

    [Fact]
    public void EnvironmentHash_IsAccepted_AlongsideConfiguredHashes()
    {
        var registry = Build(hashes: [HashOf(ConfiguredGuid)], hashEnv: HashOf("env-supplied-key-value"));

        registry.Count.Should().Be(2, "environment keys add to the configured ones rather than replacing them");
        registry.Matches(ConfiguredGuid).Should().BeTrue();
        registry.Matches("env-supplied-key-value").Should().BeTrue();
    }

    [Fact]
    public void RawKeyFromEnvironment_IsAccepted_WithoutHashingByHand()
    {
        var registry = Build(rawKeyEnv: "a-perfectly-ordinary-key");

        registry.IsConfigured.Should().BeTrue();
        registry.Matches("a-perfectly-ordinary-key").Should().BeTrue();
    }

    [Fact]
    public void RawKeysFromConfiguration_AreAccepted()
    {
        var registry = Build(keys: ["first-raw-key-value", "second-raw-key-value"]);

        registry.Matches("first-raw-key-value").Should().BeTrue();
        registry.Matches("second-raw-key-value").Should().BeTrue();
    }

    [Fact]
    public void SeveralKeysCanShareOneEnvironmentVariable()
    {
        var registry = Build(rawKeyEnv: "key-number-one-here, key-number-two-here");

        registry.Count.Should().Be(2);
        registry.Matches("key-number-one-here").Should().BeTrue();
        registry.Matches("key-number-two-here").Should().BeTrue();
    }

    [Fact]
    public void GuidKeys_MatchRegardlessOfRepresentation()
    {
        // Hashes minted while keys had to be GUIDs were taken over the canonical
        // lowercase "D" form, so those hashes have to survive this change.
        var registry = Build(hashes: [HashOf(ConfiguredGuid)]);

        registry.Matches(ConfiguredGuid.ToUpperInvariant()).Should().BeTrue();
        registry.Matches($"{{{ConfiguredGuid}}}").Should().BeTrue("braced GUIDs parse to the same value");
        registry.Matches($"  {ConfiguredGuid}  ").Should().BeTrue("surrounding whitespace is trimmed");
    }

    [Fact]
    public void NonGuidKeys_AreCaseSensitive()
    {
        var registry = Build(keys: ["MixedCaseKeyValue"]);

        registry.Matches("MixedCaseKeyValue").Should().BeTrue();
        registry.Matches("mixedcasekeyvalue").Should().BeFalse("case carries entropy in a freely chosen key");
    }

    [Fact]
    public void UnknownKey_IsRejected()
    {
        var registry = Build(keys: ["the-only-configured-key"]);

        registry.Matches("some-other-key-entirely").Should().BeFalse();
    }

    [Fact]
    public void StoredHash_IsNotItselfAKey()
    {
        // Presenting the digest instead of the key must not authenticate.
        var hash = HashOf(ConfiguredGuid);
        var registry = Build(hashes: [hash]);

        registry.Matches(hash).Should().BeFalse();
    }

    [Fact]
    public void WithNoSources_TheSchemeIsInactive()
    {
        var registry = Build();

        registry.IsConfigured.Should().BeFalse();
        registry.Sources.Should().BeEmpty();
        registry.Matches("anything at all").Should().BeFalse();
    }

    [Fact]
    public void BlankValues_AreIgnored()
    {
        var registry = Build(hashes: ["", "   "], keys: [""], rawKeyEnv: " , ", hashEnv: "");

        registry.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void DuplicateKeysAcrossSources_AreCountedOnce()
    {
        var registry = Build(keys: ["shared-key-across-sources"], rawKeyEnv: "shared-key-across-sources");

        registry.Count.Should().Be(1);
    }

    [Fact]
    public void Sources_AreReportedForStartupLogging()
    {
        var registry = Build(hashes: [HashOf(ConfiguredGuid)], rawKeyEnv: "an-environment-key-value");

        registry.Sources.Should().HaveCount(2);
        registry.Sources.Should().Contain(s => s.Contains(AdminApiKeyRegistry.RawKeyEnvVar));
        registry.Sources.Should().NotContain(s => s.Contains("an-environment-key-value"),
            "the log line must never carry the key itself");
    }
}
