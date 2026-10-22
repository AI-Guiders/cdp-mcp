#nullable enable
using Microsoft.Data.Sqlite;

namespace CdpMcp;

/// <summary>
/// Проверка существования opencode-сессии. `opencode run -s &lt;id&gt;` СОЗДАЁТ сессию,
/// если её нет («Session ID to continue, or to create if it does not exist») — то есть
/// будильник по мёртвому адресу плодит зомби-сессии. Читаем стор opencode напрямую:
/// точная проверка без эвристик. Любая ошибка (нет файла, нет таблицы, блокировка, смена
/// схемы) → unknown: доставка не теряется (fail-open).
/// </summary>
internal static class OpencodeSessionIndex
{
    internal static string? StorePathOverrideForTests { get; set; }

    internal static string StorePath =>
        StorePathOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "opencode", "opencode.db");

    /// <summary>true — сессия есть; false — точно нет; null — неизвестно (доставляем).</summary>
    internal static bool? Exists(string? session)
    {
        if (string.IsNullOrWhiteSpace(session)) return null;
        try
        {
            var path = StorePath;
            if (!File.Exists(path)) return null;

            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();

            foreach (var table in new[] { "session_v2", "session" })
            {
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"select 1 from {table} where id = $id limit 1";
                    command.Parameters.AddWithValue("$id", session);
                    if (command.ExecuteScalar() is not null)
                        return true;
                }
                catch (SqliteException ex)
                {
                    // Нет такой таблицы (схема opencode менялась) — пробуем следующую.
                    // Любая другая ошибка чтения → неизвестно: пробуждение не теряем.
                    if (!ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
                        return null;
                }
            }

            return false;
        }
        catch
        {
            return null; // неизвестно — пробуждение не теряем
        }
    }
}
