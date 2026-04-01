param(
    [string]$Project = "C:\Users\aimma\Workspace\unity-control-protocol\unity-project-dev\ucp-dev",
    [string]$Version,
    [string[]]$UnitySlots = @("6000.0", "6000.1", "6000.2", "6000.3", "6000.4"),
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
