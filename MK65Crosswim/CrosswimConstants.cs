using UnityEngine;

namespace Crosswim
{
    internal static class CrosswimConstants
    {
        public const string MissileJsonKey = "missilepack_mk65_crosswim";
        public const string MountJsonKey = "MissilePack_MK65_Crosswim_single";
        public const string WeaponInfoName = "MK-65 Crosswim";
        public const string MountDisplayName = "MK-65 Crosswim";
        public const string UnitName = "MK-65 Crosswim";
        public const string ShortName = "MK-65";
        public const string BogeyName = "Crosswim";
        public const string SeekerTypeName = "Sonar";
        public const string Description =
            "Anti-torpedo interceptor. Ballistic water entry, solid-fuel swim, intercept kinematics.";

        public const string PreviewIconFileName = "PreviewCrosswim.png";
        public const string PreviewIconResource = "Crosswim.Resources.PreviewCrosswim.png";
        public const int PreviewIconInkMin = 12;
        public const int PreviewIconAlphaBase = 255;
        public const int PreviewIconStrokeRadius = 3;

        public const string ShellMissileKey = "AShM1";
        public const string ShellMissileKeyAlt = "AShM2";
        public const string ShellMountKey = "AShM1_single";
        public const string ShellMountKeyAlt = "AShM2_single";
        public const string AshmPrefix1 = "AShM1_";
        public const string AshmPrefix2 = "AShM2_";

        public const string VisualRootName = "CrosswimVisual";
        public const string BundleModName = "MK65Crosswim";
        public const string MeshPrefabAsset = VisualRootName;
        public const string NobpFileName = "MK65Crosswim.nobp";

        // Match AShM-300 envelope (~4.4 m body).
        public const float LengthM = 4.4f;
        public const float WidthM = 0.55f;
        public const float HeightM = 0.55f;
        public const float VisualScaleMult = 0.55f;
        // Blender Main radius at DockingPlace station (~0.90m).
        public const float HullRadiusM = 0.90f;
        public const float DockingPlaceBlenderZM = 1.02967f;
        // Hardpoint sits inside the pylon plate — extra lift after rail kiss (screenshot gap).
        public const float PylonLiftExtraM = 0.43f;
        // Only sample Main verts near DockingPlace along body (±m in parent).
        public const float RailStationHalfM = 0.55f;
        public const float MountClearanceM = 0f;

        // Swim exhaust on empties (AGM-68-class plume; empty localScale 100).
        public const float FxWorldScaleM = 0.48f;
        public const float FxAftNudgeM = 0.08f;
        public const float FxMaxStartSize = 0.35f;
        // Unity FBX File Scale: child localScale 100, localPosition still in meters.
        public const float FbxChildScale = 100f;

        public const float MassKg = 500f;
        public const float BlastYieldKg = 100f;
        public const float Cost = 2.4f;
        public const float RadarSize = 0.22f;

        public const float SwimSpeedKmh = 550f;
        public const float SwimSpeedMps = SwimSpeedKmh / 3.6f;
        public const float SwimThrustRampS = 2.2f;
        public const float SwimTurnRateDeg = 180f;
        public const float SwimDepthM = 6f;
        public const float WaterDensity = 1025f;
        public const float SwimCdArea = 0.006f;
        public const float SwimPropThrustN =
            0.5f * WaterDensity * SwimSpeedMps * SwimSpeedMps * SwimCdArea;
        public const float SwimSideDamp = 4.5f;
        public const float SwimHeaveDamp = 3.5f;
        public const float SwimBuoyancyGain = 3.2f;
        public const float SwimFinAuthority = 0.00045f;
        public const float SwimMaxAngVelRad = 2.2f;
        public const float SwimAlignDegS = 180f;
        public const float SwimLinearDrag = 400f;
        public const float SwimAngularDrag = 2.5f;
        public const float InterceptLeadMaxS = 8f;
        public const float TorpedoScanRangeM = 18000f;
        public const float ShipScanRangeM = 25000f;
        public const float DetonateProximityM = 8f;
        public const float SoftKillTimeoutS = 240f;
        public const float WaterEntrySubmergeM = 1.2f;

        public const float BallisticDrag = 0.01f;
        public const float BallisticAngularDrag = 18f;
        public const float BallisticAlignDegS = 220f;
        public const float BallisticMaxAngVelRad = 1.2f;
        public const float BallisticAngVelDamp = 0.82f;

        public const float VlsbLoftAltM = 220f;
        public const float VlsbThrustMps2 = 38f;
        public const float VlsbMaxTimeS = 8f;
        public const float VlsbDryMassKg = 80f;

        public const float DockEjectSpeed = 14f;
        public const float DockMassKg = 12f;
        public const float DockDestroyS = 12f;
        public const float OpeningFrames = 120f;
        public const float OpeningFpsFallback = 24f;

        public const float Pk = 0.55f;
        public const float FireIntervalS = 1.2f;
        public const float AiMinRangeM = 400f;
        public const float AiMaxRangeM = 28000f;

        // DockingPort = mesh ring (hide on ship). DockingPlace = snap empty — never hide via this list.
        public static readonly string[] DockAliases = { "DockingPort" };
        public static readonly string[] VlsbAliases = { "VLSB" };
        public static readonly string[] MainEngineAliases = { "MainEngineEffectSpawn" };
        // Blender +X = sharp nose empty; Unity FBX flips empty to −X (aft after orient). Mesh tip stays +X→+Z.
        public static readonly string[] NoseAliases = { "MainEngineEffectSpawn" };
        // VLS booster exhaust empties — only while VLSB is active.
        public static readonly string[] VlsbFxAliases = { "VLSBEngineEffectsSpawn" };
        // No-VLS swim motor: same empty (aft after orient).
        public static readonly string[] SwimFxAliases = { "MainEngineEffectSpawn" };
        public static readonly string[] AftAliases = { "VLSB" };
        public static readonly string[] OpeningAliases = { "Cube", "OP", "Opening" };
        public static readonly string[] DecalAliases = { "Decal" };
        public static readonly string[] ShipNameTokens = { "Dynamo", "Argus" };
        // Snap ONLY to DockingPlace empty (DockingPort is a mesh ring).
        public static readonly string[] AttachPylonAliases = { "DockingPlace" };
    }
}
