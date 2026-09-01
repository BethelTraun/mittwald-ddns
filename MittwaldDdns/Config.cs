using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MittwaldDdns;

public class Config
{
    private readonly string _encryptionKey;

    public ConfigModel ConfigModel { get; private set; }
    public string ConfigPath { get; }

    public Config(string configPath, string encryptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionKey);

        _encryptionKey = encryptionKey;
        ConfigPath = ExpandPath(configPath);
        var loadedConfig = LoadConfig(ConfigPath, encryptionKey);
        ConfigModel = loadedConfig ?? new ConfigModel();
    }

    private static ConfigModel? LoadConfig(string path, string encryptionKey)
    {
        try
        {
            var json = File.ReadAllText(path);
            var encryptedConfig = JsonSerializer.Deserialize<EncryptedConfig>(json);

            if (encryptedConfig is not null
                && !string.IsNullOrWhiteSpace(encryptedConfig.Ciphertext)
                && !string.IsNullOrWhiteSpace(encryptedConfig.Nonce)
                && !string.IsNullOrWhiteSpace(encryptedConfig.Tag))
            {
                var decryptedJson = DecryptConfig(encryptedConfig, encryptionKey);
                return JsonSerializer.Deserialize<ConfigModel>(decryptedJson);
            }

            return JsonSerializer.Deserialize<ConfigModel>(json);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void SaveConfig(string path, ConfigModel configModel)
    {
        path = ExpandPath(path);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(configModel, jsonOptions);
        var encryptedConfig = EncryptConfig(json, _encryptionKey);
        var encryptedJson = JsonSerializer.Serialize(encryptedConfig, jsonOptions);
        File.WriteAllText(path, encryptedJson);
    }

    private static string ExpandPath(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);
        }

        return path;
    }

    #region Encryption

    private const int NonceSizeInBytes = 12;
    private const int TagSizeInBytes = 16;

    private static EncryptedConfig EncryptConfig(string text, string key)
    {
        var plaintext = Encoding.UTF8.GetBytes(text);
        var ciphertext = new byte[plaintext.Length];
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var tag = new byte[TagSizeInBytes];

        using var aes = new AesGcm(DeriveKey(key), TagSizeInBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return new EncryptedConfig
        {
            Ciphertext = Convert.ToBase64String(ciphertext),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag)
        };
    }

    private static string DecryptConfig(EncryptedConfig encryptedConfig, string key)
    {
        var ciphertext = Convert.FromBase64String(encryptedConfig.Ciphertext);
        var nonce = Convert.FromBase64String(encryptedConfig.Nonce);
        var tag = Convert.FromBase64String(encryptedConfig.Tag);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(DeriveKey(key), TagSizeInBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(string key)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }

    #endregion
}

public class EncryptedConfig
{
    public string Ciphertext { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
}

public class ConfigModel
{
    public string ApiKey { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
    public string GlobalWebhook { get; set; } = string.Empty;
    public int GlobalApiVersion { get; set; } = 2;
    public List<ConfigDomain> Domains { get; set; } = [];
}

public class ConfigDomain
{
    public string Id { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Webhook { get; set; } = string.Empty;
    public int ApiVersion { get; set; }
}
