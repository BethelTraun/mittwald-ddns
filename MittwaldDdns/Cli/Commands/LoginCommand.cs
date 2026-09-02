using MittwaldDdns.API;
using MittwaldDdns.Cli.Interactive;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MittwaldDdns.Cli.Commands;

public sealed class LoginCommand : AsyncCommand<LoginSettings>
{
    private readonly IAnsiConsole _console;
    private readonly HttpClient _httpClient;

    public LoginCommand()
        : this(new HttpClient(), CliConsole.Error)
    {
    }

    internal LoginCommand(HttpClient httpClient, IAnsiConsole console)
    {
        _httpClient = httpClient;
        _console = console;
    }

    protected override ValidationResult Validate(CommandContext context, LoginSettings settings)
    {
        return settings.Validate();
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        LoginSettings settings,
        CancellationToken cancellationToken)
    {
        var apiVersion = PromptMissingApiVersion(settings);
        var description = string.IsNullOrWhiteSpace(settings.Description)
            ? "mittwald-ddns"
            : settings.Description;

        var username = PromptMissingUser(settings, apiVersion);
        var password = PromptMissingPassword(settings);

        try
        {
            var apiKey = await CreateApiKeyAsync(
                settings,
                apiVersion,
                username,
                password,
                settings.MultiFactorCode,
                settings.DeviceId,
                description,
                cancellationToken);
            Console.WriteLine(apiKey);
            return 0;
        }
        catch (MittwaldSecondFactorRequiredException exception)
        {
            if (!string.IsNullOrWhiteSpace(exception.DeviceId))
                _console.MarkupLineInterpolated($"Device id: {exception.DeviceId}");

            var multiFactorCode = PromptRequired("MFA code:");
            var apiKey = await CreateApiKeyAsync(
                settings,
                apiVersion,
                username,
                password,
                multiFactorCode,
                exception.DeviceId ?? settings.DeviceId,
                description,
                cancellationToken);
            Console.WriteLine(apiKey);
            return 0;
        }
    }

    private Task<string> CreateApiKeyAsync(
        LoginSettings settings,
        int apiVersion,
        string username,
        string password,
        string? multiFactorCode,
        string? deviceId,
        string description,
        CancellationToken cancellationToken)
    {
        return apiVersion == 1
            ? MittwaldV1.CreateApiKeyAsync(_httpClient, username, password, multiFactorCode, deviceId, description,
                cancellationToken)
            : MittwaldV2.CreateApiKeyAsync(
                _httpClient,
                username,
                password,
                multiFactorCode,
                description,
                settings.ExpiresAt ?? DateTimeOffset.UtcNow.AddYears(1),
                cancellationToken);
    }

    private int PromptMissingApiVersion(LoginSettings settings)
    {
        var choice = _console.Prompt(
            new SelectionPrompt<ApiVersionChoice>()
                .Title("API version")
                .AddChoices(ApiVersionChoice.V2, ApiVersionChoice.V1)
                .UseConverter(PromptLabels.ApiVersionLabel));
        return choice == ApiVersionChoice.V1 ? 1 : 2;
    }

    private string PromptMissingUser(LoginSettings settings, int apiVersion)
    {
        var configured = apiVersion == 1
            ? settings.Username
            : settings.Email ?? settings.Username;

        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : PromptRequired(apiVersion == 1 ? "Username:" : "Email:");
    }

    private string PromptMissingPassword(LoginSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.Password)
            ? settings.Password
            : PromptSecret("Password:");
    }

    private string PromptRequired(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<string>(prompt)
                .Validate(PromptValidators.Required));
    }

    private string PromptSecret(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<string>(prompt)
                .Secret()
                .Validate(PromptValidators.Required));
    }
}

public sealed class LoginSettings : CommandSettings
{
    [CommandOption("--api-version <VERSION>")]
    public int? ApiVersion { get; init; }

    [CommandOption("--v1")] public bool V1 { get; init; }

    [CommandOption("--v2")] public bool V2 { get; init; }

    [CommandOption("--username <USERNAME>")]
    public string? Username { get; init; }

    [CommandOption("--email <EMAIL>")] public string? Email { get; init; }

    [CommandOption("--password <PASSWORD>")]
    public string? Password { get; init; }

    [CommandOption("--mfa <CODE>")] public string? MultiFactorCode { get; init; }

    [CommandOption("--device-id <DEVICE_ID>")]
    public string? DeviceId { get; init; }

    [CommandOption("--description <DESCRIPTION>")]
    public string? Description { get; init; }

    [CommandOption("--expires-at <EXPIRES_AT>")]
    public DateTimeOffset? ExpiresAt { get; init; }

    public int? SelectedApiVersion
    {
        get
        {
            if (V1) return 1;

            if (V2) return 2;

            return ApiVersion;
        }
    }

    public override ValidationResult Validate()
    {
        var selectedCount = (ApiVersion is null ? 0 : 1) + (V1 ? 1 : 0) + (V2 ? 1 : 0);
        if (selectedCount > 1) return ValidationResult.Error("Use only one of --api-version, --v1, or --v2.");

        if (ApiVersion is not null && !ConfigValidator.IsSupportedApiVersion(ApiVersion.Value))
            return ValidationResult.Error("--api-version must be 1 or 2.");

        return ValidationResult.Success();
    }
}