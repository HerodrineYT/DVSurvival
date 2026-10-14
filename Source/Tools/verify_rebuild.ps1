[CmdletBinding()]
param([string]$BaselineDll)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:ProgramW6432 'dotnet\dotnet.exe'
if (-not $BaselineDll) { $BaselineDll = Join-Path $root 'artifacts\backups\before-rewrite-20260930\DVSurvival.Core.dll' }
if (-not (Test-Path -LiteralPath $BaselineDll)) { throw 'Pass -BaselineDll pointing to the pre-rewrite optimized 0.1.2 Core DLL.' }
$probe = Join-Path $PSScriptRoot 'PerformanceProbe\PerformanceProbe.csproj'
& $dotnet build $probe -c Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Could not build the allocation probe.' }
$probeDll = Join-Path $PSScriptRoot 'PerformanceProbe\bin\Release\net8.0\PerformanceProbe.dll'
$runtime = Join-Path $root 'artifacts\build\DVSurvival'
$previousJson = & $dotnet $probeDll $BaselineDll
if ($LASTEXITCODE -ne 0) { throw 'Baseline probe failed.' }
$currentJson = & $dotnet $probeDll (Join-Path $runtime 'DVSurvival.Core.dll')
if ($LASTEXITCODE -ne 0) { throw 'Rebuilt probe failed.' }
$previous = $previousJson | ConvertFrom-Json
$current = $currentJson | ConvertFrom-Json
if ($current.bytesAllocated -ne 0 -or $current.idleCalendarBytes -ne 0 -or $current.invalidEnvironmentBytes -ne 0) {
    throw 'Unexpected allocation in the measured rebuilt paths.'
}
foreach ($property in $previous.finalState.PSObject.Properties) {
    if ($current.finalState.($property.Name) -ne $property.Value) { throw "Probe state diverged: $($property.Name)" }
}
$assemblies = foreach ($name in @('DVSurvival.dll', 'DVSurvival.Core.dll', 'DVSurvival.Multiplayer.dll')) {
    $path = Join-Path $runtime $name
    [ordered]@{ name = $name; version = [Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString(); sha256 = (Get-FileHash -LiteralPath $path).Hash }
}
$report = [ordered]@{
    measuredAtUtc = [DateTime]::UtcNow.ToString('o')
    scope = 'Isolated warmed managed allocation checks under .NET 8; not an in-game FPS or Unity frame-allocation measurement.'
    assemblies = @($assemblies)
    baseline = $previous
    rebuilt = $current
}
$destination = Join-Path $root 'artifacts\verification\rewrite-0.2.0-allocations.json'
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $destination -Encoding UTF8
Write-Host "Allocation verification passed: $destination"
