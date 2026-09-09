using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MittwaldDdns.API;

public sealed class MittwaldV2
{
    private static readonly Uri BaseUri = new("https://api.mittwald.de/v2/");
    private readonly string _apiKey;

    private readonly HttpClient _httpClient;

    public MittwaldV2(HttpClient httpClient, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async Task<IReadOnlyList<V2Domain>> ListDomainsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "domains", null, cancellationToken);

        return await ReadJsonAsync<List<V2Domain>>(response.Content, cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<V2DnsZone>> ListDnsZonesAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        using var response = await SendAsync(
            HttpMethod.Get,
            $"projects/{Uri.EscapeDataString(projectId)}/dns-zones",
            null,
            cancellationToken);

        return await ReadJsonAsync<List<V2DnsZone>>(response.Content, cancellationToken)
               ?? [];
    }

    public async Task UpdateARecordSetAsync(
        string dnsZoneId,
        IPAddress ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dnsZoneId);

        var request = ipAddress.AddressFamily == AddressFamily.InterNetworkV6
            ? V2UpdateARecordSetRequest.ForIpv6(ipAddress.ToString())
            : V2UpdateARecordSetRequest.ForIpv4(ipAddress.ToString());

        using var response = await SendAsync(
            HttpMethod.Put,
            $"dns-zones/{Uri.EscapeDataString(dnsZoneId)}/record-sets/a",
            request,
            cancellationToken);
    }

    public static async Task<string> CreateApiKeyAsync(
        HttpClient httpClient,
        string email,
        string password,
        string? multiFactorCode,
        string description,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var session = await AuthenticateAsync(httpClient, email, password, multiFactorCode, cancellationToken);

        if (session.SecondFactorRequired) throw new MittwaldSecondFactorRequiredException(null);

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "users/self/api-tokens"))
        {
            Content = JsonContent.Create(new V2CreateApiTokenRequest(
                description,
                expiresAt,
                ["api_read", "api_write"]))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, HttpStatusCode.Created, cancellationToken);

        var createdToken = await ReadJsonAsync<V2CreateApiTokenResponse>(response.Content, cancellationToken)
                           ?? throw new InvalidOperationException("Mittwald returned an empty token response.");

        if (string.IsNullOrWhiteSpace(createdToken.Token))
            throw new InvalidOperationException("Mittwald did not return a usable API token.");

        return createdToken.Token;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri, relativeUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        if (body is not null) request.Content = JsonContent.Create(body);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken: cancellationToken);
        return response;
    }

    private static async Task<V2AuthenticationResult> AuthenticateAsync(
        HttpClient httpClient,
        string email,
        string password,
        string? multiFactorCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var path = string.IsNullOrWhiteSpace(multiFactorCode)
            ? "authenticate"
            : "authenticate-mfa";

        var request = new V2AuthenticateRequest(email, password, multiFactorCode);
        using var response = await httpClient.PostAsJsonAsync(new Uri(BaseUri, path), request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Accepted) return V2AuthenticationResult.RequiresSecondFactor();

        await EnsureSuccessAsync(response, cancellationToken: cancellationToken);

        var token = await ReadJsonAsync<V2AuthenticationResponse>(response.Content, cancellationToken)
                    ?? throw new InvalidOperationException("Mittwald returned an empty login response.");

        return V2AuthenticationResult.Success(token.Token, token.Expires);
    }

    // Ignore the malformed "charset=utf8" response header returned by some API endpoints
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
            $"Mittwald API v2 returned {(int)response.StatusCode} {response.ReasonPhrase}: {content}");
    }
}

public sealed record V2Domain(
    [property: JsonPropertyName("domainId")]
    string DomainId,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("projectId")]
    string ProjectId,
    [property: JsonPropertyName("connected")]
    bool Connected);

public sealed record V2DnsZone(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("recordSet")]
    V2DnsRecordSet RecordSet);

public sealed record V2DnsRecordSet(
    [property: JsonPropertyName("combinedARecords")]
    V2CombinedARecords? CombinedARecords,
    [property: JsonPropertyName("cname")] object? CName,
    [property: JsonPropertyName("mx")] object? Mx,
    [property: JsonPropertyName("txt")] object? Txt,
    [property: JsonPropertyName("srv")] object? Srv,
    [property: JsonPropertyName("caa")] object? Caa);

public sealed record V2CombinedARecords(
    [property: JsonPropertyName("a")] IReadOnlyList<string>? A,
    [property: JsonPropertyName("aaaa")] IReadOnlyList<string>? Aaaa);

internal sealed record V2AuthenticateRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")]
    string Password,
    [property: JsonPropertyName("multiFactorCode")]
    string? MultiFactorCode);

internal sealed record V2AuthenticationResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("refreshToken")]
    string RefreshToken,
    [property: JsonPropertyName("expires")]
    DateTimeOffset Expires);

internal sealed record V2CreateApiTokenRequest(
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("expiresAt")]
    DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles);

internal sealed record V2CreateApiTokenResponse(
    [property: JsonPropertyName("token")] string Token);

internal sealed record V2UpdateARecordSetRequest(
    [property: JsonPropertyName("a")] IReadOnlyList<string> A,
    [property: JsonPropertyName("aaaa")] IReadOnlyList<string> Aaaa,
    [property: JsonPropertyName("settings")]
    V2RecordSettings Settings)
{
    public static V2UpdateARecordSetRequest ForIpv4(string ipAddress)
    {
        return new V2UpdateARecordSetRequest([ipAddress], [], V2RecordSettings.AutoTtl);
    }

    public static V2UpdateARecordSetRequest ForIpv6(string ipAddress)
    {
        return new V2UpdateARecordSetRequest([], [ipAddress], V2RecordSettings.AutoTtl);
    }
}

internal sealed record V2RecordSettings(
    [property: JsonPropertyName("ttl")] V2Ttl Ttl)
{
    public static V2RecordSettings AutoTtl { get; } = new(new V2Ttl(true));
}

internal sealed record V2Ttl(
    [property: JsonPropertyName("auto")] bool Auto);

internal sealed class V2AuthenticationResult
{
    private V2AuthenticationResult(string token, DateTimeOffset expires, bool secondFactorRequired)
    {
        Token = token;
        Expires = expires;
        SecondFactorRequired = secondFactorRequired;
    }

    public string Token { get; }
    public DateTimeOffset Expires { get; }
    public bool SecondFactorRequired { get; }

    public static V2AuthenticationResult Success(string token, DateTimeOffset expires)
    {
        return new V2AuthenticationResult(token, expires, false);
    }

    public static V2AuthenticationResult RequiresSecondFactor()
    {
        return new V2AuthenticationResult(string.Empty, DateTimeOffset.MinValue, true);
    }
}
