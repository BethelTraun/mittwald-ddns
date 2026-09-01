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

`MITTWALD_SECRET` is required for encrypted config files.

For v1, `Id` is the account name or id and `Domain` is the domain name. For v2, `Id` is the DNS zone id. If `Id` is empty, set `ProjectId` and the worker will find the DNS zone by `Domain`.
