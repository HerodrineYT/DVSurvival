[CmdletBinding()]
param(
    [switch]$RequireBuildOutput,
    [string]$DVInstallDir
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'artifacts\build\DVSurvival'
$manifestSource = Join-Path $projectRoot 'DVSurvival.Game\info.source.json'
$manifest = Get-Content -LiteralPath $manifestSource -Raw | ConvertFrom-Json
$releaseVersion = '0.1.3'
$protocolVersion = 26

if ($manifest.Id -ne 'DVSurvival') { throw 'Unexpected mod id in info.source.json.' }
if ($manifest.Version -ne $releaseVersion) { throw 'Manifest version does not match this release.' }
if ($manifest.HomePage -ne 'https://www.nexusmods.com/derailvalley/mods/1713') { throw 'HomePage must point to the requested Nexus Mods page.' }
if ($manifest.MultiplayerCompatibility -ne 'All') { throw 'The mod must require matching host/client installs.' }
if ('Multiplayer' -in $manifest.Requirements) { throw 'Multiplayer must remain optional.' }
if ('custom_item_mod-0.2.0' -notin $manifest.Requirements) { throw 'Custom Item Mod 0.2.0 must be a required dependency.' }
if ('DVLangHelper-1.2.1' -notin $manifest.Requirements) { throw 'Language Helper 1.2.1 must be a required dependency.' }

$translations = @(Import-Csv -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\Localization\strings.csv') -Encoding UTF8)
if ($translations.Count -lt 90 -or ($translations | Where-Object { -not $_.English -or -not $_.Russian })) { throw 'Incomplete RU/EN Language Helper catalogue.' }
if (($translations | Select-Object -ExpandProperty Key -Unique).Count -ne $translations.Count) { throw 'Duplicate translation keys.' }
if ('Grandpa''s moonshine' -in $translations.English -or 'Alcohol harms your health.' -in $translations.English) { throw 'Retired alcohol text remains in the runtime catalogue.' }
if ('You have not rested properly for a long time. Sleep well to avoid consequences.' -notin $translations.English) { throw 'Delayed sleep-deprivation warning is not localized.' }
$speedometerSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\FatigueSpeedometer.cs') -Raw
if ($speedometerSource -notmatch 'IndicatorGaugeLagging' -or $speedometerSource -notmatch 'LateTick' -or
    $speedometerSource -notmatch 'IndicatorPortReader') { throw 'Final DE2 speedometer enforcement is missing.' }
$localizationSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\ModLocalization.cs') -Raw
if ($localizationSource -notmatch 'new TranslationInjector' -or $localizationSource -notmatch 'AddTranslationsFromCsv') { throw 'Translations must use Language Helper.' }

$sourceRequired = @(
    'DVSurvival.Core\GameCalendarClock.cs',
    'DVSurvival.Core\SleepCalendarReconciler.cs',
    'DVSurvival.Core\SurvivalConstants.cs',
    'DVSurvival.Core\CabHeaterActionValidator.cs',
    'DVSurvival.Core\SurvivalSimulator.cs',
    'DVSurvival.Core\Simulation\NeedsEngine.cs',
    'DVSurvival.Core\Runtime\PlayerSlots.cs',
    'DVSurvival.Core\Runtime\SessionSchedule.cs',
    'DVSurvival.Game\NeedsPresenter.cs',
    'DVSurvival.Game\SurvivalRuntime.cs',
    'DVSurvival.Game\EnvironmentSampler.cs',
    'DVSurvival.Game\CabHeaterSwitchSystem.cs',
    'DVSurvival.Game\PlayerImpactMonitor.cs',
    'DVSurvival.Game\SurvivalHud.cs',
    'DVSurvival.Multiplayer\MultiplayerSurvivalBridge.cs',
    'Docs\RELEASE_0.2.0_RU.md',
    'dependencies.lock.json',
    'README.md'
)
foreach ($relative in $sourceRequired) {
    $path = Join-Path $projectRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing project file: $relative" }
}
$constantsSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Core\SurvivalConstants.cs') -Raw
if ($constantsSource -notmatch ("ProtocolVersion\s*=\s*" + $protocolVersion) -or
    $constantsSource -notmatch ('ModVersion\s*=\s*"' + [regex]::Escape($releaseVersion) + '"')) {
    throw 'Core mod/protocol constants do not match this release.'
}
foreach ($project in @('DVSurvival.Core\DVSurvival.Core.csproj',
        'DVSurvival.Game\DVSurvival.Game.csproj', 'DVSurvival.Multiplayer\DVSurvival.Multiplayer.csproj')) {
    [xml]$projectXml = Get-Content -LiteralPath (Join-Path $projectRoot $project) -Raw
    if ($projectXml.Project.PropertyGroup.Version -ne $releaseVersion) {
        throw "Project version does not match this release: $project"
    }
}

# Guard the frame-hot paths that caused the 0.1.0 FPS regression.
$runtimeSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\SurvivalRuntime.cs') -Raw
$runtimeSource += (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\Runtime') -Filter 'SurvivalRuntime.*.cs' | Sort-Object Name | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
$environmentSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\EnvironmentSampler.cs') -Raw
$impactSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\PlayerImpactMonitor.cs') -Raw
$hudSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\SurvivalHud.cs') -Raw
$bridgeSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Multiplayer\MultiplayerSurvivalBridge.cs') -Raw
$mainSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\Main.cs') -Raw
$warningSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Core\SurvivalWarningTracker.cs') -Raw
$climateSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Core\CabinClimate.cs') -Raw
$calendarSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Core\GameCalendarClock.cs') -Raw
$calendarReconcilerSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Core\SleepCalendarReconciler.cs') -Raw
$contractsSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Core\NetworkContracts.cs') -Raw
$actionKindsSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Core\ProvisionKind.cs') -Raw
$packetsSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Multiplayer\SurvivalPackets.cs') -Raw
$heaterSwitchSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\CabHeaterSwitchSystem.cs') -Raw
$saveSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\SurvivalSaveData.cs') -Raw
if ($runtimeSource -notmatch 'NeedsEngine\.Advance' -or
    $runtimeSource -notmatch 'PlayerSlots<SurvivalPlayerRecord>' -or
    $runtimeSource -notmatch 'schedule\.TakeSimulation' -or
    $runtimeSource -notmatch 'schedule\.TakeTreatment' -or
    $runtimeSource -notmatch 'state\.CopyTo\(currentState\)' -or
    $hudSource -notmatch 'presenter\.Capture' -or
    $calendarReconcilerSource -notmatch 'committedScratch\.Clear\(\)') {
    throw 'Rebuilt simulation, storage, schedule or retained presentation is not connected.'
}
if ($runtimeSource -notmatch 'LocalEnvironmentIntervalSeconds' -or
    $runtimeSource -notmatch 'nextLocalEnvironmentUpdate') {
    throw 'The local environment sampler must remain rate-limited.'
}
if ($runtimeSource -notmatch 'activeRecords\.TryGetValue\(localId, out existing\)') {
    throw 'The local authority record needs its no-allocation fast path.'
}
if ($runtimeSource -notmatch 'AppUtilProbeIntervalSeconds') {
    throw 'AppUtil lookup must remain cached and rate-limited.'
}
if ($environmentSource -notmatch 'FireboxRegistry.GetLoaded\(\)' -or
    $environmentSource -match 'FindObjectsOfType<FireboxSimController>') {
    throw 'Firebox discovery must remain cached and rate-limited.'
}
if ($environmentSource -notmatch 'TrainCarType\.LocoDM1U' -or
    $environmentSource -notmatch 'cachedDm1uHeaterControl' -or
    $environmentSource -notmatch 'heatingRateMultiplier = isDm1u && !fanOn \? 0\.25f : isDm3 \? heaterPower : 1f' -or
    $environmentSource -notmatch 'running && heaterAllowsEngineHeat' -or
    $environmentSource -notmatch 'if \(!isDm1u && cachedFirebox != null\)' -or
    $environmentSource -notmatch 'NormalizedName.Contains\(name, "cheatingrotary", true\)' -or
    $climateSource -notmatch '!heaterEnabled' -or
    $climateSource -notmatch 'var artificialBoost' -or
    $climateSource -notmatch 'seconds \* heatingRateMultiplier') {
    throw 'DM1U heater/fan-gated quarter-speed cabin heating is missing.'
}
if ($calendarSource -notmatch 'MaximumObservedJumpHours = 168d' -or
    $calendarSource -notmatch 'LastObservedTicks' -or
    $calendarSource -notmatch 'IsDiscontinuousAdvance' -or
    $calendarSource -notmatch 'elapsed\.TotalHours' -or
    $runtimeSource -notmatch 'private readonly SleepCalendarReconciler sleepCalendar' -or
    $runtimeSource -notmatch 'gameCalendarClock\.LastObservedTicks' -or
    $runtimeSource -notmatch 'gameCalendarClock\.Observe\(current\)' -or
    $runtimeSource -notmatch 'GameCalendarClock\.IsDiscontinuousAdvance' -or
    $runtimeSource -notmatch 'environment\.TryGetGameDateTime' -or
    $runtimeSource -notmatch 'AdvanceInCalendarChunks' -or
    $runtimeSource -notmatch 'sleepCalendar\.TryRecordJump' -or
    $runtimeSource -notmatch 'sleepCalendar\.TryRecordOrderedAdvance' -or
    $runtimeSource -notmatch 'sleepCalendar\.TryRecordSleep' -or
    $runtimeSource -notmatch 'DrainCalendarOperations\(now\)' -or
    $runtimeSource -notmatch 'sleepCalendar\.DrainOperations\(nowSeconds\)' -or
    $runtimeSource -notmatch 'SleepCalendarOperationKind\.CommitSleep' -or
    $runtimeSource -notmatch 'CommitPendingNativeSleep\(operation\.Sleep\)' -or
    $runtimeSource -notmatch 'SleepCalendarOperationKind\.RejectSleep' -or
    $runtimeSource -notmatch 'RejectPendingNativeSleep\(operation\.Sleep\)' -or
    $calendarReconcilerSource -notmatch 'public sealed class SleepCalendarReconciler' -or
    $calendarReconcilerSource -notmatch 'DefaultGracePeriodSeconds = 10d' -or
    $calendarReconcilerSource -notmatch 'DurationAlignmentToleranceTicks = TimeSpan\.TicksPerSecond' -or
    $calendarReconcilerSource -notmatch 'AllowsDurationAlignment' -or
    $calendarReconcilerSource -notmatch 'BestDurationAlignmentJumpIndex' -or
    $calendarReconcilerSource -notmatch 'AmbiguousDurationAlignmentIndex' -or
    $calendarReconcilerSource -notmatch 'if \(durationAlignmentIndex >= 0\)' -or
    $calendarReconcilerSource -notmatch 'if \(jumpIndex != durationAlignmentIndex\) return false;' -or
    $calendarReconcilerSource -notmatch 'TryRecordOrderedAdvance' -or
    $calendarReconcilerSource -notmatch 'DrainOperations' -or
    $calendarReconcilerSource -notmatch 'SleepCalendarOperationKind\.CommitSleep' -or
    $calendarReconcilerSource -notmatch 'SleepCalendarOperationKind\.RejectSleep' -or
    $calendarReconcilerSource -notmatch 'GetAwakeHours' -or
    $contractsSource -notmatch 'CalendarBeforeTicks' -or
    $contractsSource -notmatch 'CalendarAfterTicks' -or
    $packetsSource -notmatch 'writer\.Write\(value\.CalendarBeforeTicks\)' -or
    $packetsSource -notmatch 'writer\.Write\(value\.CalendarAfterTicks\)' -or
    $environmentSource -notmatch 'weather\.manager\.RealDateTime') {
    throw 'Sleep deprivation must use ordered SleepCalendarReconciler operations and exact native-sleep calendar intervals.'
}

if ($runtimeSource -notmatch 'public void OnWorldTimeAdvanceCompleted' -or
    $runtimeSource -notmatch 'ObserveCalendar\(before, realSeconds, false\)' -or
    $runtimeSource -notmatch 'sleepCalendar\.PendingSleepCount > 0' -or
    $runtimeSource -notmatch 'ObserveCalendar\(after, 0f, true,' -or
    $runtimeSource -notmatch 'public bool OnLocalSleepStarting' -or
    $runtimeSource -notmatch '!HasConfirmedLocalState \|\| lastHostMessage == null' -or
    $runtimeSource -notmatch 'var sent = RequestAction' -or
    $runtimeSource -notmatch '(?s)var sent = network\.SendAction\(request\);.*?return sent;' -or
    $contractsSource -notmatch 'bool SendAction\(SurvivalActionRequest request\)' -or
    $runtimeSource -notmatch 'new SleepCalendarJump\(sequence, beforeTicks, afterTicks,' -or
    $runtimeSource -notmatch 'Time\.realtimeSinceStartup, allowsDurationAlignment\)' -or
    $runtimeSource -notmatch 'BeginAuthorityEpoch\(\)' -or
    $runtimeSource -notmatch 'document\.SessionId = Guid\.NewGuid\(\)\.ToString\("D"\)' -or
    $runtimeSource -notmatch 'retiredHostSessions\.Contains\(message\.SessionId\)' -or
    $runtimeSource -notmatch 'RetireHostSession\(lastHostMessage\.SessionId\)' -or
    $runtimeSource -notmatch 'HandleNetworkConnectionGeneration' -or
    $runtimeSource -notmatch 'network\.ConnectionGeneration' -or
    $bridgeSource -notmatch 'public uint ConnectionGeneration' -or
    $bridgeSource -notmatch 'AdvanceConnectionGeneration\(\)' -or
    $runtimeSource -match '(?s)private void HandleAuthorityTransition\(\).*?DrainCalendarOperations\(double\.MaxValue\).*?private void EnsureLocalAuthorityRecord') {
    throw 'Host TimeAdvance capture and authority-epoch replay protection are incomplete.'
}

$actionPacketStart = $packetsSource.IndexOf('public sealed class SurvivalActionPacket',
    [StringComparison]::Ordinal)
$environmentPacketStart = $packetsSource.IndexOf('public sealed class SurvivalEnvironmentPacket',
    [StringComparison]::Ordinal)
if ($actionPacketStart -lt 0 -or $environmentPacketStart -le $actionPacketStart) {
    throw 'The survival action packet source section could not be located.'
}
$actionPacketSource = $packetsSource.Substring($actionPacketStart,
    $environmentPacketStart - $actionPacketStart)
if ($contractsSource -notmatch '(?s)class SurvivalActionRequest.*?public string SessionId = string\.Empty;' -or
    $contractsSource -notmatch '(?s)interface ISurvivalNetworkBridge.*?uint ConnectionGeneration \{ get; \}' -or
    $runtimeSource -notmatch 'request\.SessionId = network\.IsSessionActive' -or
    $runtimeSource -notmatch '(?s)private void ProcessAction\(.*?if \(request\.Protocol != SurvivalConstants\.ProtocolVersion\).*?return;\s*\}\s*if \(document == null \|\| string\.IsNullOrEmpty\(request\.SessionId\)' -or
    $actionPacketSource -notmatch '(?s)writer\.Write\(value\.Protocol\);.*?writer\.Write\(value\.SessionId.*?writer\.Write\(value\.RequestId\);' -or
    $actionPacketSource -notmatch '(?s)var protocol = reader\.ReadInt32\(\);\s*if \(protocol != SurvivalConstants\.ProtocolVersion\)\s*\{.*?Request = new SurvivalActionRequest \{ Protocol = protocol \};\s*return;.*?\}\s*Request = new SurvivalActionRequest\s*\{.*?SessionId = Safe\(reader\.ReadString\(\), 80\)') {
    throw 'Actions must carry the current session id and reject protocol mismatches before reading the versioned payload.'
}
if ($environmentSource -notmatch 'TrainCarType\.LocoShunter' -or
    $environmentSource -notmatch 'TrainCarType\.LocoDH4' -or
    $environmentSource -notmatch 'TrainCarType\.LocoDiesel' -or
    $environmentSource -notmatch 'CabFanOutputPortId = "cabFan\.OUTPUT"' -or
    $environmentSource -notmatch 'var vanillaDieselHeaterOn = isVanillaDieselHeater && heaterPower > 0f' -or
    $environmentSource -notmatch 'heaterControlReady = cabHeaters\.Ensure\(car\)' -or
    $environmentSource -notmatch 'var heaterAllowsEngineHeat = isDm1u \? dm1uHeaterOn' -or
    $environmentSource -notmatch 'fanOutputValue > 0\.1f' -or
    $environmentSource -notmatch 'if \(sameContext && \(cachedCabScanComplete \|\|') {
    throw 'DE2, DH4 and DE6 must use an independent cached CabHeater while CabFan remains separate.'
}
if ($heaterSwitchSource -notmatch 'TemplatePath = "PanelCluster/CabHeater"' -or
    $heaterSwitchSource -notmatch 'rotary\.notches = 2;' -or
    $heaterSwitchSource -notmatch 'rotary\.jointLimitMax = 30f;' -or
    $heaterSwitchSource -notmatch 'TrainCarType\.LocoDM3' -or
    $heaterSwitchSource -notmatch 'container\.SetActive\(false\)' -or
    $heaterSwitchSource -notmatch 'container\.SetActive\(true\)' -or
    $heaterSwitchSource -notmatch 'GetComponent<ControlImplBase>' -or
    $heaterSwitchSource -notmatch 'control\.ValueChanged \+= OnValueChanged' -or
    $heaterSwitchSource -notmatch 'MaximumSetupAttempts = 3' -or
    $environmentSource -notmatch 'cabHeaters\.IsSetupSettled\(car\)' -or
    $heaterSwitchSource -notmatch 'new Dictionary<string, float>\(StringComparer\.OrdinalIgnoreCase\)' -or
    $heaterSwitchSource -match '\bUpdate\s*\(' -or
    $heaterSwitchSource -match 'FindObjectsOfType|FindObjectOfType|GameObject\.Find') {
    throw 'Physical CabHeater controls must retain stock desktop/VR setup and avoid frame-hot searches.'
}
if ($actionKindsSource -notmatch 'SetCabHeater = 6' -or
    $contractsSource -notmatch 'OccupiedCarId' -or
    $contractsSource -notmatch 'OccupiedCarSupportsCabHeater' -or
    $contractsSource -notmatch 'CabHeaterCarId' -or
    $contractsSource -notmatch 'CabHeaterLevel' -or
    $packetsSource -notmatch 'Safe\(value\.CabHeaterCarId, 80\)' -or
    $bridgeSource -notmatch 'OccupiedCarSupportsCabHeater = SupportsCabHeater\(car\)' -or
    $runtimeSource -notmatch 'CabHeaterActionValidator\.IsValid' -or
    $runtimeSource -notmatch 'document\.SetCabHeater' -or
    $runtimeSource -notmatch '(?s)private void ResetCabHeaterControls\(\).*?cabHeaters\.Reset\(\);.*?environment\.InvalidateCabControls\(\);' -or
    $saveSource -notmatch 'maximumRecords = 128') {
    throw 'Host-authoritative CabHeater validation, persistence or protocol fields are incomplete.'
}
if ($impactSource -notmatch 'OverlapCapsuleNonAlloc' -or
    $impactSource -notmatch 'probeCadence.Interval\(now\)' -or
    $impactSource -notmatch 'Physics.CheckSphere' -or
    $impactSource -notmatch 'probeCadence.ObserveRisk\(dismountedAt\)') {
    throw 'Train contact fallback must remain non-allocating and rate-limited.'
}
if ($impactSource -notmatch 'Train_Big_Collider' -or
    $impactSource -notmatch 'OnTriggerEnter') {
    throw 'Train contact detection must remain restricted to the train collider layer with callback fallback.'
}
if ($runtimeSource -notmatch 'MarkerType\.House' -or
    $runtimeSource -notmatch 'playerTeleportAnchor') {
    throw 'Death respawn must keep using the marked house fast-travel anchor.'
}
if ($runtimeSource -notmatch 'GetComponentInChildren<CustomFirstPersonController>') {
    throw 'The impact monitor must remain attached to the actual player physics controller.'
}
$officeSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\StationOfficeVolumes.cs') -Raw
if ($environmentSource -notmatch 'stationOffices\.Contains\(BuildingProbePosition' -or
    $environmentSource -notmatch 'PlayerManager\.PlayerCamera\.transform\.position' -or
    $environmentSource -notmatch 'Mathf\.Max\(ambient, HeatedBuildingTemperature\)' -or
    $officeSource -notmatch 'ClimateSceneDiscovery.EnsureInitialized\(\)' -or
    $officeSource -notmatch 'IsOfficeInterior\(controller\.transform\.parent\)' -or
    $officeSource -notmatch 'TutorialPlayerDetectorType\.StationOffice' -or
    $officeSource -notmatch 'InverseTransformPoint\(position\) - room\.center' -or
    $officeSource -notmatch 'room\.size \* \.5f' -or
    $officeSource -match 'FindObjectsOfType') {
    throw 'Station offices must use cached authored room volumes, camera containment and a heating-only thermostat.'
}
$climateLifecycleSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\ClimateObjectLifecycle.cs') -Raw
$climateSceneSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\ClimateSceneDiscovery.cs') -Raw
$fireboxSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\FireboxRegistry.cs') -Raw
if ($climateLifecycleSource -notmatch 'typeof\(PostProcessingVolumeAOController\), "OnEnable"' -or
    $climateLifecycleSource -notmatch 'typeof\(TutorialPlayerDetector\), "Awake"' -or
    $climateLifecycleSource -notmatch 'nameof\(FireboxSimController.Init\)' -or
    $climateSceneSource -notmatch 'SceneManager.sceneLoaded \+=' -or
    $climateSceneSource -notmatch 'SceneManager.sceneLoaded -=' -or
    $climateSceneSource -notmatch 'scene.GetRootGameObjects' -or
    $fireboxSource -match 'FindObjectsOfType' -or
    $climateSceneSource -notmatch 'n < 64' -or
    $climateLifecycleSource -notmatch 'typeof\(Shop\), "Awake"' -or
    $climateLifecycleSource -notmatch 'typeof\(BedSleeping\), "Start"' -or
    $environmentSource -match 'FindObjectsOfType<FireboxSimController>' -or
    $environmentSource -notmatch 'cabScanRetry.RecordAttempt' -or
    $environmentSource -notmatch 'cabinStatus.Capture') {
    throw 'Climate discovery must keep lifecycle registration, rare reconciliation, retry backoff and lazy diagnostics.'
}
if ((Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\SurvivalModSettings.cs') -Raw) -notmatch
    'TemperatureChangeMultiplier') {
    throw 'The configurable body temperature change rate is missing.'
}
if ($runtimeSource -match 'PlayerManager.TeleportPlayer\(|IsPlayerPositionValid\(position\)' -or
    $runtimeSource -notmatch 'NativeHomeFastTravel.Start' -or
    $runtimeSource -notmatch 'nextHouseFallbackScan = Time.realtimeSinceStartup \+ 5f') {
    throw 'House respawn must use native fast travel and bounded house lookups, not raw teleports.'
}
$nativeTravelSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\NativeHomeFastTravel.cs') -Raw
$patchSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\SurvivalPatches.cs') -Raw
if ($nativeTravelSource -notmatch 'house, withoutLoco, false, 0' -or
    $nativeTravelSource -match 'RemoveMoney\(|SetMoney\(|Physics\.' -or
    $patchSource -match 'PlayerHomeRespawnHoldPatch' -or
    $runtimeSource -notmatch 'homeRespawn.Finish\(revision, error == null\)') {
    throw 'Respawn must preserve native loading, bypass travel payment and ignore stale callbacks.'
}
if ($hudSource -notmatch 'EventType\.Repaint' -or $hudSource -notmatch 'nextDisplayRefresh') {
    throw 'The IMGUI HUD must remain repaint-only with rate-limited text updates.'
}
if ($hudSource -match 'LowRestStarted|/48 |Until distortion ' -or
    $mainSource -match 'Continuous low rest, game hours: |Until speedometer distortion: ' -or
    $warningSource -match 'LowRestStarted' -or
    $warningSource -notmatch 'previousLowRestHours >= 24d' -or
    $runtimeSource -notmatch 'hud\.ResetStateWarnings\(\)' -or
    $hudSource -notmatch 'ClearLowRestWarnings\(\)') {
    throw 'Sleep-deprivation warnings must stay silent before 24 hours and contain no live counters.'
}
if ($patchSource -notmatch 'HarmonyPatch\(typeof\(TimeAdvance\), "AdvanceTime"\)' -or
    $patchSource -notmatch 'OnLocalSleepWithoutTimeAdvanceCompleted' -or
    $runtimeSource -notmatch 'PersonalSleepValidator\.IsValid\(network\.IsSessionActive' -or
    $bridgeSource -match '(?s)public bool IsNativeSleepTimeSuppressed.*?PlayerCount.*?public bool IsAvailable' -or
    $runtimeSource -notmatch 'private bool IsPersonalSleepMode => PersonalSleepValidator\.IsNoAdvanceMode' -or
    $environmentSource -notmatch 'weather\.TimeOfDayHours\.IsOverridden' -or
    $bridgeSource -notmatch 'GetProperty\("FastTravelAdvancesTime"\)' -or
    $patchSource -notmatch 'HarmonyBefore\("Multiplayer"\)' -or
    $patchSource -notmatch 'private static void Prefix' -or
    $patchSource -notmatch 'BeforeTicks' -or
    $patchSource -notmatch 'after\.Ticks <= __state\.BeforeTicks' -or
    $patchSource -notmatch 'runtime\.OnLocalSleepStarting' -or
    $patchSource -notmatch 'runtime\.OnWorldTimeAdvanceCompleted\(__state\.BeforeTicks, after\.Ticks,' -or
    $patchSource -notmatch 'force \|\|' -or
    $patchSource -notmatch '__state\.IsNativeSleep = NativeSleepOriginPatch\.IsAdvancingSleep' -or
    $patchSource -notmatch 'AccessTools\.EnumeratorMoveNext' -or
    $patchSource -notmatch 'typeof\(BedSleepingController\), "SleepCoro"' -or
    $patchSource -notmatch 'finally \{ activeBed = previousBed; advanceDepth--; \}' -or
    $patchSource -notmatch 'new CodeInstruction\(OpCodes.Ldfld, bedField\)' -or
    $runtimeSource -notmatch 'environment\.IsPlayerNearNativeBed\(player, request\)' -or
    $environmentSource -notmatch 'NativeBedValidation\.IsNearReportedBed' -or
    $patchSource -match 'StackTrace|controller\.IsSleeping' -or
    $patchSource -match 'HarmonyPatch\(typeof\(BedSleepingController\), "Sleep"\)' -or
    $runtimeSource -notmatch 'OnLocalSleepCompleted' -or
    $runtimeSource -match 'OnLocalSleepRequested') {
    throw 'Native sleep must be marked at the exact SleepCoro time skip, without stack inspection or animation timers.'
}
if ($hudSource -notmatch '!LoadingScreenManager.IsLoading' -or
    $hudSource -notmatch '!FastTravelController.IsFastTravelling' -or
    $hudSource -notmatch '!runtime.IsHomeTravelPending' -or
    $hudSource -notmatch '!locoHudVisibility.BlocksSurvivalHud\(\)' -or
    ([regex]::Matches($hudSource, 'if \(!CanDisplay\(\)\) return;').Count -ne 1) -or
    $hudSource -notmatch 'var visible = CanDisplay\(\);' -or
    $hudSource -notmatch 'if \(!visible\) return;') {
    throw 'HUD drawing and refresh must stop during native travel, loading, pending home respawn and the locomotive menu.'
}
if ($bridgeSource -notmatch 'get \{ return isSupported; \}') {
    throw 'Multiplayer version compatibility must remain cached.'
}
if ($runtimeSource -notmatch 'RefreshLocalTravelPause\(\)' -or
    $runtimeSource -notmatch 'calendarTravelPauses.TryGetValue\(advance.SourceJumpSequence' -or
    $runtimeSource -notmatch 'travelPausedPlayers: observedTravelPause' -or
    $runtimeSource -notmatch 'travelPause.Update\(player.PlayerId, report.IsTravelling' -or
    $runtimeSource -notmatch 'string.Equals\(report.SessionId, document.SessionId' -or
    $patchSource -notmatch 'runtime.RefreshLocalTravelPause\(\)' -or
    $bridgeSource -notmatch 'environmentDelivery.Prepare\(report' -or
    $bridgeSource -notmatch 'new SurvivalEnvironmentPacket \{ Report = report \}, reliable' -or
    $runtimeSource -notmatch 'order.Accept\(report.TravelRevision, report.IsTravelling\)') {
    throw 'Personal travel pause, deferred calendar protection or reliable session-scoped reports are missing.'
}
if ($mainSource -match '\[EnableReloading\]') {
    throw 'Hot reload is unsafe because Multiplayer packet handlers cannot be unregistered.'
}

$catalogSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\CustomProvisionCatalog.cs') -Raw
if ($catalogSource -notmatch 'Amount = 5' -or $catalogSource -notmatch 'i < ProvisionCatalog.Count' -or
    $catalogSource -match 'ProvisionLocalizationPatch' -or
    $catalogSource -notmatch 'localizationKeyName = ModLocalization.ProvisionKey') { throw 'Native stock limits or Language Helper item terms are missing.' }
$personalItemsSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\PersonalProvisionItems.cs') -Raw
$modelsSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\ProvisionModels.cs') -Raw
$inventoryArtworkSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\ProvisionInventoryArtwork.cs') -Raw
$layoutSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\SurvivalHud.Layouts.cs') -Raw
$settingsSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\SurvivalModSettings.cs') -Raw
if ($settingsSource -notmatch 'DisableNeedsConsumption = DisableNeedsConsumption' -or
    $mainSource -notmatch 'settings.DisableNeedsConsumption = GUILayout.Toggle' -or
    $runtimeSource -notmatch 'previewTuning.DisableNeedsConsumption = lastHostMessage.DisableNeedsConsumption' -or
    $runtimeSource -notmatch 'DisableNeedsConsumption = tuning.DisableNeedsConsumption' -or
    'Disable needs consumption' -notin $translations.English) {
    throw 'Needs-consumption setting, localization or host-authoritative sleep preview is missing.'
}
if ($catalogSource -notmatch 'new ShelfDisplayLayout' -or
    $catalogSource -notmatch 'display.localPosition = new Vector3\(placement.OffsetX, placement.OffsetY, placement.OffsetZ\)' -or
    $catalogSource -notmatch 'shelfItem.height = placement.Height' -or
    $layoutSource -notmatch 'DrawLegacyNeeds\(0, 0\)' -or
    $mainSource -notmatch 'HorizontalSlider\(settings.HudScale, 0.65f, 1.75f\)' -or
    $mainSource -match 'HorizontalSlider\(settings.CompactHudScale') {
    throw 'Shelf centering, original HUD artwork or unified scale slider is missing.'
}
$dragSource = Get-Content -LiteralPath (Join-Path $projectRoot 'DVSurvival.Game\SurvivalHud.Dragging.cs') -Raw
if ($mainSource -notmatch 'GUILayout.SelectionGrid\(settings.HudStyle' -or
    $settingsSource -notmatch 'HudStyle = HudLayout.Validate' -or
    $hudSource -notmatch 'DrawLayout\(scale\)' -or $dragSource -notmatch 'HudLayout.ContentScale' -or
    $dragSource -notmatch 'Cursor.lockState != CursorLockMode.Locked' -or
    $dragSource -notmatch 'runtime.SaveSettings\(\)' -or
    $settingsSource -notmatch 'HudPanelPositions = HudPlacement.ValidatePositions' -or
    $layoutSource -match 'FindObjectsOfType|FindObjectOfType|new Texture2D|new GUIStyle|Resources.Load') {
    throw 'HUD styles must be selectable, draggable, locally persistent and reuse artwork/styles without scene scans.'
}
if ($catalogSource -match 'DVSurvival.Hud.icons.png' -or
    $catalogSource -notmatch 'PreviewPrefab = preview' -or
    $runtimeSource -notmatch 'ProvisionShopPricing.LocalPrice\(settings.MealPrice\)' -or
    $runtimeSource -notmatch 'MealPrice = GetPrice\(ProvisionKind.Meal\)' -or
    $inventoryArtworkSource -match 'Camera.Render|RenderTexture|FindObjectsOfType|FindObjectOfType' -or
    $inventoryArtworkSource -notmatch 'ItemIconSpriteDropped') {
    throw 'Inventory model previews must remain cached; sandbox pricing must reach shop data and host messages.'
}
if ($catalogSource -match 'TextMesh|CreateDynamicFontFromOSFont|CreatePrimitive' -or
    $modelsSource -match 'TextMesh|CreateDynamicFontFromOSFont|CreatePrimitive' -or
    $modelsSource -notmatch 'Shader.Find\("Standard"\)' -or
    $modelsSource -notmatch 'SetInt\("_ZWrite", 1\)' -or
    $modelsSource -notmatch 'renderQueue = 2000' -or
    ([regex]::Matches($modelsSource, 'AddComponent<BoxCollider>').Count -ne 1) -or
    ([regex]::Matches($modelsSource, 'AddComponent<MeshRenderer>').Count -ne 1)) {
    throw 'Provision models must keep opaque depth-tested printing, one renderer and one collider.'
}
if ($catalogSource -match 'HarmonyPatch\(typeof\(ScanItemCashRegisterModule\)' -or
    $catalogSource -notmatch 'AddComponent<NativeProvisionToken>' -or
    $hudSource -match 'KeyCode.F6|DrawWindow|OpenPurchase|RequestPurchase|PanelKey' -or
    $runtimeSource -notmatch 'actionRequests.TryAccept') {
    throw 'Native shop checkout, physical items or replay safeguards are missing.'
}
if ($personalItemsSource -match 'FindObjectsOfType|FindObjectOfType|GameObject.Find' -or
    $personalItemsSource -notmatch 'nextCheck = Time.realtimeSinceStartup \+ 0.5f') {
    throw 'Personal items must use cached references with rate-limited cleanup.'
}

if ($RequireBuildOutput) {
    $required = @(
        'DVSurvival.dll',
        'DVSurvival.Core.dll',
        'DVSurvival.Multiplayer.dll',
        'info.json',
        'README.md',
        'dependencies.lock.json'
        'Localization\strings.csv'
    )
    foreach ($name in $required) {
        $path = Join-Path $output $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing build artifact: $name" }
    }
    if (Test-Path -LiteralPath (Join-Path $output 'MultiplayerAPI.dll')) {
        throw 'MultiplayerAPI.dll must not be redistributed with DVSurvival.'
    }
    if (Test-Path -LiteralPath (Join-Path $output 'custom_item_mod.dll')) {
        throw 'Custom Item Mod must remain a separate UMM mod, not a DLL embedded in DVSurvival.'
    }
    if (Test-Path -LiteralPath (Join-Path $output 'DVLangHelper.Runtime.dll')) { throw 'Language Helper must remain a separate mod.' }
    $builtManifest = Get-Content -LiteralPath (Join-Path $output 'info.json') -Raw | ConvertFrom-Json
    if ($builtManifest.Version -ne $manifest.Version) { throw 'Built manifest version mismatch.' }
    foreach ($assembly in @('DVSurvival.dll', 'DVSurvival.Core.dll', 'DVSurvival.Multiplayer.dll')) {
        $assemblyPath = Join-Path $output $assembly
        $name = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
        $expectedAssemblyVersion = [version]$manifest.Version
        if ($name.Version.Major -ne $expectedAssemblyVersion.Major -or
            $name.Version.Minor -ne $expectedAssemblyVersion.Minor -or
            $name.Version.Build -ne $expectedAssemblyVersion.Build) {
            throw "Unexpected assembly version for ${assembly}: $($name.Version)"
        }
    }
    if ($DVInstallDir) {
        $cscCandidates = @(
            (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
            (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
        )
        $csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if (-not $csc) { throw 'The .NET Framework C# compiler was not found for the assembly-load smoke test.' }
        $verificationDirectory = Join-Path $projectRoot 'artifacts\verification'
        New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
        $verifier = Join-Path $verificationDirectory 'VerifyAssemblyLoad.exe'
        & $csc /nologo "/out:$verifier" (Join-Path $PSScriptRoot 'VerifyAssemblyLoad.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Could not compile the assembly-load smoke test.' }
        $managed = Join-Path $DVInstallDir 'DerailValley_Data\Managed'
        $umm = Join-Path $managed 'UnityModManager'
        $multiplayer = Join-Path $DVInstallDir 'Mods\Multiplayer'
        $customItems = Join-Path $projectRoot 'artifacts\dependencies\CustomItemMod-0.2.0'
        $languageHelper = Join-Path $projectRoot 'artifacts\dependencies\LanguageHelper-1.2.1\DVLangHelper'
        & $verifier $output $managed $umm $multiplayer $customItems $languageHelper
        if ($LASTEXITCODE -ne 0) { throw 'DVSurvival assembly-load smoke test failed.' }
        & $verifier $output $managed $umm $multiplayer $customItems $languageHelper --offline
        if ($LASTEXITCODE -ne 0) { throw 'DVSurvival no-Multiplayer smoke test failed.' }
    }
}

Write-Host 'DVSurvival project verification passed.'
