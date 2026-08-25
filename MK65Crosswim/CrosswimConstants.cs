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
        public const float VisualScaleMult = 0.75f;
        public const float MountClearanceM = 0f;
        // Unity FBX File Scale: child localScale 100, localPosition still in meters.
        public const float FbxChildScale = 100f;

        public const float MassKg = 500f;
        public const float BlastYieldKg = 100f;
        public const float Cost = 2.4f;
        public const float RadarSize = 0.22f;

        public const float SwimSpeedKmh = 550f;
        public const float SwimSpeedMps = SwimSpeedKmh / 3.6f;
        public const float SwimThrustRampS = 2.2f;
        public const float SwimTurnRateDeg = 55f;
        public const float SwimDepthM = 6f;
        public const float SwimLinearDrag = 4.2f;
        public const float SwimAngularDrag = 8f;
        public const float InterceptLeadMaxS = 8f;
        public const float TorpedoScanRangeM = 18000f;
        public const float ShipScanRangeM = 25000f;
        public const float DetonateProximityM = 8f;
        public const float SoftKillTimeoutS = 240f;
        public const float WaterEntrySubmergeM = 1.2f;

        public const float BallisticDrag = 0.04f;
        public const float BallisticAngularDrag = 12f;
        public const float BallisticAlignDegS = 40f;

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
        // Blender +X = sharp nose (MainEngine / OP). VLSB = blunt booster at −X.
        // Nose marker is the empty only — never OP meshes (those are geometry).
        public static readonly string[] NoseAliases = { "MainEngineEffectSpawn" };
        public static readonly string[] AftAliases = { "VLSB" };
        public static readonly string[] OpeningAliases = { "Cube", "OP", "Opening" };
        public static readonly string[] DecalAliases = { "Decal" };
        public static readonly string[] ShipNameTokens = { "Dynamo", "Argus" };
        // Snap ONLY to DockingPlace empty (DockingPort is a mesh ring).
        public static readonly string[] AttachPylonAliases = { "DockingPlace" };
    }
}
