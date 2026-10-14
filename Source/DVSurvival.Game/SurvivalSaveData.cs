using System;
using System.Collections.Generic;
using System.Linq;
using DVSurvival.Core;
using Newtonsoft.Json;

namespace DVSurvival.Mod
{
    [Serializable]
    internal sealed class SurvivalPlayerRecord
    {
        public string IdentityId = string.Empty;
        public string DisplayName = string.Empty;
        public long LastSeenUtcTicks;
        public SurvivalState State = new SurvivalState();
    }

    [Serializable]
    internal sealed class CabHeaterSaveRecord
    {
        public string CarId = string.Empty;
        public bool IsOn;
        public float? Level;
        [JsonIgnore]
        public float EffectiveLevel => CabHeaterSetting.NormalizeSwitch(Level ?? (IsOn ? 1f : 0f));
        public long LastSeenUtcTicks;
    }

    [Serializable]
    internal sealed class SurvivalSaveDocument
    {
        public int Version = SurvivalConstants.SaveVersion;
        public string SessionId = string.Empty;
        public List<SurvivalPlayerRecord> Players = new List<SurvivalPlayerRecord>();
        public HashSet<string> ConsumedItems = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, PartialProvisionRecord> PartialItems = new Dictionary<string, PartialProvisionRecord>(StringComparer.Ordinal);
        public List<CabHeaterSaveRecord> CabHeaters = new List<CabHeaterSaveRecord>();
        private readonly Dictionary<string, int> playerIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> heaterIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public SurvivalPlayerRecord GetOrCreate(string identityId, string displayName, SurvivalTuning tuning)
        {
            SurvivalPlayerRecord record = null;
            for (int i = 0; i < Players.Count; i++)
                if (string.Equals(Players[i].IdentityId, identityId, StringComparison.OrdinalIgnoreCase))
                { record = Players[i]; break; }
            if (record == null)
            {
                record = new SurvivalPlayerRecord
                {
                    IdentityId = identityId,
                    DisplayName = displayName ?? string.Empty,
                    State = SurvivalState.CreateDefault(tuning)
                };
                Players.Add(record);
            }
            record.DisplayName = string.IsNullOrEmpty(displayName) ? record.DisplayName : displayName;
            record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            if (record.State == null || !record.State.IsValid()) record.State = SurvivalState.CreateDefault(tuning);
            Prune();
            return record;
        }

        public float GetCabHeater(string carId)
        {
            if (string.IsNullOrWhiteSpace(carId) || CabHeaters == null) return 0f;
            CabHeaterSaveRecord record = null;
            for (int i = 0; i < CabHeaters.Count; i++)
                if (CabHeaters[i] != null && string.Equals(CabHeaters[i].CarId, carId, StringComparison.OrdinalIgnoreCase))
                { record = CabHeaters[i]; break; }
            return record == null ? 0f : record.EffectiveLevel;
        }

        public void SetCabHeater(string carId, float level)
        {
            if (string.IsNullOrWhiteSpace(carId)) return;
            if (CabHeaters == null) CabHeaters = new List<CabHeaterSaveRecord>();
            var normalizedId = carId.Trim();
            var record = CabHeaters.FirstOrDefault(value => value != null &&
                string.Equals(value.CarId, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                record = new CabHeaterSaveRecord { CarId = normalizedId };
                CabHeaters.Add(record);
            }
            record.Level = CabHeaterSetting.NormalizeSwitch(level);
            record.IsOn = record.Level > 0f;
            record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            PruneCabHeaters();
        }

        public void Normalize(SurvivalTuning tuning)
        {
            Version = SurvivalConstants.SaveVersion;
            if (ConsumedItems == null) ConsumedItems = new HashSet<string>(StringComparer.Ordinal);
            if (PartialItems == null) PartialItems = new Dictionary<string, PartialProvisionRecord>(StringComparer.Ordinal);
            if (CabHeaters == null) CabHeaters = new List<CabHeaterSaveRecord>();
            Guid sessionGuid;
            if (!Guid.TryParse(SessionId, out sessionGuid) || sessionGuid == Guid.Empty)
                SessionId = Guid.NewGuid().ToString("D");
            if (Players == null) Players = new List<SurvivalPlayerRecord>();
            playerIndices.Clear();
            int playerCount = 0;
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                if (p == null || string.IsNullOrWhiteSpace(p.IdentityId)) continue;
                int index;
                if (playerIndices.TryGetValue(p.IdentityId, out index))
                { if (p.LastSeenUtcTicks > Players[index].LastSeenUtcTicks) Players[index] = p; }
                else { playerIndices[p.IdentityId] = playerCount; Players[playerCount++] = p; }
            }
            if (playerCount < Players.Count) Players.RemoveRange(playerCount, Players.Count - playerCount);
            playerIndices.Clear();
            foreach (var player in Players)
            {
                if (player.State == null || !player.State.IsValid())
                    player.State = SurvivalState.CreateDefault(tuning);
                else
                    player.State.Clamp();
            }
            heaterIndices.Clear();
            int heaterCount = 0;
            for (int i = 0; i < CabHeaters.Count; i++)
            {
                var value = CabHeaters[i];
                if (value == null || string.IsNullOrWhiteSpace(value.CarId) || value.CarId.Length > 80) continue;
                var id = value.CarId.Trim();
                int index;
                if (heaterIndices.TryGetValue(id, out index))
                { if (value.LastSeenUtcTicks > CabHeaters[index].LastSeenUtcTicks) CabHeaters[index] = value; }
                else { heaterIndices[id] = heaterCount; CabHeaters[heaterCount++] = value; }
            }
            if (heaterCount < CabHeaters.Count) CabHeaters.RemoveRange(heaterCount, CabHeaters.Count - heaterCount);
            heaterIndices.Clear();
            Prune();
            PruneCabHeaters();
        }

        private void Prune()
        {
            const int maximumRecords = 64;
            if (Players.Count <= maximumRecords) return;
            Players = Players.OrderByDescending(p => p.LastSeenUtcTicks).Take(maximumRecords).ToList();
        }

        private void PruneCabHeaters()
        {
            const int maximumRecords = 128;
            if (CabHeaters.Count <= maximumRecords) return;
            CabHeaters = CabHeaters.OrderByDescending(value => value.LastSeenUtcTicks)
                .Take(maximumRecords).ToList();
        }
    }

    internal static class SurvivalSaveData
    {
        private const string SaveKey = "DVSurvival.State";
        private const int MaximumJsonLength = 1024 * 1024;

        public static SurvivalSaveDocument Read(SaveGameData data, SurvivalTuning tuning)
        {
            SurvivalSaveDocument document = null;
            if (data != null)
            {
                var json = data.GetString(SaveKey);
                if (!string.IsNullOrWhiteSpace(json) && json.Length <= MaximumJsonLength)
                {
                    try { document = JsonConvert.DeserializeObject<SurvivalSaveDocument>(json); }
                    catch (JsonException) { document = null; }
                }
            }
            if (document == null) document = new SurvivalSaveDocument();
            document.Normalize(tuning);
            return document;
        }

        public static void Write(SaveGameData data, SurvivalSaveDocument document, SurvivalTuning tuning)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.Normalize(tuning);
            var json = JsonConvert.SerializeObject(document, Formatting.None);
            if (json.Length > MaximumJsonLength)
                throw new InvalidOperationException("DVSurvival save payload exceeded the safety limit.");
            data.SetString(SaveKey, json);
        }
    }
}
