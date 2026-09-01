using System.Net;
using MittwaldDdns.API;

namespace MittwaldDdns;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly HttpClient HttpClient = new();
    private readonly Dictionary<string, MittwaldV1> _v1Clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MittwaldV2> _v2Clients = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var encSecret = Environment.GetEnvironmentVariable("MITTWALD_SECRET")
            ?? throw new InvalidOperationException("MITTWALD_SECRET environment variable not set");
        var configPath = Environment.GetEnvironmentVariable("MITTWALD_CONFIG_PATH") ?? "~/.mittwald_config.json";

        var config = new Config(configPath, encSecret);
        var delay = config.ConfigModel.Timeout > TimeSpan.Zero
            ? config.ConfigModel.Timeout
            : TimeSpan.FromMinutes(5);

        logger.LogInformation(
            "Mittwald DDNS worker started. ConfigPath: {ConfigPath}; Domains: {DomainCount}; Interval: {Interval}",
            config.ConfigPath,
            config.ConfigModel.Domains.Count,
            delay);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Mittwald DDNS check started at: {Time}", DateTimeOffset.Now);

            try
            {
                var ipAddress = await GetCurrentIpAddressAsync(stoppingToken);
                await UpdateDomainsAsync(config.ConfigModel, ipAddress, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Mittwald DDNS check failed");
            }

            logger.LogInformation("Mittwald DDNS check finished at: {Time}", DateTimeOffset.Now);

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task UpdateDomainsAsync(ConfigModel config, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        foreach (var domain in config.Domains)
        {
            var apiVersion = domain.ApiVersion != 0
                ? domain.ApiVersion
                : config.GlobalApiVersion != 0 ? config.GlobalApiVersion : 2;
            var apiKey = string.IsNullOrWhiteSpace(domain.ApiKey)
                ? config.ApiKey
                : domain.ApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogError("Failed to update {Domain}: no API key is configured", domain.Domain);
                continue;
            }

            try
            {
                if (apiVersion == 1)
                {
                    await UpdateV1DomainAsync(GetV1Client(apiKey), domain, ipAddress, cancellationToken);
                }
                else if (apiVersion == 2)
                {
                    await UpdateV2DomainAsync(GetV2Client(apiKey), domain, ipAddress, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported Mittwald API version: {apiVersion}");
                }

                logger.LogInformation(
                    "Updated {Domain} with {IpAddress} by Mittwald API v{ApiVersion}",
                    domain.Domain,
                    ipAddress,
                    apiVersion);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to update {Domain}", domain.Domain);
            }
        }
    }

    private MittwaldV1 GetV1Client(string apiKey)
    {
        if (!_v1Clients.TryGetValue(apiKey, out var client))
        {
            client = new MittwaldV1(HttpClient, apiKey);
            _v1Clients.Add(apiKey, client);
        }

        return client;
    }

    private MittwaldV2 GetV2Client(string apiKey)
    {
        if (!_v2Clients.TryGetValue(apiKey, out var client))
        {
            client = new MittwaldV2(HttpClient, apiKey);
            _v2Clients.Add(apiKey, client);
        }

        return client;
    }

    private static Task UpdateV1DomainAsync(
        MittwaldV1 client,
        ConfigDomain domain,
        IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain.Id))
        {
            throw new InvalidOperationException($"Domain {domain.Domain} has no v1 account id.");
        }

        if (string.IsNullOrWhiteSpace(domain.Domain))
        {
            throw new InvalidOperationException("A v1 domain needs a domain name.");
        }

        return client.UpdateTargetAsync(domain.Id, domain.Domain, ipAddress, cancellationToken);
    }

    private static async Task UpdateV2DomainAsync(
        MittwaldV2 client,
        ConfigDomain domain,
        IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        var dnsZoneId = domain.Id;
        if (string.IsNullOrWhiteSpace(dnsZoneId))
        {
            dnsZoneId = await ResolveV2DnsZoneIdAsync(client, domain, cancellationToken);
        }

        await client.UpdateARecordSetAsync(dnsZoneId, ipAddress, cancellationToken);
    }

    private static async Task<string> ResolveV2DnsZoneIdAsync(
        MittwaldV2 client,
        ConfigDomain domain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain.ProjectId))
        {
            throw new InvalidOperationException($"Domain {domain.Domain} has no v2 DNS zone id or project id.");
        }

        var zones = await client.ListDnsZonesAsync(domain.ProjectId, cancellationToken);
        var zone = zones.FirstOrDefault(candidate =>
            string.Equals(candidate.Domain, domain.Domain, StringComparison.OrdinalIgnoreCase));

        return zone?.Id
            ?? throw new InvalidOperationException($"No DNS zone found for {domain.Domain} in project {domain.ProjectId}.");
    }

    private static async Task<IPAddress> GetCurrentIpAddressAsync(CancellationToken cancellationToken)
    {
        var ipServiceUrl = Environment.GetEnvironmentVariable("MITTWALD_IP_SERVICE_URL")
            ?? "https://api64.ipify.org";

        var response = await HttpClient.GetStringAsync(ipServiceUrl, cancellationToken);
        return IPAddress.Parse(response.Trim());
    }
}
