using System;
using System.Collections.Generic;

namespace DVSurvival.Core
{
    [Serializable]
    public sealed class SurvivalPlayerInfo
    {
        public byte PlayerId;
        public string DisplayName = string.Empty;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public bool IsHost;
        public bool IsLoaded;
        public bool IsOnCar;
        public bool OccupiedCarIsLocomotive;
        public string OccupiedCarId = string.Empty;
        public bool OccupiedCarSupportsCabHeater;
    }

    [Serializable]
    public sealed class SurvivalHello
    {
        public int Protocol = SurvivalConstants.ProtocolVersion;
        public string IdentityId = string.Empty;
        public string ModVersion = SurvivalConstants.ModVersion;
    }

    [Serializable]
    public sealed class SurvivalActionRequest
    {
        public int Protocol = SurvivalConstants.ProtocolVersion;
        // Reject delayed actions from a previous save/session after authority changes.
        public string SessionId = string.Empty;
        public uint RequestId;
        public SurvivalActionKind Action;
        public ProvisionKind Provision;
        public float Amount;
        public float SecondaryAmount;
        // For a native sleep action these are the sleeping client's exact game-calendar bounds
        // around TimeAdvance.AdvanceTime. The host validates their duration, then aligns it to
        // its own captured authoritative interval; client/host calendar offsets are never trusted.
        public long CalendarBeforeTicks;
        public long CalendarAfterTicks;
        public TraumaKind Trauma;
        public string ItemIdentity = string.Empty;
        // Captured from the actual native BedSleeping instance, in absolute world coordinates.
        public bool HasNativeBed;
        public float BedWorldX;
        public float BedWorldY;
        public float BedWorldZ;
    }

    [Serializable]
    public sealed class SurvivalEnvironmentReport
    {
        public int Protocol = SurvivalConstants.ProtocolVersion;
        public uint Sequence;
        public string SessionId = string.Empty;
        public bool IsTravelling;
        public uint TravelRevision;
        public SurvivalEnvironment Environment = new SurvivalEnvironment();
    }

    [Serializable]
    public sealed class SurvivalStateMessage
    {
        public int Protocol = SurvivalConstants.ProtocolVersion;
        public uint Sequence;
        public uint RequestId;
        public SurvivalResultCode Result;
        public string SessionId = string.Empty;
        public string StatusKey = string.Empty;
        public SurvivalState State = new SurvivalState();
        public uint ConfigurationRevision = 1;
        public bool HasConfiguration = true;
        public bool DisableNeedsConsumption;
        public int MealPrice;
        public int WaterPrice;
        public int CoffeePrice;
        public int FirstAidPrice;
        public int HeatPackPrice;
        public int CannedFoodPrice;
        public int TeaPrice;
        public string CabHeaterCarId = string.Empty;
        public float CabHeaterLevel;
        public string ItemIdentity = string.Empty;
        public int ItemUsedUnits = -1;

        public int GetPrice(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return MealPrice;
                case ProvisionKind.Water: return WaterPrice;
                case ProvisionKind.Coffee: return CoffeePrice;
                case ProvisionKind.FirstAid: return FirstAidPrice;
                case ProvisionKind.HeatPack: return HeatPackPrice;
                case ProvisionKind.CannedFood: return CannedFoodPrice;
                case ProvisionKind.Tea: return TeaPrice;
                default: return -1;
            }
        }
    }

    public interface ISurvivalNetworkBridge : IDisposable
    {
        bool IsAvailable { get; }
        bool IsSupported { get; }
        bool IsSessionActive { get; }
        bool IsAuthority { get; }
        bool IsNativeSleepTimeSuppressed { get; }
        // Changes whenever the underlying Multiplayer client connection is replaced or stopped.
        // A client may adopt a new host epoch only after this transport boundary (or a world/role
        // transition), because MPAPI 0.1.15.8 does not expose the remote host identity reliably.
        uint ConnectionGeneration { get; }
        byte LocalPlayerId { get; }
        string MultiplayerVersion { get; }
        string Status { get; }
        event Action<SurvivalPlayerInfo, SurvivalHello> HelloReceived;
        event Action<SurvivalPlayerInfo, SurvivalActionRequest> ActionReceived;
        event Action<SurvivalPlayerInfo, SurvivalEnvironmentReport> EnvironmentReceived;
        event Action<byte> PlayerDisconnected;
        event Action<SurvivalStateMessage> StateReceived;
        void Initialize(string modId);
        void SetEnabled(bool enabled);
        IReadOnlyList<SurvivalPlayerInfo> GetPlayers();
        void SendHello(SurvivalHello hello);
        bool SendAction(SurvivalActionRequest request);
        void SendEnvironment(SurvivalEnvironmentReport report);
        void SendStateToPlayer(byte playerId, SurvivalStateMessage message, bool reliable);
    }
}
