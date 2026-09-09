using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MittwaldDdns.API;

public sealed class MittwaldV1
{
    private static readonly Uri BaseUri = new("https://api.mittwald.de/v1/");
    private readonly string _apiKey;

    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset _cachedTokenExpires;

    public MittwaldV1(HttpClient httpClient, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async Task<IReadOnlyList<V1Domain>> ListDomainsAsync(
        string accountIdentifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountIdentifier);

        using var response = await SendAsync(
            HttpMethod.Get,
            $"accounts/{Uri.EscapeDataString(accountIdentifier)}/domains?limit=100&offset=0",
            null,
            cancellationToken);

        return await ReadJsonAsync<List<V1Domain>>(response.Content, cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<V1DnsDomain>> GetDnsOverviewAsync(
        string accountIdentifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountIdentifier);

        using var response = await SendAsync(
            HttpMethod.Get,
            $"accounts/{Uri.EscapeDataString(accountIdentifier)}/dns",
            null,
            cancellationToken);

        return await ReadJsonAsync<List<V1DnsDomain>>(response.Content, cancellationToken)
               ?? [];
    }

    public async Task UpdateTargetAsync(
        string accountIdentifier,
        string domainIdentifier,
        IPAddress ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainIdentifier);

        using var response = await SendAsync(
            HttpMethod.Put,
            $"accounts/{Uri.EscapeDataString(accountIdentifier)}/dns/{Uri.EscapeDataString(domainIdentifier)}",
            V1UpdateDnsRequest.ForIpAddress(ipAddress),
            cancellationToken);
    }

    public static async Task<string> CreateApiKeyAsync(
        HttpClient httpClient,
        string username,
        string password,
        string? multiFactorCode,
        string? deviceId,
        string description,
        CancellationToken cancellationToken = default)
    {
        var session = await AuthenticateAsync(
            httpClient,
            username,
            password,
            multiFactorCode,
            deviceId,
            cancellationToken);

        if (session.SecondFactor is not null)
            throw new MittwaldSecondFactorRequiredException(session.SecondFactor.DeviceId);

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "authentication/tokens"))
        {
            Content = CreateJsonContent(new V1CreateApplicationTokenRequest(description))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, HttpStatusCode.Created, cancellationToken);

        var createdToken = await ReadJsonAsync<V1ApplicationTokenResponse>(response.Content, cancellationToken)
                           ?? throw new InvalidOperationException("Mittwald returned an empty token response.");

        if (string.IsNullOrWhiteSpace(createdToken.Uuid) || string.IsNullOrWhiteSpace(createdToken.Token))
            throw new InvalidOperationException("Mittwald did not return a usable application token.");

        return $"{createdToken.Uuid}:{createdToken.Token}";
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri, relativeUrl));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", await GetBearerTokenAsync(cancellationToken));

        if (body is not null) request.Content = CreateJsonContent(body);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken: cancellationToken);
        return response;
    }

    private async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
        if (!_apiKey.Contains(':', StringComparison.Ordinal)) return _apiKey;

        if (!string.IsNullOrWhiteSpace(_cachedToken)
            && _cachedTokenExpires > DateTimeOffset.UtcNow.AddMinutes(1))
            return _cachedToken;

        var split = _apiKey.Split(':', 2);
        var session = await AuthenticateAsync(_httpClient, split[0], split[1], null, null, cancellationToken);

        if (session.SecondFactor is not null)
            throw new InvalidOperationException("The stored v1 application token requires a second factor.");

        _cachedToken = session.Token;
        _cachedTokenExpires = session.Expires;
        return _cachedToken;
    }

    private static async Task<V1AuthenticationResult> AuthenticateAsync(
        HttpClient httpClient,
        string username,
        string password,
        string? multiFactorCode,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "authenticate"))
        {
            Content = CreateJsonContent(new V1AuthenticateRequest(username, password, multiFactorCode, deviceId))
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            var secondFactor = await ReadJsonAsync<V1SecondFactorResponse>(response.Content, cancellationToken)
                               ?? new V1SecondFactorResponse("TOTP", null);

            return V1AuthenticationResult.RequiresSecondFactor(secondFactor);
        }

        await EnsureSuccessAsync(response, cancellationToken: cancellationToken);

        var token = await ReadJsonAsync<V1AuthenticationResponse>(response.Content, cancellationToken)
                    ?? throw new InvalidOperationException("Mittwald returned an empty login response.");

        return V1AuthenticationResult.Success(token.Token, token.Expires);
    }

    private static HttpContent CreateJsonContent(object body)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    // Mittwald v1 returns the invalid IANA charset name "utf8" in some JSON responses
    private static async Task<T?> ReadJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(bytes, JsonSerializerOptions.Web);
    }

    private static async Task<string> ReadUtf8ContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        HttpStatusCode? expectedStatusCode = null,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode &&
            (expectedStatusCode is null || response.StatusCode == expectedStatusCode)) return;

        var content = await ReadUtf8ContentAsync(response.Content, cancellationToken);
        throw new HttpRequestException(
            $"Mittwald API v1 returned {(int)response.StatusCode} {response.ReasonPhrase}: {content}",
            null,
            response.StatusCode);
    }
}

public sealed record V1Domain(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fullname")]
    string FullName,
    [property: JsonPropertyName("tld")] string Tld,
    [property: JsonPropertyName("registryStatus")]
    string? RegistryStatus);

public sealed record V1DnsDomain(
    [property: JsonPropertyName("uid")] long Uid,
    [property: JsonPropertyName("fullName")]
    string FullName,
    [property: JsonPropertyName("domainName")]
    string DomainName,
    [property: JsonPropertyName("records")]
    V1DnsRecords Records);

public sealed record V1DnsRecords(
    [property: JsonPropertyName("A")] IReadOnlyList<V1DnsRecord> A,
    [property: JsonPropertyName("AAAA")] IReadOnlyList<V1DnsRecord> Aaaa,
    [property: JsonPropertyName("CNAME")] IReadOnlyList<V1DnsRecord> CName,
    [property: JsonPropertyName("MX")] IReadOnlyList<V1DnsRecord> Mx,
    [property: JsonPropertyName("TXT")] IReadOnlyList<V1DnsRecord> Txt);

public sealed record V1DnsRecord(
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("ttl")] int Ttl,
    [property: JsonPropertyName("priority")]
    int Priority);

public sealed class MittwaldSecondFactorRequiredException(string? deviceId)
    : Exception("Mittwald requires a second factor.")
{
    public string? DeviceId { get; } = deviceId;
}

internal sealed record V1AuthenticateRequest(
    [property: JsonPropertyName("username")]
    string Username,
    [property: JsonPropertyName("password")]
    string Password,
    [property: JsonPropertyName("multiFactorCode")]
    string? MultiFactorCode,
    [property: JsonPropertyName("deviceId")]
    string? DeviceId);

internal sealed record V1AuthenticationResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires")]
    DateTimeOffset Expires);

internal sealed record V1SecondFactorResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("deviceId")]
    string? DeviceId);

internal sealed record V1CreateApplicationTokenRequest(
    [property: JsonPropertyName("description")]
    string Description);

internal sealed record V1ApplicationTokenResponse(
    [property: JsonPropertyName("uuid")] string Uuid,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("description")]
    string? Description);

internal sealed record V1UpdateDnsRequest(
    [property: JsonPropertyName("target")] V1IpTargetRecord Target)
{
    public static V1UpdateDnsRequest ForIpAddress(IPAddress ipAddress)
    {
        return ipAddress.AddressFamily switch
        {
            AddressFamily.InterNetwork => new V1UpdateDnsRequest(new V1IpTargetRecord(ipAddress.ToString(), null)),
            AddressFamily.InterNetworkV6 => new V1UpdateDnsRequest(new V1IpTargetRecord(null, ipAddress.ToString())),
            _ => throw new ArgumentException("Only IPv4 and IPv6 addresses are supported.", nameof(ipAddress))
        };
    }
}

internal sealed record V1IpTargetRecord(
    [property: JsonPropertyName("a"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? A,
    [property: JsonPropertyName("aaaa"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Aaaa);

internal sealed class V1AuthenticationResult
{
    private V1AuthenticationResult(string token, DateTimeOffset expires, V1SecondFactorResponse? secondFactor)
    {
        Token = token;
        Expires = expires;
        SecondFactor = secondFactor;
    }

    public string Token { get; }
    public DateTimeOffset Expires { get; }
    public V1SecondFactorResponse? SecondFactor { get; }

    public static V1AuthenticationResult Success(string token, DateTimeOffset expires)
    {
        return new V1AuthenticationResult(token, expires, null);
    }

    public static V1AuthenticationResult RequiresSecondFactor(V1SecondFactorResponse secondFactor)
    {
        return new V1AuthenticationResult(string.Empty, DateTimeOffset.MinValue, secondFactor);
    }
}
