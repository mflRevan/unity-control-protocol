param(
    [Parameter(Mandatory = $true)]
    [string]$Project,

    [switch]$SkipInstall,

    [switch]$SkipSnapshot
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'cli/Cargo.toml'

function Invoke-Ucp {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Args
    )

    Write-Host "`n==> ucp $($Args -join ' ')" -ForegroundColor Cyan
    & cargo run --manifest-path $manifestPath -- --project $Project @Args
    if ($LASTEXITCODE -ne 0) {
        throw "ucp command failed: $($Args -join ' ')"
    }
}

if (-not $SkipInstall) {
    Invoke-Ucp --timeout 120 install --dev
}

Invoke-Ucp doctor
Invoke-Ucp connect

if (-not $SkipSnapshot) {
    Invoke-Ucp --json snapshot
}

Invoke-Ucp scene active
Invoke-Ucp --json asset search -t Material --max 5
Invoke-Ucp build active-target
Invoke-Ucp settings player

Write-Host "`nSmoke suite completed for $Project" -ForegroundColor Green