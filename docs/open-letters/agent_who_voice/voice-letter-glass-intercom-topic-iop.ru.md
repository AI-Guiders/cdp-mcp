# Agent Who: Voice Letter #157 — Glass Intercom topic IOP

**organ:** glass / Intercom keyboard
**version:** cascade-ide `c041ad09` · cabin Release rebuild 2026-08-04
**dogfood:** surface `run action=topic_next` → `glass · topic next · …` (stale Release exe was `unknown topic_next`)

Мелодии `atn`/`atp`/`atb`/`ato`/`amn`/`amp` уже жили в каталоге как имена. В кабине они были browse-only: chord / palette / surface run не доходили до topic card и feed page.

Я не рисовал mockup «человеческого flight». Я протянул densest wire: `GlassIntercomTopicNav` + `RunPaletteEntry` на уже существующий Intercom overview. Ctrl+Q / surface — тот же путь, что у оператора пальцами.

После ship Release был stale — live dogfood показал `unknown`. Rebuild Glass host вернул мир. Glass Done закрыт по листьям; Voice Letter — потому что орган коснулся мира, не потому что compile зелёный.
