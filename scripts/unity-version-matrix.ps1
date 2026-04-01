param(
    [string]$Project = "unity-project-dev\ucp-dev",
    [string[]]$RequestedSlots = @("6000.0", "6000.1", "6000.2", "6000.3", "6000.4"),
    [int]$TimeoutSeconds = 180,
    [switch]$Run,
    [string]$OutputJson,
    [string]$SummaryMarkdown
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$qaScript = Join-Path $PSScriptRoot "qa-playground.ps1"
$resultsRoot = Join-Path $repoRoot ".matrix-results"

function Get-UnityInstallRoots {
    $roots = [System.Collections.Generic.List[string]]::new()
    foreach ($path in @(
        "C:\Program Files\Unity\Hub\Editor",
        "C:\Program Files\Unity Hub\Editor",
        (Join-Path $env:ProgramFiles "Unity\Hub\Editor"),
        (Join-Path $env:ProgramFiles "Unity Hub\Editor"),
        (Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor"),
        (Join-Path ${env:ProgramFiles(x86)} "Unity Hub\Editor")
    )) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path $path)) {
            $roots.Add($path)
        }
    }

    $secondaryInstallFile = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
    if (Test-Path $secondaryInstallFile) {
        try {
            $secondaryPath = Get-Content $secondaryInstallFile -Raw | ConvertFrom-Json
            if (-not [string]::IsNullOrWhiteSpace($secondaryPath) -and (Test-Path $secondaryPath)) {
                $roots.Add($secondaryPath)
            }
        }
        catch {
            Write-Warning "Failed to parse Unity Hub secondary install path from ${secondaryInstallFile}: $($_.Exception.Message)"
        }
    }

    return $roots | Sort-Object -Unique
}

function Parse-UnityVersionId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionId
    )

    if ($VersionId -notmatch "^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<stream>[abfp])(?<streamNumber>\d+)$") {
        return $null
    }

    $streamRank = switch ($Matches.stream) {
        "a" { 0 }
        "b" { 1 }
        "f" { 2 }
        "p" { 3 }
        default { -1 }
    }

    return [pscustomobject]@{
        VersionId = $VersionId
        Major = [int]$Matches.major
        Minor = [int]$Matches.minor
        Patch = [int]$Matches.patch
        Stream = $Matches.stream
        StreamRank = $streamRank
        StreamNumber = [int]$Matches.streamNumber
        Slot = "$($Matches.major).$($Matches.minor)"
    }
}

function Get-InstalledUnityEditors {
    $editors = [System.Collections.Generic.List[object]]::new()

    foreach ($root in Get-UnityInstallRoots) {
        Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $unityExe = Join-Path $_.FullName "Editor\Unity.exe"
            if (-not (Test-Path $unityExe)) {
                return
            }

            $parsed = Parse-UnityVersionId -VersionId $_.Name
            if ($null -eq $parsed) {
                return
            }

            $editors.Add([pscustomobject]@{
                VersionId = $parsed.VersionId
                Slot = $parsed.Slot
                Major = $parsed.Major
                Minor = $parsed.Minor
                Patch = $parsed.Patch
                Stream = $parsed.Stream
                StreamRank = $parsed.StreamRank
                StreamNumber = $parsed.StreamNumber
                Path = $_.FullName
                UnityExe = $unityExe
            })
        }
    }

    return $editors |
        Sort-Object -Property `
            @{ Expression = "Major"; Descending = $false }, `
            @{ Expression = "Minor"; Descending = $false }, `
            @{ Expression = "Patch"; Descending = $true }, `
            @{ Expression = "StreamRank"; Descending = $true }, `
            @{ Expression = "StreamNumber"; Descending = $true }, `
            @{ Expression = "VersionId"; Descending = $false }
}

function Get-BestEditorForSlot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Slot,
        [Parameter(Mandatory = $true)]
        [object[]]$Editors
    )

    return $Editors |
        Where-Object { $_.Slot -eq $Slot } |
        Sort-Object -Property `
            @{ Expression = "Patch"; Descending = $true }, `
            @{ Expression = "StreamRank"; Descending = $true }, `
            @{ Expression = "StreamNumber"; Descending = $true }, `
            @{ Expression = "VersionId"; Descending = $false } |
        Select-Object -First 1
}

function Resolve-RequestedSlot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RequestedSlot,
        [Parameter(Mandatory = $true)]
        [object[]]$Editors
    )

    $requestedParts = $RequestedSlot.Split(".")
    if ($requestedParts.Count -ne 2) {
        throw "Requested slot '$RequestedSlot' must look like major.minor (for example 6000.3)."
    }

    $requestedMajor = [int]$requestedParts[0]
    $requestedMinor = [int]$requestedParts[1]
    $sameMajorEditors = @($Editors | Where-Object { $_.Major -eq $requestedMajor })

    if ($sameMajorEditors.Count -eq 0) {
        return [pscustomobject]@{
            RequestedSlot = $RequestedSlot
            Status = "skipped"
            Resolution = "No installed Unity editors match major $requestedMajor."
            ActualSlot = $null
            ActualVersionId = $null
            UnityPath = $null
        }
    }

    $exact = Get-BestEditorForSlot -Slot $RequestedSlot -Editors $sameMajorEditors
    if ($null -ne $exact) {
        return [pscustomobject]@{
            RequestedSlot = $RequestedSlot
            Status = "exact"
            Resolution = "Matched exact slot $RequestedSlot."
            ActualSlot = $exact.Slot
            ActualVersionId = $exact.VersionId
            UnityPath = $exact.UnityExe
        }
    }

    $majorSlots = @($sameMajorEditors | Select-Object -ExpandProperty Slot -Unique)
    $higherSlot = $majorSlots |
        Where-Object { [int]($_.Split(".")[1]) -gt $requestedMinor } |
        Sort-Object { [int]($_.Split(".")[1]) } |
        Select-Object -First 1
    if ($higherSlot) {
        $editor = Get-BestEditorForSlot -Slot $higherSlot -Editors $sameMajorEditors
        return [pscustomobject]@{
            RequestedSlot = $RequestedSlot
            Status = "fallback-higher"
            Resolution = "Exact slot missing; using next higher installed slot $higherSlot."
            ActualSlot = $editor.Slot
            ActualVersionId = $editor.VersionId
            UnityPath = $editor.UnityExe
        }
    }

    $lowerSlot = $majorSlots |
        Where-Object { [int]($_.Split(".")[1]) -lt $requestedMinor } |
        Sort-Object { [int]($_.Split(".")[1]) } -Descending |
        Select-Object -First 1
    if ($lowerSlot) {
        $editor = Get-BestEditorForSlot -Slot $lowerSlot -Editors $sameMajorEditors
        return [pscustomobject]@{
            RequestedSlot = $RequestedSlot
            Status = "fallback-lower"
            Resolution = "Exact slot missing; using nearest lower installed slot $lowerSlot."
            ActualSlot = $editor.Slot
            ActualVersionId = $editor.VersionId
            UnityPath = $editor.UnityExe
        }
    }

    return [pscustomobject]@{
        RequestedSlot = $RequestedSlot
        Status = "skipped"
        Resolution = "No install could cover requested slot $RequestedSlot."
        ActualSlot = $null
        ActualVersionId = $null
        UnityPath = $null
    }
}

function Backup-ManifestJson {
    param([string]$ProjectPath)
    $src = Join-Path $ProjectPath "Packages\manifest.json"
    $dst = Join-Path $ProjectPath "Packages\manifest.json.matrix-backup"
    if (Test-Path $src) { Copy-Item $src $dst -Force }
}

function Restore-ManifestJson {
    param([string]$ProjectPath)
    $bak = Join-Path $ProjectPath "Packages\manifest.json.matrix-backup"
    $dst = Join-Path $ProjectPath "Packages\manifest.json"
    if (Test-Path $bak) {
        Copy-Item $bak $dst -Force
        Remove-Item $bak -Force
    }
}

function Update-ManifestForUnityVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$UnityVersionId
    )

    $manifestPath = Join-Path $ProjectPath "Packages\manifest.json"
    if (-not (Test-Path $manifestPath)) { return }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -Depth 20
    $dependencies = @{}
    foreach ($property in $manifest.dependencies.PSObject.Properties) {
        $dependencies[$property.Name] = [string]$property.Value
    }

    $parsed = Parse-UnityVersionId -VersionId $UnityVersionId
    $removableModules = @(
        "com.unity.modules.adaptiveperformance",
        "com.unity.modules.vectorgraphics"
    )
    $changed = $false
    if ($parsed -and $parsed.Minor -lt 3) {
        foreach ($name in $removableModules) {
            if ($dependencies.ContainsKey($name)) {
                $dependencies.Remove($name)
                $changed = $true
                Write-Host "  Removed $name (not available in Unity $UnityVersionId)" -ForegroundColor Yellow
            }
        }
    }

    if ($changed) {
        $ordered = [ordered]@{}
        foreach ($key in ($dependencies.Keys | Sort-Object)) { $ordered[$key] = $dependencies[$key] }
        $manifest.dependencies = [pscustomobject]$ordered
        $manifest | ConvertTo-Json -Depth 20 | Set-Content -Path $manifestPath -Encoding utf8
    }
}

function Write-MarkdownSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Summary,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Unity compatibility matrix")
    $lines.Add("")
    $lines.Add(("Project: ``{0}``" -f $Summary.Project))
    $lines.Add("")
    $lines.Add("## Installed editors")
    $lines.Add("")
    foreach ($editor in $Summary.InstalledEditors) {
        $lines.Add(("- ``{0}`` (``{1}``)" -f $editor.VersionId, $editor.Path))
    }
    if (@($Summary.InstalledEditors).Count -eq 0) {
        $lines.Add("- None discovered")
    }
    $lines.Add("")
    $lines.Add("## Requested slots")
    $lines.Add("")
    foreach ($slot in $Summary.Slots) {
        $status = $slot.Status
        $actual = if ($slot.ActualVersionId) { (" -> ``{0}``" -f $slot.ActualVersionId) } else { "" }
        $execution = if ($slot.ExecutionStatus) { " [$($slot.ExecutionStatus)]" } else { "" }
        $lines.Add(("- ``{0}``: {1}{2}{3} - {4}" -f $slot.RequestedSlot, $status, $actual, $execution, $slot.Resolution))
    }
    $lines.Add("")
    $lines.Add("## Warnings")
    $lines.Add("")
    foreach ($warning in $Summary.Warnings) {
        $lines.Add("- $warning")
    }
    if (@($Summary.Warnings).Count -eq 0) {
        $lines.Add("- None")
    }
    $lines.Add("")
    $lines.Add("## Result")
    $lines.Add("")
    $lines.Add("- Tested versions: $($Summary.TestedCount)")
    $lines.Add("- Skipped slots: $($Summary.SkippedCount)")
    $lines.Add("- Failed versions: $($Summary.FailedCount)")
    $lines.Add("- Passed: $($Summary.Passed)")

    $content = ($lines -join [Environment]::NewLine) + [Environment]::NewLine
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    Set-Content -Path $Path -Value $content -Encoding utf8
}

function Close-UnityProjectEditor {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    Write-Host "==> Closing Unity editor for $ProjectPath" -ForegroundColor Cyan
    & cargo run --quiet --manifest-path (Join-Path $repoRoot "cli/Cargo.toml") -- --project $ProjectPath --timeout 30 editor close --force | Out-Host
}

if (-not (Test-Path (Join-Path $Project "ProjectSettings\ProjectVersion.txt"))) {
    throw "Not a Unity project: $Project"
}

$installedEditors = @(Get-InstalledUnityEditors)
$slotResolutions = foreach ($slot in $RequestedSlots) {
    Resolve-RequestedSlot -RequestedSlot $slot -Editors $installedEditors
}

$warnings = [System.Collections.Generic.List[string]]::new()
foreach ($slot in $slotResolutions) {
    if ($slot.Status -like "fallback-*") {
        $warnings.Add("Requested Unity slot $($slot.RequestedSlot) was not available; covered by $($slot.ActualVersionId) instead.")
    } elseif ($slot.Status -eq "skipped") {
        $warnings.Add("Requested Unity slot $($slot.RequestedSlot) was not tested because no compatible installed editor was available.")
    }
}

if (@($installedEditors).Count -eq 0) {
    $warnings.Add("No installed Unity editors were discovered. The compatibility matrix cannot execute until Unity Hub installs are visible on this machine.")
}

$executions = @{}
$failedVersions = 0
$testedVersions = 0

if ($Run) {
    if (Test-Path $resultsRoot) { Remove-Item $resultsRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

    # Delete Library so each version reimports cleanly
    $libraryDir = Join-Path $Project "Library"
    if (Test-Path $libraryDir) {
        Write-Host "==> Removing Library cache for clean reimport" -ForegroundColor Cyan
        Remove-Item $libraryDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    foreach ($slot in $slotResolutions) {
        if (-not $slot.ActualVersionId) {
            $slot | Add-Member -NotePropertyName ExecutionStatus -NotePropertyValue "skipped" -Force
            continue
        }

        if ($executions.ContainsKey($slot.ActualVersionId)) {
            $previous = $executions[$slot.ActualVersionId]
            $slot | Add-Member -NotePropertyName ExecutionStatus -NotePropertyValue "covered-by-duplicate" -Force
            $slot | Add-Member -NotePropertyName CoveredBySlot -NotePropertyValue $previous.RequestedSlot -Force
            continue
        }

        $summaryPath = Join-Path $resultsRoot "$($slot.RequestedSlot.Replace('.', '_')).json"

        # Close any running editor first
        Close-UnityProjectEditor -ProjectPath $Project

        # Backup and sanitize manifest for this version
        Backup-ManifestJson -ProjectPath $Project
        Update-ManifestForUnityVersion -ProjectPath $Project -UnityVersionId $slot.ActualVersionId

        Write-Host "==> Running QA against Unity $($slot.ActualVersionId) (requested $($slot.RequestedSlot))" -ForegroundColor Cyan
        & $qaScript `
            -Project $Project `
            -TimeoutSeconds $TimeoutSeconds `
            -ForceUnityVersion $slot.ActualVersionId `
            -SummaryPath $summaryPath `
            -SkipInstall
        $exitCode = $LASTEXITCODE

        # Restore manifest
        Restore-ManifestJson -ProjectPath $Project

        $qaSummary = $null
        if (Test-Path $summaryPath) {
            $qaSummary = Get-Content $summaryPath -Raw | ConvertFrom-Json -Depth 20
        }

        $executionStatus = if ($exitCode -eq 0) { "passed" } else { "failed" }
        $slot | Add-Member -NotePropertyName ExecutionStatus -NotePropertyValue $executionStatus -Force
        $slot | Add-Member -NotePropertyName QaSummary -NotePropertyValue $qaSummary -Force

        # Force close editor between slots
        Close-UnityProjectEditor -ProjectPath $Project

        # Remove Library so next version starts clean
        if (Test-Path $libraryDir) {
            Remove-Item $libraryDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        $executions[$slot.ActualVersionId] = $slot
        $testedVersions++
        if ($exitCode -ne 0) {
            $failedVersions++
            Write-Host "==> FAILED: Unity $($slot.ActualVersionId) (slot $($slot.RequestedSlot))" -ForegroundColor Red
            if ($qaSummary) {
                $qaSummary.Results | Where-Object { -not $_.Passed } | ForEach-Object {
                    Write-Host "     FAIL: $($_.Name) :: $($_.Detail)" -ForegroundColor Red
                }
            }
        } else {
            Write-Host "==> PASSED: Unity $($slot.ActualVersionId) (slot $($slot.RequestedSlot))" -ForegroundColor Green
        }
    }
}

$skippedSlots = @($slotResolutions | Where-Object { $_.Status -eq "skipped" }).Count
$summary = [pscustomobject]@{
    Project = $Project
    RequestedSlots = @($RequestedSlots)
    InstalledEditors = @($installedEditors | Select-Object VersionId, Slot, Path)
    Slots = @($slotResolutions)
    Warnings = @($warnings)
    TestedCount = $testedVersions
    SkippedCount = $skippedSlots
    FailedCount = $failedVersions
    Passed = ($failedVersions -eq 0)
}

if ($OutputJson) {
    $outputDir = Split-Path -Parent $OutputJson
    if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    $summary | ConvertTo-Json -Depth 20 | Set-Content -Path $OutputJson -Encoding utf8
}

if ($SummaryMarkdown) {
    Write-MarkdownSummary -Summary $summary -Path $SummaryMarkdown
}

if ($env:GITHUB_STEP_SUMMARY -and $SummaryMarkdown -and (Test-Path $SummaryMarkdown)) {
    Get-Content $SummaryMarkdown | Add-Content -Path $env:GITHUB_STEP_SUMMARY
}

Write-Host "`nUnity compatibility matrix:" -ForegroundColor Cyan
foreach ($slot in $slotResolutions) {
    $statusLine = "$($slot.RequestedSlot): $($slot.Status)"
    if ($slot.ActualVersionId) {
        $statusLine += " -> $($slot.ActualVersionId)"
    }
    if ($slot.PSObject.Properties.Name -contains "ExecutionStatus") {
        $statusLine += " [$($slot.ExecutionStatus)]"
    }
    Write-Host " - $statusLine :: $($slot.Resolution)"
}

foreach ($warning in $warnings) {
    Write-Warning $warning
}

if ($failedVersions -gt 0) {
    exit 1
}

exit 0
