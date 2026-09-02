using System.Text.Json.Serialization;

namespace MittwaldDdns;

public sealed class ConfigModel
{
    [JsonPropertyName("global_webhook")] public string? GlobalWebhook { get; set; }

    [JsonPropertyName("default_api_version")]
    public int DefaultApiVersion { get; set; } = 2;

    [JsonPropertyName("timeout")] public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("accounts")] public List<ConfigAccount> Accounts { get; set; } = [];
}

public sealed class ConfigAccount
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("api_key")] public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("api_version")] public int? ApiVersion { get; set; }

    [JsonPropertyName("webhook")] public string? Webhook { get; set; }

    [JsonPropertyName("domains")] public List<ConfigDomain> Domains { get; set; } = [];
}

public sealed class ConfigDomain
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ProjectId { get; set; }
}

public sealed class EncryptedConfig
{
    [JsonPropertyName("format")] public string Format { get; set; } = ConfigStore.EncryptedFormat;

    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = ConfigStore.EncryptionAlgorithm;

    [JsonPropertyName("ciphertext")] public string Ciphertext { get; set; } = string.Empty;

    [JsonPropertyName("nonce")] public string Nonce { get; set; } = string.Empty;

    [JsonPropertyName("tag")] public string Tag { get; set; } = string.Empty;
}