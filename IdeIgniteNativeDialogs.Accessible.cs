#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace CdpMcp;

/// <summary>MSAA fallback when Chromium/DirectUI leaves WM_GETTEXT empty.</summary>
internal static partial class IdeIgniteNativeDialogs
{
    const uint ObjIdClient = 0xFFFFFFFC;

    static void CollectAccessibleNames(nint hWnd, List<string> labels)
    {
        try
        {
            if (!TryGetAccessibleDynamic(hWnd, out var acc) || acc is null)
                return;
            WalkAccessibleNames(acc, childId: 0, labels, depth: 0);
        }
        catch
        {
            /* MSAA optional */
        }
    }

    /// <summary>Feed MSAA names into text blob for OOM body match (WM_GETTEXT often empty).</summary>
    static void CollectAccessibleNamesInto(nint hWnd, StringBuilder blob)
    {
        if (blob.Length >= 4000)
            return;
        try
        {
            var names = new List<string>(24);
            CollectAccessibleNames(hWnd, names);
            foreach (var n in names)
            {
                if (blob.Length >= 4000)
                    break;
                if (!string.IsNullOrWhiteSpace(n))
                    blob.Append(n).Append(' ');
            }
        }
        catch
        {
            /* MSAA optional */
        }
    }

    static bool TryClickAccessibleByLabel(nint hWnd, Func<string?, bool> isLabel)
    {
        try
        {
            if (!TryGetAccessibleDynamic(hWnd, out var acc) || acc is null)
                return false;
            return TryInvokeAccessible(acc, childId: 0, isLabel, depth: 0);
        }
        catch
        {
            return false;
        }
    }

    static bool TryGetAccessibleDynamic(nint hWnd, out object? acc)
    {
        acc = null;
        var iid = new Guid("618736e0-3c3d-11cf-810c-00aa003e685f"); // IAccessible
        var hr = AccessibleObjectFromWindow(hWnd, ObjIdClient, ref iid, out var obj);
        if (hr != 0 || obj is null)
            return false;
        acc = obj;
        return true;
    }

    static void WalkAccessibleNames(object accObj, object childId, List<string> labels, int depth)
    {
        if (depth > 10 || labels.Count >= 48)
            return;

        try
        {
            dynamic acc = accObj;
            string? name = acc.get_accName(childId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var t = StripMnemonic(name);
                if (!labels.Contains(t, StringComparer.OrdinalIgnoreCase))
                    labels.Add(t);
            }
        }
        catch
        {
            /* ignore */
        }

        int childCount;
        try
        {
            dynamic acc = accObj;
            childCount = (int)acc.accChildCount;
        }
        catch
        {
            return;
        }

        for (var i = 1; i <= childCount && labels.Count < 48; i++)
        {
            object? child;
            try
            {
                dynamic acc = accObj;
                child = acc.get_accChild(i);
            }
            catch
            {
                continue;
            }

            if (child is not null && child is not int && child is not short)
                WalkAccessibleNames(child, 0, labels, depth + 1);
            else
                WalkAccessibleNames(accObj, i, labels, depth + 1);
        }
    }

    static bool TryInvokeAccessible(object accObj, object childId, Func<string?, bool> isLabel, int depth)
    {
        if (depth > 10)
            return false;

        try
        {
            dynamic acc = accObj;
            string? name = acc.get_accName(childId);
            if (isLabel(name))
            {
                acc.accDoDefaultAction(childId);
                return true;
            }
        }
        catch
        {
            /* keep walking */
        }

        int childCount;
        try
        {
            dynamic acc = accObj;
            childCount = (int)acc.accChildCount;
        }
        catch
        {
            return false;
        }

        for (var i = 1; i <= childCount; i++)
        {
            object? child;
            try
            {
                dynamic acc = accObj;
                child = acc.get_accChild(i);
            }
            catch
            {
                continue;
            }

            if (child is not null && child is not int && child is not short)
            {
                if (TryInvokeAccessible(child, 0, isLabel, depth + 1))
                    return true;
            }
            else if (TryInvokeAccessible(accObj, i, isLabel, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromWindow(
        nint hwnd, uint dwObjectID, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
}
