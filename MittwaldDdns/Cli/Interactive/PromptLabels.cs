namespace MittwaldDdns.Cli.Interactive;

public static class PromptLabels
{
    public static string MainMenuLabel(MainMenuAction action)
    {
        return action switch
        {
            MainMenuAction.AddAccount => "Add API key/account",
            MainMenuAction.ImportPlainJson => "Import plain JSON",
            MainMenuAction.Accounts => "Accounts",
            MainMenuAction.GlobalSettings => "Global settings",
            MainMenuAction.ReviewEffectiveConfig => "Review effective config",
            MainMenuAction.ExportPlainJson => "Export plain JSON",
            MainMenuAction.Save => "Save",
            MainMenuAction.Exit => "Exit",
            MainMenuAction.ExitWithoutSaving => "Exit without saving",
            _ => action.ToString()
        };
    }

    public static string AccountMenuLabel(AccountMenuAction action)
    {
        return action switch
        {
            AccountMenuAction.AddAccount => "Add API key/account",
            AccountMenuAction.EditAccount => "Edit account",
            AccountMenuAction.Back => "Back",
            _ => action.ToString()
        };
    }

    public static string DomainMenuLabel(DomainMenuAction action)
    {
        return action switch
        {
            DomainMenuAction.SyncFromMittwald => "Sync from Mittwald",
            DomainMenuAction.AddManually => "Add manually",
            DomainMenuAction.EditDomain => "Edit domain",
            DomainMenuAction.RemoveDomain => "Remove domain",
            DomainMenuAction.Back => "Back",
            _ => action.ToString()
        };
    }

    public static string GlobalSettingsLabel(GlobalSettingsAction action)
    {
        return action switch
        {
            GlobalSettingsAction.ChangeInterval => "Change interval",
            GlobalSettingsAction.ChangeDefaultApiVersion => "Change default API version",
            GlobalSettingsAction.ChangeGlobalWebhook => "Change global webhook",
            GlobalSettingsAction.ClearGlobalWebhook => "Clear global webhook",
            GlobalSettingsAction.Back => "Back",
            _ => action.ToString()
        };
    }

    public static string ExportDestinationLabel(ExportDestination destination)
    {
        return destination switch
        {
            ExportDestination.Stdout => "Plain JSON to stdout",
            ExportDestination.File => "Plain JSON to file",
            ExportDestination.Back => "Back",
            _ => destination.ToString()
        };
    }

    public static string ApiVersionLabel(ApiVersionChoice choice)
    {
        return choice switch
        {
            ApiVersionChoice.AccountSetting => "Use account setting",
            ApiVersionChoice.GlobalDefault => "Use global default",
            ApiVersionChoice.V1 => "API v1",
            ApiVersionChoice.V2 => "API v2",
            _ => choice.ToString()
        };
    }
}