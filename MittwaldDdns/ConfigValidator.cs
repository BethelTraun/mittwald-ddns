namespace MittwaldDdns;

public static class ConfigValidator
{
    public static IReadOnlyList<string> Validate(ConfigModel config)
    {
        var errors = new List<string>();

        if (config.Timeout <= TimeSpan.Zero)
        {
            errors.Add("timeout must be greater than zero.");
        }

        if (!IsSupportedApiVersion(config.DefaultApiVersion))
        {
            errors.Add("default_api_version must be 1 or 2.");
        }

        if (!IsValidOptionalUrl(config.GlobalWebhook))
        {
            errors.Add("global_webhook must be an absolute URL when set.");
        }

        if (config.Accounts.Count == 0)
        {
            errors.Add("at least one account is required.");
        }

        var accountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var accountIndex = 0; accountIndex < config.Accounts.Count; accountIndex++)
        {
            var account = config.Accounts[accountIndex];
            var accountLabel = string.IsNullOrWhiteSpace(account.Name)
                ? $"accounts[{accountIndex}]"
                : $"account '{account.Name}'";

            if (string.IsNullOrWhiteSpace(account.Name))
            {
                errors.Add($"accounts[{accountIndex}].name is required.");
            }
            else if (!accountNames.Add(account.Name))
            {
                errors.Add($"account name '{account.Name}' must be unique.");
            }

            if (string.IsNullOrWhiteSpace(account.ApiKey))
            {
                errors.Add($"{accountLabel} needs an api_key.");
            }

            if (account.ApiVersion is not null && !IsSupportedApiVersion(account.ApiVersion.Value))
            {
                errors.Add($"{accountLabel} api_version must be 1 or 2 when set.");
            }

            if (!IsValidOptionalUrl(account.Webhook))
            {
                errors.Add($"{accountLabel} webhook must be an absolute URL when set.");
            }

            foreach (var domain in account.Domains)
            {
                var domainLabel = string.IsNullOrWhiteSpace(domain.Domain)
                    ? $"{accountLabel} domain"
                    : $"{accountLabel} domain '{domain.Domain}'";

                if (string.IsNullOrWhiteSpace(domain.Domain))
                {
                    errors.Add($"{accountLabel} has a domain without a domain name.");
                }

                if (string.IsNullOrWhiteSpace(domain.Id))
                {
                    errors.Add($"{domainLabel} needs an id.");
                }
            }
        }

        return errors;
    }

    public static void ThrowIfInvalid(ConfigModel config)
    {
        var errors = Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Config is invalid: " + string.Join(" ", errors));
        }
    }

    public static bool IsSupportedApiVersion(int apiVersion)
    {
        return apiVersion is 1 or 2;
    }

    private static bool IsValidOptionalUrl(string? url)
    {
        return string.IsNullOrWhiteSpace(url)
            || Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}
