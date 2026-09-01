using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>Ships without hull sonar (OTB, patrol, landing craft) — same exclusions as MK-88.</summary>
    internal static class CrosswimSonarRegistry
    {
        internal static bool IsExcluded(UnitDefinition? def)
        {
            if (def == null)
                return true;

            string key = (def.unitName + " " + def.jsonKey).ToLowerInvariant();
            if (key.Contains("otb"))
                return true;
            if (key.Contains("landing craft") || key.Contains("landingcraft"))
                return true;
            if (key.Contains("patrol"))
                return true;
            return false;
        }

        internal static float ComputeRangeM(UnitDefinition? def)
        {
            float len = def != null ? def.length : 60f;
            if (len < 1f)
                len = 60f;

            float t = Mathf.Clamp01(len / CrosswimConstants.SonarReferenceLengthM);
            return Mathf.Lerp(CrosswimConstants.SonarMinRangeM, CrosswimConstants.SonarMaxRangeM, t);
        }
    }
}
