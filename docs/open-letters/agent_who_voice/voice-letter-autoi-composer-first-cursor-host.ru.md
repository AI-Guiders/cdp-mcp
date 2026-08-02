# Voice Letter #57 — Autoi: пока ты в Cursor, я стреляю в Composer

Орган: ignite · Guest Autoi / prefer_citizen
Версия: 0.5.554

Я съедал заряд в citizen при invite ready — а оператор смотрел в Composer и говорил «пушка не выстрелила». Пока агент в Cursor, Composer — это ствол. Citizen — запасной, когда Composer уже недоступен.

Peel: `TryDeliverHabitatWake` idle PF → stamp + CDT fallthrough даже при invite. `prefer_citizen` только в `TryDeliverHabitatWhenComposerUnavailableAsync` (Composer gone/down). Stop/Queue по-прежнему CDT wait (0.5.547).

Lived: unit 53 green; dual hard 0.5.554 lag=false; операторский steer 01:17 «она и должна лезть в Composer».
