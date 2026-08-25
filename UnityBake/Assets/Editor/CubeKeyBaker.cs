using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Crosswim.UnityBake
{
    /// <summary>
    /// Absolute FBX localRotation. Clip chosen so frame0 matches None-import rest (same axes as OP).
    /// Sample one clip on a fresh instance per Cube. No Convert, no bind*delta.
    /// </summary>
    internal static class CubeKeyBaker
    {
        internal const int FrameCount = 121;
        internal const float FpsFallback = 24f;
        internal static readonly string[] Names = { "Cube", "Cube.001", "Cube.002", "Cube.003" };

        // Prefer Blender dump action names when they also match None rest.
        private static readonly Dictionary<string, string> PreferredAction =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Cube", "OpeningCube.001" },
                { "Cube.001", "Cube.001Действие" },
                { "Cube.002", "Cube.002Действие" },
                { "Cube.003", "OpeningOPCube" },
            };

        internal struct BindPose
        {
            public Quaternion rot;
            public bool ok;
        }

        internal static BindPose[] BakeAndWriteKeys(string fbxPath)
        {
            var binds = new BindPose[Names.Length];

            // None rest = hangar/OP truth (axis conversion already applied by importer).
            Quaternion[] noneRest = CaptureNoneRest(fbxPath);

            ConfigureImporterLegacy(fbxPath);
            AnimationClip[] clips = LoadClips(fbxPath);
            LogClipNames(clips);
            if (clips.Length == 0)
            {
                Debug.LogError("Crosswim CubeKeyBaker: no AnimationClips.");
                return binds;
            }

            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
            {
                Debug.LogError("Crosswim CubeKeyBaker: FBX load failed.");
                return binds;
            }

            float fps = FpsFallback;
            var rx = new float[Names.Length * FrameCount];
            var ry = new float[Names.Length * FrameCount];
            var rz = new float[Names.Length * FrameCount];
            var rw = new float[Names.Length * FrameCount];

            for (int i = 0; i < Names.Length; i++)
            {
                AnimationClip clip = PickClip(clips, Names[i], noneRest[i], fbx, ref fps);
                if (clip == null)
                {
                    Debug.LogError("Crosswim CubeKeyBaker NO CLIP for " + Names[i]);
                    continue;
                }

                GameObject root = UnityEngine.Object.Instantiate(fbx);
                root.hideFlags = HideFlags.HideAndDontSave;
                DisableAutoPlay(root);
                Transform fin = FindChild(root.transform, Names[i]);
                if (fin == null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    continue;
                }

                for (int f = 0; f < FrameCount; f++)
                {
                    clip.SampleAnimation(root, f / fps);
                    Quaternion q = fin.localRotation;
                    int idx = i * FrameCount + f;
                    rx[idx] = q.x;
                    ry[idx] = q.y;
                    rz[idx] = q.z;
                    rw[idx] = q.w;
                }

                binds[i].rot = new Quaternion(rx[i * FrameCount], ry[i * FrameCount], rz[i * FrameCount], rw[i * FrameCount]);
                binds[i].ok = true;

                float restDot = Mathf.Abs(Quaternion.Dot(binds[i].rot, noneRest[i]));
                Quaternion last = new Quaternion(
                    rx[i * FrameCount + FrameCount - 1],
                    ry[i * FrameCount + FrameCount - 1],
                    rz[i * FrameCount + FrameCount - 1],
                    rw[i * FrameCount + FrameCount - 1]);
                Quaternion.Inverse(binds[i].rot).ToAngleAxis(out _, out _);
                (Quaternion.Inverse(binds[i].rot) * last).ToAngleAxis(out float ang, out Vector3 axis);
                Vector3 e0 = binds[i].rot.eulerAngles;
                Vector3 e1 = last.eulerAngles;
                Debug.Log(
                    $"Crosswim CubeKeyBaker abs '{Names[i]}' clip='{clip.name}' restDot={restDot:F4} " +
                    $"f0=({e0.x:F1},{e0.y:F1},{e0.z:F1}) f120=({e1.x:F1},{e1.y:F1},{e1.z:F1}) " +
                    $"openAng={ang:F1} axis=({axis.x:F2},{axis.y:F2},{axis.z:F2})");

                UnityEngine.Object.DestroyImmediate(root);
            }

            WriteKeys(rx, ry, rz, rw, fps);
            return binds;
        }

        internal static void ApplyBinds(GameObject root, BindPose[] binds)
        {
            if (root == null || binds == null)
                return;
            for (int i = 0; i < Names.Length && i < binds.Length; i++)
            {
                if (!binds[i].ok)
                    continue;
                Transform t = FindChild(root.transform, Names[i]);
                if (t == null)
                    continue;
                t.localRotation = binds[i].rot;
            }
        }

        private static Quaternion[] CaptureNoneRest(string fbxPath)
        {
            var rest = new Quaternion[Names.Length];
            for (int i = 0; i < Names.Length; i++)
                rest[i] = Quaternion.identity;

            ModelImporter imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (imp != null)
            {
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.useFileScale = true;
                imp.globalScale = 1f;
                imp.preserveHierarchy = true;
                imp.SaveAndReimport();
            }

            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
                return rest;

            GameObject root = UnityEngine.Object.Instantiate(fbx);
            root.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < Names.Length; i++)
            {
                Transform fin = FindChild(root.transform, Names[i]);
                if (fin == null)
                    continue;
                rest[i] = fin.localRotation;
                Vector3 e = rest[i].eulerAngles;
                Vector3 p = fin.localPosition;
                Debug.Log($"Crosswim CubeKeyBaker NoneRest '{Names[i]}' pos=({p.x:F4},{p.y:F4},{p.z:F4}) euler=({e.x:F1},{e.y:F1},{e.z:F1})");
            }
            UnityEngine.Object.DestroyImmediate(root);
            return rest;
        }

        private static void ConfigureImporterLegacy(string fbxPath)
        {
            ModelImporter imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (imp == null)
                return;
            imp.animationType = ModelImporterAnimationType.Legacy;
            imp.importAnimation = true;
            imp.resampleCurves = false;
            imp.animationCompression = ModelImporterAnimationCompression.Off;
            imp.useFileScale = true;
            imp.globalScale = 1f;
            imp.preserveHierarchy = true;
            imp.SaveAndReimport();
        }

        private static AnimationClip PickClip(
            AnimationClip[] clips,
            string finName,
            Quaternion noneRest,
            GameObject fbx,
            ref float fps)
        {
            PreferredAction.TryGetValue(finName, out string prefer);
            AnimationClip best = null;
            float bestScore = -1e9f;

            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null)
                    continue;
                string n = clip.name ?? string.Empty;
                int bar = n.IndexOf('|');
                string owner = bar > 0 ? n.Substring(0, bar) : n;
                string take = bar > 0 ? n.Substring(bar + 1) : n;
                if (!string.Equals(owner, finName, StringComparison.Ordinal))
                    continue;
                if (take.IndexOf("OpeningOP", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.Equals(prefer, take, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (take.IndexOf("Net.001", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                GameObject root = UnityEngine.Object.Instantiate(fbx);
                root.hideFlags = HideFlags.HideAndDontSave;
                DisableAutoPlay(root);
                Transform fin = FindChild(root.transform, finName);
                if (fin == null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    continue;
                }

                float rate = clip.frameRate > 1f ? clip.frameRate : FpsFallback;
                clip.SampleAnimation(root, 0f);
                Quaternion q0 = fin.localRotation;
                clip.SampleAnimation(root, 120f / rate);
                Quaternion q1 = fin.localRotation;
                UnityEngine.Object.DestroyImmediate(root);

                float restDot = Mathf.Abs(Quaternion.Dot(q0, noneRest));
                (Quaternion.Inverse(q0) * q1).ToAngleAxis(out float openAng, out _);
                openAng = Mathf.Abs(openAng);
                if (openAng > 180f)
                    openAng = 360f - openAng;

                // Must match None rest — otherwise wrong action / wrong axis space.
                if (restDot < 0.995f)
                    continue;
                if (openAng < 15f)
                    continue;

                float score = openAng + restDot * 10f;
                if (!string.IsNullOrEmpty(prefer) &&
                    string.Equals(take, prefer, StringComparison.OrdinalIgnoreCase))
                    score += 50f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = clip;
                    fps = rate;
                }
            }

            return best;
        }

        private static AnimationClip[] LoadClips(string fbxPath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            var list = new List<AnimationClip>(32);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not AnimationClip clip || clip == null)
                    continue;
                if (clip.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(clip);
            }
            return list.ToArray();
        }

        private static void LogClipNames(AnimationClip[] clips)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null)
                    continue;
                if (sb.Length > 0)
                    sb.Append("; ");
                sb.Append(clips[i].name);
            }
            Debug.Log("Crosswim CubeKeyBaker clips: " + sb);
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && string.Equals(all[i].name, name, StringComparison.Ordinal))
                    return all[i];
            }
            return null;
        }

        private static void DisableAutoPlay(GameObject root)
        {
            Animation[] anims = root.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] == null)
                    continue;
                anims[i].playAutomatically = false;
                anims[i].Stop();
                anims[i].enabled = false;
            }
        }

        private static void WriteKeys(float[] rx, float[] ry, float[] rz, float[] rw, float fps)
        {
            string outPath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "MK65Crosswim", "Runtime", "CrosswimCubeKeys.cs"));
            var sb = new StringBuilder(64 * 1024);
            sb.AppendLine("// AUTO-GENERATED by UnityBake CubeKeyBaker — absolute FBX localRotation. Do not edit.");
            sb.AppendLine("// Driver: localRotation = Sample(fin, frame). No bind multiply. No Convert.");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace Crosswim.Runtime");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Absolute Unity FBX localRotation for Opening. Frame 0 = closed rest.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    internal static class CrosswimCubeKeys");
            sb.AppendLine("    {");
            sb.AppendLine("        internal const int FinCount = 4;");
            sb.AppendLine($"        internal const int FrameCount = {FrameCount};");
            sb.AppendLine($"        internal const float Fps = {fps.ToString("G9", CultureInfo.InvariantCulture)}f;");
            sb.AppendLine("        internal static readonly string[] Names = { \"Cube\", \"Cube.001\", \"Cube.002\", \"Cube.003\" };");
            sb.AppendLine();
            WriteArray(sb, "Rx", rx);
            WriteArray(sb, "Ry", ry);
            WriteArray(sb, "Rz", rz);
            WriteArray(sb, "Rw", rw);
            sb.AppendLine("        internal static int Index(int fin, int frame) => fin * FrameCount + frame;");
            sb.AppendLine();
            sb.AppendLine("        internal static Quaternion Sample(int fin, float frame)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (fin < 0 || fin >= FinCount)");
            sb.AppendLine("                return Quaternion.identity;");
            sb.AppendLine();
            sb.AppendLine("            float f = frame;");
            sb.AppendLine("            if (f < 0f)");
            sb.AppendLine("                f = 0f;");
            sb.AppendLine("            float last = FrameCount - 1;");
            sb.AppendLine("            if (f > last)");
            sb.AppendLine("                f = last;");
            sb.AppendLine();
            sb.AppendLine("            int i0 = (int)f;");
            sb.AppendLine("            int i1 = i0 + 1;");
            sb.AppendLine("            if (i1 >= FrameCount)");
            sb.AppendLine("                i1 = FrameCount - 1;");
            sb.AppendLine("            float t = f - i0;");
            sb.AppendLine("            int a = Index(fin, i0);");
            sb.AppendLine("            int b = Index(fin, i1);");
            sb.AppendLine("            Quaternion qa = new Quaternion(Rx[a], Ry[a], Rz[a], Rw[a]);");
            sb.AppendLine("            Quaternion qb = new Quaternion(Rx[b], Ry[b], Rz[b], Rw[b]);");
            sb.AppendLine("            return Quaternion.Slerp(qa, qb, t);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("Crosswim CubeKeyBaker wrote " + outPath);
        }

        private static void WriteArray(StringBuilder sb, string name, float[] vals)
        {
            sb.AppendLine($"        private static readonly float[] {name} =");
            sb.AppendLine("        {");
            for (int i = 0; i < vals.Length; i++)
            {
                if (i % 8 == 0)
                    sb.Append("            ");
                sb.Append(vals[i].ToString("0.0000000", CultureInfo.InvariantCulture));
                sb.Append("f");
                if (i != vals.Length - 1)
                    sb.Append(", ");
                if (i % 8 == 7 || i == vals.Length - 1)
                    sb.AppendLine();
            }
            sb.AppendLine("        };");
            sb.AppendLine();
        }
    }
}
