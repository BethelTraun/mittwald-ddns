using System.Text.Json;
using MittwaldDdns.API;

namespace MittwaldDdns;

public sealed class ConfigCommand
{
    private readonly ConfigCommandOptions _options;
    private readonly HttpClient _httpClient;

    private ConfigCommand(ConfigCommandOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public static bool TryCreate(string[] args, out ConfigCommand? command)
    {
        var parsed = ConfigCommandOptions.Parse(args);
        if (!parsed.IsConfigCommand)
        {
            command = null;
            return false;
        }

        command = new ConfigCommand(parsed, new HttpClient());
        return true;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var store = CreateStore();

        try
        {
            if (string.Equals(_options.Subcommand, "export", StringComparison.OrdinalIgnoreCase))
            {
                ExportConfig(store);
                return 0;
            }

            if (string.Equals(_options.Subcommand, "save", StringComparison.OrdinalIgnoreCase))
            {
                SaveImportedConfig(store);
                return 0;
            }

            return await RunWorkspaceAsync(store, cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static ConfigStore CreateStore()
    {
        var configPath = Environment.GetEnvironmentVariable("MITTWALD_CONFIG_PATH")
            ?? ConfigStore.DefaultConfigPath;
        var secret = Environment.GetEnvironmentVariable("MITTWALD_SECRET");
        return new ConfigStore(configPath, secret);
    }

    private void ExportConfig(ConfigStore store)
    {
        var loaded = store.LoadRequired();
        ConfigValidator.ThrowIfInvalid(loaded.Config);
        var json = JsonSerializer.Serialize(loaded.Config, ConfigStore.JsonOptions);

        if (string.IsNullOrWhiteSpace(_options.File))
        {
            Console.WriteLine(json);
            return;
        }

        ConfigStore.WriteAllTextAtomic(_options.File, json);
    }

    private void SaveImportedConfig(ConfigStore store)
    {
        if (string.IsNullOrWhiteSpace(_options.File))
        {
            throw new ArgumentException("config save needs --file.");
        }

        var imported = ConfigStore.LoadFromFile(_options.File, Environment.GetEnvironmentVariable("MITTWALD_SECRET"));
        ConfigValidator.ThrowIfInvalid(imported.Config);
        store.Save(imported.Config, imported.Format);
        Console.Error.WriteLine($"Saved {store.ConfigPath}");
    }

    private async Task<int> RunWorkspaceAsync(ConfigStore store, CancellationToken cancellationToken)
    {
        var config = store.Load() ?? new ConfigModel();
        var dirty = false;

        while (true)
        {
            Console.WriteLine();
            PrintSummary(config, dirty);

            var options = config.Accounts.Count == 0
                ? new[] { "Add API key/account", "Import plain JSON", "Global settings", "Exit" }
                : new[]
                {
                    "Accounts",
                    "Global settings",
                    "Review effective config",
                    "Export plain JSON",
                    "Save",
                    "Exit without saving"
                };

            var selected = PromptSelection(options);
            var choice = options[selected];

            if (choice == "Add API key/account")
            {
                var added = await AddAccountAsync(config, cancellationToken);
                dirty = dirty || added;
            }
            else if (choice == "Import plain JSON")
            {
                var imported = ImportPlainJson();
                if (imported is not null)
                {
                    config = imported;
                    dirty = true;
                }
            }
            else if (choice == "Accounts")
            {
                dirty = await RunAccountsMenuAsync(config, cancellationToken) || dirty;
            }
            else if (choice == "Global settings")
            {
                dirty = RunGlobalSettings(config) || dirty;
            }
            else if (choice == "Review effective config")
            {
                PrintEffectiveConfig(config);
                PromptToContinue();
            }
            else if (choice == "Export plain JSON")
            {
                ExportPlainJsonFromWorkspace(config);
            }
            else if (choice == "Save")
            {
                ConfigValidator.ThrowIfInvalid(config);
                store.SaveEncrypted(config);
                dirty = false;
                Console.WriteLine($"Saved encrypted config to {store.ConfigPath}");
            }
            else
            {
                if (!dirty || PromptYesNo("Exit without saving changes? [y/N]: ", defaultValue: false))
                {
                    return 0;
                }
            }
        }
    }

    private async Task<bool> RunAccountsMenuAsync(ConfigModel config, CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Accounts");
            var options = new List<string> { "Add API key/account" };
            options.AddRange(config.Accounts.Select(account => account.Name));
            options.Add("Back");

            var selected = PromptSelection(options);
            if (selected == 0)
            {
                if (await AddAccountAsync(config, cancellationToken))
                {
                    return true;
                }
            }
            else if (selected == options.Count - 1)
            {
                return false;
            }
            else if (await EditAccountAsync(config, config.Accounts[selected - 1], cancellationToken))
            {
                return true;
            }
        }
    }

    private async Task<bool> AddAccountAsync(ConfigModel config, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("Add API key/account");

        var name = PromptRequired("Account name: ");
        if (config.Accounts.Any(account => string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Account name must be unique.");
            return false;
        }

        var account = new ConfigAccount
        {
            Name = name,
            ApiKey = ReadSecret("API key: ")
        };

        var selectedApiVersion = PromptApiVersion("API version to use for domain discovery", includeAccountSetting: false);
        account.ApiVersion = selectedApiVersion.UseGlobal ? null : selectedApiVersion.ApiVersion;
        var discoveryVersion = ResolveApiVersion(config, account, selectedApiVersion);

        if (!await ValidateApiKeyAsync(account, discoveryVersion, cancellationToken))
        {
            return false;
        }

        account.Domains = await DiscoverDomainsWithFallbackAsync(config, account, discoveryVersion, cancellationToken);
        account.Webhook = EmptyToNull(Prompt("Webhook override (empty for global): "));

        config.Accounts.Add(account);
        return true;
    }

    private async Task<bool> EditAccountAsync(
        ConfigModel config,
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"Account: {account.Name}");
            Console.WriteLine("API key: ********");
            Console.WriteLine($"API version: {(account.ApiVersion is null ? "global default" : $"v{account.ApiVersion}")}");
            Console.WriteLine($"Webhook: {(string.IsNullOrWhiteSpace(account.Webhook) ? "global default" : "custom")}");
            Console.WriteLine($"Domains: {account.Domains.Count}");

            var options = new[]
            {
                "Replace API key",
                "Rename account",
                "Set API version override",
                "Set webhook override",
                "Manage domains",
                "Remove account",
                "Back"
            };

            var selected = PromptSelection(options);
            switch (selected)
            {
                case 0:
                    if (await ReplaceApiKeyAsync(config, account, cancellationToken))
                    {
                        return true;
                    }
                    break;
                case 1:
                    return RenameAccount(config, account);
                case 2:
                    account.ApiVersion = PromptApiVersionOverride();
                    return true;
                case 3:
                    account.Webhook = EmptyToNull(Prompt("Webhook override (empty for global): "));
                    return true;
                case 4:
                    if (await ManageDomainsAsync(config, account, cancellationToken))
                    {
                        return true;
                    }
                    break;
                case 5:
                    if (PromptYesNo($"Remove account '{account.Name}' and all domains? [y/N]: ", defaultValue: false))
                    {
                        config.Accounts.Remove(account);
                        return true;
                    }
                    break;
                default:
                    return false;
            }
        }
    }

    private async Task<bool> ReplaceApiKeyAsync(
        ConfigModel config,
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        var apiKey = ReadSecret("New API key: ");
        var selectedApiVersion = PromptApiVersion("API version to use for validation", includeAccountSetting: true);
        var apiVersion = ResolveApiVersion(config, account, selectedApiVersion);

        var draft = new ConfigAccount
        {
            Name = account.Name,
            ApiKey = apiKey,
            ApiVersion = account.ApiVersion,
            Webhook = account.Webhook,
            Domains = account.Domains
        };

        if (!await ValidateApiKeyAsync(draft, apiVersion, cancellationToken))
        {
            return false;
        }

        account.ApiKey = apiKey;
        Console.WriteLine("API key replaced and validated.");
        var selected = PromptSelection(["Rediscover domains now", "Keep existing domain list"]);
        if (selected == 0)
        {
            var discovery = PromptApiVersion("API version to use for domain discovery", includeAccountSetting: true);
            account.Domains = await DiscoverDomainsWithFallbackAsync(
                config,
                account,
                ResolveApiVersion(config, account, discovery),
                cancellationToken);
        }

        return true;
    }

    private static bool RenameAccount(ConfigModel config, ConfigAccount account)
    {
        var newName = PromptRequired("New account name: ");
        if (config.Accounts.Any(candidate =>
                !ReferenceEquals(candidate, account)
                && string.Equals(candidate.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Account name must be unique.");
            return false;
        }

        account.Name = newName;
        return true;
    }

    private async Task<bool> ManageDomainsAsync(
        ConfigModel config,
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"Domains for account: {account.Name}");
            foreach (var domain in account.Domains.OrderBy(domain => domain.Domain, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {domain.Domain} ({domain.Id})");
            }

            var options = new[]
            {
                "Sync from Mittwald",
                "Add manually",
                "Edit domain",
                "Remove domain",
                "Back"
            };

            var selected = PromptSelection(options);
            switch (selected)
            {
                case 0:
                    var selectedApiVersion = PromptApiVersion("API version to use for domain discovery", includeAccountSetting: true);
                    account.Domains = await DiscoverDomainsWithFallbackAsync(
                        config,
                        account,
                        ResolveApiVersion(config, account, selectedApiVersion),
                        cancellationToken,
                        account.Domains);
                    return true;
                case 1:
                    account.Domains.Add(ReadManualDomain());
                    return true;
                case 2:
                    return EditDomain(account);
                case 3:
                    return RemoveDomain(account);
                default:
                    return false;
            }
        }
    }

    private static ConfigDomain ReadManualDomain()
    {
        Console.WriteLine("Manual domain add");
        Console.WriteLine("The domain will be accepted without a live Mittwald lookup.");

        return new ConfigDomain
        {
            Domain = PromptRequired("Domain name: "),
            Id = PromptRequired("Domain ID: "),
            ProjectId = EmptyToNull(Prompt("Project ID (optional): "))
        };
    }

    private static bool EditDomain(ConfigAccount account)
    {
        if (account.Domains.Count == 0)
        {
            Console.WriteLine("No domains configured.");
            return false;
        }

        var index = PromptSelection(account.Domains.Select(domain => domain.Domain).ToArray());
        var domain = account.Domains[index];
        var name = Prompt($"Domain name [{domain.Domain}]: ");
        var id = Prompt($"Domain ID [{domain.Id}]: ");
        var projectId = Prompt($"Project ID [{domain.ProjectId}]: ");

        domain.Domain = string.IsNullOrWhiteSpace(name) ? domain.Domain : name;
        domain.Id = string.IsNullOrWhiteSpace(id) ? domain.Id : id;
        domain.ProjectId = string.IsNullOrWhiteSpace(projectId) ? domain.ProjectId : projectId;
        return true;
    }

    private static bool RemoveDomain(ConfigAccount account)
    {
        if (account.Domains.Count == 0)
        {
            Console.WriteLine("No domains configured.");
            return false;
        }

        var index = PromptSelection(account.Domains.Select(domain => domain.Domain).ToArray());
        var domain = account.Domains[index];
        if (!PromptYesNo($"Remove domain '{domain.Domain}'? [y/N]: ", defaultValue: false))
        {
            return false;
        }

        account.Domains.RemoveAt(index);
        return true;
    }

    private async Task<bool> ValidateApiKeyAsync(
        ConfigAccount account,
        int apiVersion,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                if (apiVersion == 1)
                {
                    await new MittwaldV1(_httpClient, account.ApiKey).GetDnsOverviewAsync(account.Name, cancellationToken);
                }
                else
                {
                    await new MittwaldV2(_httpClient, account.ApiKey).ListDomainsAsync(cancellationToken);
                }

                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Console.Error.WriteLine($"Mittwald API validation failed: {exception.Message}");
                if (!PromptYesNo("Retry API key validation? [y/N]: ", defaultValue: false))
                {
                    return false;
                }

                account.ApiKey = ReadSecret("API key: ");
            }
        }
    }

    private async Task<List<ConfigDomain>> DiscoverDomainsWithFallbackAsync(
        ConfigModel config,
        ConfigAccount account,
        int apiVersion,
        CancellationToken cancellationToken,
        IReadOnlyList<ConfigDomain>? currentDomains = null)
    {
        try
        {
            var discovered = apiVersion == 1
                ? await DiscoverV1DomainsAsync(account, cancellationToken)
                : await DiscoverV2DomainsAsync(account, cancellationToken);

            return SelectDomains(discovered, currentDomains);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Domain discovery failed: {exception.Message}");
            if (PromptYesNo("Add a domain manually? [y/N]: ", defaultValue: false))
            {
                return [.. currentDomains ?? [], ReadManualDomain()];
            }

            return currentDomains?.ToList() ?? [];
        }
    }

    private async Task<List<ConfigDomain>> DiscoverV1DomainsAsync(
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        var domains = await new MittwaldV1(_httpClient, account.ApiKey).GetDnsOverviewAsync(account.Name, cancellationToken);

        return domains
            .Select(domain => new ConfigDomain
            {
                Id = domain.Uid.ToString(),
                Domain = FirstNonEmpty(domain.FullName, domain.DomainName)
            })
            .Where(domain => !string.IsNullOrWhiteSpace(domain.Domain))
            .OrderBy(domain => domain.Domain, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<ConfigDomain>> DiscoverV2DomainsAsync(
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        var client = new MittwaldV2(_httpClient, account.ApiKey);
        var domains = await client.ListDomainsAsync(cancellationToken);
        var zonesByProject = new Dictionary<string, IReadOnlyList<V2DnsZone>>(StringComparer.Ordinal);
        var discovered = new List<ConfigDomain>();

        foreach (var domain in domains.Where(domain => domain.Connected))
        {
            IReadOnlyList<V2DnsZone> zones = [];
            var projectId = domain.ProjectId;
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                if (zonesByProject.TryGetValue(projectId, out var cachedZones))
                {
                    zones = cachedZones;
                }
                else
                {
                    zones = await client.ListDnsZonesAsync(projectId, cancellationToken);
                    zonesByProject.Add(projectId, zones);
                }
            }

            var zone = zones.FirstOrDefault(candidate =>
                string.Equals(candidate.Domain, domain.Domain, StringComparison.OrdinalIgnoreCase));

            discovered.Add(new ConfigDomain
            {
                Id = zone?.Id ?? domain.DomainId,
                Domain = domain.Domain,
                ProjectId = domain.ProjectId
            });
        }

        return discovered
            .OrderBy(domain => domain.Domain, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ConfigDomain> SelectDomains(
        IReadOnlyList<ConfigDomain> discovered,
        IReadOnlyList<ConfigDomain>? currentDomains)
    {
        if (discovered.Count == 0)
        {
            Console.WriteLine("No domains found.");
            return currentDomains?.ToList() ?? [];
        }

        var current = currentDomains?.Select(domain => domain.Domain).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine();
        Console.WriteLine("Select domains to manage. Enter numbers separated by commas, or empty for preselected/all.");
        for (var index = 0; index < discovered.Count; index++)
        {
            var mark = current.Count == 0 || current.Contains(discovered[index].Domain) ? "x" : " ";
            Console.WriteLine($"{index + 1}. [{mark}] {discovered[index].Domain}");
        }

        var input = Prompt("> ");
        if (string.IsNullOrWhiteSpace(input))
        {
            return current.Count == 0
                ? discovered.ToList()
                : discovered.Where(domain => current.Contains(domain.Domain)).ToList();
        }

        var selectedIndexes = input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed - 1 : -1)
            .Where(index => index >= 0 && index < discovered.Count)
            .Distinct()
            .ToList();

        return selectedIndexes.Select(index => discovered[index]).ToList();
    }

    private static bool RunGlobalSettings(ConfigModel config)
    {
        Console.WriteLine();
        Console.WriteLine("Global settings");
        Console.WriteLine($"Interval: {config.Timeout}");
        Console.WriteLine($"Default API version: {config.DefaultApiVersion}");
        Console.WriteLine($"Global webhook: {(string.IsNullOrWhiteSpace(config.GlobalWebhook) ? "unset" : "configured")}");

        var selected = PromptSelection([
            "Change interval",
            "Change default API version",
            "Change global webhook",
            "Clear global webhook",
            "Back"
        ]);

        switch (selected)
        {
            case 0:
                config.Timeout = PromptTimeSpan("Interval: ");
                return true;
            case 1:
                config.DefaultApiVersion = PromptSelection(["API v1", "API v2"]) + 1;
                return true;
            case 2:
                config.GlobalWebhook = EmptyToNull(Prompt("Global webhook: "));
                return true;
            case 3:
                config.GlobalWebhook = null;
                return true;
            default:
                return false;
        }
    }

    private static ConfigModel? ImportPlainJson()
    {
        var file = PromptRequired("Plain JSON file: ");
        var loaded = ConfigStore.LoadFromFile(file, encryptionSecret: null);
        if (loaded.Format != ConfigFileFormat.Plain)
        {
            Console.Error.WriteLine("Interactive import expects plain JSON.");
            return null;
        }

        var errors = ConfigValidator.Validate(loaded.Config);
        if (errors.Count > 0)
        {
            Console.Error.WriteLine("Imported config is invalid:");
            foreach (var error in errors)
            {
                Console.Error.WriteLine($"- {error}");
            }

            return null;
        }

        return loaded.Config;
    }

    private static void ExportPlainJsonFromWorkspace(ConfigModel config)
    {
        var selected = PromptSelection(["Plain JSON to stdout", "Plain JSON to file", "Back"]);
        if (selected == 2)
        {
            return;
        }

        var json = JsonSerializer.Serialize(config, ConfigStore.JsonOptions);
        if (selected == 0)
        {
            Console.WriteLine(json);
            return;
        }

        ConfigStore.WriteAllTextAtomic(PromptRequired("File: "), json);
    }

    private static void PrintSummary(ConfigModel config, bool dirty)
    {
        Console.WriteLine("Mittwald DDNS Config");
        Console.WriteLine();
        Console.WriteLine("Global:");
        Console.WriteLine($"  Interval: {config.Timeout}");
        Console.WriteLine($"  Default API version: {config.DefaultApiVersion}");
        Console.WriteLine($"  Global webhook: {(string.IsNullOrWhiteSpace(config.GlobalWebhook) ? "unset" : "configured")}");
        Console.WriteLine();
        Console.WriteLine("Accounts:");

        if (config.Accounts.Count == 0)
        {
            Console.WriteLine("  No accounts configured yet.");
        }
        else
        {
            foreach (var account in config.Accounts)
            {
                var webhook = string.IsNullOrWhiteSpace(account.Webhook) ? "global" : "custom";
                var api = account.ApiVersion is null ? "global" : $"v{account.ApiVersion}";
                Console.WriteLine($"  {account.Name}  {account.Domains.Count} domains  webhook: {webhook}  api: {api}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Unsaved changes: {(dirty ? "yes" : "no")}");
    }

    private static void PrintEffectiveConfig(ConfigModel config)
    {
        Console.WriteLine();
        Console.WriteLine("Effective config");

        foreach (var account in config.Accounts)
        {
            foreach (var domain in account.Domains)
            {
                Console.WriteLine();
                Console.WriteLine(domain.Domain);
                Console.WriteLine($"  Account: {account.Name}");
                Console.WriteLine("  API key: ********");
                Console.WriteLine($"  API version: {account.ApiVersion ?? config.DefaultApiVersion}");
                Console.WriteLine($"  Webhook: {WebhookSource(config, account)}");
            }
        }
    }

    private static int ResolveApiVersion(ConfigModel config, ConfigAccount account, ApiVersionSelection selection)
    {
        if (selection.ApiVersion is not null)
        {
            return selection.ApiVersion.Value;
        }

        if (selection.UseAccountSetting && account.ApiVersion is not null)
        {
            return account.ApiVersion.Value;
        }

        return config.DefaultApiVersion;
    }

    private static int? PromptApiVersionOverride()
    {
        var selected = PromptSelection(["Use global default", "API v1", "API v2"]);
        return selected == 0 ? null : selected;
    }

    private static ApiVersionSelection PromptApiVersion(string title, bool includeAccountSetting)
    {
        Console.WriteLine();
        Console.WriteLine(title);

        var options = includeAccountSetting
            ? new[] { "Use account setting", "Use global default", "API v1", "API v2" }
            : ["Use global default", "API v1", "API v2"];

        var selected = PromptSelection(options);
        if (includeAccountSetting && selected == 0)
        {
            return new ApiVersionSelection(null, UseGlobal: false, UseAccountSetting: true);
        }

        if ((!includeAccountSetting && selected == 0) || (includeAccountSetting && selected == 1))
        {
            return new ApiVersionSelection(null, UseGlobal: true, UseAccountSetting: false);
        }

        var apiVersion = includeAccountSetting ? selected - 1 : selected;
        return new ApiVersionSelection(apiVersion, UseGlobal: false, UseAccountSetting: false);
    }

    private static int PromptSelection(IReadOnlyList<string> options)
    {
        for (var index = 0; index < options.Count; index++)
        {
            Console.WriteLine($"{index + 1}. {options[index]}");
        }

        while (true)
        {
            var input = Prompt("> ");
            if (int.TryParse(input, out var selected)
                && selected >= 1
                && selected <= options.Count)
            {
                return selected - 1;
            }

            Console.Error.WriteLine($"Please enter a number from 1 to {options.Count}.");
        }
    }

    private static TimeSpan PromptTimeSpan(string prompt)
    {
        while (true)
        {
            var input = PromptRequired(prompt);
            if (TimeSpan.TryParse(input, out var timeSpan) && timeSpan > TimeSpan.Zero)
            {
                return timeSpan;
            }

            if (TryParseShortTimeSpan(input, out timeSpan))
            {
                return timeSpan;
            }

            Console.Error.WriteLine("Enter a positive TimeSpan value, or a short form like 30s, 5m, or 1h.");
        }
    }

    private static bool TryParseShortTimeSpan(string input, out TimeSpan value)
    {
        value = default;
        if (input.Length < 2 || !double.TryParse(input[..^1], out var amount) || amount <= 0)
        {
            return false;
        }

        value = char.ToLowerInvariant(input[^1]) switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            _ => default
        };

        return value > TimeSpan.Zero;
    }

    private static bool PromptYesNo(string prompt, bool defaultValue)
    {
        var input = Prompt(prompt);
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultValue;
        }

        return input.StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }

    private static string PromptRequired(string prompt)
    {
        while (true)
        {
            var value = Prompt(prompt);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.Error.WriteLine("A value is required.");
        }
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

        var secret = string.Empty;
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return secret;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret = secret[..^1];
                    Console.Error.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                secret += key.KeyChar;
                Console.Error.Write('*');
            }
        }
    }

    private static void PromptToContinue()
    {
        Console.Error.Write("Press enter to continue.");
        Console.ReadLine();
    }

    private static string WebhookSource(ConfigModel config, ConfigAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.Webhook))
        {
            return "account override";
        }

        if (!string.IsNullOrWhiteSpace(config.GlobalWebhook))
        {
            return "global";
        }

        return "unset";
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private sealed record ApiVersionSelection(int? ApiVersion, bool UseGlobal, bool UseAccountSetting);
}

public sealed record ConfigCommandOptions
{
    public bool IsConfigCommand { get; private init; }
    public string? Subcommand { get; private init; }
    public string? File { get; private init; }

    public static ConfigCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "config", StringComparison.OrdinalIgnoreCase))
        {
            return new ConfigCommandOptions();
        }

        var options = new ConfigCommandOptions { IsConfigCommand = true };
        var index = 1;
        if (index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
        {
            options = options with { Subcommand = args[index] };
            index++;
        }

        for (; index < args.Length; index++)
        {
            var arg = OptionName(args[index]);
            options = arg switch
            {
                "--file" => options with { File = RequireValue(ReadValue(args, ref index), arg) },
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
}
