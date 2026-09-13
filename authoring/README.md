# CDP deploy catalog (GDL)

SSOT for REPL/CCL/Citizen deploy heads per [GUIDERS-ADR-0053](https://github.com/AI-Guiders/guiders-platform/blob/main/docs/adr/GUIDERS-ADR-0053-planet-responsibilities.md) / [0065](https://github.com/AI-Guiders/guiders-platform/blob/main/docs/adr/GUIDERS-ADR-0065-gdl-emit-operational-paths.md).

## Layout

```text
authoring/
  cdp-mcp.gdlproj
  catalog/deploy.catalog.gdl   # declare SSOT
Cdp.Deploy/Generated/
  CdpDeployCatalog.g.cs        # regen-owned (check-in)
  CdpDeployCatalog.User.cs     # planet partial (go + default mode)
```

## Regen

```powershell
dotnet build Cdp.Deploy/Cdp.Deploy.csproj -p:GdlEmitForce=true
# or
dotnet exec ..\authoring-toolchain\src\Gdlc.Cli\bin\Release\net10.0\gdlc.dll emit --lang=cs `
  authoring/catalog/deploy.catalog.gdl --namespace Cdp.Deploy.Generated --class CdpDeployCatalog `
  > Cdp.Deploy/Generated/CdpDeployCatalog.g.cs
```

## v1 scope

Commands: `deploy`, `hard_deploy`, `soft_deploy` only. Mode/target/dry_run arg parsing stays in `CdpDeployReplParser` until `CdpDeployCatalogResolver` lands (CommandPlane + Notations).
