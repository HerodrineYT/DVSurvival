using System;
using System.Collections.Generic;

namespace DVSurvival.Core
{
    public sealed class SessionConfigurationReceiver
    {
        private readonly Dictionary<string, StateConfigurationCache> sessions = new Dictionary<string, StateConfigurationCache>(StringComparer.Ordinal);
        private readonly Queue<string> order = new Queue<string>();
        public bool Apply(SurvivalStateMessage message)
        {
            if (string.IsNullOrEmpty(message.SessionId)) return false;
            StateConfigurationCache cache;
            if (!sessions.TryGetValue(message.SessionId, out cache))
            {
                if (!message.HasConfiguration) return false;
                if (sessions.Count >= 16) sessions.Remove(order.Dequeue());
                cache = new StateConfigurationCache();
                sessions.Add(message.SessionId, cache); order.Enqueue(message.SessionId);
            }
            return cache.Apply(message);
        }
        public void Reset() { sessions.Clear(); order.Clear(); }
    }
}
