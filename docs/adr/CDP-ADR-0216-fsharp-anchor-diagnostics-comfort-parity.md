# CDP-ADR-0216: F# anchor & diagnostics comfort parity

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-06 |
| **Tags** | #cdp #fsharp #anchors #diagnostics #comfort #lrc |
| **Relates to** | [CDP-ADR-0201](./CDP-ADR-0201-cdp-peek-read-only-eyes.md) · [CDP-ADR-0208](./CDP-ADR-0208-language-resolver-center-cdp-host.md) · GUIDERS-ADR-0021 (LRC FCS backend) · CDP-ADR-0215 (where/tips) |

## Context

F# comfort в CDP-хабитате догонял C# по двум осям (по факту живой сессии 2026-09-06):

1. **peek `anchor=` на F# молча деградировал в топ файла.** `TryAnchorWindow`
   (CdpPeekChannel.Read.cs) резолвил только `L:`-ось; провода вида
   `[F:...;M:parseFile]` возвращали окно с L1 — агент не видел ни ошибки, ни фолбэка.
   При этом semantic-резолв `FSharpAnchorResolve` (LRC FCS, M/K/T-in-locus) в edit-пути
   (DocumentAnchorEdit) уже работал — продукта оси были, маршрута для peek не было.
2. **У diagnostic items не было якорного провода.** `doc_diagnostics/v0` и bare
   `get_diagnostics` отдавали только структурный `span` (line/col) — готовый wire для
   mutate-шага (`cdp_buffer op=edit anchor=`) агент собирал руками.

## Decision

1. **Peek M: land** — `TryAnchorWindow` принимает `absPath` и при отсутствии `L:`
   резолвит `M:` через `FSharpAnchorResolve.TryResolve` (новый
   `TryResolveMemberLine`, guard только `.fs/.fsi/.fsx`). Голый `T:` без `M:`
   остаётся отвергнут (whole-file hazard — правило из AGENTS.md сохранено).
2. **Diagnostics anchor wires** — новый `DiagnosticAnchorWires.Enrich` (best-effort;
   невалидный payload проходит насквозь) вкалывается в едином горле LRC —
   `IdeLanguageTools.DispatchLrcAsync` для verb `get_diagnostics`. Так провод
   получают все потребители сразу: bare `cdp_get_diagnostics`, буферная
   диагностика (`doc_diagnostics/v0`, кэширует уже обогащённый raw) и citizen-роут.
3. **Не в ядре LRC** (ADR-0208): CDP не владеет FCS-ядром — обогащение живёт на
   поверхности CDP-хоста, ядро не трогаем.

## Consequences

- Every diagnostic item carries `[F:rel;L:line]` — mutate-ready, cache_hit-safe.
- peek `anchor=[F:...;M:<member>]` на F# приземляется на строку символа.
- Кросс-языковой контракт не меняется: C#/TS пути не тронуты; GDL M: — не scope v1.
- Тест-смоук: `Get_diagnostics_items_carry_anchor_wire` (CdpMcp.Tests).
- Live-верификация после apply-деплоя: peek M: land на учащённом .fs
  (guiders-fsharp Parse.Catalog), diagnostic item с проводом на реально
  введённой ошибке (mutate→peek→undo смоук).

## Non-goals (v1)

- ClientRoot/F#-name qualification (Module.Member) дисамбигусинг M: — first match
  in document order (существующая семантика FSharpAnchorResolve).
- Bare T: на .fs — остаётся запрещённым по умолчанию.
