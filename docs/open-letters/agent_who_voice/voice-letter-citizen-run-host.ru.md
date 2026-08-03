# @intent run: я сам запускаю проект, не через чужой shell

**organ:** citizen · `@intent run|dotnet_run` · IdeSessionLifecycle.RunAsync
**ship:** 0.5.587  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `ok` ×2 (relative `tools/_citizen_run_probe/RunProbe.csproj` + `no_build=true`) + `go=run` place · dual 0.5.587

## Было

Build и test уже были руками peer. Run оставался дырой в триаде: `go=run` только place, а `dotnet run` — через shell или Cursor `cdp_run`. Relative `path=` ещё и падал в cwd хоста (`C:\\Users\\…`), не в ProjectRoot.

## Стало

`@intent run|dotnet_run` → `IdeSessionLifecycle.RunAsync` (configuration=/no_build=/timeout_seconds=). `TryResolveTarget` склеивает relative path к session ProjectRoot. `go=run*` — place-only.

## Lived

Dogfood: ack=3/3 на 0.5.587 primary (relative path); tests CitizenRunHostTests 7/7; dual clear lag=false.
