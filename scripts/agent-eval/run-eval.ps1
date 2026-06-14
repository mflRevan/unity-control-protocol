<#
.SYNOPSIS
  Agent-in-the-loop evaluation harness for the `ucp` CLI surface.

.DESCRIPTION
  UCP's primary users are AI agents, not the humans behind them. This harness exercises a
  feature (or the whole surface) the way a real agent would: it stands up a live Unity editor
  with the LOCAL bridge embedded, then hands a deliberately terse task to a weak, cheap model
  driving `ucp` through opencode. The model only has the CLI's own `--help`/docstrings to work
  from — so wherever it stumbles, flails, or misuses a flag, that is a documentation or ergonomics
  defect to fix, surfaced cheaply before it reaches a frontier agent.

  The run captures: the full model transcript, every `ucp` command the model issued (parsed from
  the transcript), and a before/after scene snapshot for grading. Nothing is judged automatically;
  read runs/<name>/calls.log and transcript.txt and decide what to harden.

.PARAMETER Task
  Inline task string. Mutually exclusive with -TaskFile.

.PARAMETER TaskFile
  Path to a markdown task file (see scripts/agent-eval/tasks/).

.PARAMETER Model
  opencode model id. Default: a free weak model, deliberately, to stress the docs.

.PARAMETER Project
  Unity project to drive. Default: the dev project.

.PARAMETER Name
  Short label for the run directory.

.PARAMETER SkipBridgeSetup
  Assume the editor + local bridge are already up; just run the model.

.EXAMPLE
  ./scripts/agent-eval/run-eval.ps1 -TaskFile scripts/agent-eval/tasks/place-props.md -Name place-props
#>
param(
    [string]$Task,
    [string]$TaskFile,
    [string]$Model = 'opencode/deepseek-v4-flash-free',
    [string]$Project = 'C:/Users/aimma/Workspace/unity-control-protocol/unity-project-dev/ucp-dev',
    [string]$Name = 'eval',
    [int]$ModelTimeoutSeconds = 900,
    [switch]$SkipBridgeSetup
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$evalRoot = Join-Path $repoRoot 'scripts/agent-eval'
$sandbox = Join-Path $evalRoot 'sandbox'
$ucp = Join-Path $repoRoot 'cli/target/debug/ucp.exe'

if (-not (Test-Path $ucp)) {
    Write-Host "Building ucp (debug)..." -ForegroundColor Cyan
    Push-Location (Join-Path $repoRoot 'cli')
    try { cargo build | Out-Null } finally { Pop-Location }
}

if ($TaskFile) { $Task = Get-Content -Raw -Path (Resolve-Path $TaskFile) }
if (-not $Task) { throw 'Provide -Task or -TaskFile.' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $evalRoot "runs/$Name-$stamp"
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
Set-Content -Path (Join-Path $runDir 'task.md') -Value $Task -Encoding utf8

# ucp on PATH + project pinned via env so the model never needs --project.
$ucpDir = Split-Path -Parent $ucp
$env:PATH = "$ucpDir;$env:PATH"
$env:UCP_PROJECT = $Project

function Invoke-Ucp([string[]]$ucpArgs, [int]$TimeoutSec = 60) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath $ucp -ArgumentList $ucpArgs -NoNewWindow -PassThru `
            -RedirectStandardOutput $out -RedirectStandardError $err
        if (-not $p.WaitForExit($TimeoutSec * 1000)) { try { $p.Kill($true) } catch {} ; return $null }
        return ((Get-Content $out -Raw) + (Get-Content $err -Raw))
    }
    finally { Remove-Item $out, $err -Force -ErrorAction SilentlyContinue }
}

if (-not $SkipBridgeSetup) {
    Write-Host "Ensuring local bridge + editor are up..." -ForegroundColor Cyan
    $probe = Invoke-Ucp @('connect', '--json') 30
    if (-not ($probe -and $probe -match '"success"\s*:\s*true')) {
        Write-Host "  bridge not reachable; mounting local bridge (install --dev) and opening editor" -ForegroundColor Yellow
        Invoke-Ucp @('install', '--dev', '--no-wait', '--json') 120 | Out-Null
        Invoke-Ucp @('open', '--json') 40 | Out-Null
        $ready = $false
        for ($i = 0; $i -lt 60; $i++) {
            Start-Sleep -Seconds 3
            $c = Invoke-Ucp @('connect', '--json') 30
            if ($c -and $c -match '"success"\s*:\s*true') { $ready = $true; break }
            Write-Host "  waiting for bridge ($($i+1)/60)..." -ForegroundColor DarkGray
        }
        if (-not $ready) { throw 'Bridge never came up. Editor may be locked/compiling; see runs dir.' }
    }
    Write-Host "  bridge ready." -ForegroundColor Green
}

# Capture a before snapshot for grading.
Invoke-Ucp @('scene', 'snapshot', '--json') 60 | Set-Content -Path (Join-Path $runDir 'before.json') -Encoding utf8

Write-Host "Running model '$Model' on the task (timeout ${ModelTimeoutSeconds}s)..." -ForegroundColor Cyan
$transcriptJson = Join-Path $runDir 'transcript.jsonl'
$transcriptTxt = Join-Path $runDir 'transcript.txt'
$stderrLog = Join-Path $runDir 'opencode.stderr.log'

# opencode inherits PATH/UCP_PROJECT; --dir sandboxes the model's cwd; skip-permissions so it
# can actually run ucp non-interactively. Default (formatted) output is the readable transcript.
$ocArgs = @(
    'run', '-m', $Model, '--dir', $sandbox, '--dangerously-skip-permissions', $Task
)
$proc = Start-Process -FilePath 'opencode' -ArgumentList $ocArgs -NoNewWindow -PassThru `
    -RedirectStandardOutput $transcriptTxt -RedirectStandardError $stderrLog
if (-not $proc.WaitForExit($ModelTimeoutSeconds * 1000)) {
    try { $proc.Kill($true) } catch {}
    Write-Host "  model timed out after ${ModelTimeoutSeconds}s (partial transcript kept)." -ForegroundColor Yellow
}

# Extract every ucp command the model issued, in order, from the transcript.
$calls = Select-String -Path $transcriptTxt -Pattern 'ucp\s+[^\r\n`]+' -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object { $_.Value.Trim() }
$calls | Set-Content -Path (Join-Path $runDir 'calls.log') -Encoding utf8

# After snapshot.
Invoke-Ucp @('scene', 'snapshot', '--json') 60 | Set-Content -Path (Join-Path $runDir 'after.json') -Encoding utf8

Write-Host ""
Write-Host "=== Eval complete: $runDir ===" -ForegroundColor Cyan
Write-Host ("  ucp commands issued: {0}" -f @($calls).Count)
Write-Host "  transcript.txt  — full model reasoning + tool calls"
Write-Host "  calls.log       — every ucp command, in order"
Write-Host "  before/after.json — scene state for grading"
Write-Host ""
Write-Host "Review calls.log for misused flags, repeated --help probing, dead-ends, and wrong" -ForegroundColor DarkGray
Write-Host "addressing. Each is a docstring/skill defect to fix, then re-run to confirm." -ForegroundColor DarkGray
