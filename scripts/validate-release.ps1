param(
    [string]$Project = "C:\Users\aimma\Workspace\unity-control-protocol\unity-project-dev\ucp-dev",
    [string]$Version,
    [string[]]$UnitySlots = @("6000.0", "6000.1", "6000.2", "6000.3", "6000.4", "6000.5", "6000.6"),
    [switch]$SkipCargo,
    [switch]$SkipWebsite,
    [switch]$SkipUnityMatrix,
    [switch]$KeepTempProjects,
    [int]$TimeoutSeconds = 120,
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$matrixScript = Join-Path $PSScriptRoot "unity-version-matrix.ps1"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = if ($env:RUNNER_TEMP) {
        Join-Path (Join-Path $env:RUNNER_TEMP "ucp-release-validation") ([Guid]::NewGuid().ToString("N"))
    } else {
        Join-Path (Join-Path ([System.IO.Path]::GetTempPath()) "ucp-release-validation") ([Guid]::NewGuid().ToString("N"))
    }
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

# This preflight IS the Unity compatibility matrix: it closes, wipes, and relaunches the QA
# project's editor once per slot. A second harness on the same project, or a human editor on it,
# corrupts both runs (Library deleted under a live editor, dialogs answered by the wrong loop).
# Refuse to start in that situation instead of discovering it an hour later.
function Assert-PreflightAlone {
    param([string]$ProjectPath)
    $resolved = (Resolve-Path $ProjectPath).Path
    $all = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue
    # Our own ancestors (the shell that launched us) carry this script's name on their command
    # line too; only other PowerShell hosts count as a competing harness.
    $lineage = @($PID)
    $cursor = $all | Where-Object { $_.ProcessId -eq $PID }
    while ($cursor -and $cursor.ParentProcessId -and $lineage -notcontains $cursor.ParentProcessId) {
        $lineage += $cursor.ParentProcessId
        $cursor = $all | Where-Object { $_.ProcessId -eq $cursor.ParentProcessId }
    }
    $harness = $all | Where-Object {
        $_.Name -in @('pwsh.exe', 'powershell.exe') -and $lineage -notcontains $_.ProcessId -and $_.CommandLine -and (
            $_.CommandLine -like '*unity-version-matrix.ps1*' -or
            $_.CommandLine -like '*qa-playground.ps1*' -or
            $_.CommandLine -like '*validate-release.ps1*')
    }
    if ($harness) {
        $list = ($harness | ForEach-Object { "pid $($_.ProcessId)" }) -join ', '
        throw "Another QA harness is already running ($list). Wait for it or stop it; two runs on the same project destroy each other."
    }
    $editors = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -and ($_.CommandLine -replace '/', '\') -like "*$($resolved -replace '/', '\')*"
    }
    if ($editors) {
        throw "A Unity editor is open on $resolved (pid $(($editors | ForEach-Object ProcessId) -join ', ')). The matrix wipes that project's Library per slot; close the editor first (ucp --project <path> editor close)."
    }
}

if (-not $SkipUnityMatrix) {
    Assert-PreflightAlone -ProjectPath $Project
    Write-Host "==> Preflight runs the Unity matrix on $Project for slots: $($UnitySlots -join ', ')" -ForegroundColor Cyan
    Write-Host "    Do not start another matrix or open that project until this finishes." -ForegroundColor Cyan
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Script
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Script
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

Push-Location $repoRoot
try {
    if (-not $SkipCargo) {
        Invoke-CheckedCommand -Name "cargo test" -Script { cargo test --manifest-path cli/Cargo.toml }
        Invoke-CheckedCommand -Name "cargo check" -Script { cargo check --manifest-path cli/Cargo.toml }
    }

    if (-not $SkipWebsite) {
        Invoke-CheckedCommand -Name "website npm ci" -Script {
            Push-Location website
            try {
                npm ci
            }
            finally {
                Pop-Location
            }
        }
        Invoke-CheckedCommand -Name "website build" -Script {
            Push-Location website
            try {
                npm run build
            }
            finally {
                Pop-Location
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        Invoke-CheckedCommand -Name "sync-version check" -Script { node scripts/sync-version.mjs --check $Version }
        Invoke-CheckedCommand -Name "sync-skills check" -Script { node scripts/sync-skills.mjs --check }
    }

    if (-not $SkipUnityMatrix) {
        $matrixJson = Join-Path $OutputRoot "unity-matrix.json"
        $matrixMd = Join-Path $OutputRoot "unity-matrix.md"
        Invoke-CheckedCommand -Name "unity compatibility matrix" -Script {
            & $matrixScript `
                -Project $Project `
                -RequestedSlots $UnitySlots `
                -TimeoutSeconds $TimeoutSeconds `
                -Run `
                -OutputJson $matrixJson `
                -SummaryMarkdown $matrixMd `
                -KeepTempProjects:$KeepTempProjects
        }
    }
}
finally {
    Pop-Location
}
