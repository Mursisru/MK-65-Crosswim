using System.Collections.Generic;
using Crosswim.Bootstrap;
using NuclearOption.Networking;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Light HE blast for interceptor. Torpedoes die via KillMissile (no victim warhead).
    /// Ships get tiny collateral only — never multi-collider carrier nukes.
    /// </summary>
    internal static class CrosswimBlast
    {
        private static readonly Collider[] Buf = new Collider[128];
        private static readonly HashSet<int> HitUnits = new HashSet<int>(32);

        internal static void Apply(Missile source, Vector3 worldPos)
        {
            if (source == null)
                return;

            float yieldKg = CrosswimConstants.BlastYieldKg;
            float blastPower = Mathf.Pow(Mathf.Max(yieldKg, 1f), 0.3333f);
            float blastRadius = blastPower * 13f;
            // Soft peak — interceptor HE, not Shockwave front.
            float peakOp = CrosswimConstants.BlastPeakOverpressure *
                           (yieldKg / CrosswimConstants.BlastShockRefKg) *
                           CrosswimConstants.BlastShipDamageScale;
            PersistentID dealer = source.persistentID;
            FactionHQ? hq = source.NetworkHQ;

            bool server = NetworkManagerNuclearOption.i != null &&
                          NetworkManagerNuclearOption.i.Server.Active;
            if (!server && !(source.LocalSim || source.IsServer))
                return;

            HitUnits.Clear();
            int n = Physics.OverlapSphereNonAlloc(worldPos, blastRadius, Buf);
            for (int i = 0; i < n; i++)
            {
                Collider col = Buf[i];
                if (col == null || col.transform.IsChildOf(source.transform))
                    continue;

                Missile? otherMis = col.GetComponentInParent<Missile>();
                if (otherMis != null)
                {
                    if (ReferenceEquals(otherMis, source) || CrosswimBootstrap.IsOurMissile(otherMis))
                        continue;
                    if (hq != null && otherMis.NetworkHQ != null && otherMis.NetworkHQ == hq)
                        continue;
                    KillMissile(otherMis, dealer);
                    continue;
                }

                // Interceptor: no meaningful ship/structure kill from blast.
                if (col.GetComponentInParent<Ship>() != null)
                    continue;

                IDamageable? dmg = col.GetComponentInParent<IDamageable>();
                if (dmg == null)
                    continue;

                Unit? u = dmg.GetUnit();
                if (u != null && hq != null && u.NetworkHQ != null && u.NetworkHQ == hq)
                    continue;
                if (u != null && !HitUnits.Add(u.GetInstanceID()))
                    continue;

                float dist = Vector3.Distance(col.bounds.center, worldPos);
                float r = Mathf.Max(dist / Mathf.Max(blastPower, 0.5f), 1f);
                float overpressure = peakOp / (r * r * r);
                if (overpressure < 0.5f)
                    continue;

                float proximity = 1f - Mathf.Clamp01(dist / Mathf.Max(blastRadius, 1f));
                dmg.TakeDamage(0f, overpressure, Mathf.Clamp01(proximity), 0f, 0f, dealer);
            }
        }

        /// <summary>Kill torpedo without firing its warhead (Hydra 450 kg / AShM would nuke the area).</summary>
        internal static void KillMissile(Missile victim, PersistentID dealer)
        {
            if (victim == null || victim.disabled)
                return;

            try
            {
                victim.TakeDamage(0f, CrosswimConstants.BlastYieldKg, 1f, 0f, 5000f, dealer);
            }
            catch
            {
                // ignore
            }

            if (!victim.disabled)
            {
                try
                {
                    victim.Networkdisabled = true;
                }
                catch
                {
                    // ignore
                }
            }

            if (victim != null && victim.gameObject != null)
                Object.Destroy(victim.gameObject);
        }
    }
}
