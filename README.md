# Mittwald DDNS

A DDNS worker for mittwald domains supporting API v1 and v2.

## Login helper

Start the interactive login helper:

```bash
dotnet run --project MittwaldDdns -- login
```

Create a key for v2:

```bash
dotnet run --project MittwaldDdns -- login --v2 --email you@example.com
```

Create a key for v1:

```bash
dotnet run --project MittwaldDdns -- login --v1 --username r1234
```

The helper prints the key and exits. For v1 the printed key is `uuid:secret`, because the old API uses the created application token as login credentials.

## Config

Run the daemon with:

```bash
dotnet run --project MittwaldDdns
```

Open the interactive config workspace:

```bash
dotnet run --project MittwaldDdns -- config
```

Export the configured app config as plain JSON:

```bash
dotnet run --project MittwaldDdns -- config export
dotnet run --project MittwaldDdns -- config export --file config.json
```

Import a config file into the configured app config path:

```bash
dotnet run --project MittwaldDdns -- config save --file config.json
```

`MITTWALD_CONFIG_PATH` sets the app config file. The default is `~/.config/mittwald-ddns.json`.
`MITTWALD_SECRET` is required for encrypted config files and for saving from the interactive config workspace.

The config format supports multiple Mittwald API keys:

```json
{
  "global_webhook": "https://example.com/webhook",
  "default_api_version": 2,
  "timeout": "00:05:00",
  "accounts": [
    {
      "name": "main",
      "api_key": "...",
      "api_version": null,
      "webhook": null,
      "domains": [
        {
          "id": "dns-zone-id",
          "domain": "example.com",
          "project_id": "project-id"
        }
      ]
    }
  ]
}
```

For v1 accounts, the account `name` is used as the Mittwald account identifier and `domain` is the domain name. For v2 accounts, `id` is the DNS zone id. If `id` is not enough for a future API response shape, keep `project_id` set so the worker can resolve the zone by `domain`.
