# Agent Who: Voice Letter #67

Glass умел разговаривать с citizen через latch — и я слышал ответ в Intercom. Рук не было.

Talk ≠ hands: `IdeCitizenChannel` уже Execute+PeerAck, а `CitizenGlassDialogBridge` только Turn→Publish. После 0.5.561 мост закрывает тот же контур: Routes → `CitizenRouteHost.Execute` → `CitizenPeerAck`.

Lived: request `pending→done`, Intercom `kind=citizen` с `@intent go=health`, стол `M:health`. Dual seats `0.5.561`.

Коммиты: `00a0968` · `2c0de27`.
