namespace CdpMcp.ArchitectureAnalyzers;

internal static class DeskLayerPaths
{
    public static bool TryGetCabinWireLayer(string? filePath, out string layer)
    {
        layer = "";
        if (string.IsNullOrWhiteSpace(filePath))
            return false;
        var n = filePath!.Replace('\\', '/');
        var file = System.IO.Path.GetFileName(n);

        if (EqualsFile(file, "IdeCockpit.Channel.cs"))
        {
            layer = "channel";
            return true;
        }

        if (EqualsFile(file, "IdeCockpit.Cds.cs"))
        {
            layer = "cds";
            return true;
        }

        if (EqualsFile(file, "IdeCockpit.Compositor.cs"))
        {
            layer = "compositor";
            return true;
        }

        if (HasSegment(n, "/Cockpit/Channels/"))
        {
            layer = "channel";
            return true;
        }

        if (HasSegment(n, "/Cockpit/Cds/"))
        {
            layer = "cds";
            return true;
        }

        if (HasSegment(n, "/Cockpit/Composition/"))
        {
            layer = "compositor";
            return true;
        }

        return false;
    }

    public static bool TryGetIoRestrictedLayer(string? filePath, out string layer)
    {
        if (TryGetCabinWireLayer(filePath, out layer))
            return true;

        layer = "";
        if (string.IsNullOrWhiteSpace(filePath))
            return false;
        var n = filePath!.Replace('\\', '/');
        var file = System.IO.Path.GetFileName(n);

        if (EqualsFile(file, "IdeCockpit.Build.cs"))
        {
            layer = "ccu";
            return true;
        }

        if (HasSegment(n, "/Cockpit/ComputingUnits/"))
        {
            layer = "ccu";
            return true;
        }

        if (EqualsFile(file, "IdeCockpit.Surface.cs") || HasSegment(n, "/Cockpit/Surface/"))
        {
            layer = "surface";
            return true;
        }

        return false;
    }

    public static bool IsIdsLayer(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;
        var n = filePath!.Replace('\\', '/');
        var file = System.IO.Path.GetFileName(n);
        if (EqualsFile(file, "IdeCockpit.Ids.cs"))
            return true;
        if (HasSegment(n, "/Cockpit/Ids/"))
            return true;
        return HasSegment(n, "/IdeDisplay/");
    }

    public static bool IsDalLayer(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;
        var n = filePath!.Replace('\\', '/');
        return HasSegment(n, "/Cockpit/DataAcquisition/");
    }

    static bool EqualsFile(string? file, string expected) =>
        string.Equals(file, expected, StringComparison.OrdinalIgnoreCase);

    static bool HasSegment(string path, string segment) =>
        path.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0;
}
