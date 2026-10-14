using System;
using System.Collections.Generic;

namespace DVSurvival.Core
{
    // Borrowed until the next refresh. Never use these records for deferred actions/hello packets.
    public sealed class ReusablePlayerSnapshots
    {
        private readonly SurvivalPlayerInfo[] records = new SurvivalPlayerInfo[256];
        private readonly bool[] present = new bool[256];
        private readonly List<SurvivalPlayerInfo> players = new List<SurvivalPlayerInfo>(256);
        private readonly IReadOnlyList<SurvivalPlayerInfo> view;
        public ReusablePlayerSnapshots() { view = players.AsReadOnly(); }
        public IReadOnlyList<SurvivalPlayerInfo> View => view;
        public void Begin() { players.Clear(); Array.Clear(present, 0, present.Length); }
        public SurvivalPlayerInfo Get(byte id) => records[id] ?? (records[id] = new SurvivalPlayerInfo());
        public bool Contains(byte id) => present[id];
        public void Add(byte id) { if (!present[id]) { present[id] = true; players.Add(Get(id)); } }
        public void Reset() { Begin(); Array.Clear(records, 0, records.Length); }
    }
}
