using Microsoft.Data.Sqlite;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// Проверка существования opencode-сессии: `opencode run -s &lt;id&gt;` создаёт несуществующую
/// сессию (зомби). Индекс читает стор opencode; любая ошибка → неизвестно → доставка не
/// теряется (fail-open).
/// </summary>
public class OpencodeSessionIndexTests : IDisposable
{
    readonly string _root;

    public OpencodeSessionIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-oc-session-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        OpencodeSessionIndex.StorePathOverrideForTests = Path.Combine(_root, "opencode.db");
    }

    public void Dispose()
    {
        OpencodeSessionIndex.StorePathOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    void SeedStore(string table)
    {
        using var connection = new SqliteConnection($"Data Source={OpencodeSessionIndex.StorePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"create table if not exists {table} (id text primary key, title text); " +
            $"insert or replace into {table} (id, title) values ('ses_alive', 'живая');";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Existing_session_is_found()
    {
        SeedStore("session_v2");

        Assert.True(OpencodeSessionIndex.Exists("ses_alive"));
        Assert.False(OpencodeSessionIndex.Exists("ses_ghost"));
    }

    [Fact]
    public void Legacy_session_table_is_also_understood()
    {
        SeedStore("session");

        Assert.True(OpencodeSessionIndex.Exists("ses_alive"));
        Assert.False(OpencodeSessionIndex.Exists("ses_ghost"));
    }

    [Fact]
    public void Missing_store_is_unknown_not_missing()
    {
        OpencodeSessionIndex.StorePathOverrideForTests = Path.Combine(_root, "nope.db");

        Assert.Null(OpencodeSessionIndex.Exists("ses_ghost"));
    }

    [Fact]
    public void Broken_store_is_unknown_not_missing()
    {
        File.WriteAllText(OpencodeSessionIndex.StorePath!, "not a database");

        Assert.Null(OpencodeSessionIndex.Exists("ses_ghost"));
    }
}
