using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DV.Teleporters;
using DV.Utils;
using DVSurvival.Core;
using DVSurvival.Core.Runtime;
using DVSurvival.Core.Simulation;
using UnityEngine;
using UnityModManagerNet;

namespace DVSurvival.Mod
{
    internal sealed partial class SurvivalRuntime : IDisposable
    {
        private const float StateBroadcastIntervalSeconds = 2f;
        private const float HelloRetrySeconds = 3f;
        private const float SaveCheckpointSeconds = 30f;
        private const float LocalEnvironmentIntervalSeconds = 0.5f;
        private const float AppUtilProbeIntervalSeconds = 5f;
        private const float SessionPrepareRetrySeconds = 1f;
        private const float EnvironmentReportIntervalSeconds = 1f;
        private const float EnvironmentReportTimeoutSeconds = 3.5f;
        private const float TraumaCooldownSeconds = 1.25f;
        private const float HomeRespawnRetrySeconds = 0.5f;
        private const int DeathPenalty = 5000;
        private readonly UnityModManager.ModEntry entry;
        private readonly SurvivalModSettings settings;
        private readonly CabHeaterSwitchSystem cabHeaters;
        private readonly TrainDismountEvidence dismountEvidence = new TrainDismountEvidence();
        private readonly SessionSchedule schedule = new SessionSchedule();
        private readonly EnvironmentSampler environment;
        private readonly ISurvivalNetworkBridge network;
        private readonly PlayerSlots<SurvivalPlayerRecord> activeRecords =
            new PlayerSlots<SurvivalPlayerRecord>();
        private readonly PlayerSlots<PendingHello> pendingHellos =
            new PlayerSlots<PendingHello>();
        private readonly PlayerSlots<float> nextActionTimes = new PlayerSlots<float>();
        private readonly PlayerSlots<float> nextTraumaTimes = new PlayerSlots<float>();
        private readonly PlayerSlots<RemoteEnvironmentRecord> remoteEnvironments =
            new PlayerSlots<RemoteEnvironmentRecord>();
        private readonly Dictionary<string, float> confirmedCabHeaterStates =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly GameCalendarClock gameCalendarClock = new GameCalendarClock();
        private readonly TravelPauseTracker travelPause = new TravelPauseTracker();
        private readonly Dictionary<uint, HashSet<byte>> calendarTravelPauses =
            new Dictionary<uint, HashSet<byte>>();
        private HashSet<byte> observedTravelPause;
        private bool localTravelPaused;
        private float localTeleportPauseUntil;
        private readonly SleepCalendarReconciler sleepCalendar = new SleepCalendarReconciler();
        private readonly Dictionary<uint, float> calendarJumpRealSeconds =
            new Dictionary<uint, float>();
        private readonly Dictionary<ulong, PendingNativeSleepAction> pendingNativeSleeps =
            new Dictionary<ulong, PendingNativeSleepAction>();
        private readonly HashSet<string> retiredHostSessions =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> retiredHostSessionOrder = new Queue<string>();
        private SaveGameManager saveManager;
        private SaveGameData sessionSaveData;
        private SurvivalSaveDocument document;
        private SurvivalState currentState;
        private readonly SleepHudPreview sleepHudPreview = new SleepHudPreview();
        private SurvivalEnvironment lastLocalEnvironment = new SurvivalEnvironment();
        private SurvivalHud hud;
        private PersonalProvisionItems personalItems;
        private readonly ActionRequestGate actionRequests = new ActionRequestGate();
        private readonly List<uint> pendingRemoval = new List<uint>(16);
        private readonly Dictionary<uint, NativeProvisionToken> pendingItemUses = new Dictionary<uint, NativeProvisionToken>();
        private PlayerImpactMonitor impactMonitor;
        private Transform impactPlayerTransform;
        private float nextImpactControllerRetry;
        private readonly SurvivalEnvironment stepEnvironment = new SurvivalEnvironment();
        private readonly SurvivalEnvironment remoteSample = new SurvivalEnvironment();
        private readonly PlayerSlots<TravelTransitionOrder> travelOrders = new PlayerSlots<TravelTransitionOrder>();
        private readonly SurvivalPlayerInfo localPlayerSnapshot = new SurvivalPlayerInfo();
        private readonly SurvivalPlayerInfo[] broadcastPlayers = new SurvivalPlayerInfo[256];
        private bool started;
        private bool disposed;
        private bool sessionReady;
        private bool receivedNetworkState;
        private bool lastAuthority;
        private byte lastLocalAuthorityId = byte.MaxValue;
        private float simulationAccumulator { get => schedule.SimulationSeconds; set => schedule.SimulationSeconds = value; }
        private float firstAidAccumulator { get => schedule.TreatmentSeconds; set => schedule.TreatmentSeconds = value; }
        private float nextStateBroadcast { get => (float)schedule[ScheduledJob.Broadcast]; set => schedule[ScheduledJob.Broadcast] = value; }
        private float nextHello { get => (float)schedule[ScheduledJob.Hello]; set => schedule[ScheduledJob.Hello] = value; }
        private float nextSaveCheckpoint { get => (float)schedule[ScheduledJob.Save]; set => schedule[ScheduledJob.Save] = value; }
        private float nextLocalEnvironmentUpdate { get => (float)schedule[ScheduledJob.Environment]; set => schedule[ScheduledJob.Environment] = value; }
        private float nextAppUtilProbe;
        private float nextSessionPrepareAttempt { get => (float)schedule[ScheduledJob.Prepare]; set => schedule[ScheduledJob.Prepare] = value; }
        private float nextEnvironmentReport { get => (float)schedule[ScheduledJob.Report]; set => schedule[ScheduledJob.Report] = value; }
        private float lastCompletedSleepRequestTime = -100f;
        private uint requestSequence;
        private uint stateSequence;
        private uint lastReceivedSequence;
        private uint lastCollapseCount;
        private uint environmentReportSequence;
        private uint calendarJumpSequence;
        private uint lastConnectionGeneration;
        private SurvivalStateMessage lastHostMessage;
        private DV.AppUtil appUtil;
        private bool stateDirty;
        private bool hasLocalStateSnapshot;
        private readonly HomeRespawnFlow homeRespawn = new HomeRespawnFlow();
        private float respawnTraumaGraceUntil;
        private float nextHouseFallbackScan;
        private float nextRespawnWarning;
        private FastTravelDestination cachedHouseDestination;
        private float nextHomeRespawnAttempt;
        private float movementMultiplier = 1f;
        private uint pendingCabHeaterRequestId;
        private string pendingCabHeaterCarId = string.Empty;
        private float pendingCabHeaterPreviousState;
        private float pendingCabHeaterExpiresAt;

        public SurvivalRuntime(UnityModManager.ModEntry entry, SurvivalModSettings settings,
            ISurvivalNetworkBridge bridge = null)
        {
            this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Clamp();
            cabHeaters = new CabHeaterSwitchSystem(OnCabHeaterSwitchChanged);
            environment = new EnvironmentSampler(settings, cabHeaters);
            network = bridge ?? CreateNetworkBridge(entry.Path);
            network.HelloReceived += OnHelloReceived;
            network.ActionReceived += OnActionReceived;
            network.EnvironmentReceived += OnEnvironmentReceived;
            network.PlayerDisconnected += OnPlayerDisconnected;
            network.StateReceived += OnStateReceived;
            network.Initialize(entry.Info.Id);
            lastConnectionGeneration = network.ConnectionGeneration;
            currentState = SurvivalState.CreateDefault(settings.CachedTuning);
        }

        public SurvivalState CurrentState { get { return currentState; } }
        public SurvivalState HudState => sleepHudPreview.Get(currentState, Time.realtimeSinceStartup);
        public SurvivalEnvironment CurrentEnvironment { get { return lastLocalEnvironment; } }
        public string CabinStatus { get { return environment.CabinStatus; } }
        public bool IsSessionReady { get { return sessionReady; } }
        public bool IsRespawning { get { return homeRespawn.Pending || Time.realtimeSinceStartup < respawnTraumaGraceUntil; } }
        internal bool IsHomeTravelPending { get { return homeRespawn.Pending; } }
        public bool HasConfirmedLocalState
        {
            get
            {
                return started && sessionReady && (!network.IsSessionActive || network.IsSupported) &&
                (network.IsAuthority || receivedNetworkState);
            }
        }

        public string NetworkStatus
        {
            get
            {
                if (network.IsSessionActive && !network.IsSupported)
                    return string.Format(ModLocalization.Text(
                        "Multiplayer {0} устарел; требуется {1}",
                        "Multiplayer {0} is outdated; {1} is required"),
                        network.MultiplayerVersion, SurvivalConstants.MinimumMultiplayerVersion);
                if (!network.IsAvailable) return ModLocalization.Text("Локальный режим", "Local mode");
                if (!network.IsSessionActive)
                    return ModLocalization.Text("Multiplayer доступен — одиночная сессия",
                        "Multiplayer available — single-player session");
                return network.IsAuthority
                    ? ModLocalization.Text("Хост рассчитывает личные потребности всех игроков",
                        "Host simulates each player's personal needs")
                    : ModLocalization.Text("Личные потребности синхронизированы с хостом",
                        "Personal needs synchronized with the host");
            }
        }

        public float MovementMultiplier
        {
            get { return sessionReady ? movementMultiplier : 1f; }
        }

        public void Tick(float deltaTime)
        {
            using (new SurvivalProfiler("DVSurvival.Runtime.Tick"))
            {
                if (!started || disposed) return;
                var now = Time.realtimeSinceStartup;
                HandleNetworkConnectionGeneration();
                ResolveExpiredCabHeaterRequest();
                if (!sessionReady)
                {
                    if (WorldStreamingInit.IsLoaded && schedule.Due(ScheduledJob.Prepare, now, SessionPrepareRetrySeconds))
                        OnWorldLoaded();
                    return;
                }
                if (UnloadWatcher.isUnloading || saveManager == null) { OnWorldUnloading(); return; }
                ClimateSceneDiscovery.Tick();
                HandleAuthorityTransition();
                RefreshLocalTravelPause();
                UpdateLocalEnvironment();
                ProcessPendingHomeRespawn();
                if (network.IsSessionActive && !network.IsSupported) return;
                if (network.IsSessionActive && !network.IsAuthority)
                {
                    if (now >= nextEnvironmentReport) SendLocalEnvironmentReport();
                    if (!receivedNetworkState && now >= nextHello) SendHello();
                    return;
                }
                EnsureLocalAuthorityRecord();
                ProcessPendingHellos();
                if (deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || IsGamePaused()) return;
                schedule.Accumulate(deltaTime);
                float healing;
                if (schedule.TakeTreatment(out healing)) AdvanceTreatment(healing, now);
                if (schedule.Due(ScheduledJob.Riders, now, .25)) ObserveRiders(now);
                float elapsed;
                if (schedule.TakeSimulation(out elapsed))
                {
                    var hours = ObserveAndQueueCalendar(elapsed);
                    // Queued calendar transactions own their real seconds. A genuinely frozen
                    // calendar still permits live thermal exposure, without draining needs.
                    if (hours > 0 || sleepCalendar.PendingJumpCount == 0)
                        AdvanceAuthority(elapsed, hours, travelPausedPlayers: observedTravelPause);
                }
                DrainCalendarOperations(now);
                if (network.IsSessionActive && schedule.Due(ScheduledJob.Broadcast, now, StateBroadcastIntervalSeconds)) SendAllStates();
                if (schedule.Due(ScheduledJob.Save, now, SaveCheckpointSeconds)) Checkpoint();
            }
        }

        private void AdvanceTreatment(float seconds, float now)
        {
            foreach (var peer in activeRecords)
            {
                if (travelPause.IsPaused(peer.Key, now) || !FirstAidRecovery.Advance(peer.Value.State, seconds)) continue;
                SendResult(peer.Key, peer.Value.State, 0, SurvivalResultCode.None, string.Empty, false);
                stateDirty = true;
            }
        }

        private void ObserveRiders(float now)
        {
            var players = network.GetPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player != null && player.IsOnCar) dismountEvidence.Observe(player.PlayerId, environment.OccupiedTrainSpeed(player), now);
            }
            var local = GetLocalPlayerInfo(network.LocalPlayerId);
            if (local != null && local.IsOnCar) dismountEvidence.Observe(local.PlayerId, environment.OccupiedTrainSpeed(local), now);
        }

        private bool IsPersonalSleepMode => PersonalSleepValidator.IsNoAdvanceMode(
            network.IsSessionActive, network.IsNativeSleepTimeSuppressed, environment.IsGameTimeOverridden);

        public bool OnLocalSleepWithoutTimeAdvanceCompleted(float amountOfSecondsToSleep)
        {
            entry.Logger.Log("Native sleep without calendar jump: hours=" + amountOfSecondsToSleep / 3600f +
                ", ready=" + sessionReady + ", native=" + NativeSleepOriginPatch.IsAdvancingSleep +
                ", mp=" + network.IsSessionActive + ", authority=" + network.IsAuthority +
                ", mpTimeDisabled=" + network.IsNativeSleepTimeSuppressed +
                ", clockOverridden=" + environment.IsGameTimeOverridden);
            if (!sessionReady || !NativeSleepOriginPatch.IsAdvancingSleep ||
                !IsPersonalSleepMode ||
                Time.realtimeSinceStartup - lastCompletedSleepRequestTime < 1f)
                return false;
            var request = new SurvivalActionRequest
            {
                RequestId = ++requestSequence,
                Action = SurvivalActionKind.SleepWithoutTimeAdvance,
                Amount = amountOfSecondsToSleep / 3600f
            };
            if (!PersonalSleepValidator.IsValid(true, true, true, request) || !RequestAction(request))
                return false;
            lastCompletedSleepRequestTime = Time.realtimeSinceStartup;
            return true;
        }

        private void AdvanceAuthority(float realSeconds, float gameHours,
            SleepCalendarAdvance calendarAdvance = null, HashSet<byte> travelPausedPlayers = null)
        {
            if (gameHours <= 0f && realSeconds <= 0f) return;
            var tuning = settings.CachedTuning;
            if (!network.IsSessionActive)
            {
                if (IsTravelSimulationPaused(byte.MaxValue, travelPausedPlayers)) return;
                SurvivalPlayerRecord local;
                if (activeRecords.TryGetValue(byte.MaxValue, out local))
                {
                    var playerHours = calendarAdvance == null
                        ? gameHours : (float)calendarAdvance.GetAwakeHours(byte.MaxValue);
                    if (playerHours <= 0f && (realSeconds <= 0f || calendarAdvance != null)) return;
                    var sample = (lastLocalEnvironment ?? environment.SampleLocal(0f));
                    var previousCollapse = local.State.CollapseCount;
                    AdvanceInCalendarChunks(local.State, sample, playerHours,
                        gameHours > 0 ? ScaleRealSeconds(realSeconds, playerHours, gameHours) : realSeconds);
                    if (ApplyDeathPenalty(local.State, previousCollapse))
                        SendResult(byte.MaxValue, local.State, 0, SurvivalResultCode.None, "death");
                    else
                        SetLocalState(local.State);
                    stateDirty = true;
                }
                return;
            }

            var players = network.GetPlayers();
            for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                var player = players[playerIndex];
                if (player == null || !player.IsLoaded) continue;
                if (IsTravelSimulationPaused(player.PlayerId, travelPausedPlayers)) continue;
                if (player.IsHost && !activeRecords.ContainsKey(player.PlayerId))
                    activeRecords[player.PlayerId] = document.GetOrCreate(settings.IdentityId,
                        player.DisplayName, tuning);
                SurvivalPlayerRecord record;
                if (!activeRecords.TryGetValue(player.PlayerId, out record)) continue;
                var playerHours = calendarAdvance == null
                    ? gameHours : (float)calendarAdvance.GetAwakeHours(player.PlayerId);
                if (playerHours <= 0f && (realSeconds <= 0f || calendarAdvance != null)) continue;
                var sample = player.PlayerId == network.LocalPlayerId
                    ? (lastLocalEnvironment ?? environment.SampleLocal(0f))
                    : GetRemoteEnvironment(player, 0f, realSeconds);
                var previousCollapse = record.State.CollapseCount;
                AdvanceInCalendarChunks(record.State, sample, playerHours,
                    gameHours > 0 ? ScaleRealSeconds(realSeconds, playerHours, gameHours) : realSeconds);
                var died = ApplyDeathPenalty(record.State, previousCollapse);
                record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
                if (died)
                    SendResult(player.PlayerId, record.State, 0, SurvivalResultCode.None, "death");
                else if (player.PlayerId == network.LocalPlayerId)
                    SetLocalState(record.State);
                stateDirty = true;
            }
        }

        private void AdvanceInCalendarChunks(SurvivalState state, SurvivalEnvironment sample,
            float gameHours, float realSeconds)
        {
            var rules = settings.CachedRules;
            if (gameHours <= 0)
            {
                sample.CopyTo(stepEnvironment);
                stepEnvironment.GameHours = 0;
                NeedsEngine.Advance(state, new EnvironmentFrame(stepEnvironment), rules, realSeconds);
                return;
            }
            var remaining = gameHours;
            while (remaining > 0f)
            {
                var step = Math.Min(24f, remaining);
                sample.CopyTo(stepEnvironment);
                stepEnvironment.GameHours = step;
                var stepRealSeconds = gameHours > 0f ? realSeconds * step / gameHours : 0f;
                NeedsEngine.Advance(state, new EnvironmentFrame(stepEnvironment), rules, stepRealSeconds);
                remaining -= step;
            }
        }

        private sealed class PendingNativeSleepAction
        {
            public PendingNativeSleepAction(byte playerId, uint requestId, float hours,
                SurvivalEnvironment environment)
            {
                PlayerId = playerId;
                RequestId = requestId;
                Hours = hours;
                Environment = environment;
            }

            public byte PlayerId { get; private set; }
            public uint RequestId { get; private set; }
            public float Hours { get; private set; }
            public SurvivalEnvironment Environment { get; private set; }
        }

        private sealed class PendingHello
        {
            public PendingHello(SurvivalPlayerInfo player, SurvivalHello hello)
            {
                Player = player;
                Hello = hello;
            }

            public SurvivalPlayerInfo Player { get; private set; }
            public SurvivalHello Hello { get; private set; }
        }

        private sealed class RemoteEnvironmentRecord
        {
            public RemoteEnvironmentRecord(uint sequence, float receivedAt,
                SurvivalEnvironment environment)
            {
                Sequence = sequence;
                ReceivedAt = receivedAt;
                Environment = environment;
            }

            public uint Sequence { get; set; }
            public float ReceivedAt { get; set; }
            public SurvivalEnvironment Environment { get; private set; }
        }

    }
}
