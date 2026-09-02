using System.Text.Json;

namespace MittwaldDdns.Tests;

public sealed class ConfigStoreTests
{
    [Fact]
    public void PlainConfig_RoundTripsWithCurrentJsonShape()
    {
        var path = NewTempPath();
        var config = CreateValidConfig();

        try
        {
            new ConfigStore(path, null).SavePlain(config);

            var json = File.ReadAllText(path);
            Assert.Contains("\"global_webhook\"", json);
            Assert.Contains("\"default_api_version\"", json);
            Assert.Contains("\"api_key\"", json);
            Assert.Contains("\"project_id\"", json);

            var loaded = ConfigStore.LoadFromFile(path, null);
            Assert.Equal(ConfigFileFormat.Plain, loaded.Format);
            Assert.Empty(ConfigValidator.Validate(loaded.Config));
            Assert.Equal("example.com", loaded.Config.Accounts[0].Domains[0].Domain);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EncryptedConfig_RoundTripsThroughExistingEnvelope()
    {
        var path = NewTempPath();
        var config = CreateValidConfig();

        try
        {
            new ConfigStore(path, "secret").SaveEncrypted(config);

            var json = File.ReadAllText(path);
            Assert.Contains("\"format\"", json);
            Assert.Contains(ConfigStore.EncryptedFormat, json);
            Assert.DoesNotContain("api-key", json);

            var loaded = ConfigStore.LoadFromFile(path, "secret");
            Assert.Equal(ConfigFileFormat.Encrypted, loaded.Format);
            Assert.Empty(ConfigValidator.Validate(loaded.Config));
            Assert.Equal("main", loaded.Config.Accounts[0].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyConfig_StillImports()
    {
        var path = NewTempPath();
        var legacy = new
        {
            ApiKey = "api-key",
            Timeout = TimeSpan.FromMinutes(10),
            GlobalWebhook = "https://example.com/hook",
            GlobalApiVersion = 1,
            Domains = new[]
            {
                new
                {
                    Id = "domain-id",
                    Domain = "example.com",
                    ProjectId = "project-id"
                }
            }
        };

        try
        {
            ConfigStore.WriteAllTextAtomic(path, JsonSerializer.Serialize(legacy, ConfigStore.JsonOptions));

            var loaded = ConfigStore.LoadFromFile(path, null);

            Assert.Equal(ConfigFileFormat.Plain, loaded.Format);
            Assert.Equal("main", loaded.Config.Accounts[0].Name);
            Assert.Equal("api-key", loaded.Config.Accounts[0].ApiKey);
            Assert.Equal(1, loaded.Config.DefaultApiVersion);
            Assert.Equal(TimeSpan.FromMinutes(10), loaded.Config.Timeout);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ConfigModel CreateValidConfig()
    {
        return new ConfigModel
        {
            GlobalWebhook = "https://example.com/hook",
            DefaultApiVersion = 2,
            Timeout = TimeSpan.FromMinutes(5),
            Accounts =
            [
                new ConfigAccount
                {
                    Name = "main",
                    ApiKey = "api-key",
                    Domains =
                    [
                        new ConfigDomain
                        {
                            Id = "domain-id",
                            Domain = "example.com",
                            ProjectId = "project-id"
                        }
                    ]
                }
            ]
        };
    }

    private static string NewTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"mittwald-ddns-tests-{Guid.NewGuid():N}.json");
    }
}