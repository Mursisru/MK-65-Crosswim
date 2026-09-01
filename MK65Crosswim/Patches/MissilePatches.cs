using HarmonyLib;
using Crosswim.Bootstrap;
using Crosswim.Runtime;
using UnityEngine;

namespace Crosswim.Patches
{
    [HarmonyPatch(typeof(Missile), "LocalStart")]
    internal static class MissileLocalStartPatch
    {
        private static void Postfix(Missile __instance)
        {
            if (CrosswimBootstrap.IsOurMissile(__instance))
                CrosswimSpawnGate.EnsureController(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class MissileStartMissilePatch
    {
        private static void Postfix(Missile __instance)
        {
            if (CrosswimBootstrap.IsOurMissile(__instance))
                CrosswimSpawnGate.EnsureController(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Arm))]
    internal static class MissileArmBlockPatch
    {
        private static bool Prefix(Missile __instance)
        {
            if (!CrosswimBootstrap.IsOurMissile(__instance))
                return true;
            CrosswimFlight? f = __instance.GetComponent<CrosswimFlight>();
            return f != null && f.Phase == CrosswimPhase.Swim;
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate))]
    internal static class MissileDetonateBlockPatch
    {
        private static bool Prefix(Missile __instance)
        {
            if (!CrosswimBootstrap.IsOurMissile(__instance))
                return true;
            if (CrosswimDetonateGate.Allow || CrosswimDetonateGate.CombatDepth > 0)
            {
                CrosswimShellPrep.EnsureBlastYield(__instance);
                CrosswimWarheadFx.Ensure(__instance);
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile.Warhead), nameof(Missile.Warhead.Detonate))]
    internal static class WarheadDetonateYieldPatch
    {
        private static void Prefix(Rigidbody rb, ref float blastYield)
        {
            if (rb == null)
                return;
            Missile? m = rb.GetComponent<Missile>() ?? rb.GetComponentInParent<Missile>();
            if (m == null || !CrosswimBootstrap.IsOurMissile(m))
                return;
            blastYield = CrosswimConstants.BlastYieldKg;
        }
    }

    [HarmonyPatch(typeof(Shockwave), "Start")]
    internal static class ShockwaveStartCrosswimPatch
    {
        private static void Prefix(Shockwave __instance)
        {
            CrosswimWarheadFx.ForceLightYield(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.TakeDamage))]
    internal static class MissileTakeDamageCrosswimPatch
    {
        private static void Prefix(Missile __instance, ref float impactDamage)
        {
            if (!CrosswimBootstrap.IsOurMissile(__instance))
                return;
            impactDamage = 0f;
            CrosswimDetonateGate.CombatDepth++;
        }

        private static void Postfix(Missile __instance)
        {
            if (!CrosswimBootstrap.IsOurMissile(__instance))
                return;
            if (CrosswimDetonateGate.CombatDepth > 0)
                CrosswimDetonateGate.CombatDepth--;
        }
    }

    [HarmonyPatch(typeof(Missile), "DetectCollisions")]
    internal static class MissileDetectCollisionsPatch
    {
        // Always skip vanilla Linecasts for Crosswim — we handle water by sea Y (no hitch).
        private static bool Prefix(Missile __instance) =>
            !CrosswimBootstrap.IsOurMissile(__instance);
    }

    [HarmonyPatch(typeof(Missile), "Steering")]
    internal static class MissileSteeringSkipPatch
    {
        private static bool Prefix(Missile __instance) =>
            !CrosswimBootstrap.IsOurMissile(__instance);
    }

    [HarmonyPatch(typeof(Missile), "ApplyAero")]
    internal static class MissileApplyAeroSkipPatch
    {
        private static bool Prefix(Missile __instance) =>
            !CrosswimBootstrap.IsOurMissile(__instance);
    }

    [HarmonyPatch(typeof(Missile), "MotorThrust")]
    internal static class MissileMotorThrustSkipPatch
    {
        private static bool Prefix(Missile __instance) =>
            !CrosswimBootstrap.IsOurMissile(__instance);
    }
}
