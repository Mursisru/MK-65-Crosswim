using UnityEngine;

namespace Crosswim.Runtime
{
    internal sealed class CrosswimTag : MonoBehaviour
    {
    }

    internal static class CrosswimDetonateGate
    {
        internal static bool Allow;
        internal static int CombatDepth;
    }

    internal static class CrosswimMass
    {
        private static readonly System.Reflection.FieldInfo? MassField =
            typeof(Missile).GetField("mass", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        internal static void Apply(Missile missile, float kg)
        {
            if (missile == null || kg <= 0f)
                return;
            MassField?.SetValue(missile, kg);
            Rigidbody? rb = missile.rb != null ? missile.rb : missile.GetComponent<Rigidbody>();
            if (rb != null)
                rb.mass = kg;
        }
    }

    internal static class CrosswimShellPrep
    {
        private static readonly System.Reflection.FieldInfo? ImpactFuseField =
            typeof(Missile).GetField("impactFuse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo? WarheadField =
            typeof(Missile).GetField("warhead", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo? BlastYieldField =
            typeof(Missile).GetField("blastYield", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        internal static void Prepare(Missile missile)
        {
            if (missile == null)
                return;
            ImpactFuseField?.SetValue(missile, false);
            BlastYieldField?.SetValue(missile, CrosswimConstants.BlastYieldKg);
            Disarm(missile);
        }

        internal static void Disarm(Missile missile)
        {
            if (missile == null)
                return;
            if (WarheadField?.GetValue(missile) is Missile.Warhead wh)
                wh.Armed = false;
        }

        internal static void Arm(Missile missile)
        {
            if (missile == null)
                return;
            if (WarheadField?.GetValue(missile) is Missile.Warhead wh)
                wh.Armed = true;
        }
    }
}
