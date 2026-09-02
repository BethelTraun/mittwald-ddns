using System.Net;
using MittwaldDdns.API;

namespace MittwaldDdns;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly HttpClient HttpClient = new();
    private readonly Lock _configLock = new();
    private readonly Dictionary<string, MittwaldV1> _v1Clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MittwaldV2> _v2Clients = new(StringComparer.Ordinal);
    private ConfigModel _activeConfig = new();
    private FileSystemWatcher? _configWatcher;
    private CancellationTokenSource? _reloadDebounce;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var encSecret = Environment.GetEnvironmentVariable("MITTWALD_SECRET");
        var configPath = Environment.GetEnvironmentVariable("MITTWALD_CONFIG_PATH")
                         ?? ConfigStore.DefaultConfigPath;

        var configStore = new ConfigStore(configPath, encSecret);
        var loadedConfig = configStore.LoadRequired().Config;
        ConfigValidator.ThrowIfInvalid(loadedConfig);
        SetActiveConfig(loadedConfig);
        StartConfigWatcher(configStore, stoppingToken);

        logger.LogInformation(
            "Mittwald DDNS worker started. ConfigPath: {ConfigPath}; Domains: {DomainCount}; Interval: {Interval}",
            configStore.ConfigPath,
            CountDomains(loadedConfig),
            loadedConfig.Timeout);

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = GetActiveConfig();
            var delay = config.Timeout > TimeSpan.Zero
                ? config.Timeout
                : TimeSpan.FromMinutes(5);

            logger.LogInformation("Mittwald DDNS check started at: {Time}", DateTimeOffset.Now);

            try
            {
                var ipAddress = await GetCurrentIpAddressAsync(stoppingToken);
                await UpdateDomainsAsync(config, ipAddress, stoppingToken);
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
        foreach (var account in config.Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.ApiKey))
            {
                logger.LogError("Skipping account {Account}: no API key is configured", account.Name);
                continue;
            }

            var apiVersion = account.ApiVersion ?? config.DefaultApiVersion;
            foreach (var domain in account.Domains)
                try
                {
                    if (apiVersion == 1)
                        await UpdateV1DomainAsync(GetV1Client(account.ApiKey), account, domain, ipAddress,
                            cancellationToken);
                    else if (apiVersion == 2)
                        await UpdateV2DomainAsync(GetV2Client(account.ApiKey), domain, ipAddress, cancellationToken);
                    else
                        throw new InvalidOperationException($"Unsupported Mittwald API version: {apiVersion}");

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
        ConfigAccount account,
        ConfigDomain domain,
        IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.Name))
            throw new InvalidOperationException($"Domain {domain.Domain} has no v1 account identifier.");

        if (string.IsNullOrWhiteSpace(domain.Domain))
            throw new InvalidOperationException("A v1 domain needs a domain name.");

        return client.UpdateTargetAsync(account.Name, domain.Domain, ipAddress, cancellationToken);
    }

    private static async Task UpdateV2DomainAsync(
        MittwaldV2 client,
        ConfigDomain domain,
        IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        var dnsZoneId = domain.Id;
        if (string.IsNullOrWhiteSpace(dnsZoneId))
            dnsZoneId = await ResolveV2DnsZoneIdAsync(client, domain, cancellationToken);

        await client.UpdateARecordSetAsync(dnsZoneId, ipAddress, cancellationToken);
    }

    private static async Task<string> ResolveV2DnsZoneIdAsync(
        MittwaldV2 client,
        ConfigDomain domain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain.ProjectId))
            throw new InvalidOperationException($"Domain {domain.Domain} has no v2 DNS zone id or project id.");

        var zones = await client.ListDnsZonesAsync(domain.ProjectId, cancellationToken);
        var zone = zones.FirstOrDefault(candidate =>
            string.Equals(candidate.Domain, domain.Domain, StringComparison.OrdinalIgnoreCase));

        return zone?.Id
               ?? throw new InvalidOperationException(
                   $"No DNS zone found for {domain.Domain} in project {domain.ProjectId}.");
    }

    private static async Task<IPAddress> GetCurrentIpAddressAsync(CancellationToken cancellationToken)
    {
        var ipServiceUrl = Environment.GetEnvironmentVariable("MITTWALD_IP_SERVICE_URL")
                           ?? "https://api64.ipify.org";

        var response = await HttpClient.GetStringAsync(ipServiceUrl, cancellationToken);
        return IPAddress.Parse(response.Trim());
    }

    private void StartConfigWatcher(ConfigStore configStore, CancellationToken stoppingToken)
    {
        var directory = Path.GetDirectoryName(configStore.ConfigPath);
        if (string.IsNullOrWhiteSpace(directory)) directory = Directory.GetCurrentDirectory();

        Directory.CreateDirectory(directory);

        _configWatcher = new FileSystemWatcher(directory, Path.GetFileName(configStore.ConfigPath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size |
                           NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _configWatcher.Changed += (_, _) => QueueConfigReload(configStore, stoppingToken);
        _configWatcher.Created += (_, _) => QueueConfigReload(configStore, stoppingToken);
        _configWatcher.Renamed += (_, _) => QueueConfigReload(configStore, stoppingToken);
    }

    private void QueueConfigReload(ConfigStore configStore, CancellationToken stoppingToken)
    {
        var previousDebounce = Interlocked.Exchange(
            ref _reloadDebounce,
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken));
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        var debounce = _reloadDebounce;
        if (debounce is null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), debounce.Token);
                var loadedConfig = configStore.LoadRequired().Config;
                ConfigValidator.ThrowIfInvalid(loadedConfig);
                SetActiveConfig(loadedConfig);

                logger.LogInformation(
                    "Reloaded config. Domains: {DomainCount}; Interval: {Interval}",
                    CountDomains(loadedConfig),
                    loadedConfig.Timeout);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Config reload failed. Keeping previous valid config.");
            }
        }, CancellationToken.None);
    }

    private ConfigModel GetActiveConfig()
    {
        lock (_configLock)
        {
            return _activeConfig;
        }
    }

    private void SetActiveConfig(ConfigModel config)
    {
        lock (_configLock)
        {
            _activeConfig = config;
        }
    }

    private static int CountDomains(ConfigModel config)
    {
        return config.Accounts.Sum(account => account.Domains.Count);
    }

    public override void Dispose()
    {
        _configWatcher?.Dispose();
        _reloadDebounce?.Dispose();
        base.Dispose();
    }
}