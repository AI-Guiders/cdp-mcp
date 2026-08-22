using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class FindInFilesSessionRootTests
{
    [Fact]
    public void SessionSearchRoot_prefers_scm_over_project()
    {
        var session = new SessionContext
        {
            ScmRoot = Environment.CurrentDirectory,
            ProjectRoot = Path.GetTempPath()
        };

        Assert.Equal(Environment.CurrentDirectory, FindInFiles.SessionSearchRoot(session));
    }

    [Fact]
    public void SessionSearchRoot_falls_back_to_project()
    {
        var session = new SessionContext { ProjectRoot = Environment.CurrentDirectory };
        Assert.Equal(Environment.CurrentDirectory, FindInFiles.SessionSearchRoot(session));
    }
}
