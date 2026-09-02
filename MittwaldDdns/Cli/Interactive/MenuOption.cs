namespace MittwaldDdns.Cli.Interactive;

public sealed record MenuOption<T>(T Value, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

public enum MainMenuAction
{
    AddAccount,
    ImportPlainJson,
    Accounts,
    GlobalSettings,
    ReviewEffectiveConfig,
    ExportPlainJson,
    Save,
    Exit,
    ExitWithoutSaving
}

public enum AccountMenuAction
{
    AddAccount,
    EditAccount,
    Back
}

public enum DomainMenuAction
{
    SyncFromMittwald,
    AddManually,
    EditDomain,
    RemoveDomain,
    Back
}

public enum GlobalSettingsAction
{
    ChangeInterval,
    ChangeDefaultApiVersion,
    ChangeGlobalWebhook,
    ClearGlobalWebhook,
    Back
}

public enum ExportDestination
{
    Stdout,
    File,
    Back
}

public enum ApiVersionChoice
{
    AccountSetting,
    GlobalDefault,
    V1,
    V2
}
