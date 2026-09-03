# Shared personal canon seeding for Install-Cdp.ps1 (kb-public L0 + newcomer tail).

function Get-AgentNotesPublicCutHead {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "agent-notes.md not found: $Path"
    }
    $notes = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
    $marker = "<!-- public-cut"
    $idx = $notes.IndexOf($marker)
    if ($idx -ge 0) {
        return $notes.Substring(0, $idx).TrimEnd()
    }
    return $notes.TrimEnd()
}

function Get-FencedMarkdownBlock {
    param([Parameter(Mandatory)][string]$Text)
    if ($Text -match '(?s)```markdown\s*\r?\n(.*?)```') {
        return $Matches[1].TrimEnd()
    }
    return $null
}

function Replace-AgentNotesSection {
    param(
        [Parameter(Mandatory)][string]$Notes,
        [Parameter(Mandatory)][string]$SectionId,
        [Parameter(Mandatory)][string]$ReplacementBlock
    )
    $pattern = "(?s)<!-- section:$([regex]::Escape($SectionId)) -->.*?<!-- /section:$([regex]::Escape($SectionId)) -->"
    if ($Notes -match $pattern) {
        return [regex]::Replace($Notes, $pattern, $ReplacementBlock.Trim())
    }
    return $Notes
}

function Get-NewcomerHotSectionFromTemplate {
    param([Parameter(Mandatory)][string]$TemplatePath)
    if (-not (Test-Path -LiteralPath $TemplatePath)) { return $null }
    $raw = [System.IO.File]::ReadAllText($TemplatePath, [System.Text.UTF8Encoding]::new($false))
    return Get-FencedMarkdownBlock -Text $raw
}

function New-PersonalAgentNotesSeedBody {
    param(
        [Parameter(Mandatory)][string]$KbPublicRoot,
        [Parameter(Mandatory)][string]$NewcomerDir
    )
    $kbNotes = Join-Path $KbPublicRoot "agent-notes.md"
    $head = Get-AgentNotesPublicCutHead -Path $kbNotes

    $neutralActiveScope = @'
<!-- section:active-scope -->
## Active scope (указатель) — контракт L0

**PRIMARY:** not set — bind workspace paths in `knowledge/work/local/workspace-scope-map-v1.md`; add `scope-*` below public-cut when the track is stable.

Резолв `workspace_path` → slice: **`knowledge/worlds/workspace-context/active-scope-resolution-extended-v1.md`**. Протокол: **`playbook-multi-project-context-v1.md`** §6c · **`playbook-project-switch-v1.md`**.
<!-- /section:active-scope -->
'@.Trim()
    $head = Replace-AgentNotesSection -Notes $head -SectionId "active-scope" -ReplacementBlock $neutralActiveScope

    $rootsTpl = Join-Path $NewcomerDir "template-clean-setup-hot-knowledge-roots-routing-v1.md"
    $rootsBlock = Get-NewcomerHotSectionFromTemplate -TemplatePath $rootsTpl
    if ($rootsBlock) {
        $head = Replace-AgentNotesSection -Notes $head -SectionId "knowledge-roots-routing-v1" -ReplacementBlock $rootsBlock
    }

    $tailParts = @()
    $cleanTpl = Join-Path $NewcomerDir "template-clean-setup-hot-clean-setup-routing-v1.md"
    $cleanBlock = Get-NewcomerHotSectionFromTemplate -TemplatePath $cleanTpl
    if ($cleanBlock) { $tailParts += $cleanBlock }

    $tail = ($tailParts -join "`n`n").Trim()
    $pieces = @($head, "<!-- public-cut -->", "<!-- Personal / machine-local hot below. Not in kb-public. -->")
    if ($tail.Length -gt 0) { $pieces += $tail }
    return ($pieces -join "`n`n").TrimEnd() + "`n"
}
