[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$baseline = Join-Path $projectRoot 'artifacts\backups\performance-audit-20260928\DVSurvival-0.1.2-GitHub.zip'
$current = Join-Path $projectRoot 'artifacts\build\DVSurvival'
$rows = @()
$archive = [IO.Compression.ZipFile]::OpenRead($baseline)
try {
    foreach ($name in @('DVSurvival.dll', 'DVSurvival.Core.dll', 'DVSurvival.Multiplayer.dll')) {
        foreach ($build in @('baseline', 'optimized')) {
            if ($build -eq 'baseline') {
                $stream = $archive.GetEntry('DVSurvival/' + $name).Open()
                $buffer = [IO.MemoryStream]::new()
                try { $stream.CopyTo($buffer); $bytes = $buffer.ToArray() }
                finally { $stream.Dispose(); $buffer.Dispose() }
            } else { $bytes = [IO.File]::ReadAllBytes((Join-Path $current $name)) }
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') }
            finally { $sha.Dispose() }
            $assembly = [Reflection.Assembly]::Load($bytes)
            $rows += [pscustomobject]@{ build = $build; assembly = $name; sha256 = $hash;
                mvid = $assembly.ManifestModule.ModuleVersionId.ToString(); version = $assembly.GetName().Version.ToString() }
        }
    }
} finally { $archive.Dispose() }
$target = Join-Path $projectRoot 'artifacts\verification\performance-0.1.2-build-identity.json'
$rows | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $target -Encoding utf8
Write-Host "Build identities recorded: $target"
