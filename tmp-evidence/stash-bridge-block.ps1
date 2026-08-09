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
## agent_state 2026-08-06 23:45
SHIPPED VL#188 tip need @intent project path= (519dab6+39e61ab) · dual 20:22:29Z · tip Fact green
BLOCKER env: CallMcpTool Not connected while CdpMcp.exe up + GetMcpTools ready (Cursor host bridge)
DIG: ops antipattern — do NOT KillRunning every Not connected; prefer Recover -SoftFirst first (domain ops.md)
OPEN when CallMcpTool reconnects: live write_card tip dogfood · TM hub `.` done · dig densest SoftFL-safe residual · cdp_ignite last_once
TM focus: Dig densest FullReady peer residual SoftFL invent REJECT
'@
$j.body = $body
$j.stash_utc = [DateTimeOffset]::UtcNow.ToString('o')
$j.wave = @('VL188', 'mcp-calltool-bridge', 'dig-next-densest')
$j.plan_note = 'Dig densest FullReady peer residual nested[axb] SoftFL invent REJECT'
$j.ignite_note = 're-ARM last_once via cdp_ignite when MCP CallTool reconnects; SoftFirst before kill'
$j | ConvertTo-Json -Depth 6 | Set-Content $stash -Encoding utf8
@"
# Pressure stash (pre-compact)

- armed: True
- stash_utc: $($j.stash_utc)
- why: L1 / VL188 tail blocked on CallMcpTool
- project_root: D:\Experiments\Personal Cursor Folder\Financial\software\open\cdp-mcp
- phase: Explore/Code
- ignite: SoftFirst before kill; re-ARM via cdp_ignite when CallTool up
- plan: Dig densest FullReady peer residual nested[axb] SoftFL invent REJECT
- recall_gate: ready

## wave

- VL188
- mcp-calltool-bridge
- dig-next-densest

## Body

$body
"@ | Set-Content $md -Encoding utf8
$due = ([DateTimeOffset]::UtcNow).AddMinutes(2).ToString('o')
$now = ([DateTimeOffset]::UtcNow).ToString('o')
@{
  schema = 'ignite_arms/v1'
  saved_utc = $now
  arms = @(
    @{
      id = 'leaf-wake-vl188-bridge'
      event = 'timer'
      message = 'Resume the current authorized local development task from Task Manager. Habitat=CDP. Keep flying the started TM leaf; re-arm continuity as insurance after work — timer ≠ idle license.'
      charge_mode = 'minimal'
      task = 'Dig densest FullReady peer residual nested[axb] SoftFL invent REJECT focus'
      port = 9222
      once = $true
      last_once = $true
      ok_only = $true
      settle_seconds = 2
      wait_seconds = 90
      status = 'armed'
      created_utc = $now
      due_utc = $due
    }
  )
} | ConvertTo-Json -Depth 6 | Set-Content 'C:\Users\dkara\AppData\Local\cdp-mcp\ignite-arms-cdp.json' -Encoding utf8
Write-Output 'stash_arm_ok'
