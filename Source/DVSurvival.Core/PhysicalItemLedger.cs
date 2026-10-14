using System;
using System.Collections.Generic;

namespace DVSurvival.Core
{
    public static class PhysicalItemLedger
    {
        public static SurvivalResultCode Consume(HashSet<string> consumed, string identity,
            SurvivalState state, ProvisionKind kind, SurvivalTuning tuning, SurvivalEnvironment environment = null)
        {
            Guid parsed;
            if (consumed == null || state == null || tuning == null || identity == null || identity.Length != 36 ||
                !Guid.TryParse(identity, out parsed) || parsed == Guid.Empty ||
                !ProvisionCatalog.IsSupported(kind))
                return SurvivalResultCode.InvalidRequest;
            identity = parsed.ToString("D");
            // Retries retire a duplicate model without applying the effect a second time.
            if (consumed.Contains(identity)) return SurvivalResultCode.Success;
            // Never evict IDs silently: an old saved copy must not become usable again.
            if (consumed.Count >= 16000) return SurvivalResultCode.InvalidRequest;
            var result = SurvivalSimulator.ConsumePhysical(state, kind, tuning, environment);
            if (result == SurvivalResultCode.Success) consumed.Add(identity);
            return result;
        }
    }
}
