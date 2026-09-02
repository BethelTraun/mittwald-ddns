using Spectre.Console;

namespace MittwaldDdns.Cli.Interactive;

public static class PromptValidators
{
    public static ValidationResult Required(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ValidationResult.Error("A value is required.")
            : ValidationResult.Success();
    }

    public static ValidationResult OptionalUrl(string value)
    {
        return string.IsNullOrWhiteSpace(value) || Uri.TryCreate(value, UriKind.Absolute, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error("Enter an absolute URL, or leave empty.");
    }

    public static ValidationResult ApiVersion(int apiVersion)
    {
        return ConfigValidator.IsSupportedApiVersion(apiVersion)
            ? ValidationResult.Success()
            : ValidationResult.Error("Enter 1 or 2.");
    }

    public static ValidationResult Interval(string value)
    {
        return TryParseTimeSpan(value, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error("Enter a positive TimeSpan value, or a short form like 30s, 5m, or 1h.");
    }

    public static bool TryParseTimeSpan(string input, out TimeSpan value)
    {
        value = default;
        if (TimeSpan.TryParse(input, out var timeSpan) && timeSpan > TimeSpan.Zero)
        {
            value = timeSpan;
            return true;
        }

        return TryParseShortTimeSpan(input, out value);
    }

    public static bool TryParseShortTimeSpan(string input, out TimeSpan value)
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
}
