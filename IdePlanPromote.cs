#nullable enable
using System.Text;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Plan inbox markdown + confirm/reject. Prefer calling via <see cref="IdeShare.SharePlan"/>
/// (<c>share with=operator what=plan ask=confirm</c>); <c>promote</c> remains an alias.
/// Partials: Ops (promote/confirm), Markdown, Persist, Models.
/// </summary>
internal static partial class IdePlanPromote
{
    public const string SchemaVersion = "plan_promote/v0";
    public const string Awaiting = "awaiting_confirm";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
}
