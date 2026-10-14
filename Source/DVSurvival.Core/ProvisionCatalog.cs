using System;

namespace DVSurvival.Core
{
    public static class ProvisionCatalog
    {
        public const int Count = 7;
        public static bool IsSupported(ProvisionKind kind) =>
            (kind >= ProvisionKind.Meal && kind <= ProvisionKind.HeatPack) ||
            kind == ProvisionKind.CannedFood || kind == ProvisionKind.Tea;
        public static ProvisionKind At(int index)
        {
            if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return (ProvisionKind)(index < 5 ? index + 1 : index + 2);
        }
        public static int ArtworkIndex(ProvisionKind kind)
        {
            if (!IsSupported(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            return (int)kind - (kind >= ProvisionKind.CannedFood ? 2 : 1);
        }
    }
}
