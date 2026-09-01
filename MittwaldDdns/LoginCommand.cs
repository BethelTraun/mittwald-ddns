using MittwaldDdns.API;

namespace MittwaldDdns;

public sealed class LoginCommand
{
    private readonly LoginOptions _options;
    private readonly HttpClient _httpClient;

    private LoginCommand(LoginOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public static bool TryCreate(string[] args, out LoginCommand? command)
    {
        var parsed = LoginOptions.Parse(args);
        if (!parsed.Login)
        {
            command = null;
            return false;
        }

        command = new LoginCommand(parsed, new HttpClient());
        return true;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var apiVersion = PromptMissingApiVersion();
        var description = string.IsNullOrWhiteSpace(_options.Description)
            ? "mittwald-ddns"
            : _options.Description;

        var username = PromptMissingUser(apiVersion);
        var password = PromptMissingPassword();

        try
        {
            var apiKey = await CreateApiKeyAsync(
                apiVersion,
                username,
                password,
                _options.MultiFactorCode,
                _options.DeviceId,
                description,
                cancellationToken);
            Console.WriteLine(apiKey);
            return 0;
        }
        catch (MittwaldSecondFactorRequiredException exception)
        {
            if (!string.IsNullOrWhiteSpace(exception.DeviceId))
            {
                Console.Error.WriteLine($"Device id: {exception.DeviceId}");
            }

            var multiFactorCode = Prompt("MFA code: ");
            var apiKey = await CreateApiKeyAsync(
                apiVersion,
                username,
                password,
                multiFactorCode,
                exception.DeviceId ?? _options.DeviceId,
                description,
                cancellationToken);
            Console.WriteLine(apiKey);
            return 0;
        }
    }

    private Task<string> CreateApiKeyAsync(
        int apiVersion,
        string username,
        string password,
        string? multiFactorCode,
        string? deviceId,
        string description,
        CancellationToken cancellationToken)
    {
        return apiVersion == 1
            ? MittwaldV1.CreateApiKeyAsync(_httpClient, username, password, multiFactorCode, deviceId, description, cancellationToken)
            : MittwaldV2.CreateApiKeyAsync(
                _httpClient,
                username,
                password,
                multiFactorCode,
                description,
                _options.ExpiresAt ?? DateTimeOffset.UtcNow.AddYears(1),
                cancellationToken);
    }

    private int PromptMissingApiVersion()
    {
        if (_options.ApiVersion is not null)
        {
            return _options.ApiVersion.Value;
        }

        while (true)
        {
            var value = Prompt("API version (1 or 2) [2]: ");
            if (string.IsNullOrWhiteSpace(value))
            {
                return 2;
            }

            if (int.TryParse(value, out var apiVersion) && apiVersion is 1 or 2)
            {
                return apiVersion;
            }

            Console.Error.WriteLine("Please enter 1 or 2.");
        }
    }

    private string PromptMissingUser(int apiVersion)
    {
        var configured = apiVersion == 1
            ? _options.Username
            : _options.Email ?? _options.Username;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Prompt(apiVersion == 1 ? "Username: " : "Email: ");
    }

    private string PromptMissingPassword()
    {
        if (!string.IsNullOrWhiteSpace(_options.Password))
        {
            return _options.Password;
        }

        return ReadSecret("Password: ");
    }

    private static string Prompt(string prompt)
    {
        Console.Error.Write(prompt);
        return Console.ReadLine() ?? string.Empty;
    }

    private static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            return Prompt(prompt);
        }

        Console.Error.Write(prompt);

        var password = string.Empty;
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return password;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password = password[..^1];
                    Console.Error.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password += key.KeyChar;
                Console.Error.Write('*');
            }
        }
    }
}

public sealed record LoginOptions
{
    public bool Login { get; private init; }
    public int? ApiVersion { get; private init; }
    public string? Username { get; private init; }
    public string? Email { get; private init; }
    public string? Password { get; private init; }
    public string? MultiFactorCode { get; private init; }
    public string? DeviceId { get; private init; }
    public string? Description { get; private init; }
    public DateTimeOffset? ExpiresAt { get; private init; }

    public static LoginOptions Parse(string[] args)
    {
        var options = new LoginOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = OptionName(args[index]);

            options = arg switch
            {
                "login" => options with { Login = true },
                "--login" => options with { Login = true },
                "--api-version" => options with { ApiVersion = ParseApiVersion(ReadValue(args, ref index), arg) },
                "--v1" => options with { ApiVersion = 1 },
                "--v2" => options with { ApiVersion = 2 },
                "--username" => options with { Username = RequireValue(ReadValue(args, ref index), arg) },
                "--email" => options with { Email = RequireValue(ReadValue(args, ref index), arg) },
                "--password" => options with { Password = RequireValue(ReadValue(args, ref index), arg) },
                "--mfa" => options with { MultiFactorCode = RequireValue(ReadValue(args, ref index), arg) },
                "--device-id" => options with { DeviceId = RequireValue(ReadValue(args, ref index), arg) },
                "--description" => options with { Description = RequireValue(ReadValue(args, ref index), arg) },
                "--expires-at" => options with { ExpiresAt = DateTimeOffset.Parse(RequireValue(ReadValue(args, ref index), arg)) },
                _ => options
            };
        }

        return options;
    }

    private static string? ReadValue(string[] args, ref int index)
    {
        var arg = args[index];
        var equalsIndex = arg.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex >= 0)
        {
            return arg[(equalsIndex + 1)..];
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        index++;
        return args[index];
    }

    private static string OptionName(string arg)
    {
        var equalsIndex = arg.IndexOf('=', StringComparison.Ordinal);
        return equalsIndex >= 0 ? arg[..equalsIndex] : arg;
    }

    private static string RequireValue(string? value, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{option} needs a value.");
        }

        return value;
    }

    private static int ParseApiVersion(string? value, string option)
    {
        if (!int.TryParse(RequireValue(value, option), out var apiVersion) || apiVersion is not (1 or 2))
        {
            throw new ArgumentException($"{option} must be 1 or 2.");
        }

        return apiVersion;
    }
}
