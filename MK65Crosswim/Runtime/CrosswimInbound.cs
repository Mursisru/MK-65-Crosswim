using System.Collections.Generic;

namespace Crosswim.Runtime
{
    /// <summary>One live Crosswim per underwater missile (ships use 45 s cadence, no inbound lock).</summary>
    internal static class CrosswimInbound
    {
        private static readonly Dictionary<PersistentID, int> Counts = new Dictionary<PersistentID, int>(32);

        internal static void Add(PersistentID id)
        {
            if (!id.IsValid)
                return;
            Counts.TryGetValue(id, out int n);
            Counts[id] = n + 1;
        }

        internal static void Remove(PersistentID id)
        {
            if (!id.IsValid)
                return;
            if (!Counts.TryGetValue(id, out int n))
                return;
            if (n <= 1)
                Counts.Remove(id);
            else
                Counts[id] = n - 1;
        }

        internal static bool HasRoom(Missile? target)
        {
            if (target == null || !target.persistentID.IsValid)
                return true;
            Counts.TryGetValue(target.persistentID, out int n);
            return n < CrosswimConstants.ShipMaxInboundPerMissile;
        }
    }
}
