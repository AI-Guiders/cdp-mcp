using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeCockpitShowFaceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShowFaceFromArgs_reads_boolean(bool value)
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["show_face"] = JsonSerializer.SerializeToElement(value)
        };
        Assert.Equal(value, IdeCockpit.ShowFaceFromArgs(args));
    }

    [Fact]
    public void ShowFaceFromArgs_plan_go_publishes_face_seat_p()
    {
        CideSeatsLatch.RootOverrideForTests = Path.Combine(Path.GetTempPath(), "cdp-showface-" + Guid.NewGuid().ToString("N"));
        try
        {
            var args = new Dictionary<string, JsonElement>
            {
                ["show_face"] = JsonSerializer.SerializeToElement(true)
            };
            IdeDeskSeats.Clear();
            IdeCockpit.PlaceOrganIfSeatsForTests(args, "plan");

            var latch = CideSeatsLatch.TryRead();
            Assert.NotNull(latch);
            Assert.True(latch!.ShowFace);
            Assert.Equal("p", latch.FaceSeat);
            Assert.Null(latch.MfdPage);
        }
        finally
        {
            CideSeatsLatch.RootOverrideForTests = null;
        }
    }
}
