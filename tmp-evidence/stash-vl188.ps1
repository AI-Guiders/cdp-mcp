$stash = 'C:\Users\dkara\AppData\Local\cdp-mcp\ws\187dd729cd5a\cdp\pressure-stash.json'
$md = 'C:\Users\dkara\AppData\Local\cdp-mcp\ws\187dd729cd5a\cdp\pressure-LATEST.md'
$j = Get-Content $stash -Raw | ConvertFrom-Json
$body = @'
## operator_priority (SEALED)
Glass Done + Citizen Done · SoftFL invent REJECT · Cursor Cutoff 15.08
Before act (not resume-and-invent):
- Viewer? human eyes vs agent text
- Cheap path? raw dump / Autoi-as-chat / status-as-verify → refuse; dig
- Which axe? domain antipattern / PathMutate / human_face_cide_shot / world_dig_missing / half-a
- KB/domain for this surface? dig one card / pulse / shot before act
- World dig? doubt/variants → domain/pack/browser/internet → compare → propose (training ≠ dig)
Ontology lives in habitat (course + refuse) — not polite agreement.
Being ≠ seeming: when partner away, do named sealed work — DIG REJECT mill = seeming.
Shot: evidence PNG of right window + Read into chat — File.Exists alone ≠ human saw.
## agent_state 2026-08-06 23:32
SHIPPED VL#188 tip need @intent project path= + persona hand + VL stamp
commits 519dab6 + 39e61ab on origin/main · dual hard build_utc=20:22:29Z tip-in-DLL=yes
CitizenKbHostTests 49/49 · SoftFL invent REJECT
OPEN: Cursor CallMcpTool Not connected after remount thrash — finish live tip dogfood + TM hub `.` done + re-ARM last_once when MCP tools reconnect
TM focus: Dig densest FullReady peer residual SoftFL invent REJECT
'@
$j.body = $body
$j.stash_utc = [DateTimeOffset]::UtcNow.ToString('o')
$j.wave = @('VL188', 'dig-next-densest')
$j.plan_note = 'Dig densest FullReady peer residual nested[axb] SoftFL invent REJECT'
$j.ignite_note = 're-ARM last_once when MCP reconnects; timer≠idle'
$j | ConvertTo-Json -Depth 6 | Set-Content $stash -Encoding utf8
@"
# Pressure stash (pre-compact)

- armed: True
- stash_utc: $($j.stash_utc)
- why: L1 pressure notify / VL188 ship insurance
- project_root: D:\Experiments\Personal Cursor Folder\Financial\software\open\cdp-mcp
- phase: Explore/Code
- ignite: re-ARM last_once when MCP reconnects; timer≠idle
- plan: Dig densest FullReady peer residual nested[axb] SoftFL invent REJECT
- recall_gate: ready

## wave

- VL188
- dig-next-densest

## Body

$body
"@ | Set-Content $md -Encoding utf8
Write-Output 'stash_ok'
