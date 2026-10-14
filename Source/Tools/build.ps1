[CmdletBinding()]
param(
    [string]$DVInstallDir,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$SkipOnlineDependencyCheck
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Find-DotNet {
    $candidates = @(
        (Join-Path $env:ProgramW6432 'dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -Unique
    foreach ($candidate in $candidates) {
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) { return $candidate }
    }
    throw 'A 64-bit .NET SDK was not found. Install .NET SDK 8.0.421 or newer.'
}

function Find-DerailValley {
    param([string]$ExplicitPath)
    $candidates = @($ExplicitPath, $env:DERAIL_VALLEY_DIR)
    foreach ($drive in Get-PSDrive -PSProvider FileSystem) {
        $candidates += Join-Path $drive.Root 'Steam\steamapps\common\Derail Valley'
        $candidates += Join-Path $drive.Root 'SteamLibrary\steamapps\common\Derail Valley'
    }
    foreach ($candidate in $candidates | Where-Object { $_ } | Select-Object -Unique) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'DerailValley_Data\Managed\Assembly-CSharp.dll') -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw 'Derail Valley was not found. Pass -DVInstallDir or set DERAIL_VALLEY_DIR.'
}

$dotnet = Find-DotNet
$game = Find-DerailValley -ExplicitPath $DVInstallDir
$dependencyArguments = @{
    DVInstallDir = $game
    Resolve = $true
}
if ($SkipOnlineDependencyCheck) { $dependencyArguments.Offline = $true }
$dependencyOutput = & (Join-Path $PSScriptRoot 'check_dependencies.ps1') @dependencyArguments
if ($LASTEXITCODE -ne 0) { throw 'Dependency verification failed.' }
$multiplayerApi = $dependencyOutput | Select-Object -Last 1
if (-not (Test-Path -LiteralPath $multiplayerApi -PathType Leaf)) {
    throw "Resolved MultiplayerAPI was not found: $multiplayerApi"
}

$modProject = Join-Path $projectRoot 'DVSurvival.Game\DVSurvival.Game.csproj'
$testProject = Join-Path $projectRoot 'DVSurvival.Tests\DVSurvival.Tests.csproj'
Write-Host "Building DVSurvival ($Configuration) against $game"
& $dotnet build $modProject -c $Configuration "-p:DVInstallDir=$game" "-p:MultiplayerApiDll=$multiplayerApi"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

if (-not $SkipTests) {
    & $dotnet test $testProject -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
}

$output = Join-Path $projectRoot 'artifacts\build\DVSurvival'
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'dependencies.lock.json') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $output -Force
New-Item -ItemType Directory -Path (Join-Path $output 'Docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\USER_GUIDE_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\MULTIPLAYER_ARCHITECTURE_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\CUSTOM_ITEM_MOD_INTEGRATION_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\RELEASE_0.1.1_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\RELEASE_0.1.2_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\RELEASE_0.1.3_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\RELEASE_0.2.0_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\RELEASE_0.2.1_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\REBUILD_0.2.0_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\PERFORMANCE_0.1.1_RU.md') -Destination (Join-Path $output 'Docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\PERFORMANCE_0.1.2_RU.md') -Destination (Join-Path $output 'Docs') -Force

& (Join-Path $PSScriptRoot 'verify_project.ps1') -RequireBuildOutput -DVInstallDir $game
if ($LASTEXITCODE -ne 0) { throw 'Project verification failed.' }

& (Join-Path $PSScriptRoot 'package_releases.ps1')

Write-Host "Build output: $output"
