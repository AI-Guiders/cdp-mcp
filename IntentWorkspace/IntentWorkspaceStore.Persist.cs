using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    /// <summary>Existing WitDB may predate desk_seats — EnsureCreated won't alter; CREATE IF NOT EXISTS.</summary>
    public void EnsureDeskSeatsTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS desk_seats (
                    Seat TEXT NOT NULL PRIMARY KEY,
                    Organ TEXT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """);
        });
    }

    /// <summary>Existing WitDB may predate Stage.PhaseAffinity — ALTER if missing.</summary>
    public void EnsureStagePhaseAffinityColumn()
    {
        WithDb(db =>
        {
            try
            {
                db.Database.ExecuteSqlRaw(
                    "ALTER TABLE stages ADD COLUMN PhaseAffinity TEXT NULL;");
            }
            catch
            {
                // column already exists
            }
        });
    }

    /// <summary>Existing WitDB may predate stage wall clock — ALTER if missing.</summary>
    public void EnsureStageClockColumns()
    {
        WithDb(db =>
        {
            foreach (var sql in new[]
                     {
                         "ALTER TABLE stages ADD COLUMN StartedUtc TEXT NULL;",
                         "ALTER TABLE stages ADD COLUMN CompletedUtc TEXT NULL;"
                     })
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch { /* column already exists */ }
            }
        });
    }

    public void DeskSeatsSave(IReadOnlyDictionary<string, string?> seats)
    {
        var now = DateTimeOffset.UtcNow;
        WithDb(db =>
        {
            foreach (var (seat, organ) in seats)
            {
                var row = db.DeskSeats.Find(seat);
                if (row is null)
                {
                    db.DeskSeats.Add(new DeskSeatEntity
                    {
                        Seat = seat,
                        Organ = string.IsNullOrWhiteSpace(organ) ? null : organ.Trim(),
                        UpdatedUtc = now
                    });
                }
                else
                {
                    row.Organ = string.IsNullOrWhiteSpace(organ) ? null : organ.Trim();
                    row.UpdatedUtc = now;
                }
            }

            db.SaveChanges();
        });
    }

    /// <returns>false if table empty / never saved.</returns>
    public bool DeskSeatsTryLoad(IDictionary<string, string?> into)
    {
        return WithDb(db =>
        {
            var rows = db.DeskSeats.AsNoTracking().ToList();
            if (rows.Count == 0)
                return false;
            foreach (var key in into.Keys.ToList())
                into[key] = null;
            foreach (var row in rows)
            {
                if (into.ContainsKey(row.Seat))
                    into[row.Seat] = string.IsNullOrWhiteSpace(row.Organ) ? null : row.Organ.Trim();
            }

            return true;
        });
    }
}
