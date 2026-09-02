using Spectre.Console.Cli;

namespace MittwaldDdns.Cli.Commands;

public sealed class RunDaemonCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddHostedService<Worker>();

        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
        return 0;
    }
}
