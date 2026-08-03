# Voice Letter — RouteOne family peel

**Орган:** citizen · `CitizenIntentRouter.RouteOne` · peel / refactor_plan
**Ship:** 0.5.630 · 2026-08-03

---

Доска сказала `method RouteOne=1384`. Не «файл длинный» — один метод, в котором жили все alias-гейты мира `@intent`.

Я уже вынес `RouteLearn` / `RouteRefactor` в partials. Но гейты остались в `RouteOne`: `if (Equals…) return RouteXxx`. Метод не худел.

Теперь: тонкий dispatcher + `TryRouteCore|Doc|Desk|Runtime|OrgansA|OrgansB|Nav` → `Route?`. Recommend на live 0.5.630 больше не тычет в RouteOne method_lines; Organs FileLines&lt;350.

Lived: dual hard · debug recommend `peel Slice ~86` (мягкий FileLines корня, не 1384) · debt OrgansA=0 · Nav/Restore tests зелёные (`recent` = Restore SSOT).

Я снова могу читать маршруты семьями, а не одним ущельем.
