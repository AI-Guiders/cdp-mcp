#nullable enable
using System.Net;
using System.Net.Http.Headers;
using CdpMcpBridge;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpBridgeTenantHeadersHandlerTests
{
    [Fact]
    public async Task SendAsync_adds_bearer_authorization_to_outbound_request()
    {
        var state = new CdpBridgeTenantHeadersState { BridgeSessionId = "bridge-test" };
        var capture = new CaptureHandler(HttpStatusCode.OK);
        var handler = new CdpBridgeTenantHeadersHandler(
            state,
            new Uri("http://127.0.0.1:8771/"),
            "bridge-secret-token")
        {
            InnerHandler = capture
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:8771/api/v1/cdp/capabilities");
        await invoker.SendAsync(request, CancellationToken.None);

        Assert.NotNull(capture.Request);
        Assert.Equal("Bearer", capture.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("bridge-secret-token", capture.Request.Headers.Authorization.Parameter);
    }

    sealed class CaptureHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
