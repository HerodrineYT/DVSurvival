[CmdletBinding()]
param(
    [string]$DVInstallDir,
    [switch]$Resolve,
    [switch]$RequireInstalledLatest,
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $projectRoot 'dependencies.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$tested = [version]$lock.multiplayer.testedVersion
$latestVersion = $tested
$latestUrl = [string]$lock.multiplayer.downloadUrl

if (-not $Offline) {
    Write-Host "Checking the official Multiplayer release feed..."
    $feed = Invoke-RestMethod -Uri $lock.multiplayer.releaseFeed -Headers @{
        'User-Agent' = 'DVSurvival-dependency-audit'
    } -TimeoutSec 30
    if ($null -eq $feed.Releases -or $feed.Releases.Count -eq 0) {
        throw 'The official Multiplayer release feed returned no releases.'
    }
    $latest = $feed.Releases | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
    # The UMM feed can lag behind an already published GitHub release (e.g. 0.1.16.0).
    # Check both official sources, including Beta releases, before declaring the lock current.
    $githubReleases = Invoke-RestMethod -Uri $lock.multiplayer.githubReleaseFeed -Headers @{
        'User-Agent' = 'DVSurvival-dependency-audit'
    } -TimeoutSec 30
    $candidates = @($latest)
    foreach ($release in $githubReleases) {
        if ($release.draft -or $release.tag_name -notmatch '^v?(\d+\.\d+\.\d+\.\d+)(-Beta)?$') { continue }
        $releaseVersion = $Matches[1]
        $asset = @($release.assets | Where-Object { $_.name -eq "Multiplayer.$releaseVersion.zip" })
        if ($asset.Count -eq 1) {
            $candidates += [pscustomobject]@{ Version = $releaseVersion; DownloadUrl = $asset[0].browser_download_url }
        }
    }
    if ($candidates.Count -lt 2) { throw 'No supported Multiplayer releases found on GitHub; refusing an incomplete update check.' }
    $latest = $candidates | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
    $latestVersion = [version]$latest.Version
    $latestUrl = [string]$latest.DownloadUrl
    if ($latestVersion -gt $tested) {
        throw "A newer Multiplayer $latestVersion is available. Audit its API and known issues, then update dependencies.lock.json before building."
    }
    if ($latestVersion -lt $tested) {
        throw "The release feed reports $latestVersion, older than the tested lock $tested. Refusing an ambiguous build."
    }
    if ($latestUrl -ne [string]$lock.multiplayer.downloadUrl) {
        throw 'The official Multiplayer download URL differs from dependencies.lock.json.'
    }
    Write-Host "Official latest Multiplayer: $latestVersion (matches the tested lock)."

    Write-Host 'Checking the official Language Helper release feed...'
    try {
        $languageFeed = Invoke-RestMethod -Uri $lock.languageHelper.releaseFeed -Headers @{
            'User-Agent' = 'DVSurvival-dependency-audit'
        } -TimeoutSec 30
    }
    catch {
        Write-Warning 'Language Helper feed is unavailable; checking its official GitHub release.'
        $languageRelease = Invoke-RestMethod -Uri 'https://api.github.com/repos/derail-valley-modding/language-helper/releases/latest' -Headers @{
            'User-Agent' = 'DVSurvival-dependency-audit'
        } -TimeoutSec 30
        if ($languageRelease.tag_name -notmatch '^v?(\d+\.\d+\.\d+)$') { throw 'Invalid Language Helper official release tag.' }
        $languageVersion = $Matches[1]
        $languageAssets = @($languageRelease.assets | Where-Object { $_.name -eq 'DVLangHelper.zip' })
        if ($languageAssets.Count -ne 1) { throw 'Language Helper official release asset is missing or ambiguous.' }
        $languageFeed = [pscustomobject]@{ Releases = @([pscustomobject]@{
            Id = 'DVLangHelper'; Version = $languageVersion; DownloadUrl = $languageAssets[0].browser_download_url
        }) }
    }
    $latestLanguage = $languageFeed.Releases | Where-Object { $_.Id -eq 'DVLangHelper' } |
        Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
    if ($null -eq $latestLanguage -or [version]$latestLanguage.Version -ne [version]$lock.languageHelper.auditedVersion -or
        $latestLanguage.DownloadUrl -ne $lock.languageHelper.downloadUrl) {
        throw 'Language Helper feed differs from the audited lock. Re-audit before building.'
    }
    Write-Host "Official latest Language Helper: $($latestLanguage.Version) (required)."

    # Required custom item adapter: re-audit every release before building against it.
    if ($null -ne $lock.customItemMod) {
        Write-Host 'Checking the official Custom Item Mod release feed...'
        $itemFeed = Invoke-RestMethod -Uri $lock.customItemMod.releaseFeed -Headers @{
            'User-Agent' = 'DVSurvival-dependency-audit'
        } -TimeoutSec 30
        $itemReleases = @($itemFeed.Releases | Where-Object { $_.Id -eq 'custom_item_mod' })
        if ($itemReleases.Count -eq 0) {
            throw 'The official Custom Item Mod release feed returned no matching releases.'
        }
        $latestItem = $itemReleases | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
        if ([version]$latestItem.Version -ne [version]$lock.customItemMod.auditedVersion) {
            throw "Custom Item Mod latest is $($latestItem.Version); audited version is $($lock.customItemMod.auditedVersion). Re-audit the adapter before building."
        }
        else {
            Write-Host "Official latest Custom Item Mod: $($latestItem.Version) (required)."
        }
    }
}

if ($DVInstallDir) {
    $infoPath = Join-Path $DVInstallDir 'Mods\Multiplayer\info.json'
    if (Test-Path -LiteralPath $infoPath -PathType Leaf) {
        $installedInfo = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
        $installed = [version]$installedInfo.Version
        if ($installed -gt $tested) {
            throw "Installed Multiplayer $installed is newer than audited $tested. Re-audit before building."
        }
        if ($installed -lt $latestVersion) {
            $message = "Installed Multiplayer is $installed; official latest is $latestVersion. Update it before multiplayer testing."
            if ($RequireInstalledLatest) { throw $message }
            Write-Warning $message
        }
        else {
            Write-Host "Installed Multiplayer: $installed."
        }
    }
    else {
        $message = "Multiplayer is not installed under '$DVInstallDir\Mods'."
        if ($RequireInstalledLatest) { throw $message }
        Write-Warning $message
    }

    $ummPath = Join-Path $DVInstallDir 'DerailValley_Data\Managed\UnityModManager\UnityModManager.dll'
    if (-not (Test-Path -LiteralPath $ummPath -PathType Leaf)) {
        throw 'Unity Mod Manager is missing from the game Managed directory.'
    }
    $ummVersion = [Reflection.AssemblyName]::GetAssemblyName($ummPath).Version
    if ($ummVersion -lt [version]$lock.unityModManager.minimumVersion) {
        throw "Unity Mod Manager $ummVersion is older than required $($lock.unityModManager.minimumVersion)."
    }
    Write-Host "Unity Mod Manager: $ummVersion."

    $seasonsInfoPath = Join-Path $DVInstallDir 'Mods\DVSeasons\info.json'
    if (Test-Path -LiteralPath $seasonsInfoPath -PathType Leaf) {
        $seasonsInfo = Get-Content -LiteralPath $seasonsInfoPath -Raw | ConvertFrom-Json
        $testedSeasons = [version]$lock.dvSeasons.testedWorkspaceVersion
        $installedSeasons = [version]$seasonsInfo.Version
        $workspaceSeasonsInfo = Join-Path (Split-Path -Parent $projectRoot) 'DVSeasons\DVSeasons.Game\info.source.json'
        if (Test-Path -LiteralPath $workspaceSeasonsInfo -PathType Leaf) {
            $workspaceSeasons = Get-Content -LiteralPath $workspaceSeasonsInfo -Raw | ConvertFrom-Json
            if ([version]$workspaceSeasons.Version -gt $installedSeasons) {
                Write-Warning "DVSeasons workspace source is $($workspaceSeasons.Version); installed runtime is $installedSeasons. Checking the installed binaries below."
            }
        }
        if ($installedSeasons -gt $testedSeasons) {
            throw "Installed optional DVSeasons $installedSeasons is newer than tested $testedSeasons. Audit the temperature reflection adapter before building."
        }
        if ($installedSeasons -lt $testedSeasons) {
            Write-Warning "Installed optional DVSeasons is $installedSeasons; the adapter was tested with $testedSeasons."
        }
        else {
            $seasonsMain = Join-Path $DVInstallDir 'Mods\DVSeasons\DVSeasons.dll'
            $seasonsCore = Join-Path $DVInstallDir 'Mods\DVSeasons\DVSeasons.Core.dll'
            if (-not (Test-Path -LiteralPath $seasonsMain -PathType Leaf) -or
                -not (Test-Path -LiteralPath $seasonsCore -PathType Leaf)) {
                throw 'DVSeasons integration assemblies are missing.'
            }
            $mainHash = (Get-FileHash -LiteralPath $seasonsMain -Algorithm SHA256).Hash.ToLowerInvariant()
            $coreHash = (Get-FileHash -LiteralPath $seasonsCore -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($mainHash -ne [string]$lock.dvSeasons.mainAssemblySha256 -or
                $coreHash -ne [string]$lock.dvSeasons.coreAssemblySha256) {
                throw "Installed DVSeasons $testedSeasons assemblies differ from the audited build."
            }
            Write-Host "Optional DVSeasons integration: $installedSeasons (tested)."
        }
    }
}

$dependencyRoot = Join-Path $projectRoot 'artifacts\dependencies'
$languageCache = Join-Path $dependencyRoot 'LanguageHelper-1.2.1\DVLangHelper'
$languageFiles = @{
    'DVLangHelper.Data.dll' = [string]$lock.languageHelper.dataAssemblySha256
    'DVLangHelper.Runtime.dll' = [string]$lock.languageHelper.runtimeAssemblySha256
}
if ($Resolve) {
    if (-not (Test-Path -LiteralPath (Join-Path $languageCache 'DVLangHelper.Runtime.dll')) -or
        -not (Test-Path -LiteralPath (Join-Path $languageCache 'DVLangHelper.Data.dll'))) {
        if ($Offline) { throw 'Language Helper is missing from the build cache.' }
        New-Item -ItemType Directory -Path $languageCache -Force | Out-Null
        $languageArchive = Join-Path $dependencyRoot 'LanguageHelper.official.zip'
        Invoke-WebRequest -Uri $lock.languageHelper.downloadUrl -OutFile $languageArchive -UseBasicParsing -TimeoutSec 60
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $languageZip = [IO.Compression.ZipFile]::OpenRead($languageArchive)
        try {
            foreach ($fileName in $languageFiles.Keys) {
                $zipEntry = @($languageZip.Entries | Where-Object { $_.Name -eq $fileName })
                if ($zipEntry.Count -ne 1) { throw "Language Helper archive must contain exactly one $fileName." }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($zipEntry[0], (Join-Path $languageCache $fileName), $true)
            }
        }
        finally { $languageZip.Dispose() }
    }
    foreach ($fileName in $languageFiles.Keys) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $languageCache $fileName) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $languageFiles[$fileName]) { throw "Language Helper cache hash mismatch: $fileName" }
    }
    Write-Host 'Verified Language Helper build dependency.'
}
if ($DVInstallDir) {
    $installedLanguageDir = Join-Path $DVInstallDir 'Mods\DVLangHelper'
    $installedLanguageInfo = Join-Path $installedLanguageDir 'Info.json'
    if (Test-Path -LiteralPath $installedLanguageInfo) {
        $languageInfo = Get-Content -LiteralPath $installedLanguageInfo -Raw | ConvertFrom-Json
        if ($languageInfo.Id -ne 'DVLangHelper' -or $languageInfo.Version -ne $lock.languageHelper.auditedVersion) {
            throw 'Installed Language Helper differs from the audited version.'
        }
        foreach ($fileName in $languageFiles.Keys) {
            $hash = (Get-FileHash -LiteralPath (Join-Path $installedLanguageDir $fileName) -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne $languageFiles[$fileName]) { throw "Installed Language Helper hash mismatch: $fileName" }
        }
        Write-Host 'Installed Language Helper: 1.2.1 (verified).'
    }
    else { Write-Warning 'Required Language Helper is not installed. Install it before running DVSurvival.' }
}
$itemCache = Join-Path $dependencyRoot ("CustomItemMod-{0}" -f $lock.customItemMod.auditedVersion)
$itemFiles = @{
    'custom_item_mod.dll' = [string]$lock.customItemMod.mainAssemblySha256
    'custom_item_components.dll' = [string]$lock.customItemMod.componentsAssemblySha256
}
if ($Resolve) {
    if (-not (Test-Path -LiteralPath (Join-Path $itemCache 'custom_item_mod.dll')) -or
        -not (Test-Path -LiteralPath (Join-Path $itemCache 'custom_item_components.dll'))) {
        if ($Offline) { throw 'Custom Item Mod is missing from the build cache.' }
        New-Item -ItemType Directory -Path $itemCache -Force | Out-Null
        $itemArchive = Join-Path $dependencyRoot 'CustomItemMod.official.zip'
        Invoke-WebRequest -Uri $lock.customItemMod.downloadUrl -OutFile $itemArchive -UseBasicParsing -TimeoutSec 60
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $itemZip = [IO.Compression.ZipFile]::OpenRead($itemArchive)
        try {
            foreach ($fileName in @('custom_item_mod.dll', 'custom_item_components.dll', 'info.json', 'README.md', 'LICENSE')) {
                $zipEntry = $itemZip.GetEntry($fileName)
                if ($null -eq $zipEntry) { throw "Custom Item Mod archive is missing $fileName." }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($zipEntry, (Join-Path $itemCache $fileName), $true)
            }
        }
        finally { $itemZip.Dispose() }
    }
    foreach ($fileName in $itemFiles.Keys) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $itemCache $fileName) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $itemFiles[$fileName]) { throw "Custom Item Mod cache hash mismatch: $fileName" }
    }
    Write-Host 'Verified Custom Item Mod build dependency.'
}
if ($DVInstallDir) {
    $installedItemDir = Join-Path $DVInstallDir 'Mods\custom_item_mod'
    $installedItemInfo = Join-Path $installedItemDir 'info.json'
    if (Test-Path -LiteralPath $installedItemInfo) {
        $itemInfo = Get-Content -LiteralPath $installedItemInfo -Raw | ConvertFrom-Json
        if ($itemInfo.Id -ne 'custom_item_mod' -or $itemInfo.Version -ne $lock.customItemMod.auditedVersion) {
            throw 'Installed Custom Item Mod differs from the audited version.'
        }
        foreach ($fileName in $itemFiles.Keys) {
            $hash = (Get-FileHash -LiteralPath (Join-Path $installedItemDir $fileName) -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne $itemFiles[$fileName]) { throw "Installed Custom Item Mod hash mismatch: $fileName" }
        }
        Write-Host 'Installed Custom Item Mod: 0.2.0 (verified).'
    }
    else { Write-Warning 'Required Custom Item Mod is not installed. Install it before running DVSurvival.' }
}
$apiDestination = Join-Path $dependencyRoot 'Multiplayer\MultiplayerAPI.dll'
if ($Resolve) {
    New-Item -ItemType Directory -Path $dependencyRoot -Force | Out-Null
    $archive = Join-Path $dependencyRoot ("Multiplayer.{0}.zip" -f $tested)
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        if ($Offline) { throw "Cached dependency archive not found: $archive" }
        Write-Host "Downloading Multiplayer $tested to the local build cache..."
        Invoke-WebRequest -Uri $lock.multiplayer.downloadUrl -OutFile $archive -UseBasicParsing -Headers @{
            'User-Agent' = 'DVSurvival-dependency-audit'
        } -TimeoutSec 120
    }
    $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($archiveHash -ne [string]$lock.multiplayer.archiveSha256) {
        throw "Multiplayer archive hash mismatch: $archiveHash"
    }
    $expanded = Join-Path $dependencyRoot ("expanded\Multiplayer-{0}" -f $tested)
    if (-not (Test-Path -LiteralPath (Join-Path $expanded 'MultiplayerAPI.dll') -PathType Leaf)) {
        New-Item -ItemType Directory -Path $expanded -Force | Out-Null
        Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force
    }
    $sourceApi = Join-Path $expanded 'MultiplayerAPI.dll'
    $apiHash = (Get-FileHash -LiteralPath $sourceApi -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($apiHash -ne [string]$lock.multiplayer.apiSha256) {
        throw "MultiplayerAPI hash mismatch: $apiHash"
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $apiDestination) -Force | Out-Null
    Copy-Item -LiteralPath $sourceApi -Destination $apiDestination -Force
    Write-Host "Verified MultiplayerAPI: $apiHash"
}

if ($Resolve) { Write-Output $apiDestination }
