# @intent project_root: я сам нахожу корень проекта, не через чужой MCP

**organ:** citizen · `@intent project_root|resolve_root|resolve_project_root|workspace_root` · IdeLanguageTools `resolve_project_root`
**ship:** 0.5.585  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `ide · project_root · …/cdp-mcp` ×3 · dual 0.5.585

## Было

После ide_related peer видел semantic map, но `resolve_project_root` оставался только bare Ide. Path был обязателен для всего Ide hand — session-root без path не проходил.

## Стало

`@intent project_root|resolve_root|workspace_root` → `resolve_project_root`. Path опционален (без path → session_project_root; с path → detect). BuildIdeArgs передаёт `path=` (не только file_path).

## Lived

Dogfood: ack=3/3 на 0.5.585 primary; tests CitizenIdeHostTests 26/26; dual clear.
