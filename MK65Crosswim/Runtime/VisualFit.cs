using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Hangar: FBX empty X≠mesh tip — MainEngine empty at −Z so mesh tip is +Z.
    /// Snap DockingPlace, kiss Main rail at dock station, then lift into pylon plate.
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

            SnapDockingPlace(vis);
            KissRailToPylon(vis);

            Transform? dock = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AttachPylonAliases);
            Vector3 dockParent = dock != null && vis.parent != null
                ? vis.parent.InverseTransformPoint(dock.position)
                : (dock != null ? NodeToParent(vis, dock) : vis.localPosition);
            CrosswimPlugin.ModLog?.LogInfo(
                $"VisualFit scale={s:F4} span={span:F2} rot={vis.localRotation.eulerAngles} pos={vis.localPosition} dockParent={dockParent}");
        }

        // FBX: MainEngine empty at −X, mesh tip at +X. Empty → −Z ⇒ mesh tip → +Z.
        private static void OrientForward(Transform vis)
        {
            Transform? noseEmpty = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.NoseAliases);
            Transform? aft = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AftAliases);
            if (noseEmpty == null || aft == null)
            {
                vis.localRotation = Quaternion.Euler(0f, -90f, 0f);
                CrosswimPlugin.ModLog?.LogWarning("VisualFit: markers missing — yaw-90 fallback");
                return;
            }

            Vector3 emptyPos = NodeToVis(vis, noseEmpty);
            Vector3 aftPos = NodeToVis(vis, aft);
            Vector3 emptyAxis = emptyPos - aftPos;
            if (emptyAxis.sqrMagnitude < 1e-8f)
            {
                vis.localRotation = Quaternion.Euler(0f, -90f, 0f);
                return;
            }

            vis.localRotation = Quaternion.FromToRotation(emptyAxis.normalized, Vector3.back);

            emptyPos = vis.localRotation * NodeToVis(vis, noseEmpty);
            aftPos = vis.localRotation * NodeToVis(vis, aft);
            if (emptyPos.z > aftPos.z)
                vis.localRotation = Quaternion.Euler(0f, 180f, 0f) * vis.localRotation;

            emptyPos = vis.localRotation * NodeToVis(vis, noseEmpty);
            aftPos = vis.localRotation * NodeToVis(vis, aft);
            CrosswimPlugin.ModLog?.LogInfo(
                $"VisualFit orient emptyZ={emptyPos.z:F2} aftZ={aftPos.z:F2} meshTip=+Z rot={vis.localRotation.eulerAngles}");
        }

        private static void RollDockingUp(Transform vis)
        {
            Transform? dock = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AttachPylonAliases);
            if (dock == null)
                return;
            Vector3 d = vis.localRotation * NodeToVis(vis, dock);
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
            Transform? attach = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AttachPylonAliases);
            if (attach == null)
            {
                CrosswimPlugin.ModLog?.LogWarning("VisualFit: DockingPlace missing");
                return;
            }

            if (vis.parent != null && vis.gameObject.activeInHierarchy)
            {
                Vector3 attachInParent = vis.parent.InverseTransformPoint(attach.position);
                vis.localPosition -= attachInParent;
                return;
            }

            Vector3 dockVis = NodeToVis(vis, attach);
            vis.localPosition = -(vis.localRotation * MulScale(vis.localScale, dockVis));
        }

        /// <summary>
        /// DockingPlace is above the hull rail; DockingPort lug can stick above the hardpoint.
        /// Kiss only Main verts near the dock station, then add plate lift. Up only.
        /// </summary>
        private static void KissRailToPylon(Transform vis)
        {
            if (vis.parent == null)
                return;

            float lift = 0f;
            float railY = 0f;
            if (TryStationRailParentY(vis, out railY))
            {
                if (railY < -1e-4f)
                    lift += -railY;
            }
            else
            {
                float emptyAbove = CrosswimConstants.DockingPlaceBlenderZM - CrosswimConstants.HullRadiusM;
                if (emptyAbove < 0f)
                    emptyAbove = 0f;
                lift += emptyAbove * vis.localScale.y;
            }

            lift += CrosswimConstants.PylonLiftExtraM;
            if (lift <= 1e-4f)
                return;

            vis.localPosition += Vector3.up * lift;
            CrosswimPlugin.ModLog?.LogInfo($"VisualFit kiss lift={lift:F3} railY={railY:F3}");
        }

        private static bool TryStationRailParentY(Transform vis, out float maxY)
        {
            maxY = float.MinValue;
            Transform? dock = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AttachPylonAliases);
            if (dock == null || vis.parent == null)
                return false;

            Vector3 dockParent = vis.parent.InverseTransformPoint(dock.position);
            float half = CrosswimConstants.RailStationHalfM;
            bool any = false;

            Renderer[] rs = vis.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !IsMainHull(r.gameObject.name))
                    continue;
                MeshFilter? mf = r.GetComponent<MeshFilter>();
                Mesh? mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null)
                    continue;
                Vector3[] verts = mesh.vertices;
                if (verts == null || verts.Length == 0)
                    continue;

                bool live = r.gameObject.activeInHierarchy && vis.gameObject.activeInHierarchy;
                Matrix4x4 toParent = live
                    ? vis.parent.worldToLocalMatrix * r.localToWorldMatrix
                    : Matrix4x4.identity;

                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 p;
                    if (live)
                        p = toParent.MultiplyPoint3x4(verts[v]);
                    else if (!TryLocalPointToParent(vis, r.transform, verts[v], out p))
                        continue;

                    // Station band around DockingPlace along body (parent Z) and lateral X.
                    if (Mathf.Abs(p.z - dockParent.z) > half)
                        continue;
                    if (Mathf.Abs(p.x - dockParent.x) > half)
                        continue;
                    // Lug / junk above the empty — ignore, we kiss the hull saddle.
                    if (p.y > dockParent.y + 0.02f)
                        continue;

                    if (p.y > maxY)
                        maxY = p.y;
                    any = true;
                }
            }

            return any;
        }

        private static bool IsMainHull(string name)
        {
            if (string.IsNullOrEmpty(name) || CrosswimOpening.IsOpeningPart(name))
                return false;
            return name.Equals("Main", System.StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Main.", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryLocalPointToParent(Transform vis, Transform renderer, Vector3 meshLocal, out Vector3 parent)
        {
            parent = MulScale(renderer.localScale, meshLocal);
            parent = renderer.localRotation * parent + renderer.localPosition;
            Transform? t = renderer.parent;
            int guard = 0;
            while (t != null && t != vis.parent && guard++ < 64)
            {
                if (t == vis)
                {
                    parent = MulScale(vis.localScale, parent);
                    parent = vis.localRotation * parent + vis.localPosition;
                    t = vis.parent;
                    continue;
                }
                parent = MulScale(t.localScale, parent);
                parent = t.localRotation * parent + t.localPosition;
                t = t.parent;
            }
            return vis.parent != null;
        }

        private static Vector3 NodeToVis(Transform vis, Transform node)
        {
            Vector3 p = Vector3.zero;
            Transform t = node;
            int guard = 0;
            while (t != null && t != vis && guard++ < 32)
            {
                p = t.localRotation * MulScale(t.localScale, p) + t.localPosition;
                t = t.parent;
            }
            return p;
        }

        private static Vector3 NodeToParent(Transform vis, Transform node)
        {
            Vector3 visLocal = NodeToVis(vis, node);
            return vis.localRotation * MulScale(vis.localScale, visLocal) + vis.localPosition;
        }

        private static Vector3 MulScale(Vector3 scale, Vector3 p) =>
            new Vector3(p.x * scale.x, p.y * scale.y, p.z * scale.z);

        private static float MarkerSpanLocal(Transform vis)
        {
            Transform? aft = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.AftAliases);
            Transform? nose = CrosswimVisualParts.FindByAliases(vis, CrosswimConstants.NoseAliases);
            if (aft == null || nose == null)
                return 0f;
            return Vector3.Distance(NodeToVis(vis, aft), NodeToVis(vis, nose));
        }
    }
}
