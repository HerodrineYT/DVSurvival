namespace DVSurvival.Core
{
    public sealed class EnvironmentDeliveryPolicy
    {
        private bool initialized, travelling;
        private string session;
        private uint revision;
        private float nextReliable;
        public bool Prepare(SurvivalEnvironmentReport report, float now)
        {
            bool changed = !initialized || report.IsTravelling != travelling || session != report.SessionId;
            if (changed)
            {
                revision = session != report.SessionId ? 1u : revision + 1u;
                if (revision == 0) revision = 1;
            }
            bool reliable = changed || now >= nextReliable;
            if (reliable) nextReliable = now + 3f;
            initialized = true; travelling = report.IsTravelling; session = report.SessionId;
            report.TravelRevision = revision;
            return reliable;
        }
        public void Reset() { initialized = false; session = null; revision = 0; nextReliable = 0f; }
    }
    public sealed class TravelTransitionOrder
    {
        private uint revision;
        private bool initialized, travelling;
        public bool Accept(uint incoming, bool value)
        {
            if (incoming == 0) return false;
            if (initialized && incoming < revision) return false;
            if (initialized && incoming == revision && travelling != value) return false;
            initialized = true; revision = incoming; travelling = value;
            return true;
        }
    }
}
