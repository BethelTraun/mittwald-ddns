using MittwaldDdns.Cli.Commands;
using Spectre.Console.Cli;

namespace MittwaldDdns.Tests;

public sealed class CliRoutingTests
{
    [Fact]
    public async Task NoArgs_SelectsDaemonCommand()
    {
        var result = await CreateProbeApp().RunAsync([]);

        Assert.Equal(10, result);
    }

    [Fact]
    public async Task Login_RoutesToLoginCommand()
    {
        var result = await CreateProbeApp().RunAsync(["login", "--v2", "--email", "user@example.com"]);

        Assert.Equal(20, result);
    }

    [Fact]
    public async Task Config_RoutesToInteractiveConfigCommand()
    {
        var result = await CreateProbeApp().RunAsync(["config"]);

        Assert.Equal(30, result);
    }

    [Fact]
    public async Task ConfigExport_RoutesToExportCommand()
    {
        var result = await CreateProbeApp().RunAsync(["config", "export"]);

        Assert.Equal(40, result);
    }

    [Fact]
    public async Task ConfigSaveWithFile_RoutesToSaveCommand()
    {
        var result = await CreateProbeApp().RunAsync(["config", "save", "--file", "config.json"]);

        Assert.Equal(50, result);
    }

    [Fact]
    public async Task UnknownArgs_FailInsteadOfBeingIgnored()
    {
        var result = await CreateProbeApp().RunAsync(["login", "--does-not-exist"]);

        Assert.NotEqual(20, result);
        Assert.NotEqual(0, result);
    }

    private static CommandApp CreateProbeApp()
    {
        var app = new CommandApp();
        app.SetDefaultCommand<ProbeDaemonCommand>();
        app.Configure(config =>
            CliApplication
                .Configure<ProbeLoginCommand, ProbeConfigCommand, ProbeConfigExportCommand,
                    ProbeConfigSaveCommand>(config));
        return app;
    }

    private sealed class ProbeDaemonCommand : Command
    {
        protected override int Execute(CommandContext context, CancellationToken cancellationToken)
        {
            return 10;
        }
    }

    private sealed class ProbeLoginCommand : Command<LoginSettings>
    {
        protected override int Execute(CommandContext context, LoginSettings settings,
            CancellationToken cancellationToken)
        {
            return 20;
        }
    }

    private sealed class ProbeConfigCommand : Command<ConfigSettings>
    {
        protected override int Execute(CommandContext context, ConfigSettings settings,
            CancellationToken cancellationToken)
        {
            return 30;
        }
    }

    private sealed class ProbeConfigExportCommand : Command<ConfigExportSettings>
    {
        protected override int Execute(CommandContext context, ConfigExportSettings settings,
            CancellationToken cancellationToken)
        {
            return 40;
        }
    }

    private sealed class ProbeConfigSaveCommand : Command<ConfigSaveSettings>
    {
        protected override int Execute(CommandContext context, ConfigSaveSettings settings,
            CancellationToken cancellationToken)
        {
            return string.Equals(settings.File, "config.json", StringComparison.Ordinal) ? 50 : 51;
        }
    }
}