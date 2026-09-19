#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace CdpMcp.GraphQl;

/// <summary>Portal-style LIKE → rg regex (ADR-0233). Not MSSQL EF.Functions.Like.</summary>
public static class LikeToRg
{
    public static string Translate(string likePattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(likePattern);
        var sb = new StringBuilder(likePattern.Length * 2);
        sb.Append('^');
        foreach (var ch in likePattern)
        {
            switch (ch)
            {
                case '%':
                    sb.Append(".*");
                    break;
                case '_':
                    sb.Append('.');
                    break;
                default:
                    sb.Append(Regex.Escape(ch.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}
