[CmdletBinding()]
param(
    [string]$DVInstallDir = 'F:\Steam\steamapps\common\Derail Valley',
    [string]$UnityEditor = 'D:\UnityHub\Editor\2019.4.40f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unityProject = Join-Path $projectRoot 'artifacts\verification\UnityClimate'
if (Test-Path -LiteralPath (Join-Path $unityProject 'Temp\UnityLockfile')) { throw 'Unity verification project is already open.' }
foreach ($folder in @('Assets\Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $unityProject $folder) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityClimateEntry.cs') -Destination (Join-Path $unityProject 'Assets\Editor\UnityClimateEntry.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityVerification\Packages\manifest.json') -Destination (Join-Path $unityProject 'Packages\manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityVerification\ProjectSettings\ProjectVersion.txt') -Destination (Join-Path $unityProject 'ProjectSettings\ProjectVersion.txt') -Force
$unityData = Join-Path (Split-Path -Parent $UnityEditor) 'Data'
$compiler = Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\csc.exe'
$mono = Join-Path $unityData 'MonoBleedingEdge\bin\mono.exe'
$managed = Join-Path $unityData 'Managed'
$target = Join-Path $projectRoot 'artifacts\verification\VerifyStationOffice.dll'
New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
$references = @('UnityEngine\UnityEngine.CoreModule.dll', 'UnityEngine\UnityEngine.PhysicsModule.dll',
    'UnityEngine\UnityEngine.dll', 'UnityEditor.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
& $mono $compiler /nologo /target:library "/out:$target" @references (Join-Path $PSScriptRoot 'VerifyStationOffice.cs')
if ($LASTEXITCODE -ne 0) { throw 'Climate fixture compilation failed.' }
$previousGame = $env:DVSURVIVAL_VERIFY_GAME
$previousRoot = $env:DVSURVIVAL_VERIFY_ROOT
try {
    $env:DVSURVIVAL_VERIFY_GAME = $DVInstallDir
    $env:DVSURVIVAL_VERIFY_ROOT = $projectRoot
    $log = Join-Path $projectRoot 'artifacts\verification\thermal-items-0.1.3-climate-unity.log'
    $arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $unityProject + '"'),
        '-executeMethod', 'UnityClimateEntry.Run', '-logFile', ('"' + $log + '"'))
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    # Wait for this editor, not background helper descendants retained by Windows Start-Process -Wait.
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Climate verification failed; see $log" }
    if (-not (Select-String -LiteralPath $log -Pattern 'WORLD_REGISTRY_OK:') -or
        -not (Select-String -LiteralPath $log -Pattern 'TRAVEL_MODELS_OK:') -or
        -not (Select-String -LiteralPath $log -Pattern 'CLIMATE_CACHE_OK:') -or
        -not (Select-String -LiteralPath $log -Pattern 'STATION_OFFICE_OK:')) { throw 'Climate success markers missing.' }
    Write-Host "Climate verification passed: $log"
}
finally { $env:DVSURVIVAL_VERIFY_GAME = $previousGame; $env:DVSURVIVAL_VERIFY_ROOT = $previousRoot }
