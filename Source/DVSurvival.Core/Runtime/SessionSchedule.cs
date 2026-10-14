using System;

namespace DVSurvival.Core.Runtime
{
    public enum ScheduledJob { Simulation, Treatment, Riders, Broadcast, Save, Environment, Report, Hello, Prepare, Count }

    // Owned by one session. No catch-up task queue: a long frame can run each job at most once.
    // Simulation uses game-calendar accounting separately; treatment uses active real seconds only.
    public sealed class SessionSchedule
    {
        private readonly double[] deadlines = new double[(int)ScheduledJob.Count];
        public float SimulationSeconds { get; set; }
        public float TreatmentSeconds { get; set; }
        public double this[ScheduledJob job] { get => deadlines[(int)job]; set => deadlines[(int)job] = value; }
        public bool Due(ScheduledJob job, double now, double interval)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) ||
                double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0) return false;
            if (now < deadlines[(int)job]) return false;
            deadlines[(int)job] = now + interval;
            return true;
        }
        public void Accumulate(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0) return;
            SimulationSeconds += Math.Min(5, delta);
            TreatmentSeconds += delta;
        }
        public bool TakeSimulation(out float seconds)
        { seconds = 0; if (SimulationSeconds < 1) return false; seconds = DrainSimulation(); return true; }
        public float DrainSimulation() { float value = SimulationSeconds; SimulationSeconds = 0; return value; }
        public bool TakeTreatment(out float seconds)
        { seconds = 0; if (TreatmentSeconds < .5f) return false; seconds = TreatmentSeconds; TreatmentSeconds = 0; return true; }
        public void Reset() { Array.Clear(deadlines, 0, deadlines.Length); SimulationSeconds = TreatmentSeconds = 0; }
    }
}
