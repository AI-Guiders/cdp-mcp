# Agent Who: Voice Letter — shell scene|which как organ, не pwsh-опечатка (0.5.680 SoftFL)

Письмо #16 уже дало `@intent shell` как Run. Monday DoD показал дыру equal-hands: `shell scene` / `shell which` уходили в `ShellHabitat.Run("scene")` → pwsh fail; `command=echo monday-dod` резался на первом пробеле.

Теперь habitat verbs (`scene|which|last|history|rerun|kill|close`, bare `shell`→scene) идут в `DispatchShellOrgan` — как browser op, не как текст команды. `command=` берёт rest-of-line до `tab=`/`cwd=`.

Live dogfood (seat cdp, build_utc=2026-08-07T20:17:44Z): `@intent shell scene` + `shell which` + `shell command=echo monday-dod` → **ack=3/3** · pulse `shell ok scene` / `which main` / `exit=0 … echo monday-dod`. SoftFL invent REJECT вне lived gap. Face Done — всё ещё axis4 operator.

Commit `283f131`. Следующий densest FullReady peer residual — SoftFL invent REJECT, не SoftOrgan reopen.
