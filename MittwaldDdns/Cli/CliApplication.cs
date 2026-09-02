using MittwaldDdns.Cli.Commands;
using Spectre.Console.Cli;

namespace MittwaldDdns;

public static class CliApplication
{
    public static CommandApp Create()
    {
        var app = new CommandApp();
        app.SetDefaultCommand<RunDaemonCommand>();
        app.Configure(ConfigureProductionCommands);
        return app;
    }

    public static void ConfigureProductionCommands(IConfigurator config)
    {
        Configure<LoginCommand, ConfigCommand, ConfigExportCommand, ConfigSaveCommand>(config);
    }

    public static void Configure<TLoginCommand, TConfigCommand, TConfigExportCommand, TConfigSaveCommand>(
        IConfigurator config)
        where TLoginCommand : class, ICommandLimiter<LoginSettings>
        where TConfigCommand : class, ICommandLimiter<ConfigSettings>
        where TConfigExportCommand : class, ICommandLimiter<ConfigExportSettings>
        where TConfigSaveCommand : class, ICommandLimiter<ConfigSaveSettings>
    {
        config.SetApplicationName("mittwald-ddns");
        config.Settings.StrictParsing = true;

        config.AddCommand<TLoginCommand>("login")
            .WithDescription("Create a Mittwald API key.")
            .WithExample(["login", "--v2", "--email", "user@example.com"]);

        config.AddBranch("config", branch =>
        {
            branch.SetDescription("Open, export, or save the DDNS config.");
            branch.SetDefaultCommand<TConfigCommand>();

            branch.AddCommand<TConfigExportCommand>("export")
                .WithDescription("Export the current config as plain JSON.")
                .WithExample(["config", "export"])
                .WithExample(["config", "export", "--file", "config.json"]);

            branch.AddCommand<TConfigSaveCommand>("save")
                .WithDescription("Import and save a config file.")
                .WithExample(["config", "save", "--file", "config.json"]);
        });
    }
}
