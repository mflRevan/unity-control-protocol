param(
	[string]$Project = "C:/Users/aimma/Workspace/unity-control-protocol/unity-project-dev/ucp-dev",
	[switch]$SkipInstall,
	[switch]$KeepArtifacts,
	[int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'cli/Cargo.toml'

$results = New-Object System.Collections.Generic.List[object]

function Invoke-UcpJson {
	param(
		[Parameter(Mandatory = $true)]
		[string[]]$UcpArgs,
		[switch]$AllowFailure
	)

	$command = @(
		'run',
		'--quiet',
		'--manifest-path', $manifestPath,
		'--',
		'--project', $Project,
		'--timeout', "$TimeoutSeconds",
		'--json'
	) + $UcpArgs

	$output = & cargo @command 2>&1
	$exitCode = $LASTEXITCODE
	$text = ($output | Out-String).Trim()

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
}

function Wait-BridgeReady {
	param(
		[int]$MaxAttempts = 60,
		[int]$DelaySeconds = 2
	)

	for ($i = 0; $i -lt $MaxAttempts; $i++) {
		$probe = Invoke-UcpJson -UcpArgs @('connect') -AllowFailure
		if ($probe.ExitCode -eq 0 -and $probe.Json -and $probe.Json.success) {
			return $true
		}
		Start-Sleep -Seconds $DelaySeconds
	}

	return $false
}

function Run-Step {
	param(
		[string]$Name,
		[string[]]$UcpArgs,
		[scriptblock]$Assert,
		[switch]$AllowFailure
	)

	try {
		$result = Invoke-UcpJson -UcpArgs $UcpArgs -AllowFailure:$AllowFailure
		$assertResult = & $Assert $result
		Add-Result -Name $Name -Passed:$assertResult.Passed -Detail $assertResult.Detail
		return $result
	}
	catch {
		Add-Result -Name $Name -Passed:$false -Detail $_.Exception.Message
		return $null
	}
}

Write-Host "Running extensive UCP QA against: $Project" -ForegroundColor Cyan

if (-not (Test-Path (Join-Path $Project 'ProjectSettings/ProjectSettings.asset'))) {
	throw "Not a Unity project: $Project"
}

if (-not $SkipInstall) {
	Run-Step -Name 'install-dev-no-wait' -UcpArgs @('install', '--dev', '--no-wait') -Assert {
		param($r)
		if ($r.ExitCode -ne 0 -or -not $r.Json.success) {
			return [pscustomobject]@{ Passed = $false; Detail = $r.Raw }
		}
		[pscustomobject]@{ Passed = $true; Detail = "bridgeStatus=$($r.Json.data.bridgeStatus)" }
	} | Out-Null
}

# Wait for bridge connectivity.
[void](Wait-BridgeReady -MaxAttempts 90 -DelaySeconds 2)

$connect = Run-Step -Name 'connect' -UcpArgs @('connect') -Assert {
	param($r)
	[pscustomobject]@{ Passed = ($r.ExitCode -eq 0 -and $r.Json.success); Detail = $r.Raw }
}

$snapshot = Run-Step -Name 'snapshot-root' -UcpArgs @('snapshot') -Assert {
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
	[pscustomobject]@{ Passed = ($r.Json.success -and -not [string]::IsNullOrWhiteSpace($name)); Detail = "active=$name" }
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

Run-Step -Name 'write-file' -UcpArgs @('write-file', 'Assets/UcpQaFile.txt', '--content', 'alpha smoke file') -Assert {
	param($r)
	[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
} | Out-Null

Run-Step -Name 'patch-file' -UcpArgs @('patch-file', 'Assets/UcpQaFile.txt', '--find', 'smoke', '--replace', 'patched') -Assert {
	param($r)
	[pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw }
} | Out-Null

Run-Step -Name 'read-file' -UcpArgs @('read-file', 'Assets/UcpQaFile.txt') -Assert {
	param($r)
	$content = $r.Json.data.content
	[pscustomobject]@{ Passed = ($r.Json.success -and $content -match 'patched'); Detail = "content=$content" }
} | Out-Null

Run-Step -Name 'read-file-path-traversal-rejected' -UcpArgs @('read-file', '../outside.txt') -AllowFailure -Assert {
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
	[pscustomobject]@{ Passed = ($r.ExitCode -eq 0 -and $exists); Detail = "exit=$($r.ExitCode) exists=$exists" }
} | Out-Null

Run-Step -Name 'compile-no-wait' -UcpArgs @('compile', '--no-wait') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'play' -UcpArgs @('play') -AllowFailure -Assert {
	param($r)
	$reconnected = Wait-BridgeReady -MaxAttempts 30 -DelaySeconds 2
	$passed = $reconnected
	$detail = if ($reconnected) { 'bridge-reconnected-after-play' } else { $r.Raw }
	[pscustomobject]@{ Passed = $passed; Detail = $detail }
} | Out-Null

Run-Step -Name 'pause' -UcpArgs @('pause') -AllowFailure -Assert {
	param($r)
	if (-not ($r.ExitCode -eq 0 -and $r.Json.success)) {
		if (-not (Wait-BridgeReady -MaxAttempts 30 -DelaySeconds 2)) {
			return [pscustomobject]@{ Passed = $false; Detail = $r.Raw }
		}
		$r = Invoke-UcpJson -UcpArgs @('pause') -AllowFailure
	}
	[pscustomobject]@{ Passed = ($r.ExitCode -eq 0 -and $r.Json.success); Detail = $r.Raw }
} | Out-Null

Run-Step -Name 'stop' -UcpArgs @('stop') -AllowFailure -Assert {
	param($r)
	if (-not ($r.ExitCode -eq 0 -and $r.Json.success)) {
		if (-not (Wait-BridgeReady -MaxAttempts 30 -DelaySeconds 2)) {
			return [pscustomobject]@{ Passed = $false; Detail = $r.Raw }
		}
		$r = Invoke-UcpJson -UcpArgs @('stop') -AllowFailure
	}
	$ready = Wait-BridgeReady -MaxAttempts 30 -DelaySeconds 2
	[pscustomobject]@{ Passed = ($r.ExitCode -eq 0 -and $r.Json.success -and $ready); Detail = $r.Raw }
} | Out-Null

Run-Step -Name 'exec-list' -UcpArgs @('exec', 'list') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null
Run-Step -Name 'run-tests-edit' -UcpArgs @('run-tests', '--mode', 'edit') -Assert { param($r) [pscustomobject]@{ Passed = $r.Json.success; Detail = $r.Raw } } | Out-Null

Run-Step -Name 'vcs-status-nonfatal' -UcpArgs @('vcs', 'status') -AllowFailure -Assert {
	param($r)
	$passed = ($r.ExitCode -eq 0 -and $r.Json.success) -or ($r.ExitCode -ne 0)
	[pscustomobject]@{ Passed = $passed; Detail = $r.Raw }
} | Out-Null

if (-not $KeepArtifacts -and $qaRootId -ne 0) {
	Run-Step -Name 'object-delete-root' -UcpArgs @('object', 'delete', '--id', "$qaRootId") -AllowFailure -Assert {
		param($r)
		$alreadyMissing = $r.Raw -match 'GameObject not found'
		$passed = ($r.ExitCode -eq 0 -and $r.Json.success) -or $alreadyMissing
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

Write-Host "`nQA Summary: passed=$passedCount failed=$failedCount total=$($results.Count)" -ForegroundColor Cyan

if ($failedCount -gt 0) {
	$results | Where-Object { -not $_.Passed } | Format-Table -AutoSize | Out-String | Write-Host
	exit 1
}

exit 0
