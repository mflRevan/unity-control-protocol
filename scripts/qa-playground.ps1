param(
	[string]$Project = "C:/Users/aimma/Workspace/unity-control-protocol/unity-project-dev/ucp-dev",
	[switch]$SkipInstall,
	[switch]$KeepArtifacts,
	[int]$TimeoutSeconds = 120,
	[string]$ForceUnityVersion,
	[string]$Unity,
	[string]$SummaryPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'cli/Cargo.toml'

$results = New-Object System.Collections.Generic.List[object]
$fatalFailure = $false
$fatalDiagnostics = $null
$currentStep = ''

function New-UcpCargoArgs {
	$command = @(
		'run',
		'--quiet',
		'--manifest-path', $manifestPath,
		'--',
		'--project', $Project,
		'--dialog-policy', 'ignore',
		'--timeout', "$TimeoutSeconds",
		'--json'
	)

	if (-not [string]::IsNullOrWhiteSpace($Unity)) {
		$command += @('--unity', $Unity)
	}

	if (-not [string]::IsNullOrWhiteSpace($ForceUnityVersion)) {
		$command += @('--force-unity-version', $ForceUnityVersion)
	}

	return $command
}

function Invoke-UcpJson {
	param(
		[Parameter(Mandatory = $true)]
		[string[]]$UcpArgs,
		[int]$ProcessTimeoutSeconds = 0,
		[switch]$AllowFailure
	)

	$command = (New-UcpCargoArgs) + $UcpArgs

	$effectiveProcessTimeout = if ($ProcessTimeoutSeconds -gt 0) { $ProcessTimeoutSeconds } else { $TimeoutSeconds + 10 }
	$tempOutput = [System.IO.Path]::GetTempFileName()
	$tempError = [System.IO.Path]::GetTempFileName()

	try {
		$process = Start-Process -FilePath 'cargo' -ArgumentList $command -RedirectStandardOutput $tempOutput -RedirectStandardError $tempError -NoNewWindow -PassThru
		if (-not $process.WaitForExit($effectiveProcessTimeout * 1000)) {
			try {
				$process.Kill($true)
			}
			catch {
			}

			$text = ((Get-Content $tempOutput -Raw -ErrorAction SilentlyContinue), (Get-Content $tempError -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine).Trim()
			if (-not $AllowFailure) {
				throw "ucp command timed out after ${effectiveProcessTimeout}s: $($UcpArgs -join ' ')`n$text"
			}

			return [pscustomobject]@{
				ExitCode = 124
				Json = $null
				Raw = if ($text) { $text } else { "timed out after ${effectiveProcessTimeout}s" }
			}
		}

		$stdout = Get-Content $tempOutput -Raw -ErrorAction SilentlyContinue
		$stderr = Get-Content $tempError -Raw -ErrorAction SilentlyContinue
		$text = (($stdout, $stderr) -join [Environment]::NewLine).Trim()
		$exitCode = $process.ExitCode
	}
	finally {
		Remove-Item $tempOutput, $tempError -Force -ErrorAction SilentlyContinue
	}

	$json = $null
	if (-not [string]::IsNullOrWhiteSpace($text)) {
		try {
			$json = $text | ConvertFrom-Json -Depth 100
		}
		catch {
			if (-not $AllowFailure) {
				throw "Failed to parse JSON output for 'ucp $($UcpArgs -join ' ')': $text"
			}
		}
	}

	if (-not $AllowFailure -and $exitCode -ne 0) {
		throw "ucp command failed ($exitCode): $($UcpArgs -join ' ')`n$text"
	}

	return [pscustomobject]@{
		ExitCode = $exitCode
		Json = $json
		Raw = $text
	}
}

function Get-DiagnosticPayload {
	$payload = [ordered]@{}

	foreach ($entry in @(
		@{ Name = 'editorStatus'; Args = @('editor', 'status') },
		@{ Name = 'logsStatus'; Args = @('logs', 'status') },
		@{ Name = 'editorLogs'; Args = @('editor', 'logs', '--lines', '160') }
	)) {
		$result = Invoke-UcpJson -UcpArgs $entry.Args -AllowFailure
		$payload[$entry.Name] = [ordered]@{
			exitCode = $result.ExitCode
			raw = $result.Raw
			json = $result.Json
		}
	}

	return [pscustomobject]$payload
}

function Test-UcpSuccess {
	param(
		$Result
	)

	if ($null -ne $Result.Json -and $Result.Json.success -eq $true) {
		return $true
	}

	if (-not [string]::IsNullOrWhiteSpace($Result.Raw) -and $Result.Raw -match '"success"\s*:\s*true') {
		return $true
	}

	return $false
}

function Write-QaSummary {
	param(
		[switch]$Completed
	)

	if ([string]::IsNullOrWhiteSpace($SummaryPath)) {
		return
	}

	$summaryDir = Split-Path -Parent $SummaryPath
	if (-not [string]::IsNullOrWhiteSpace($summaryDir)) {
		New-Item -ItemType Directory -Path $summaryDir -Force | Out-Null
	}

	$passedCount = @($results | Where-Object { $_.Passed }).Count
	$failedCount = @($results | Where-Object { -not $_.Passed }).Count

	try {
		$summary = '' | Select-Object `
			Project, RequestedUnityVersion, RequestedUnityPath, TimeoutSeconds, `
			CurrentStep, Completed, Passed, PassedCount, FailedCount, TotalCount, `
			Results, FatalFailure, Diagnostics
		$summary.Project = [string]$Project
		$summary.RequestedUnityVersion = [string]$ForceUnityVersion
		$summary.RequestedUnityPath = [string]$Unity
		$summary.TimeoutSeconds = [int]$TimeoutSeconds
		$summary.CurrentStep = [string]$currentStep
		$summary.Completed = [bool]$Completed.IsPresent
		$summary.Passed = [bool]($failedCount -eq 0)
		$summary.PassedCount = [int]$passedCount
		$summary.FailedCount = [int]$failedCount
		$summary.TotalCount = [int]$results.Count
		$summary.Results = @($results | Select-Object Name, Passed, Detail)
		$summary.FatalFailure = [bool]$fatalFailure
		$summary.Diagnostics = $fatalDiagnostics

		$summary | ConvertTo-Json -Depth 10 | Set-Content -Path $SummaryPath -Encoding utf8
	}
	catch {
		$fallback = '' | Select-Object CurrentStep, Completed, Error
		$fallback.CurrentStep = [string]$currentStep
		$fallback.Completed = [bool]$Completed.IsPresent
		$fallback.Error = [string]$_.Exception.Message
		$fallback | ConvertTo-Json -Depth 4 | Set-Content -Path $SummaryPath -Encoding utf8
	}
}

function Close-UnityEditorNow {
	try {
		$close = Invoke-UcpJson -UcpArgs @('editor', 'close', '--force') -AllowFailure
		$detail = if ($close.Raw) { $close.Raw } else { 'requested force close' }
		Add-Result -Name 'post-failure-close-force' -Passed:$true -Detail $detail
	}
	catch {
		Add-Result -Name 'post-failure-close-force' -Passed:$false -Detail $_.Exception.Message
	}
}

function Add-Result {
	param(
		[string]$Name,
		[bool]$Passed,
		[string]$Detail
	)

	$results.Add([pscustomobject]@{
		Name = $Name
		Passed = $Passed
		Detail = $Detail
	})

	$status = if ($Passed) { '[PASS]' } else { '[FAIL]' }
	$color = if ($Passed) { 'Green' } else { 'Red' }
	Write-Host "$status $Name :: $Detail" -ForegroundColor $color
	Write-QaSummary
}

function Wait-BridgeReady {
	param(
		[string]$Reason = 'bridge-ready',
		[int]$MaxAttempts = 60,
		[int]$DelaySeconds = 2
	)

	$lastDetail = ''
	for ($i = 0; $i -lt $MaxAttempts; $i++) {
		$probe = Invoke-UcpJson -UcpArgs @('connect') -AllowFailure
		if (Test-UcpSuccess $probe) {
			return [pscustomobject]@{
				Ready = $true
				Attempts = $i + 1
				Detail = if ($probe.Raw) { $probe.Raw } else { 'connected' }
			}
		}
		$lastDetail = if ($probe.Raw) { $probe.Raw } else { 'no output' }
		Write-Host "[WAIT] $Reason attempt $($i + 1)/$MaxAttempts :: $lastDetail" -ForegroundColor Yellow
		Start-Sleep -Seconds $DelaySeconds
	}

	return [pscustomobject]@{
		Ready = $false
		Attempts = $MaxAttempts
		Detail = $lastDetail
	}
}

function Run-Step {
	param(
		[string]$Name,
		[string[]]$UcpArgs,
		[int]$ProcessTimeoutSeconds = 0,
		[scriptblock]$Assert,
		[switch]$AllowFailure
	)

	try {
		$currentStep = $Name
		Write-QaSummary
		Write-Host "[RUN ] $Name :: ucp $($UcpArgs -join ' ')" -ForegroundColor Cyan
		$result = Invoke-UcpJson -UcpArgs $UcpArgs -ProcessTimeoutSeconds $ProcessTimeoutSeconds -AllowFailure:$AllowFailure
		$assertResult = & $Assert $result
		Add-Result -Name $Name -Passed:$assertResult.Passed -Detail $assertResult.Detail
		if (-not $assertResult.Passed) {
			$fatalFailure = $true
			$fatalDiagnostics = Get-DiagnosticPayload
			Close-UnityEditorNow
			throw "Stopping QA after failure in step '$Name'"
		}
		return $result
	}
	catch {
		if (-not ($results | Where-Object { $_.Name -eq $Name -and -not $_.Passed })) {
			Add-Result -Name $Name -Passed:$false -Detail $_.Exception.Message
		}
		if (-not $fatalFailure) {
			$fatalFailure = $true
			$fatalDiagnostics = Get-DiagnosticPayload
			Close-UnityEditorNow
		}
		return $null
	}
}

Write-Host "Running extensive UCP QA against: $Project" -ForegroundColor Cyan
if (-not [string]::IsNullOrWhiteSpace($ForceUnityVersion)) {
	Write-Host "Requested Unity version override: $ForceUnityVersion" -ForegroundColor Cyan
}
if (-not [string]::IsNullOrWhiteSpace($Unity)) {
	Write-Host "Requested Unity executable override: $Unity" -ForegroundColor Cyan
}

if (-not (Test-Path (Join-Path $Project 'ProjectSettings/ProjectSettings.asset'))) {
	throw "Not a Unity project: $Project"
}

$existingEmbeddedBridge = Test-Path (Join-Path $Project 'Packages/com.ucp.bridge/package.json')

if (-not $SkipInstall) {
	Run-Step -Name 'preflight-close-force' -UcpArgs @('editor', 'close', '--force') -AllowFailure -Assert {
		param($r)
		[pscustomobject]@{ Passed = $true; Detail = if ($r.Raw) { $r.Raw } else { 'no-op' } }
	} | Out-Null

	if ($existingEmbeddedBridge) {
		Add-Result -Name 'install-dev-no-wait' -Passed:$true -Detail 'skipped install; embedded bridge already present in project copy'
	}
	else {
		Run-Step -Name 'install-dev-no-wait' -UcpArgs @('install', '--dev', '--no-wait') -Assert {
			param($r)
			if (-not (Test-UcpSuccess $r)) {
				return [pscustomobject]@{ Passed = $false; Detail = $r.Raw }
			}
			[pscustomobject]@{ Passed = $true; Detail = "bridgeStatus=$($r.Json.data.bridgeStatus)" }
		} | Out-Null
	}
}

Run-Step -Name 'open' -UcpArgs @('open') -ProcessTimeoutSeconds ([Math]::Min($TimeoutSeconds, 30)) -AllowFailure -Assert {
	param($r)
	$passed = $false
	if ($r.ExitCode -eq 124) {
		$passed = $true
	}
	elseif (Test-UcpSuccess $r) {
		$passed = $true
	}
	$detail = if ($r.ExitCode -eq 124) { "open timed out locally; continuing with bridge probe :: $($r.Raw)" } else { $r.Raw }
	[pscustomobject]@{ Passed = $passed; Detail = $detail }
} | Out-Null

$postOpenAttempts = [Math]::Max([int][Math]::Ceiling([double]$TimeoutSeconds / 12.0), 3)
$connect = Run-Step -Name 'connect' -UcpArgs @('connect') -AllowFailure -Assert {
	param($r)
	if (Test-UcpSuccess $r) {
		return [pscustomobject]@{ Passed = $true; Detail = $r.Raw }
	}

	$wait = Wait-BridgeReady -Reason 'post-open-connect' -MaxAttempts $postOpenAttempts -DelaySeconds 2
	if (-not $wait.Ready) {
		return [pscustomobject]@{ Passed = $false; Detail = "timed out waiting for bridge after open ($($wait.Attempts) attempts): $($wait.Detail)" }
	}

	$retry = Invoke-UcpJson -UcpArgs @('connect') -AllowFailure
	[pscustomobject]@{
		Passed = (Test-UcpSuccess $retry)
		Detail = $retry.Raw
	}
}

$qaSceneRelativePath = 'Assets/Scenes/SampleScene.unity'
$qaSceneAbsolutePath = Join-Path $Project $qaSceneRelativePath
if (Test-Path $qaSceneAbsolutePath) {
	Run-Step -Name 'scene-load-qa-scene' -UcpArgs @('scene', 'load', $qaSceneRelativePath) -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null
}
else {
	Add-Result -Name 'scene-load-qa-scene' -Passed:$true -Detail "skipped; missing $qaSceneRelativePath"
}

$snapshot = Run-Step -Name 'snapshot-root' -UcpArgs @('scene', 'snapshot') -Assert {
	param($r)
	$count = @($r.Json.data.objects).Count
	[pscustomobject]@{ Passed = ($r.Json.success -and $count -ge 1); Detail = "rootCount=$count" }
}

$sceneList = Run-Step -Name 'scene-list' -UcpArgs @('scene', 'list') -Assert {
	param($r)
	$count = @($r.Json.data.scenes).Count
	[pscustomobject]@{ Passed = ($r.Json.success -and $count -ge 1); Detail = "sceneCount=$count" }
}

$sceneActive = Run-Step -Name 'scene-active' -UcpArgs @('scene', 'active') -Assert {
	param($r)
	$name = $r.Json.data.name
	$path = $r.Json.data.path
	$passed = $r.Json.success
	$detail = "active=$name path=$path"
	[pscustomobject]@{ Passed = $passed; Detail = $detail }
}

$qaRoot = Run-Step -Name 'object-create-root' -UcpArgs @('object', 'create', 'UcpQaRoot') -Assert {
	param($r)
	[pscustomobject]@{ Passed = ($r.Json.success -and $r.Json.data.instanceId); Detail = "id=$($r.Json.data.instanceId)" }
}

$qaRootId = if ($qaRoot) { [int]$qaRoot.Json.data.instanceId } else { 0 }

if ($qaRootId -ne 0) {
	Run-Step -Name 'object-set-name' -UcpArgs @('object', 'set-name', '--id', "$qaRootId", '--name', 'UcpQaRootRenamed') -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null

	Run-Step -Name 'object-set-active-false' -UcpArgs @('object', 'set-active', '--id', "$qaRootId", '--active', 'false') -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null

	Run-Step -Name 'object-set-active-true' -UcpArgs @('object', 'set-active', '--id', "$qaRootId", '--active', 'true') -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null

	Run-Step -Name 'object-add-component' -UcpArgs @('object', 'add-component', '--id', "$qaRootId", '--component', 'BoxCollider') -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null

	Run-Step -Name 'object-get-fields' -UcpArgs @('object', 'get-fields', '--id', "$qaRootId", '--component', 'Transform') -Assert {
		param($r)
		$count = @($r.Json.data.fields).Count
		[pscustomobject]@{ Passed = ($r.Json.success -and $count -ge 1); Detail = "fieldCount=$count" }
	} | Out-Null

	Run-Step -Name 'object-set-property' -UcpArgs @('object', 'set-property', '--id', "$qaRootId", '--component', 'Transform', '--property', 'm_LocalPosition', '--value', '[3,2,1]') -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null

	Run-Step -Name 'object-get-property' -UcpArgs @('object', 'get-property', '--id', "$qaRootId", '--component', 'Transform', '--property', 'm_LocalPosition') -Assert {
		param($r)
		$v = $r.Json.data.value
		if ($v -is [System.Collections.IList]) {
			$ok = $r.Json.success -and $v.Count -ge 3 -and [Math]::Abs([double]$v[0] - 3) -lt 0.01
			$detail = "value=$($v -join ',')"
		}
		else {
			$ok = $r.Json.success
			$detail = "valueType=$($v.GetType().FullName)"
		}
		[pscustomobject]@{ Passed = $ok; Detail = $detail }
	} | Out-Null

	Run-Step -Name 'object-remove-component' -UcpArgs @('object', 'remove-component', '--id', "$qaRootId", '--component', 'BoxCollider') -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null
}

$prefabPath = 'Assets/UcpQaPrefab.prefab'
if ($qaRootId -ne 0) {
	Run-Step -Name 'prefab-create' -UcpArgs @('prefab', 'create', '--id', "$qaRootId", '--path', $prefabPath) -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null

	$inst = Run-Step -Name 'object-instantiate-prefab' -UcpArgs @('object', 'instantiate', $prefabPath, '--name', 'UcpQaPrefabInstance') -Assert {
		param($r)
		[pscustomobject]@{ Passed = ($r.Json.success -and $r.Json.data.instanceId); Detail = "id=$($r.Json.data.instanceId)" }
	}

	$instId = if ($inst) { [int]$inst.Json.data.instanceId } else { 0 }
	if ($instId -ne 0) {
		Run-Step -Name 'prefab-status' -UcpArgs @('prefab', 'status', '--id', "$instId") -Assert {
			param($r)
			[pscustomobject]@{ Passed = ($r.Json.success -and $r.Json.data.isInstance); Detail = $r.Raw }
		} | Out-Null

		Run-Step -Name 'prefab-overrides' -UcpArgs @('prefab', 'overrides', '--id', "$instId") -Assert {
			param($r)
			[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
		} | Out-Null

		Run-Step -Name 'prefab-unpack' -UcpArgs @('prefab', 'unpack', '--id', "$instId", '--completely', 'true') -Assert {
			param($r)
			[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
		} | Out-Null
	}
}

Run-Step -Name 'asset-search-prefab' -UcpArgs @('asset', 'search', '-t', 'Prefab', '-n', 'UcpQa', '--max', '10') -Assert {
	param($r)
	$count = [int]$r.Json.data.returned
	[pscustomobject]@{ Passed = ($r.Json.success -and $count -ge 1); Detail = "returned=$count" }
} | Out-Null

Run-Step -Name 'asset-info-prefab' -UcpArgs @('asset', 'info', $prefabPath) -Assert {
	param($r)
	[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
} | Out-Null

$materialSearch = Run-Step -Name 'material-search' -UcpArgs @('asset', 'search', '-t', 'Material', '--max', '1') -Assert {
	param($r)
	$count = [int]$r.Json.data.returned
	[pscustomobject]@{ Passed = ($r.Json.success -and $count -ge 1); Detail = "returned=$count" }
}

if ($materialSearch -and [int]$materialSearch.Json.data.returned -ge 1) {
	$materialPath = $materialSearch.Json.data.results[0].path
	Run-Step -Name 'material-get-properties' -UcpArgs @('material', 'get-properties', '--path', $materialPath) -Assert {
		param($r)
		[pscustomobject]@{ Passed = ($r.Json.success -and @($r.Json.data.properties).Count -ge 1); Detail = $r.Raw }
	} | Out-Null

	Run-Step -Name 'material-keywords' -UcpArgs @('material', 'keywords', '--path', $materialPath) -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null
}

Run-Step -Name 'files-write' -UcpArgs @('files', 'write', 'Assets/UcpQaFile.txt', '--content', '"alpha smoke file"') -Assert {
	param($r)
	[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
} | Out-Null

Run-Step -Name 'files-patch' -UcpArgs @('files', 'patch', 'Assets/UcpQaFile.txt', '--find', 'smoke', '--replace', 'patched') -Assert {
	param($r)
	[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
} | Out-Null

Run-Step -Name 'files-read' -UcpArgs @('files', 'read', 'Assets/UcpQaFile.txt') -Assert {
	param($r)
	$content = $r.Json.data.content
	[pscustomobject]@{ Passed = ($r.Json.success -and $content -match 'patched'); Detail = "content=$content" }
} | Out-Null

Run-Step -Name 'files-read-path-traversal-rejected' -UcpArgs @('files', 'read', '../outside.txt') -AllowFailure -Assert {
	param($r)
	$passed = ($r.ExitCode -ne 0) -or ($r.Json -and -not $r.Json.success)
	[pscustomobject]@{ Passed = $passed; Detail = $r.Raw }
} | Out-Null

$playerSettings = Run-Step -Name 'settings-player' -UcpArgs @('settings', 'player') -Assert {
	param($r)
	[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
}

$originalProduct = if ($playerSettings) { [string]$playerSettings.Json.data.productName } else { '' }
if (-not [string]::IsNullOrWhiteSpace($originalProduct)) {
	Run-Step -Name 'settings-set-player-productName' -UcpArgs @('settings', 'set-player', '--key', 'productName', '--value', '"UcpQaProduct"') -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null

	Run-Step -Name 'settings-restore-player-productName' -UcpArgs @('settings', 'set-player', '--key', 'productName', '--value', ('"' + $originalProduct + '"')) -Assert {
		param($r)
		[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
	} | Out-Null
}

Run-Step -Name 'settings-quality' -UcpArgs @('settings', 'quality') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'settings-physics' -UcpArgs @('settings', 'physics') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'settings-lighting' -UcpArgs @('settings', 'lighting') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'settings-tags-layers' -UcpArgs @('settings', 'tags-layers') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null

Run-Step -Name 'build-active-target' -UcpArgs @('build', 'active-target') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'build-targets' -UcpArgs @('build', 'targets') -Assert { param($r) [pscustomobject]@{ Passed = ($r.Json.success -and @($r.Json.data.targets).Count -ge 1); Detail = $r.Raw } } | Out-Null
Run-Step -Name 'build-scenes' -UcpArgs @('build', 'scenes') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null

$defines = Run-Step -Name 'build-defines' -UcpArgs @('build', 'defines') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } }
if ($defines) {
	$defineValue = [string]$defines.Json.data.defines
	if (-not [string]::IsNullOrWhiteSpace($defineValue)) {
		Run-Step -Name 'build-set-defines-same' -UcpArgs @('build', 'set-defines', $defineValue) -Assert {
			param($r)
			[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
		} | Out-Null
	}
}

Run-Step -Name 'logs-tail' -UcpArgs @('logs', '--count', '5') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'logs-search' -UcpArgs @('logs', '--pattern', 'Exception|Error', '--count', '20') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null

$screenshotPath = Join-Path $Project 'Assets/UcpQaCapture.png'
if (Test-Path $screenshotPath) {
	Remove-Item -Force $screenshotPath
}
Run-Step -Name 'screenshot' -UcpArgs @('screenshot', '--output', $screenshotPath) -Assert {
	param($r)
	$exists = Test-Path $screenshotPath
	$passed = $exists -and ((Test-UcpSuccess $r) -or $null -eq $r.Json)
	[pscustomobject]@{ Passed = $passed; Detail = "exit=$($r.ExitCode) exists=$exists" }
} | Out-Null

Run-Step -Name 'scene-save-before-play' -UcpArgs @('scene', 'save') -Assert {
	param($r)
	[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
} | Out-Null

Run-Step -Name 'compile-no-wait' -UcpArgs @('compile', '--no-wait') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'play' -UcpArgs @('play') -AllowFailure -Assert {
	param($r)
	$reconnected = Wait-BridgeReady -Reason 'play-domain-reload' -MaxAttempts 15 -DelaySeconds 2
	$passed = $reconnected.Ready
	$detail = if ($reconnected.Ready) { 'bridge-reconnected-after-play' } else { "timeout after play: $($reconnected.Detail)" }
	[pscustomobject]@{ Passed = $passed; Detail = $detail }
} | Out-Null

Run-Step -Name 'pause' -UcpArgs @('pause') -AllowFailure -Assert {
	param($r)
	if (-not (Test-UcpSuccess $r)) {
		$wait = Wait-BridgeReady -Reason 'pause-retry' -MaxAttempts 15 -DelaySeconds 2
		if (-not $wait.Ready) {
			return [pscustomobject]@{ Passed = $false; Detail = "timeout before pause retry: $($wait.Detail)" }
		}
		$r = Invoke-UcpJson -UcpArgs @('pause') -AllowFailure
	}
	[pscustomobject]@{ Passed = (Test-UcpSuccess $r); Detail = $r.Raw }
} | Out-Null

Run-Step -Name 'stop' -UcpArgs @('stop') -AllowFailure -Assert {
	param($r)
	if (-not (Test-UcpSuccess $r)) {
		$wait = Wait-BridgeReady -Reason 'stop-retry' -MaxAttempts 15 -DelaySeconds 2
		if (-not $wait.Ready) {
			return [pscustomobject]@{ Passed = $false; Detail = "timeout before stop retry: $($wait.Detail)" }
		}
		$r = Invoke-UcpJson -UcpArgs @('stop') -AllowFailure
	}
	$ready = Wait-BridgeReady -Reason 'post-stop-stable' -MaxAttempts 15 -DelaySeconds 2
	[pscustomobject]@{
		Passed = ((Test-UcpSuccess $r) -and $ready.Ready)
		Detail = if ($ready.Ready) { $r.Raw } else { "timeout after stop: $($ready.Detail)" }
	}
} | Out-Null

Run-Step -Name 'exec-list' -UcpArgs @('exec', 'list') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'run-tests-edit' -UcpArgs @('run-tests', '--mode', 'edit') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null

Run-Step -Name 'vcs-status-nonfatal' -UcpArgs @('vcs', 'status') -AllowFailure -Assert {
	param($r)
	$passed = (Test-UcpSuccess $r) -or ($r.ExitCode -ne 0)
	[pscustomobject]@{ Passed = $passed; Detail = $r.Raw }
} | Out-Null

if (-not $KeepArtifacts -and $qaRootId -ne 0) {
	Run-Step -Name 'object-delete-root' -UcpArgs @('object', 'delete', '--id', "$qaRootId") -AllowFailure -Assert {
		param($r)
		$alreadyMissing = $r.Raw -match 'GameObject not found'
		$passed = (Test-UcpSuccess $r) -or $alreadyMissing
		$detail = if ($alreadyMissing) { 'already deleted' } else { $r.Raw }
		[pscustomobject]@{ Passed = $passed; Detail = $detail }
	} | Out-Null

	foreach ($artifact in @('Assets/UcpQaFile.txt', 'Assets/UcpQaCapture.png', 'Assets/UcpQaPrefab.prefab', 'Assets/UcpQaPrefab.prefab.meta')) {
		if (Test-Path (Join-Path $Project $artifact)) {
			Remove-Item -Force (Join-Path $Project $artifact)
		}
	}
}

$passedCount = @($results | Where-Object { $_.Passed }).Count
$failedCount = @($results | Where-Object { -not $_.Passed }).Count
$currentStep = 'completed'
Write-QaSummary -Completed

Write-Host "`nQA Summary: passed=$passedCount failed=$failedCount total=$($results.Count)" -ForegroundColor Cyan

if ($failedCount -gt 0) {
	$results | Where-Object { -not $_.Passed } | Format-Table -AutoSize | Out-String | Write-Host
	exit 1
}

exit 0
