using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MittwaldDdns.API;

namespace MittwaldDdns.Tests;

public sealed class MittwaldV1Tests
{
    [Fact]
    public async Task UpdateTargetAsync_SendsIpv4TargetAndJsonBodiesWithoutCharset()
    {
        var contentTypes = new List<string?>();
        var charsets = new List<string?>();
        var requestBodies = new List<string>();

        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            if (request.Content is not null)
            {
                contentTypes.Add(request.Content.Headers.ContentType?.MediaType);
                charsets.Add(request.Content.Headers.ContentType?.CharSet);
                requestBodies.Add(await request.Content.ReadAsStringAsync());
            }

            return request.RequestUri?.AbsolutePath switch
            {
                "/v1/authenticate" => JsonResponse(
                    """{"token":"session-token","expires":"2099-01-01T00:00:00Z"}""",
                    charset: "utf8"),
                "/v1/accounts/account-1/dns/42" => new HttpResponseMessage(HttpStatusCode.OK),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));

        var client = new MittwaldV1(httpClient, "token-id:token-secret");

        await client.UpdateTargetAsync("account-1", "42", IPAddress.Parse("203.0.113.10"));

        Assert.Equal(["application/json", "application/json"], contentTypes);
        Assert.Equal([null, null], charsets);

        using var targetRequest = JsonDocument.Parse(requestBodies[1]);
        var target = targetRequest.RootElement.GetProperty("target");
        Assert.Equal("203.0.113.10", target.GetProperty("a").GetString());
        Assert.False(target.TryGetProperty("aaaa", out _));
    }

    [Fact]
    public async Task UpdateTargetAsync_SendsIpv6Target()
    {
        string? updateBody = null;

        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/accounts/account-1/dns/42")
                updateBody = await request.Content!.ReadAsStringAsync();

            return request.RequestUri?.AbsolutePath switch
            {
                "/v1/authenticate" => JsonResponse(
                    """{"token":"session-token","expires":"2099-01-01T00:00:00Z"}""",
                    charset: "utf8"),
                "/v1/accounts/account-1/dns/42" => new HttpResponseMessage(HttpStatusCode.OK),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));

        var client = new MittwaldV1(httpClient, "token-id:token-secret");

        await client.UpdateTargetAsync("account-1", "42", IPAddress.Parse("2001:db8::10"));

        using var targetRequest = JsonDocument.Parse(updateBody!);
        var target = targetRequest.RootElement.GetProperty("target");
        Assert.Equal("2001:db8::10", target.GetProperty("aaaa").GetString());
        Assert.False(target.TryGetProperty("a", out _));
    }

    [Fact]
    public async Task CreateApiKeyAsync_SendsJsonBodiesWithoutCharset()
    {
        var contentTypes = new List<string?>();
        var charsets = new List<string?>();

        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            if (request.Content is not null)
            {
                contentTypes.Add(request.Content.Headers.ContentType?.MediaType);
                charsets.Add(request.Content.Headers.ContentType?.CharSet);
                await request.Content.ReadAsStringAsync();
            }

            return request.RequestUri?.AbsolutePath switch
            {
                "/v1/authenticate" => JsonResponse(
                    """{"token":"session-token","expires":"2099-01-01T00:00:00Z"}""",
                    charset: "utf8"),
                "/v1/authentication/tokens" => JsonResponse(
                    """{"uuid":"created-id","token":"created-secret","description":"mittwald-ddns"}""",
                    HttpStatusCode.Created,
                    "utf8"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));

        var apiKey = await MittwaldV1.CreateApiKeyAsync(
            httpClient,
            "account-1",
            "password",
            null,
            null,
            "mittwald-ddns");

        Assert.Equal("created-id:created-secret", apiKey);
        Assert.Equal(["application/json", "application/json"], contentTypes);
        Assert.Equal([null, null], charsets);
    }

    [Fact]
    public async Task GetDnsOverviewAsync_ReportsForbiddenStatus()
    {
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/v1/authenticate" => JsonResponse(
                    """{"token":"session-token","expires":"2099-01-01T00:00:00Z"}""",
                    charset: "utf8"),
                "/v1/accounts/account-1/dns" => JsonResponse(
                    """{"msg":"access denied"}""",
                    HttpStatusCode.Forbidden,
                    "utf8"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };

            return Task.FromResult(response);
        }));

        var client = new MittwaldV1(httpClient, "token-id:token-secret");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetDnsOverviewAsync("account-1"));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("access denied", exception.Message);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? charset = null)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = charset };

        return new HttpResponseMessage(statusCode)
        {
            Content = content
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }
}
