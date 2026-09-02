using System.Text.Json;
using MittwaldDdns.Cli.Interactive;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MittwaldDdns.Cli.Commands;

public sealed class ConfigCommand : AsyncCommand<ConfigSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ConfigSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var workspace = new ConfigWorkspace(CreateStore(), new HttpClient(), CliConsole.Output);
            return await workspace.RunAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            CliConsole.Error.MarkupLine(Markup.Escape(exception.Message));
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
}

public sealed class ConfigExportCommand : Command<ConfigExportSettings>
{
    protected override int Execute(
        CommandContext context,
        ConfigExportSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var store = CreateStore();
            var loaded = store.LoadRequired();
            ConfigValidator.ThrowIfInvalid(loaded.Config);
            var json = JsonSerializer.Serialize(loaded.Config, ConfigStore.JsonOptions);

            if (string.IsNullOrWhiteSpace(settings.File))
            {
                Console.WriteLine(json);
                return 0;
            }

            ConfigStore.WriteAllTextAtomic(settings.File, json);
            return 0;
        }
        catch (Exception exception)
        {
            CliConsole.Error.MarkupLine(Markup.Escape(exception.Message));
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
}

public sealed class ConfigSaveCommand : Command<ConfigSaveSettings>
{
    protected override ValidationResult Validate(CommandContext context, ConfigSaveSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.File)
            ? ValidationResult.Error("config save needs --file.")
            : ValidationResult.Success();
    }

    protected override int Execute(
        CommandContext context,
        ConfigSaveSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var store = CreateStore();
            var imported = ConfigStore.LoadFromFile(settings.File!, Environment.GetEnvironmentVariable("MITTWALD_SECRET"));
            ConfigValidator.ThrowIfInvalid(imported.Config);
            store.Save(imported.Config, imported.Format);
            CliConsole.Error.MarkupLineInterpolated($"Saved {store.ConfigPath}");
            return 0;
        }
        catch (Exception exception)
        {
            CliConsole.Error.MarkupLine(Markup.Escape(exception.Message));
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
}

public sealed class ConfigSettings : CommandSettings
{
}

public sealed class ConfigExportSettings : CommandSettings
{
    [CommandOption("--file <FILE>")]
    public string? File { get; init; }
}

public sealed class ConfigSaveSettings : CommandSettings
{
    [CommandOption("--file <FILE>")]
    public string? File { get; init; }
}
