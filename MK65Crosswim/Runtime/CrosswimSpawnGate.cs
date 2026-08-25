using System.Reflection;
using Crosswim.Bootstrap;
using Crosswim.Blueprinter;
using UnityEngine;

namespace Crosswim.Runtime
{
    internal static class CrosswimSpawnGate
    {
        private static readonly FieldInfo? InfoField =
            typeof(Missile).GetField("info", BindingFlags.Instance | BindingFlags.NonPublic);

        private const float PendingTtlS = 8f;
        internal static int Pending;
        private static float _pendingUntil = -1f;

        private static Missile? _stampMissile;
        private static UnitDefinition? _stampSavedDef;

        internal static void NoteFire()
        {
            Expire();
            Pending++;
            _pendingUntil = Time.realtimeSinceStartup + PendingTtlS;
        }

        internal static bool TryBegin()
        {
            Expire();
            if (Pending <= 0)
                return false;
            Pending--;
            return true;
        }

        internal static bool IsOurFlyPrefab(GameObject? go)
        {
            if (go == null)
                return false;
            GameObject? fly = CrosswimBootstrap.Definition?.unitPrefab;
            return fly != null && ReferenceEquals(go, fly);
        }

        internal static bool BeginPrefabStamp(GameObject? prefab)
        {
            EndPrefabStamp();
            MissileDefinition? ours = CrosswimBootstrap.Definition;
            if (prefab == null || ours == null)
                return false;
            Missile? m = prefab.GetComponent<Missile>() ?? prefab.GetComponentInChildren<Missile>(true);
            if (m == null)
                return false;
            _stampMissile = m;
            _stampSavedDef = m.definition;
            m.definition = ours;
            return true;
        }

        internal static void EndPrefabStamp()
        {
            if (_stampMissile != null && _stampSavedDef != null)
                _stampMissile.definition = _stampSavedDef;
            _stampMissile = null;
            _stampSavedDef = null;
        }

        internal static void ApplyDisplayIdentity(Missile missile)
        {
            if (missile == null)
                return;
            MissileDefinition? def = CrosswimBootstrap.Definition;
            if (def != null)
                missile.definition = def;
            missile.NetworkunitName = CrosswimConstants.UnitName;
            missile.unitName = CrosswimConstants.UnitName;
            if (!UnitRegistry.TryGetPersistentUnit(missile.persistentID, out PersistentUnit pu) || pu == null)
                return;
            pu.unitName = CrosswimConstants.UnitName;
            if (def != null)
                pu.definition = def;
        }

        internal static void Claim(Missile missile, Unit? fireTarget = null)
        {
            if (missile == null)
                return;
            ApplyDisplayIdentity(missile);
            if (CrosswimBootstrap.Info != null)
                InfoField?.SetValue(missile, CrosswimBootstrap.Info);
            if (missile.GetComponent<CrosswimTag>() == null)
                missile.gameObject.AddComponent<CrosswimTag>();

            missile.SetThrottle(0f);
            CrosswimShellPrep.Prepare(missile);
            CrosswimMass.Apply(missile, CrosswimConstants.MassKg);

            Rigidbody? rb = missile.rb != null ? missile.rb : missile.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.detectCollisions = false;
                Vector3 src = missile.startingVelocity.sqrMagnitude > 0.01f ? missile.startingVelocity : rb.velocity;
                if (src.sqrMagnitude > 0.01f)
                    rb.velocity = src;
            }

            if (fireTarget != null)
                missile.SetTarget(fireTarget);
        }

        internal static void FinishVisual(Missile missile)
        {
            if (missile == null)
                return;
            NobpContent.TryLoad();
            if (NobpContent.VisualPrefab != null)
                PrefabFactory.StampVisualLive(missile.gameObject, NobpContent.VisualPrefab);
            PrefabFactory.HideStockRenderers(missile.gameObject);
            CrosswimFlight.Attach(missile);
        }

        internal static void EnsureController(Missile missile)
        {
            if (missile == null || !CrosswimBootstrap.IsOurMissile(missile))
                return;
            Claim(missile);
            FinishVisual(missile);
        }

        private static void Expire()
        {
            if (Pending <= 0 || _pendingUntil < 0f)
                return;
            if (Time.realtimeSinceStartup <= _pendingUntil)
                return;
            Pending = 0;
            _pendingUntil = -1f;
        }
    }
}
