# Mittwald DDNS

A DDNS worker for mittwald domains supporting API v1 (login.mittwald.de) and v2 (mStudio).

## Warning

V2 remains untested, as our own systems still use the API v1

## Login helper

Start the interactive login helper:

```bash
mittwald-ddns login
```

Create a key for v2:

```bash
mittwald-ddns login --v2 --email you@example.com
```

Create a key for v1:

```bash
mittwald-ddns login --v1 --username r1234
```

The helper prints the key and exits. For v1 the printed key is `uuid:secret`, because the old API uses the created application token as login credentials.

## Config

Run the daemon with:

```bash
mittwald-ddns
```

Open the interactive config workspace:

```bash
mittwald-ddns config
```

Export the configured app config as plain JSON:

```bash
mittwald-ddns config export
mittwald-ddns config export --file config.json
```

Import a config file into the configured app config path:

```bash
mittwald-ddns config save --file config.json
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
      "name": "mittwald-project-uid",
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

For v1 accounts, `name` is the required Mittwald project/account identifier. Use the project's exact account name or numeric UID; it is not an arbitrary local label or the application-token UUID. `domain` is the domain name. For v2 accounts, `id` is the DNS zone id. If `id` is not enough for a future API response shape, keep `project_id` set so the worker can resolve the zone by `domain`.

## Docker

Run the worker with Docker Compose:

```bash
docker compose up -d
```

Set `MITTWALD_SECRET` in the `.env` to use the encrypted config file.

For one-off commands, pass the arguments after the service name:

```bash
docker compose run --rm mittwald-ddns login
docker compose run --rm mittwald-ddns config export
```

## License

MIT. See [LICENSE](LICENSE).
