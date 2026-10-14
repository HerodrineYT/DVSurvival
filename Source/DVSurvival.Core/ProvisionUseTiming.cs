namespace DVSurvival.Core
{
    public static class ProvisionUseTiming
    {
        public static float Seconds(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return 5f;
                case ProvisionKind.CannedFood: return 6f;
                case ProvisionKind.FirstAid: return 8f;
                default: return 3f;
            }
        }
    }
}
