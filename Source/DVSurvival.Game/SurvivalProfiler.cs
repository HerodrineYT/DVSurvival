using System;
using UnityEngine.Profiling;

namespace DVSurvival.Mod
{
    // No stopwatch, strings or managed allocations in the normal game loop.
    internal struct SurvivalProfiler : IDisposable
    {
        public SurvivalProfiler(string name) { Profiler.BeginSample(name); }
        public void Dispose() { Profiler.EndSample(); }
    }
}
