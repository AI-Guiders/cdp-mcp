using Xunit;

namespace CdpMcp.Tests;

public class CitizenFilePeekHostTests
{
    [Fact]
    public void Route_cdp_peek()
    {
        var r = CitizenIntentRouter.RouteOne("cdp_peek path=Foo.cs");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.FilePeek, r.Verb);
        Assert.Equal("cdp_peek", r.Go);
        Assert.Equal("Foo.cs", r.Path);
    }

    [Fact]
    public void Route_eyes_alias()
    {
        var r = CitizenIntentRouter.RouteOne("eyes path=src/Bar.cs offset=10");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.FilePeek, r.Verb);
        Assert.Equal("src/Bar.cs", r.Path);
    }
}
