using System.Collections.Generic;
using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Live Crosswim interceptors per wet target. Prunes dead/drowned so ships can re-fire.
    /// </summary>
    internal static class CrosswimInbound
    {
        // target missile ID → set of interceptor missile IDs
        private static readonly Dictionary<PersistentID, HashSet<PersistentID>> ByTarget =
            new Dictionary<PersistentID, HashSet<PersistentID>>(32);

        internal static void Add(PersistentID targetId, PersistentID interceptorId)
        {
            if (!targetId.IsValid || !interceptorId.IsValid)
                return;
            if (!ByTarget.TryGetValue(targetId, out HashSet<PersistentID>? set))
            {
                set = new HashSet<PersistentID>();
                ByTarget[targetId] = set;
            }
            set.Add(interceptorId);
        }

        internal static void RemoveInterceptor(PersistentID interceptorId)
        {
            if (!interceptorId.IsValid || ByTarget.Count == 0)
                return;

            List<PersistentID>? emptyKeys = null;
            foreach (KeyValuePair<PersistentID, HashSet<PersistentID>> kv in ByTarget)
            {
                if (!kv.Value.Remove(interceptorId))
                    continue;
                if (kv.Value.Count == 0)
                {
                    emptyKeys ??= new List<PersistentID>(4);
                    emptyKeys.Add(kv.Key);
                }
            }
            if (emptyKeys == null)
                return;
            for (int i = 0; i < emptyKeys.Count; i++)
                ByTarget.Remove(emptyKeys[i]);
        }

        internal static bool HasRoom(Missile? target)
        {
            if (target == null || !target.persistentID.IsValid)
                return true;

            Prune(target.persistentID);
            if (!ByTarget.TryGetValue(target.persistentID, out HashSet<PersistentID>? set))
                return true;
            return set.Count < CrosswimConstants.ShipMaxInboundPerMissile;
        }

        private static void Prune(PersistentID targetId)
        {
            if (!ByTarget.TryGetValue(targetId, out HashSet<PersistentID>? set) || set.Count == 0)
                return;

            List<PersistentID>? dead = null;
            foreach (PersistentID id in set)
            {
                if (IsLiveInterceptor(id))
                    continue;
                dead ??= new List<PersistentID>(4);
                dead.Add(id);
            }
            if (dead == null)
                return;
            for (int i = 0; i < dead.Count; i++)
                set.Remove(dead[i]);
            if (set.Count == 0)
                ByTarget.Remove(targetId);
        }

        private static bool IsLiveInterceptor(PersistentID id)
        {
            if (!id.IsValid)
                return false;
            if (!UnitRegistry.TryGetUnit(new PersistentID?(id), out Unit u) || u == null)
                return false;
            if (u.disabled)
                return false;
            if (u is not Missile m || !CrosswimBootstrap.IsOurMissile(m))
                return false;
            CrosswimFlight? f = m.GetComponent<CrosswimFlight>();
            return f != null && f.CoversThreat;
        }
    }
}
