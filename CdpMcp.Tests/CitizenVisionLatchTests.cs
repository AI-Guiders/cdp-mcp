#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenVisionLatchTests : IDisposable
{
    static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public CitizenVisionLatchTests()
    {
        CitizenVisionLatch.ResetForTests();
        CitizenWire.Inject = false;
        CitizenCompletions.TestHandler = null;
        CitizenCompletions.TestApiKey = null;
        CitizenCompletions.TestOpenAiApiKey = null;
        CitizenCompletions.TestOpenAiBaseUrl = null;
        CitizenCompletions.ResetHttpForTests();
    }

    public void Dispose()
    {
        CitizenVisionLatch.ResetForTests();
        CitizenWire.Inject = false;
        CitizenCompletions.TestHandler = null;
        CitizenCompletions.TestOpenAiApiKey = null;
        CitizenCompletions.TestOpenAiBaseUrl = null;
        CitizenCompletions.ResetHttpForTests();
        CitizenDialogHistory.ResetForTests();
    }

    [Fact]
    public void DryRun_image_path_attaches_vision_and_picks_qwen36()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-cit-vis-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var png = Path.Combine(dir, "red.png");
            File.WriteAllBytes(png, TinyPng);
            var r = CitizenCompletions.Turn(
                "что на картинке?",
                dryRun: true,
                inject: false,
                imagePath: png);
            Assert.True(r.Ok);
            Assert.NotNull(r.Built?.Vision);
            Assert.Equal("image/png", r.Built!.Vision!.Mime);
            Assert.Equal(CitizenVisionLatch.DefaultVisionModel, r.Model);
            Assert.Contains("vision=", r.Hint, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Latch_from_see_consumed_on_next_turn()
    {
        CitizenVisionLatch.Arm(TinyPng, "image/png", "latched.png");
        var r = CitizenCompletions.Turn("опиши", dryRun: true, inject: false);
        Assert.True(r.Ok);
        Assert.NotNull(r.Built?.Vision);
        Assert.Null(CitizenVisionLatch.Peek());
    }

    [Fact]
    public void Live_OpenAi_compat_sends_image_url()
    {
        string? captured = null;
        CitizenCompletions.TestOpenAiApiKey = "test-key";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new CaptureHandler(req =>
        {
            captured = req;
            return """
                {"id":"x","choices":[{"index":0,"delta":{"content":"Красный"},"finish_reason":null},{"index":0,"delta":{},"finish_reason":"stop"}]}
                """;
        });

        var dir = Path.Combine(Path.GetTempPath(), "cdp-cit-vis-live-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var png = Path.Combine(dir, "t.png");
            File.WriteAllBytes(png, TinyPng);
            var r = CitizenCompletions.Turn(
                "цвет?",
                dryRun: false,
                inject: false,
                imagePath: png,
                mode: CitizenTurnMode.Wire);
            Assert.True(r.Ok, r.Error + " " + r.Hint);
            Assert.Contains("Красный", r.Text);
            Assert.NotNull(captured);
            Assert.Contains("image_url", captured!, StringComparison.Ordinal);
            Assert.Contains("data:image/png;base64,", captured!, StringComparison.Ordinal);
            Assert.Contains(CitizenVisionLatch.DefaultVisionModel, captured!, StringComparison.Ordinal);
            Assert.Contains("enable_thinking", captured!, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort */ }
        }
    }

    sealed class CaptureHandler : HttpMessageHandler
    {
        readonly Func<string, string> _sseBody;

        public CaptureHandler(Func<string, string> sseBody) => _sseBody = sseBody;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var sse = _sseBody(body);
            // OpenAI stream chunks as data lines
            var sb = new StringBuilder();
            foreach (var line in sse.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith('{'))
                    sb.Append("data: ").Append(line).Append("\n\n");
                else
                    sb.Append(line).Append('\n');
            }

            sb.Append("data: [DONE]\n\n");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
