using System.Text.Json;
using MittwaldDdns.API;
using Spectre.Console;

namespace MittwaldDdns.Cli.Interactive;

public sealed class ConfigWorkspace
{
    private readonly IAnsiConsole _console;
    private readonly HttpClient _httpClient;
    private readonly ConfigStore _store;

    public ConfigWorkspace(ConfigStore store, HttpClient httpClient, IAnsiConsole console)
    {
        _store = store;
        _httpClient = httpClient;
        _console = console;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var config = _store.Load() ?? new ConfigModel();
        var dirty = false;

        while (true)
        {
            _console.WriteLine();
            WriteSummary(config, dirty);

            var action = PromptMainMenu(config);
            switch (action)
            {
                case MainMenuAction.AddAccount:
                    dirty = await AddAccountAsync(config, cancellationToken) || dirty;
                    break;
                case MainMenuAction.ImportPlainJson:
                    var imported = ImportPlainJson();
                    if (imported is not null)
                    {
                        config = imported;
                        dirty = true;
                    }

                    break;
                case MainMenuAction.Accounts:
                    dirty = await RunAccountsMenuAsync(config, cancellationToken) || dirty;
                    break;
                case MainMenuAction.GlobalSettings:
                    dirty = RunGlobalSettings(config) || dirty;
                    break;
                case MainMenuAction.ReviewEffectiveConfig:
                    WriteEffectiveConfig(config);
                    PromptToContinue();
                    break;
                case MainMenuAction.ExportPlainJson:
                    ExportPlainJsonFromWorkspace(config);
                    break;
                case MainMenuAction.Save:
                    ConfigValidator.ThrowIfInvalid(config);
                    _store.SaveEncrypted(config);
                    dirty = false;
                    _console.MarkupLineInterpolated($"Saved encrypted config to {_store.ConfigPath}");
                    break;
                case MainMenuAction.Exit:
                case MainMenuAction.ExitWithoutSaving:
                    if (!dirty || _console.Confirm("Exit without saving changes?", false)) return 0;

                    break;
            }
        }
    }

    private MainMenuAction PromptMainMenu(ConfigModel config)
    {
        var choices = config.Accounts.Count == 0
            ? new[]
            {
                MainMenuAction.AddAccount,
                MainMenuAction.ImportPlainJson,
                MainMenuAction.GlobalSettings,
                MainMenuAction.Exit
            }
            : new[]
            {
                MainMenuAction.Accounts,
                MainMenuAction.GlobalSettings,
                MainMenuAction.ReviewEffectiveConfig,
                MainMenuAction.ExportPlainJson,
                MainMenuAction.Save,
                MainMenuAction.ExitWithoutSaving
            };

        return _console.Prompt(
            new SelectionPrompt<MainMenuAction>()
                .Title("Action")
                .AddChoices(choices)
                .UseConverter(PromptLabels.MainMenuLabel));
    }

    private async Task<bool> RunAccountsMenuAsync(ConfigModel config, CancellationToken cancellationToken)
    {
        while (true)
        {
            _console.WriteLine();
            _console.MarkupLine("[bold]Accounts[/]");

            var choices = new List<MenuOption<AccountMenuSelection>>
            {
                new(new AccountMenuSelection(AccountMenuAction.AddAccount),
                    PromptLabels.AccountMenuLabel(AccountMenuAction.AddAccount))
            };
            choices.AddRange(config.Accounts.Select(account =>
                new MenuOption<AccountMenuSelection>(
                    new AccountMenuSelection(AccountMenuAction.EditAccount, account),
                    account.Name)));
            choices.Add(new MenuOption<AccountMenuSelection>(
                new AccountMenuSelection(AccountMenuAction.Back),
                PromptLabels.AccountMenuLabel(AccountMenuAction.Back)));

            var selected = _console.Prompt(
                new SelectionPrompt<MenuOption<AccountMenuSelection>>()
                    .Title("Account")
                    .AddChoices(choices));

            if (selected.Value.Action == AccountMenuAction.AddAccount)
            {
                if (await AddAccountAsync(config, cancellationToken)) return true;
            }
            else if (selected.Value.Action == AccountMenuAction.Back)
            {
                return false;
            }
            else if (selected.Value.Account is not null
                     && await EditAccountAsync(config, selected.Value.Account, cancellationToken))
            {
                return true;
            }
        }
    }

    private async Task<bool> AddAccountAsync(ConfigModel config, CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _console.MarkupLine("[bold]Add API key/account[/]");

        var name = PromptRequired("Account name:");
        if (config.Accounts.Any(account => string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _console.MarkupLine("[red]Account name must be unique.[/]");
            return false;
        }

        var account = new ConfigAccount
        {
            Name = name,
            ApiKey = PromptSecret("API key:")
        };

        var selectedApiVersion = PromptApiVersion("API version to use for domain discovery", false);
        account.ApiVersion = selectedApiVersion.UseGlobal ? null : selectedApiVersion.ApiVersion;
        var discoveryVersion = ResolveApiVersion(config, account, selectedApiVersion);

        if (!await ValidateApiKeyAsync(account, discoveryVersion, cancellationToken)) return false;

        account.Domains = await DiscoverDomainsWithFallbackAsync(config, account, discoveryVersion, cancellationToken);
        account.Webhook = EmptyToNull(PromptOptionalUrl("Webhook override (empty for global):"));

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
            _console.WriteLine();
            WriteAccountSummary(config, account);

            var selected = _console.Prompt(
                new SelectionPrompt<AccountEditAction>()
                    .Title("Account action")
                    .AddChoices(
                        AccountEditAction.ReplaceApiKey,
                        AccountEditAction.RenameAccount,
                        AccountEditAction.SetApiVersionOverride,
                        AccountEditAction.SetWebhookOverride,
                        AccountEditAction.ManageDomains,
                        AccountEditAction.RemoveAccount,
                        AccountEditAction.Back)
                    .UseConverter(AccountEditLabel));

            switch (selected)
            {
                case AccountEditAction.ReplaceApiKey:
                    if (await ReplaceApiKeyAsync(config, account, cancellationToken)) return true;

                    break;
                case AccountEditAction.RenameAccount:
                    return RenameAccount(config, account);
                case AccountEditAction.SetApiVersionOverride:
                    account.ApiVersion = PromptApiVersionOverride();
                    return true;
                case AccountEditAction.SetWebhookOverride:
                    account.Webhook = EmptyToNull(PromptOptionalUrl("Webhook override (empty for global):"));
                    return true;
                case AccountEditAction.ManageDomains:
                    if (await ManageDomainsAsync(config, account, cancellationToken)) return true;

                    break;
                case AccountEditAction.RemoveAccount:
                    if (_console.Confirm($"Remove account '{Markup.Escape(account.Name)}' and all domains?", false))
                    {
                        config.Accounts.Remove(account);
                        return true;
                    }

                    break;
                case AccountEditAction.Back:
                    return false;
            }
        }
    }

    private async Task<bool> ReplaceApiKeyAsync(
        ConfigModel config,
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        var apiKey = PromptSecret("New API key:");
        var selectedApiVersion = PromptApiVersion("API version to use for validation", true);
        var apiVersion = ResolveApiVersion(config, account, selectedApiVersion);

        var draft = new ConfigAccount
        {
            Name = account.Name,
            ApiKey = apiKey,
            ApiVersion = account.ApiVersion,
            Webhook = account.Webhook,
            Domains = account.Domains
        };

        if (!await ValidateApiKeyAsync(draft, apiVersion, cancellationToken)) return false;

        account.ApiKey = apiKey;
        _console.MarkupLine("[green]API key replaced and validated.[/]");
        var selected = _console.Prompt(
            new SelectionPrompt<bool>()
                .Title("Domains")
                .AddChoices(true, false)
                .UseConverter(value => value ? "Rediscover domains now" : "Keep existing domain list"));
        if (selected)
        {
            var discovery = PromptApiVersion("API version to use for domain discovery", true);
            account.Domains = await DiscoverDomainsWithFallbackAsync(
                config,
                account,
                ResolveApiVersion(config, account, discovery),
                cancellationToken);
        }

        return true;
    }

    private bool RenameAccount(ConfigModel config, ConfigAccount account)
    {
        var newName = PromptRequired("New account name:");
        if (config.Accounts.Any(candidate =>
                !ReferenceEquals(candidate, account)
                && string.Equals(candidate.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            _console.MarkupLine("[red]Account name must be unique.[/]");
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
            _console.WriteLine();
            _console.MarkupLineInterpolated($"[bold]Domains for account:[/] {account.Name}");
            WriteDomains(account.Domains);

            var selected = _console.Prompt(
                new SelectionPrompt<DomainMenuAction>()
                    .Title("Domain action")
                    .AddChoices(
                        DomainMenuAction.SyncFromMittwald,
                        DomainMenuAction.AddManually,
                        DomainMenuAction.EditDomain,
                        DomainMenuAction.RemoveDomain,
                        DomainMenuAction.Back)
                    .UseConverter(PromptLabels.DomainMenuLabel));

            switch (selected)
            {
                case DomainMenuAction.SyncFromMittwald:
                    var selectedApiVersion = PromptApiVersion("API version to use for domain discovery", true);
                    account.Domains = await DiscoverDomainsWithFallbackAsync(
                        config,
                        account,
                        ResolveApiVersion(config, account, selectedApiVersion),
                        cancellationToken,
                        account.Domains);
                    return true;
                case DomainMenuAction.AddManually:
                    account.Domains.Add(ReadManualDomain());
                    return true;
                case DomainMenuAction.EditDomain:
                    return EditDomain(account);
                case DomainMenuAction.RemoveDomain:
                    return RemoveDomain(account);
                case DomainMenuAction.Back:
                    return false;
            }
        }
    }

    private ConfigDomain ReadManualDomain()
    {
        _console.MarkupLine("[bold]Manual domain add[/]");
        _console.MarkupLine("The domain will be accepted without a live Mittwald lookup.");

        return new ConfigDomain
        {
            Domain = PromptRequired("Domain name:"),
            Id = PromptRequired("Domain ID:"),
            ProjectId = EmptyToNull(PromptOptional("Project ID (optional):"))
        };
    }

    private bool EditDomain(ConfigAccount account)
    {
        if (account.Domains.Count == 0)
        {
            _console.MarkupLine("[yellow]No domains configured.[/]");
            return false;
        }

        var domain = PromptDomain(account.Domains, "Domain");
        var name = PromptOptional($"Domain name [{domain.Domain}]:");
        var id = PromptOptional($"Domain ID [{domain.Id}]:");
        var projectId = PromptOptional($"Project ID [{domain.ProjectId}]:");

        domain.Domain = string.IsNullOrWhiteSpace(name) ? domain.Domain : name;
        domain.Id = string.IsNullOrWhiteSpace(id) ? domain.Id : id;
        domain.ProjectId = string.IsNullOrWhiteSpace(projectId) ? domain.ProjectId : projectId;
        return true;
    }

    private bool RemoveDomain(ConfigAccount account)
    {
        if (account.Domains.Count == 0)
        {
            _console.MarkupLine("[yellow]No domains configured.[/]");
            return false;
        }

        var domain = PromptDomain(account.Domains, "Domain to remove");
        if (!_console.Confirm($"Remove domain '{Markup.Escape(domain.Domain)}'?", false)) return false;

        account.Domains.Remove(domain);
        return true;
    }

    private async Task<bool> ValidateApiKeyAsync(
        ConfigAccount account,
        int apiVersion,
        CancellationToken cancellationToken)
    {
        while (true)
            try
            {
                if (apiVersion == 1)
                    await new MittwaldV1(_httpClient, account.ApiKey).GetDnsOverviewAsync(account.Name,
                        cancellationToken);
                else
                    await new MittwaldV2(_httpClient, account.ApiKey).ListDomainsAsync(cancellationToken);

                return true;
            }
            catch (HttpRequestException exception) when (apiVersion == 1 &&
                                                         exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _console.MarkupLine(Markup.Escape(
                    $"Mittwald API v1 authenticated the API key but denied access to account '{account.Name}'. " +
                    "Enter the Mittwald account name or numeric UID, not a local label or API-token UUID."));
                if (!_console.Confirm("Retry with another v1 account identifier?", false)) return false;

                account.Name = PromptRequired("Mittwald account name or UID:");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _console.MarkupLine(Markup.Escape($"Mittwald API validation failed: {exception.Message}"));
                if (!_console.Confirm("Retry API key validation?", false)) return false;

                account.ApiKey = PromptSecret("API key:");
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
            _console.MarkupLine(Markup.Escape($"Domain discovery failed: {exception.Message}"));
            if (_console.Confirm("Add a domain manually?", false)) return [.. currentDomains ?? [], ReadManualDomain()];

            return currentDomains?.ToList() ?? [];
        }
    }

    private async Task<List<ConfigDomain>> DiscoverV1DomainsAsync(
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        var domains =
            await new MittwaldV1(_httpClient, account.ApiKey).GetDnsOverviewAsync(account.Name, cancellationToken);

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

    private List<ConfigDomain> SelectDomains(
        IReadOnlyList<ConfigDomain> discovered,
        IReadOnlyList<ConfigDomain>? currentDomains)
    {
        if (discovered.Count == 0)
        {
            _console.MarkupLine("[yellow]No domains found.[/]");
            return currentDomains?.ToList() ?? [];
        }

        var current = currentDomains?.Select(domain => domain.Domain).ToHashSet(StringComparer.OrdinalIgnoreCase)
                      ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preselected = discovered
            .Where(domain => current.Count == 0 || current.Contains(domain.Domain))
            .ToList();

        var prompt = new MultiSelectionPrompt<ConfigDomain>()
            .Title("Select domains to manage")
            .NotRequired()
            .InstructionsText("[grey](Press space to toggle, enter to accept)[/]")
            .AddChoices(discovered)
            .UseConverter(domain => domain.Domain);

        foreach (var domain in preselected) prompt.Select(domain);

        return _console.Prompt(prompt);
    }

    private bool RunGlobalSettings(ConfigModel config)
    {
        _console.WriteLine();
        WriteGlobalSettings(config);

        var selected = _console.Prompt(
            new SelectionPrompt<GlobalSettingsAction>()
                .Title("Global setting")
                .AddChoices(
                    GlobalSettingsAction.ChangeInterval,
                    GlobalSettingsAction.ChangeDefaultApiVersion,
                    GlobalSettingsAction.ChangeGlobalWebhook,
                    GlobalSettingsAction.ClearGlobalWebhook,
                    GlobalSettingsAction.Back)
                .UseConverter(PromptLabels.GlobalSettingsLabel));

        switch (selected)
        {
            case GlobalSettingsAction.ChangeInterval:
                config.Timeout = PromptTimeSpan("Interval:");
                return true;
            case GlobalSettingsAction.ChangeDefaultApiVersion:
                config.DefaultApiVersion = PromptApiVersionValue("Default API version:");
                return true;
            case GlobalSettingsAction.ChangeGlobalWebhook:
                config.GlobalWebhook = EmptyToNull(PromptOptionalUrl("Global webhook:"));
                return true;
            case GlobalSettingsAction.ClearGlobalWebhook:
                config.GlobalWebhook = null;
                return true;
            case GlobalSettingsAction.Back:
                return false;
            default:
                return false;
        }
    }

    private ConfigModel? ImportPlainJson()
    {
        var file = PromptRequired("Plain JSON file:");
        var loaded = ConfigStore.LoadFromFile(file, null);
        if (loaded.Format != ConfigFileFormat.Plain)
        {
            _console.MarkupLine("[red]Interactive import expects plain JSON.[/]");
            return null;
        }

        var errors = ConfigValidator.Validate(loaded.Config);
        if (errors.Count > 0)
        {
            WriteValidationErrors(errors);
            return null;
        }

        return loaded.Config;
    }

    private void ExportPlainJsonFromWorkspace(ConfigModel config)
    {
        var selected = _console.Prompt(
            new SelectionPrompt<ExportDestination>()
                .Title("Export destination")
                .AddChoices(ExportDestination.Stdout, ExportDestination.File, ExportDestination.Back)
                .UseConverter(PromptLabels.ExportDestinationLabel));
        if (selected == ExportDestination.Back) return;

        var json = JsonSerializer.Serialize(config, ConfigStore.JsonOptions);
        if (selected == ExportDestination.Stdout)
        {
            Console.WriteLine(json);
            return;
        }

        ConfigStore.WriteAllTextAtomic(PromptRequired("File:"), json);
    }

    private void WriteSummary(ConfigModel config, bool dirty)
    {
        _console.MarkupLine("[bold]Mittwald DDNS Config[/]");
        _console.WriteLine();

        WriteGlobalSettings(config);
        WriteAccounts(config);
        _console.WriteLine();
        _console.MarkupLineInterpolated($"Unsaved changes: {(dirty ? "yes" : "no")}");
    }

    private void WriteGlobalSettings(ConfigModel config)
    {
        var table = new Table()
            .Title("Global")
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("Interval", config.Timeout.ToString());
        table.AddRow("Default API version", config.DefaultApiVersion.ToString());
        table.AddRow("Global webhook", string.IsNullOrWhiteSpace(config.GlobalWebhook) ? "unset" : "configured");
        _console.Write(table);
    }

    private void WriteAccounts(ConfigModel config)
    {
        var table = new Table()
            .Title("Accounts")
            .AddColumn("Name")
            .AddColumn("Domains")
            .AddColumn("Webhook")
            .AddColumn("API");

        if (config.Accounts.Count == 0)
            table.AddRow("No accounts configured yet.", string.Empty, string.Empty, string.Empty);
        else
            foreach (var account in config.Accounts)
                table.AddRow(
                    Markup.Escape(account.Name),
                    account.Domains.Count.ToString(),
                    string.IsNullOrWhiteSpace(account.Webhook) ? "global" : "custom",
                    account.ApiVersion is null ? "global" : $"v{account.ApiVersion}");

        _console.Write(table);
    }

    private void WriteAccountSummary(ConfigModel config, ConfigAccount account)
    {
        var table = new Table()
            .Title($"Account: {Markup.Escape(account.Name)}")
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("API key", "********");
        table.AddRow("API version", account.ApiVersion is null ? "global default" : $"v{account.ApiVersion}");
        table.AddRow("Webhook", string.IsNullOrWhiteSpace(account.Webhook) ? "global default" : "custom");
        table.AddRow("Domains", account.Domains.Count.ToString());
        table.AddRow("Effective API version", (account.ApiVersion ?? config.DefaultApiVersion).ToString());
        _console.Write(table);
    }

    private void WriteDomains(IEnumerable<ConfigDomain> domains)
    {
        var table = new Table()
            .AddColumn("Domain")
            .AddColumn("ID")
            .AddColumn("Project");

        foreach (var domain in domains.OrderBy(domain => domain.Domain, StringComparer.OrdinalIgnoreCase))
            table.AddRow(
                Markup.Escape(domain.Domain),
                Markup.Escape(domain.Id),
                string.IsNullOrWhiteSpace(domain.ProjectId) ? string.Empty : Markup.Escape(domain.ProjectId));

        _console.Write(table);
    }

    private void WriteEffectiveConfig(ConfigModel config)
    {
        _console.WriteLine();
        var table = new Table()
            .Title("Effective config")
            .AddColumn("Domain")
            .AddColumn("Account")
            .AddColumn("API")
            .AddColumn("Webhook");

        foreach (var account in config.Accounts)
        foreach (var domain in account.Domains)
            table.AddRow(
                Markup.Escape(domain.Domain),
                Markup.Escape(account.Name),
                (account.ApiVersion ?? config.DefaultApiVersion).ToString(),
                WebhookSource(config, account));

        _console.Write(table);
    }

    private void WriteValidationErrors(IReadOnlyList<string> errors)
    {
        var table = new Table()
            .Title("Imported config is invalid")
            .AddColumn("Error");

        foreach (var error in errors) table.AddRow(Markup.Escape(error));

        _console.Write(table);
    }

    private int ResolveApiVersion(ConfigModel config, ConfigAccount account, ApiVersionSelection selection)
    {
        if (selection.ApiVersion is not null) return selection.ApiVersion.Value;

        if (selection.UseAccountSetting && account.ApiVersion is not null) return account.ApiVersion.Value;

        return config.DefaultApiVersion;
    }

    private int? PromptApiVersionOverride()
    {
        var selected = _console.Prompt(
            new SelectionPrompt<ApiVersionChoice>()
                .Title("API version override")
                .AddChoices(ApiVersionChoice.GlobalDefault, ApiVersionChoice.V1, ApiVersionChoice.V2)
                .UseConverter(PromptLabels.ApiVersionLabel));
        return selected switch
        {
            ApiVersionChoice.V1 => 1,
            ApiVersionChoice.V2 => 2,
            _ => null
        };
    }

    private ApiVersionSelection PromptApiVersion(string title, bool includeAccountSetting)
    {
        var choices = includeAccountSetting
            ? new[]
            {
                ApiVersionChoice.AccountSetting, ApiVersionChoice.GlobalDefault, ApiVersionChoice.V1,
                ApiVersionChoice.V2
            }
            : [ApiVersionChoice.GlobalDefault, ApiVersionChoice.V1, ApiVersionChoice.V2];

        var selected = _console.Prompt(
            new SelectionPrompt<ApiVersionChoice>()
                .Title(title)
                .AddChoices(choices)
                .UseConverter(PromptLabels.ApiVersionLabel));

        return selected switch
        {
            ApiVersionChoice.AccountSetting => new ApiVersionSelection(null, false, true),
            ApiVersionChoice.GlobalDefault => new ApiVersionSelection(null, true, false),
            ApiVersionChoice.V1 => new ApiVersionSelection(1, false, false),
            ApiVersionChoice.V2 => new ApiVersionSelection(2, false, false),
            _ => new ApiVersionSelection(null, true, false)
        };
    }

    private int PromptApiVersionValue(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<int>(prompt)
                .Validate(PromptValidators.ApiVersion));
    }

    private TimeSpan PromptTimeSpan(string prompt)
    {
        var input = _console.Prompt(
            new TextPrompt<string>(prompt)
                .Validate(PromptValidators.Interval));

        PromptValidators.TryParseTimeSpan(input, out var timeSpan);
        return timeSpan;
    }

    private string PromptRequired(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<string>(prompt)
                .Validate(PromptValidators.Required));
    }

    private string PromptOptional(string prompt)
    {
        return _console.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
    }

    private string PromptOptionalUrl(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<string>(prompt)
                .AllowEmpty()
                .Validate(PromptValidators.OptionalUrl));
    }

    private string PromptSecret(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<string>(prompt)
                .Secret()
                .Validate(PromptValidators.Required));
    }

    private ConfigDomain PromptDomain(IReadOnlyList<ConfigDomain> domains, string title)
    {
        return _console.Prompt(
            new SelectionPrompt<ConfigDomain>()
                .Title(title)
                .AddChoices(domains)
                .UseConverter(domain => domain.Domain));
    }

    private void PromptToContinue()
    {
        _console.Prompt(new TextPrompt<string>("Press enter to continue.").AllowEmpty());
    }

    private static string WebhookSource(ConfigModel config, ConfigAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.Webhook)) return "account override";

        if (!string.IsNullOrWhiteSpace(config.GlobalWebhook)) return "global";

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

    private static string AccountEditLabel(AccountEditAction action)
    {
        return action switch
        {
            AccountEditAction.ReplaceApiKey => "Replace API key",
            AccountEditAction.RenameAccount => "Rename account",
            AccountEditAction.SetApiVersionOverride => "Set API version override",
            AccountEditAction.SetWebhookOverride => "Set webhook override",
            AccountEditAction.ManageDomains => "Manage domains",
            AccountEditAction.RemoveAccount => "Remove account",
            AccountEditAction.Back => "Back",
            _ => action.ToString()
        };
    }

    private sealed record AccountMenuSelection(AccountMenuAction Action, ConfigAccount? Account = null);

    private sealed record ApiVersionSelection(int? ApiVersion, bool UseGlobal, bool UseAccountSetting);

    private enum AccountEditAction
    {
        ReplaceApiKey,
        RenameAccount,
        SetApiVersionOverride,
        SetWebhookOverride,
        ManageDomains,
        RemoveAccount,
        Back
    }
}
