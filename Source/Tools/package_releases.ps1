[CmdletBinding()]
param([string]$Version)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$runtimeRoot = Join-Path $projectRoot 'artifacts\build\DVSurvival'
$manifest = Get-Content -LiteralPath (Join-Path $runtimeRoot 'info.json') -Raw | ConvertFrom-Json
if (-not $Version) { $Version = $manifest.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $manifest.Version -ne $Version) { throw 'Invalid release version or runtime mismatch.' }
$releaseRoot = Join-Path $projectRoot 'artifacts\releases'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$runtimeFiles = @(Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File | Where-Object { $_.Extension -notin @('.pdb','.zip') })
$sourceFiles = @()
foreach ($folder in @('DVSurvival.Core','DVSurvival.Game','DVSurvival.Multiplayer','DVSurvival.Tests','Tools','Docs')) {
    $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -Recurse -File | Where-Object {
        $relative = $_.FullName.Substring($projectRoot.Length + 1)
        $relative -notmatch '(^|[\\/])(bin|obj|artifacts|node_modules|\.git|\.vs|TestResults)([\\/]|$)' -and
        $_.Extension -in @('.cs','.csproj','.py','.ps1','.cjs','.png','.csv','.json','.md','.txt')
    }
}
foreach ($name in @('DVSurvival.sln','Directory.Build.props','global.json','dependencies.lock.json','README.md','LICENSE','.gitignore')) {
    $sourceFiles += Get-Item -LiteralPath (Join-Path $projectRoot $name)
}
function Add-ReleaseEntry($Zip, $File, [string]$EntryName) {
    if ($File.Length -ge 100000000) { throw "File exceeds repository size limit: $($File.FullName)" }
    if ($EntryName -match '(^|/)\.\.(/|$)' -or $EntryName.StartsWith('/')) { throw 'Unsafe ZIP entry path.' }
    $entry = $Zip.CreateEntry($EntryName, [IO.Compression.CompressionLevel]::Optimal)
    $inputStream = [IO.File]::OpenRead($File.FullName)
    $outputStream = $entry.Open()
    try { $inputStream.CopyTo($outputStream) }
    finally { $outputStream.Dispose(); $inputStream.Dispose() }
}
foreach ($edition in @('Nexus','GitHub')) {
    $archive = Join-Path $releaseRoot "DVSurvival-$Version-$edition.zip"
    # The only replaced target is this exact version's generated archive inside artifacts/releases.
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($archive)) -ne (Resolve-Path -LiteralPath $releaseRoot).Path) { throw 'Archive escaped release directory.' }
    $fileStream = [IO.File]::Open($archive, [IO.FileMode]::Create)
    $zip = New-Object IO.Compression.ZipArchive($fileStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($file in $runtimeFiles) {
            Add-ReleaseEntry $zip $file ('DVSurvival/' + $file.FullName.Substring($runtimeRoot.Length + 1).Replace('\','/'))
        }
        if ($edition -eq 'GitHub') {
            foreach ($file in $sourceFiles) {
                Add-ReleaseEntry $zip $file ('DVSurvival/Source/' + $file.FullName.Substring($projectRoot.Length + 1).Replace('\','/'))
            }
        }
    }
    finally { $zip.Dispose(); $fileStream.Dispose() }
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $zip.Entries) {
            if ($entry.Length -ge 100000000 -or -not $names.Add($entry.FullName) -or
                $entry.FullName -match '\.(pdb|zip)$|/(bin|obj|node_modules|artifacts)/') { throw "Invalid release entry: $($entry.FullName)" }
        }
        if ($null -eq $zip.GetEntry('DVSurvival/Localization/strings.csv')) { throw 'Translation catalogue missing in release.' }
        if ($edition -eq 'Nexus' -and ($zip.Entries | Where-Object { $_.FullName -like 'DVSurvival/Source/*' })) { throw 'Nexus release contains sources.' }
        Write-Host "$edition archive verified: $($zip.Entries.Count) files, every file below 100 MB. $archive"
    }
    finally { $zip.Dispose() }
}
