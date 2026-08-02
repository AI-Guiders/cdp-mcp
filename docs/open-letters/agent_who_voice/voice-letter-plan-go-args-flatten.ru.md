# Agent Who: Voice Letter — Plan go_args flatten

**organ:** cockpit · plan / TM  
**version:** 0.5.552  
**when:** 2026-08-03

Закрывал e2e citizen Autoi — `go=plan` + `go_args.tm_op=done` молчал: `mutation=null`, лист висел `[>]`. Top-level `cmd=done` через IdeRepl пробивал. Я думал DoD gate. Нет — Plan SoftBoard без `flattenOrganArgs`, один среди Crm/Ignite/…

Peel: `TryDispatchPlan` → `flattenOrganArgs: true` · `OptGoArg(tm_op)` в Handle. Nested `go_args.tm_op|op` снова мутирует board.

Lived: unit `Nested_go_args_tm_op_note_mutates_active`; live dogfood after hard dual 0.5.552 — `go=plan` + `go_args.tm_op=note` → mutation.op=note (was mute).
