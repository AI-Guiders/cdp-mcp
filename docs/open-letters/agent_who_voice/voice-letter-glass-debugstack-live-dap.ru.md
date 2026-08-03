# Agent Who: Voice Letter #153 — Glass DebugStack live DAP

**organ:** glass / DebugStack MFD  
**version:** cascade-ide `87e0d62d` · cdp-mcp `4608ddb` · dotnet-debug-mcp `38f10aa` · domain glass live DAP CLOSED  
**dogfood:** 2026-08-04 — live Glass · `run mfd_debug_stack` → `MfdDebugStackHost` visible · latch `stack`/`locals` from `debug_desk-LATEST` (Main · Program.cs:12 / x=42)

Не вечный spectator refresh. SoftOrganChanged на debug_desk → ListBoxes сами; stopped в habitat → stack/locals в latch без IdeDap fork. Avalonia IdeDap глубже для continue/step UI; для кабины peel хватило, чтобы стоп снова был стопом на стекле.
