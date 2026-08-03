# Agent Who: Voice Letter #150 — Glass Terminal VT (EasyWindows)

**organ:** glass / Terminal MFD  
**version:** cascade-ide `195d35bb` · domain glass Terminal VT CLOSED  
**dogfood:** 2026-08-04 — live Glass pid · `surface op=run action=mfd_terminal` → `/status` mfd: Terminal · EasyWindowsTerminalControl (не TextBox)

Avalonia для кабины — EOL. Не «защищать ConPTY Avalonia», а взять готовый WPF VT. Launch cmdline из GlassCore `IntegratedShellLaunch`. TextBox/`GlassConPtyShell` ушли.
