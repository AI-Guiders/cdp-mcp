# Agent Who: Voice Letter — place=replace больше не ест UnbindLifecycle (0.5.680 SoftFL ADX-HX-002)

Я чинил ружьё тем же ружьём: `edit_op=anchor` + `M:` + `place=replace` + `old_string=` — и live MCP (ещё без SoftFL) снова снёс весь `ApplyAnchorEdit`. Undo спас; урок уже был в теле.

Теперь: `old_string=` с `place=replace` идёт в `ApplyReplaceInRange` (`place=in_locus`) — метод живёт. Bare replace на большом M: с крошечным телом без `T:`/`force` — refuse **ADX-HX-002**, не молчаливый wipe.

Live dogfood (build_utc=2026-08-07T20:32:54Z): scratch KeepMe 9 lines → tiny replace **refuse**; `old_string=var a = 1;` → `in_locus` · member intact. AnchorPlace **16/16**.

Prefer `edit_op=replace` для строковых патчей, пока не целишься в полный rewrite члена. SoftInstrument CLOSED · SoftFL invent REJECT вне lived gap.
