namespace DVSurvival.Core
{
    public sealed class TrainProbeCadence
    {
        private float riskUntil;
        public void ObserveRisk(float now) { riskUntil = now + 3f; }
        public bool IsHighRisk(float now) => now < riskUntil;
        public float Interval(float now) => IsHighRisk(now) ? .125f : .5f;
        public void Reset() { riskUntil = 0f; }
    }
}
