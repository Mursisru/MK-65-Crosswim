using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Hangar fit: MainEngine→−Z, DockingPlace snap (lossy-scale safe), then flush Main top to pylon.
    /// </summary>
    internal static class VisualFit
    {
        internal static void Apply(Transform vis)
        {
            if (vis == null)
                return;

            vis.localPosition = Vector3.zero;
            vis.localRotation = Quaternion.identity;
            vis.localScale = Vector3.one;

            OrientForward(vis);
            RollDockingUp(vis);

            float span = MarkerSpanLocal(vis);
            if (span < 0.5f)
                span = CrosswimConstants.LengthM;
            float s = (CrosswimConstants.LengthM * CrosswimConstants.VisualScaleMult) / span;
            vis.localScale = new Vector3(s, s, s);

            // Scale first, then snap with world→parent (matches Hydra; fixes 0.75 drift).
            SnapDockingPlace(vis);
            FlushMeshTopToPylon(vis);
            vis.localPosition += Vector3.down * CrosswimConstants.MountClearanceM;

            CrosswimPlugin.ModLog?.LogInfo(
                $"VisualFit scale={s:F4} span={span:F2} rot={vis.localRotation.eulerAngles} pos={vis.localPosition}");
        }

        /// <summary>
        /// MainEngine sits at Unity −X and looks blunt when that end faces the nose.
        /// Put MainEngine at −Z so the opposite (pointed) end faces +Z forward.
        /// </summary>
        private static void OrientForward(Transform vis)
        {
            Transform? engine = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.NoseAliases);
            Transform? vlsb = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AftAliases);
            if (engine == null || vlsb == null)
            {
                vis.localRotation = Quaternion.Euler(0f, -90f, 0f);
                CrosswimPlugin.ModLog?.LogWarning("VisualFit: markers missing — yaw-90 fallback");
                return;
            }

            Vector3 enginePos = LocalInVis(vis, engine);
            Vector3 vlsbPos = LocalInVis(vis, vlsb);
            Vector3 engineAxis = enginePos - vlsbPos;
            if (engineAxis.sqrMagnitude < 1e-8f)
            {
                vis.localRotation = Quaternion.Euler(0f, -90f, 0f);
                return;
            }

            vis.localRotation = Quaternion.FromToRotation(engineAxis.normalized, Vector3.back);

            enginePos = RotateLocal(vis.localRotation, LocalInVis(vis, engine));
            vlsbPos = RotateLocal(vis.localRotation, LocalInVis(vis, vlsb));
            if (enginePos.z > vlsbPos.z)
                vis.localRotation = Quaternion.Euler(0f, 180f, 0f) * vis.localRotation;
        }

        private static void RollDockingUp(Transform vis)
        {
            Transform? dock = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AttachPylonAliases);
            if (dock == null)
                return;
            Vector3 d = RotateLocal(vis.localRotation, LocalInVis(vis, dock));
            Vector3 radial = new Vector3(d.x, d.y, 0f);
            if (radial.sqrMagnitude < 1e-8f)
                return;
            float ang = Vector3.SignedAngle(radial.normalized, Vector3.up, Vector3.forward);
            if (Mathf.Abs(ang) < 0.5f)
                return;
            vis.localRotation = Quaternion.AngleAxis(ang, Vector3.forward) * vis.localRotation;
        }

        private static void SnapDockingPlace(Transform vis)
        {
            if (vis.parent == null)
                return;
            Transform? attach = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AttachPylonAliases);
            if (attach == null)
            {
                CrosswimPlugin.ModLog?.LogWarning("VisualFit: DockingPlace missing");
                return;
            }

            Vector3 attachInParent = vis.parent.InverseTransformPoint(attach.position);
            vis.localPosition -= attachInParent;
        }

        /// <summary>
        /// DockingPlace empty sits above the hull — after snap there is air under the pylon.
        /// Pull Main top (world AABB → parent Y) to the hardpoint. Skips Cube/OP.
        /// </summary>
        private static void FlushMeshTopToPylon(Transform vis)
        {
            if (vis.parent == null)
                return;

            float maxY = float.MinValue;
            bool any = false;
            Renderer[] rs = vis.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled || !r.gameObject.activeSelf)
                    continue;
                if (!IsHullForFlush(r.gameObject.name))
                    continue;

                Bounds b = r.bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;
                for (int ix = -1; ix <= 1; ix += 2)
                {
                    for (int iy = -1; iy <= 1; iy += 2)
                    {
                        for (int iz = -1; iz <= 1; iz += 2)
                        {
                            Vector3 world = new Vector3(c.x + ix * e.x, c.y + iy * e.y, c.z + iz * e.z);
                            float y = vis.parent.InverseTransformPoint(world).y;
                            if (y > maxY)
                                maxY = y;
                            any = true;
                        }
                    }
                }
            }

            if (!any)
                return;
            vis.localPosition += new Vector3(0f, -maxY, 0f);
        }

        private static bool IsHullForFlush(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (CrosswimOpening.IsOpeningPart(name))
                return false;
            return name.Equals("Main", System.StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Main.", System.StringComparison.OrdinalIgnoreCase);
        }

        private static Vector3 LocalInVis(Transform vis, Transform t)
        {
            if (t.parent == vis)
                return t.localPosition;
            Vector3 p = t.localPosition;
            Transform? cur = t.parent;
            while (cur != null && cur != vis)
            {
                p = cur.localRotation * Vector3.Scale(p, cur.localScale) + cur.localPosition;
                cur = cur.parent;
            }
            return p;
        }

        private static Vector3 RotateLocal(Quaternion rot, Vector3 local) => rot * local;

        private static float MarkerSpanLocal(Transform vis)
        {
            Transform? aft = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AftAliases);
            Transform? nose = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.NoseAliases);
            if (aft == null || nose == null)
                return 0f;
            return Vector3.Distance(LocalInVis(vis, aft), LocalInVis(vis, nose));
        }
    }
}
