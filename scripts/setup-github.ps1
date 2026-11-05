<#
.SYNOPSIS
  Creates labels, the v0.1 milestone, and v0.1 issues on the current GitHub repo.

.DESCRIPTION
  Reads issues from docs/milestones/v0.1.md (UTF-8). Requires GitHub CLI (gh) logged in
  (gh auth login) and must be run from inside the cloned repository.
  Run with -DryRun first to preview everything without touching GitHub.
  Issues are created in file order, so run this only ONCE on a fresh repo
  (other docs refer to the first two issues as #1 and #2).

.EXAMPLE
  ./scripts/setup-github.ps1 -DryRun
  ./scripts/setup-github.ps1
#>
param(
    [switch]$DryRun,
    [string]$IssueFile = (Join-Path $PSScriptRoot '..\docs\milestones\v0.1.md'),
    [string]$Milestone = 'v0.1'
)

$ErrorActionPreference = 'Stop'

# ---- Labels (name, color, description) -------------------------------------
$labels = @(
    @('setup',     '6e7781', 'Project / tooling setup'),
    @('docs',      '0075ca', 'Documentation, decision records'),
    @('feature',   'a2eeef', 'New feature or system'),
    @('rule',      'd876e3', 'New RuleEffect (spec blank rule)'),
    @('stage',     'fbca04', 'Stage / level data'),
    @('bug',       'd73a4a', 'Unintended behavior'),
    @('area:core', '1d76db', 'Platformer core code'),
    @('area:spec', '5319e7', 'Spec document / rule system code'),
    @('area:both', '0e8a16', 'Both members')
)

# ---- Parse issue file ------------------------------------------------------
if (-not (Test-Path $IssueFile)) { throw "Issue file not found: $IssueFile" }
$text  = [System.IO.File]::ReadAllText((Resolve-Path $IssueFile), [System.Text.Encoding]::UTF8)
$lines = $text -split "`r?`n"

$issues  = @()
$current = $null
foreach ($line in $lines) {
    if ($line -match '^## (.+)$') {
        if ($current) { $issues += $current }
        $current = [pscustomobject]@{ Title = $Matches[1].Trim(); Labels = @(); Body = New-Object System.Collections.Generic.List[string] }
        continue
    }
    if (-not $current) { continue }   # preamble before first issue
    if ($current.Body.Count -eq 0 -and $current.Labels.Count -eq 0 -and $line -match '^labels:\s*(.+)$') {
        $current.Labels = @($Matches[1] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        continue
    }
    $current.Body.Add($line)
}
if ($current) { $issues += $current }

foreach ($i in $issues) {
    $i.Body = (($i.Body -join "`n").Trim())
    foreach ($l in $i.Labels) {
        if (-not ($labels | Where-Object { $_[0] -eq $l })) { throw "Issue '$($i.Title)' uses unknown label '$l'" }
    }
}

Write-Host ("Parsed {0} issues from {1}" -f $issues.Count, $IssueFile)

# ---- Dry run ---------------------------------------------------------------
if ($DryRun) {
    Write-Host "`n[DryRun] Labels:"
    $labels | ForEach-Object { Write-Host ("  - {0}" -f $_[0]) }
    Write-Host "`n[DryRun] Milestone: $Milestone"
    Write-Host "`n[DryRun] Issues:"
    $n = 1
    foreach ($i in $issues) {
        $bodyLines = ($i.Body -split "`n").Count
        Write-Host ("  #{0,-3} {1}  [{2}]  ({3} body lines)" -f $n, $i.Title, ($i.Labels -join ', '), $bodyLines)
        $n++
    }
    Write-Host "`nNothing was sent to GitHub."
    return
}

# ---- Preconditions ---------------------------------------------------------
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) not found. Install: https://cli.github.com" }
gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Not logged in. Run: gh auth login" }

$existing = gh issue list --state all --limit 1 --json number --jq 'length'
if ($LASTEXITCODE -ne 0) { throw "Could not read issues. Run this from inside the cloned repo." }
if ([int]$existing -gt 0) {
    throw "This repo already has issues. Issue numbers would not match the docs (#1, #2). Aborting."
}

# ---- Labels ----------------------------------------------------------------
foreach ($l in $labels) {
    gh label create $l[0] --color $l[1] --description $l[2] --force | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to create label $($l[0])" }
    Write-Host "label: $($l[0])"
}

# ---- Milestone -------------------------------------------------------------
$titles = gh api "repos/{owner}/{repo}/milestones?state=all" --jq '.[].title'
if (-not ($titles -contains $Milestone)) {
    gh api "repos/{owner}/{repo}/milestones" -f "title=$Milestone" -f "description=Playable from start to finish: 3 stages, 5 rules" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to create milestone" }
}
Write-Host "milestone: $Milestone"

# ---- Issues ----------------------------------------------------------------
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$tmp = [System.IO.Path]::GetTempFileName()
try {
    foreach ($i in $issues) {
        [System.IO.File]::WriteAllText($tmp, $i.Body, $utf8NoBom)
        $url = gh issue create --title $i.Title --body-file $tmp --label ($i.Labels -join ',') --milestone $Milestone
        if ($LASTEXITCODE -ne 0) { throw "Failed to create issue: $($i.Title)" }
        Write-Host "issue: $url"
    }
}
finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}

Write-Host "`nDone. Next: create a Projects board (repo > Projects > New project > Board) and add these issues."
