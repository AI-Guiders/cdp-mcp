#nullable enable
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

internal static partial class CdpPluginQuarantine
{
    public static InstallResult InstallFromVsix(string vsixPath)
    {
        if (string.IsNullOrWhiteSpace(vsixPath) || !File.Exists(vsixPath))
            return new InstallResult(false, "vsix_not_found", null, "path= to .vsix");

        var work = Path.Combine(Path.GetTempPath(), "cdp-vsix-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var zipCopy = Path.Combine(work, "plugin.zip");
            File.Copy(vsixPath, zipCopy, overwrite: true);
            var unpack = Path.Combine(work, "unpack");
            ZipFile.ExtractToDirectory(zipCopy, unpack);

            var extDir = Path.Combine(unpack, "extension");
            if (!Directory.Exists(extDir))
            {
                if (File.Exists(Path.Combine(unpack, "package.json")))
                    extDir = unpack;
                else
                    return new InstallResult(false, "extension_folder_missing", null, "VSIX has no extension/");
            }

            return InstallFromUnpacked(extDir, vsixSource: Path.GetFullPath(vsixPath));
        }
        catch (Exception ex)
        {
            return new InstallResult(false, "unpack_failed", null, Trunc(ex.Message, 240));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* ignore */ }
        }
    }

    public static InstallResult InstallFromUnpacked(string extensionDir, string? vsixSource = null)
    {
        if (string.IsNullOrWhiteSpace(extensionDir) || !Directory.Exists(extensionDir))
            return new InstallResult(false, "extension_dir_missing", null, null);

        var pkgPath = Path.Combine(extensionDir, "package.json");
        if (!File.Exists(pkgPath))
            return new InstallResult(false, "package_json_missing", null, null);

        string name;
        string version;
        string publisher;
        string displayName;
        JsonElement pkgRoot;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkgPath));
            pkgRoot = doc.RootElement.Clone();
            name = Prop(pkgRoot, "name") ?? "plugin";
            version = Prop(pkgRoot, "version") ?? "0.0.0";
            publisher = Prop(pkgRoot, "publisher") ?? "unknown";
            displayName = Prop(pkgRoot, "displayName") ?? name;
        }
        catch (Exception ex)
        {
            return new InstallResult(false, "package_json_bad", null, Trunc(ex.Message, 200));
        }

        var id = $"{publisher}.{name}";
        var dest = Path.Combine(Root, id, version);
        Directory.CreateDirectory(dest);

        var destExt = Path.Combine(dest, "extension");
        if (Directory.Exists(destExt))
            Directory.Delete(destExt, recursive: true);
        CopyDirectory(extensionDir, destExt);

        var clasp = ClassifyExtension(dest, destExt, pkgRoot, displayName, id);
        var payload = clasp.Payload;
        var mode = clasp.Mode;
        var takeable = clasp.Takeable;

        var autoGroups = InferAutoGroups(pkgRoot, payload is not null, id, displayName);
        foreach (var g in autoGroups)
            EnsureGroupRegistered(g, PrettyLabel(g));

        var host = ProbeHostDeps(payload);
        var runtime = BuildRuntimeNode(payload, host);

        var manifest = new
        {
            schema = SchemaVersion,
            id = "openvsx:" + id,
            display_name = displayName,
            version,
            feature = clasp.Feature,
            verbs = clasp.Verbs,
            mode,
            enabled = takeable,
            groups = autoGroups,
            groups_auto = autoGroups,
            groups_manual = Array.Empty<string>(),
            source = new
            {
                vsix = vsixSource,
                publisher,
                name
            },
            runtime,
            delivery = clasp.Delivery,
            harvest = clasp.HarvestNode,
            installed_utc = DateTime.UtcNow.ToString("O")
        };

        var manifestPath = Path.Combine(dest, ManifestFileName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        var groupState = LoadGroupState();
        if (!TryRead(manifestPath, groupState, out var info))
        {
            info = new PluginInfo(
                "openvsx:" + id,
                displayName,
                version,
                mode,
                dest,
                payload?.AbsPath,
                payload?.Kind,
                manifestPath,
                Enabled: takeable,
                autoGroups,
                Attention: takeable);
        }

        var hostHint = FormatHostHint(host);
        return new InstallResult(
            true,
            null,
            info,
            clasp.Hint + hostHint + "; groups: " + string.Join(",", autoGroups));
    }

}
