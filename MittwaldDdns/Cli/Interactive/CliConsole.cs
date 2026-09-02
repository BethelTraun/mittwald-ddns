using Spectre.Console;

namespace MittwaldDdns.Cli.Interactive;

public static class CliConsole
{
    public static IAnsiConsole Output => AnsiConsole.Console;

    public static IAnsiConsole Error { get; } = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error)
    });
}
