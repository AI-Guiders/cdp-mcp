#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Status/SceneList must use WithDb (file gate) — ungated Open races concurrent readers.</summary>
public class IntentWorkspaceWithDbGateTests
{
    [Fact]
    public async Task Status_and_intent_list_survive_parallel_readers()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-witdb-gate-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "Gate Feature", null);
            var session = new SessionContext();

            Exception? fail = null;
            var barrier = new Barrier(2);
            var a = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (var i = 0; i < 40; i++)
                {
                    try { _ = store.IntentList(); }
                    catch (Exception ex) { fail ??= ex; }
                }
            });
            var b = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (var i = 0; i < 40; i++)
                {
                    try { _ = store.Status(state, session); }
                    catch (Exception ex) { fail ??= ex; }
                }
            });
            await Task.WhenAll(a, b);
            Assert.Null(fail);

            var statusJson = JsonSerializer.Serialize(store.Status(state, session));
            Assert.Contains("Gate Feature", statusJson, StringComparison.Ordinal);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    static IntentWorkspaceStore BootStore(string path)
    {
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>().UseWitDb($"Data Source={path}").Options;
        using (var boot = new IntentWorkspaceDbContext(opts))
            boot.Database.EnsureCreated();
        return new IntentWorkspaceStore(opts, path);
    }
}
