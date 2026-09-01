using System;
using System.Reflection;
using System.Text;
using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Swim exhaust: AGM-68-class PS on Blender empties.
    /// VLSB active → VLSBEngineEffectsSpawn; else → MainEngineEffectSpawn.
    /// </summary>
    internal static class CrosswimMotorFx
    {
        private static readonly FieldInfo? MotorsField =
            typeof(Missile).GetField("motors", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type? MotorType =
            typeof(Missile).GetNestedType("Motor", BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo? ParticlesField =
            MotorType?.GetField("particleSystems", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? FuelMassField =
            MotorType?.GetField("fuelMass", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo? BurnoutMethod =
            MotorType?.GetMethod("Burnout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly string[] ExhaustParts =
        {
            "exhaust", "thrust", "flame", "fire", "engine", "motor", "plume", "nozzle"
        };

        private static GameObject? _hold;
        private static GameObject? _template;
        private static GameObject? _vlsFxHold;
        private static GameObject? _vlsFxTpl;
        private static readonly FieldInfo? TrailLifetimeField =
            typeof(TrailEmitter).GetField("emitLifetime", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void CaptureVlsBoosterFx(VLSBooster booster)
        {
            if (booster == null || _vlsFxTpl != null)
                return;

            if (_vlsFxHold == null)
            {
                _vlsFxHold = new GameObject("Crosswim_VlsFxHold");
                UnityEngine.Object.DontDestroyOnLoad(_vlsFxHold);
                _vlsFxHold.SetActive(false);
            }

            _vlsFxTpl = UnityEngine.Object.Instantiate(booster.gameObject, _vlsFxHold.transform);
            _vlsFxTpl.name = "CrosswimVlsBoosterTpl";
            _vlsFxTpl.SetActive(false);

            // Strip vanilla logic — we only want PS + TrailEmitter smoke.
            VLSBooster? logic = _vlsFxTpl.GetComponent<VLSBooster>();
            if (logic != null)
                UnityEngine.Object.DestroyImmediate(logic);

            ParticleSystem[] ps = _vlsFxTpl.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i] == null)
                    continue;
                ps[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps[i].gameObject.SetActive(false);
            }

            TrailEmitter[] trails = _vlsFxTpl.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] == null)
                    continue;
                trails[i].rb = null!;
                trails[i].enabled = false;
            }

            CrosswimPlugin.ModLog?.LogInfo(
                $"CrosswimMotorFx VLS FX tpl: ps={ps.Length} trails={trails.Length}");
        }

        internal static void Capture(Encyclopedia enc)
        {
            if (enc == null)
                return;

            ParticleSystem? best = null;
            float bestScore = -1f;
            string bestSrc = "-";
            var seen = new StringBuilder(256);

            if (enc.missiles != null)
            {
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    MissileDefinition? def = enc.missiles[i];
                    if (def?.unitPrefab == null)
                        continue;
                    int defScore = ScoreDefinition(def);
                    if (defScore < 0)
                        continue;
                    TryScanPrefab(def.unitPrefab, defScore, def.jsonKey, ref best, ref bestScore, ref bestSrc, seen);
                }
            }

            if (enc.weaponMounts != null)
            {
                for (int i = 0; i < enc.weaponMounts.Count; i++)
                {
                    WeaponMount? mount = enc.weaponMounts[i];
                    if (mount == null)
                        continue;
                    int defScore = ScoreMount(mount);
                    if (defScore < 0)
                        continue;
                    GameObject? go = mount.info != null ? mount.info.weaponPrefab : null;
                    if (go == null)
                        go = mount.prefab;
                    if (go == null)
                        continue;
                    TryScanPrefab(go, defScore, mount.jsonKey, ref best, ref bestScore, ref bestSrc, seen);
                }
            }

            if (best == null)
            {
                CrosswimPlugin.ModLog?.LogWarning(
                    "CrosswimMotorFx capture: no PS. seen=[" + seen + "]");
                return;
            }

            if (_hold == null)
            {
                _hold = new GameObject("Crosswim_FxHold");
                UnityEngine.Object.DontDestroyOnLoad(_hold);
                _hold.SetActive(false);
            }

            if (_template != null)
                UnityEngine.Object.Destroy(_template);

            _template = UnityEngine.Object.Instantiate(best.gameObject, _hold.transform);
            _template.name = "CrosswimExhaustTpl";
            _template.SetActive(false);
            CrosswimPlugin.ModLog?.LogInfo(
                $"CrosswimMotorFx capture '{best.name}' from '{bestSrc}' score={bestScore:F0}");
        }

        /// <summary>
        /// Kill vanilla AShM motor plume/thrust FX (Activate can race before Claim).
        /// Keeps our CrosswimExhaust / CrosswimBooster instances. Strips stock VLSBooster.
        /// </summary>
        internal static void SilenceStock(Missile missile)
        {
            if (missile == null)
                return;

            missile.SetThrottle(0f);
            missile.boosterIsAttached = true;

            VLSBooster[] stock = missile.GetComponentsInChildren<VLSBooster>(true);
            for (int i = 0; i < stock.Length; i++)
            {
                if (stock[i] == null)
                    continue;
                stock[i].enabled = false;
                UnityEngine.Object.Destroy(stock[i].gameObject);
            }

            if (MotorsField?.GetValue(missile) is Array motors)
            {
                for (int m = 0; m < motors.Length; m++)
                {
                    object? motor = motors.GetValue(m);
                    if (motor == null)
                        continue;
                    FuelMassField?.SetValue(motor, 0f);
                    BurnoutMethod?.Invoke(motor, new object[] { true });
                }
            }

            ParticleSystem[] all = missile.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                ParticleSystem ps = all[i];
                if (ps == null)
                    continue;
                string n = ps.gameObject.name ?? string.Empty;
                if (n.StartsWith("CrosswimExhaust", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("CrosswimBooster", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("CrosswimVls", StringComparison.OrdinalIgnoreCase))
                    continue;
                // Don't kill trail PS parented under our booster clone.
                if (IsUnderNamed(ps.transform, "CrosswimBooster"))
                    continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
            }
        }

        private static bool IsUnderNamed(Transform t, string rootName)
        {
            Transform? p = t;
            while (p != null)
            {
                if (p.name.StartsWith(rootName, StringComparison.OrdinalIgnoreCase))
                    return true;
                p = p.parent;
            }
            return false;
        }

        internal static GameObject? SpawnBooster(Missile missile, Transform? visual)
        {
            if (missile == null || visual == null)
                return null;

            Transform? socket = FindOrActivateAlias(visual, CrosswimConstants.VlsbFxAliases);
            if (socket == null)
                socket = FindOrActivateAlias(visual, CrosswimConstants.VlsbAliases);
            if (socket == null)
            {
                CrosswimPlugin.ModLog?.LogWarning("CrosswimMotorFx SpawnBooster: no VLSB socket");
                return null;
            }

            // Prefer full AShM VLSBooster FX (flame + TrailEmitter smoke).
            if (_vlsFxTpl != null)
            {
                GameObject vls = UnityEngine.Object.Instantiate(_vlsFxTpl);
                vls.name = "CrosswimBooster";
                PlaceVlsBoosterWorld(vls.transform, socket, missile);
                vls.SetActive(true);
                PrepBoosterIdle(vls);
                CrosswimPlugin.ModLog?.LogInfo(
                    $"CrosswimMotorFx booster VLS tpl at '{socket.name}' trails={vls.GetComponentsInChildren<TrailEmitter>(true).Length}");
                return vls;
            }

            ParticleSystem? src = TemplatePs() ?? FindMotorExhaust(missile);
            if (src == null)
            {
                CrosswimPlugin.ModLog?.LogWarning("CrosswimMotorFx SpawnBooster: no PS template");
                return null;
            }

            GameObject go = UnityEngine.Object.Instantiate(src.gameObject);
            go.name = "CrosswimBooster";
            PlaceBoosterOnSocket(go.transform, socket, missile);
            go.SetActive(true);
            PrepBoosterIdle(go);

            ParticleSystem[] all = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                ParticleSystem ps = all[i];
                if (ps == null)
                    continue;
                TuneBoosterExhaust(ps);
            }

            CrosswimPlugin.ModLog?.LogInfo(
                $"CrosswimMotorFx booster fallback at '{socket.name}' from '{src.name}'");
            return go;
        }

        /// <summary>Parent to missile (scale 1) at socket world pose — FBX×100 empties warp trails.</summary>
        private static void PlaceVlsBoosterWorld(Transform t, Transform socket, Missile missile)
        {
            t.SetParent(missile.transform, false);
            Vector3 aft = -missile.transform.forward;
            if (aft.sqrMagnitude < 1e-4f)
                aft = -socket.forward;
            // AShM VLS FX tip faces +Z on booster; we had aft LookRotation — flip 180°.
            t.rotation = Quaternion.LookRotation(-aft, missile.transform.up);
            t.position = socket.position + aft * CrosswimConstants.FxBoosterAftNudgeM;
            t.localScale = Vector3.one;
        }

        private static void PrepBoosterIdle(GameObject go)
        {
            ParticleSystem[] all = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                all[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                all[i].gameObject.SetActive(false);
            }

            TrailEmitter[] trails = go.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] == null)
                    continue;
                trails[i].rb = null!;
                trails[i].enabled = false;
            }
        }

        /// <summary>Ignite after tube delay — PS + TrailEmitter smoke like AShM Activate().</summary>
        internal static void ActivateBooster(GameObject? fx, Missile missile)
        {
            if (fx == null || missile == null)
                return;

            Rigidbody? rb = missile.rb != null ? missile.rb : missile.GetComponent<Rigidbody>();
            float trailLife = CrosswimAshmVls.BurnTimeS + 25f;

            TrailEmitter[] trails = fx.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                TrailEmitter te = trails[i];
                if (te == null)
                    continue;
                te.rb = rb!;
                TrailLifetimeField?.SetValue(te, trailLife);
                te.StartTrail();
            }

            ParticleSystem[] all = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                ParticleSystem ps = all[i];
                if (ps == null)
                    continue;
                ps.gameObject.SetActive(true);
                ps.Play(true);
            }

            ParticleSystemRenderer[] rs = fx.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] != null)
                    rs[i].enabled = true;
            }

            Light[] lights = fx.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                    lights[i].enabled = true;
            }

            AudioSource[] audio = fx.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audio.Length; i++)
            {
                if (audio[i] != null)
                    audio[i].Play();
            }
        }

        private static Transform? FindOrActivateAlias(Transform vis, string[] aliases)
        {
            Transform? t = FindActiveExactOrAlias(vis, aliases);
            if (t != null)
                return t;
            // Inactive bake — still find and enable.
            Transform[] all = vis.GetComponentsInChildren<Transform>(true);
            for (int a = 0; a < aliases.Length; a++)
            {
                string alias = aliases[a];
                if (string.IsNullOrEmpty(alias))
                    continue;
                for (int i = 0; i < all.Length; i++)
                {
                    Transform x = all[i];
                    if (x == null)
                        continue;
                    if (!string.Equals(x.name, alias, StringComparison.OrdinalIgnoreCase) &&
                        !x.name.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                        continue;
                    x.gameObject.SetActive(true);
                    return x;
                }
            }
            return null;
        }

        private static void PlaceBoosterOnSocket(Transform t, Transform socket, Missile missile)
        {
            t.SetParent(socket, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            float parent = (Mathf.Abs(socket.lossyScale.x) + Mathf.Abs(socket.lossyScale.y) +
                            Mathf.Abs(socket.lossyScale.z)) / 3f;
            if (parent < 1e-4f)
                parent = 1f;
            float local = CrosswimConstants.FxBoosterWorldScaleM / parent;
            t.localScale = new Vector3(local, local, local);

            Vector3 aft = -missile.transform.forward;
            if (aft.sqrMagnitude < 1e-4f)
                aft = -socket.forward;
            t.rotation = Quaternion.LookRotation(-aft, missile.transform.up);
            t.position = socket.position + aft * CrosswimConstants.FxBoosterAftNudgeM;
        }

        private static void TuneBoosterExhaust(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            if (main.duration < 1f)
                main.duration = 5f;

            if (main.startSize.mode == ParticleSystemCurveMode.TwoConstants)
            {
                float a = Mathf.Min(main.startSize.constantMin, CrosswimConstants.FxBoosterMaxStartSize);
                float b = Mathf.Min(main.startSize.constantMax, CrosswimConstants.FxBoosterMaxStartSize);
                main.startSize = new ParticleSystem.MinMaxCurve(a, b);
            }
            else
            {
                main.startSize = Mathf.Min(main.startSize.constant, CrosswimConstants.FxBoosterMaxStartSize);
            }

            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = true;
        }

        internal static void DestroyBooster(ref GameObject? fx)
        {
            if (fx == null)
                return;

            TrailEmitter[] trails = fx.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] == null)
                    continue;
                trails[i].StopTrail();
                trails[i].rb = null!;
            }

            ParticleSystem[] all = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                all[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            UnityEngine.Object.Destroy(fx);
            fx = null;
        }

        internal static void Play(Missile missile, Transform? visual)
        {
            if (missile == null || visual == null)
                return;

            Transform? socket = ResolveSocket(visual);
            if (socket == null)
            {
                CrosswimPlugin.ModLog?.LogWarning("CrosswimMotorFx: no exhaust empty");
                return;
            }

            ParticleSystem? src = TemplatePs();
            string srcName = src != null ? src.name : "";
            if (src == null)
            {
                src = FindMotorExhaust(missile);
                srcName = src != null ? src.name : "";
            }

            if (src == null)
            {
                CrosswimPlugin.ModLog?.LogWarning("CrosswimMotorFx: no motor PS (capture+live miss)");
                return;
            }

            GameObject go = UnityEngine.Object.Instantiate(src.gameObject);
            go.name = "CrosswimExhaust";
            PlaceOnSocket(go.transform, socket, missile);
            go.SetActive(true);

            ParticleSystem[] all = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                ParticleSystem ps = all[i];
                if (ps == null)
                    continue;
                TuneExhaust(ps);
                ps.gameObject.SetActive(true);
                ps.Play(true);
            }

            ParticleSystemRenderer[] rs = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] != null)
                    rs[i].enabled = true;
            }

            CrosswimPlugin.ModLog?.LogInfo(
                $"CrosswimMotorFx play at '{socket.name}' from '{srcName}' scale={go.transform.localScale.x:F3}");
        }

        private static ParticleSystem? TemplatePs()
        {
            if (_template == null)
                return null;
            ParticleSystem ps = _template.GetComponent<ParticleSystem>();
            if (ps == null)
                ps = _template.GetComponentInChildren<ParticleSystem>(true);
            return ps;
        }

        private static void TryScanPrefab(
            GameObject prefab,
            int defScore,
            string? key,
            ref ParticleSystem? best,
            ref float bestScore,
            ref string bestSrc,
            StringBuilder seen)
        {
            Missile? mis = prefab.GetComponent<Missile>();
            if (mis == null)
                mis = prefab.GetComponentInChildren<Missile>(true);

            if (mis != null && MotorsField != null && ParticlesField != null &&
                MotorsField.GetValue(mis) is Array motors)
            {
                for (int m = 0; m < motors.Length; m++)
                {
                    object? motor = motors.GetValue(m);
                    if (motor == null || ParticlesField.GetValue(motor) is not Array psArr)
                        continue;
                    for (int i = 0; i < psArr.Length; i++)
                    {
                        if (psArr.GetValue(i) is not ParticleSystem ps || ps == null)
                            continue;
                        Consider(ps, defScore, key, ref best, ref bestScore, ref bestSrc, seen);
                    }
                }
            }

            ParticleSystem[] all = prefab.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                    Consider(all[i], defScore, key, ref best, ref bestScore, ref bestSrc, seen);
            }
        }

        private static void Consider(
            ParticleSystem ps,
            int defScore,
            string? key,
            ref ParticleSystem? best,
            ref float bestScore,
            ref string bestSrc,
            StringBuilder seen)
        {
            float s = ScorePs(ps);
            if (seen.Length < 200)
            {
                if (seen.Length > 0)
                    seen.Append(',');
                seen.Append(ps.name);
            }
            if (s < 0f)
                return;
            float total = s + defScore;
            if (total > bestScore)
            {
                bestScore = total;
                best = ps;
                bestSrc = key ?? ps.name;
            }
        }

        private static int ScoreDefinition(MissileDefinition def)
        {
            return ScoreKey(def.jsonKey, def.unitName);
        }

        private static int ScoreMount(WeaponMount mount)
        {
            string? name = mount.info != null ? mount.info.weaponName : mount.mountName;
            return ScoreKey(mount.jsonKey, name);
        }

        private static int ScoreKey(string? jsonKey, string? display)
        {
            string k = jsonKey ?? string.Empty;
            string n = display ?? string.Empty;
            if (k.StartsWith("AGM1", StringComparison.OrdinalIgnoreCase))
                return -1;

            int s = 0;
            if (Contains(k, "AGM-68") || Contains(n, "AGM-68") ||
                Contains(k, "AGM68") || Contains(n, "AGM68"))
                s += 100;
            if (Contains(k, "AGM_heavy") || Contains(k, "AGMheavy"))
                s += 90;
            if (Contains(n, "AGM-68"))
                s += 80;
            if (string.Equals(k, "AShM1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k, "AShM2", StringComparison.OrdinalIgnoreCase))
                s += 20;
            if (k.StartsWith("AShM", StringComparison.OrdinalIgnoreCase))
                s += 10;
            return s > 0 ? s : -1;
        }

        private static bool Contains(string s, string part) =>
            s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Transform? ResolveSocket(Transform vis)
        {
            if (IsVlsbActive(vis))
            {
                Transform? vlsbFx = FindActiveExactOrAlias(vis, CrosswimConstants.VlsbFxAliases);
                if (vlsbFx != null)
                    return vlsbFx;
            }

            Transform? swim = FindActiveExactOrAlias(vis, CrosswimConstants.SwimFxAliases);
            if (swim != null)
                return swim;

            return FindActiveExactOrAlias(vis, CrosswimConstants.MainEngineAliases);
        }

        private static bool IsVlsbActive(Transform vis)
        {
            Transform[] all = vis.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null)
                    continue;
                if (string.Equals(t.name, "VLSB", StringComparison.OrdinalIgnoreCase) ||
                    t.name.StartsWith("VLSB.", StringComparison.OrdinalIgnoreCase))
                    return t.gameObject.activeInHierarchy;
            }
            return false;
        }

        private static Transform? FindActiveExactOrAlias(Transform vis, string[] aliases)
        {
            if (aliases == null)
                return null;
            Transform[] all = vis.GetComponentsInChildren<Transform>(true);
            for (int a = 0; a < aliases.Length; a++)
            {
                string alias = aliases[a];
                if (string.IsNullOrEmpty(alias))
                    continue;
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null || !t.gameObject.activeInHierarchy)
                        continue;
                    if (string.Equals(t.name, alias, StringComparison.OrdinalIgnoreCase))
                        return t;
                }
            }
            for (int a = 0; a < aliases.Length; a++)
            {
                string alias = aliases[a];
                if (string.IsNullOrEmpty(alias))
                    continue;
                Transform? best = null;
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null || !t.gameObject.activeInHierarchy)
                        continue;
                    if (!t.name.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (best == null || t.name.Length < best.name.Length)
                        best = t;
                }
                if (best != null)
                    return best;
            }
            return null;
        }

        private static void PlaceOnSocket(Transform t, Transform socket, Missile missile)
        {
            t.SetParent(socket, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            float parent = (Mathf.Abs(socket.lossyScale.x) + Mathf.Abs(socket.lossyScale.y) +
                            Mathf.Abs(socket.lossyScale.z)) / 3f;
            if (parent < 1e-4f)
                parent = 1f;
            float local = CrosswimConstants.FxWorldScaleM / parent;
            t.localScale = new Vector3(local, local, local);

            Vector3 aft = -missile.transform.forward;
            if (aft.sqrMagnitude < 1e-4f)
                aft = -socket.forward;
            t.rotation = Quaternion.LookRotation(aft, missile.transform.up);
            t.position = socket.position + aft * CrosswimConstants.FxAftNudgeM;
        }

        private static void TuneExhaust(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            if (main.duration < 1f)
                main.duration = 5f;

            if (main.startSize.mode == ParticleSystemCurveMode.TwoConstants)
            {
                float a = Mathf.Min(main.startSize.constantMin, CrosswimConstants.FxMaxStartSize);
                float b = Mathf.Min(main.startSize.constantMax, CrosswimConstants.FxMaxStartSize);
                main.startSize = new ParticleSystem.MinMaxCurve(a, b);
            }
            else
            {
                main.startSize = Mathf.Min(main.startSize.constant, CrosswimConstants.FxMaxStartSize);
            }

            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = true;

            ParticleSystem.ShapeModule shape = ps.shape;
            if (shape.enabled && shape.radius > 0.35f)
                shape.radius = 0.12f;
        }

        private static ParticleSystem? FindMotorExhaust(Missile missile)
        {
            ParticleSystem? best = null;
            float bestScore = -1f;

            if (MotorsField != null && ParticlesField != null &&
                MotorsField.GetValue(missile) is Array motors)
            {
                for (int m = 0; m < motors.Length; m++)
                {
                    object? motor = motors.GetValue(m);
                    if (motor == null || ParticlesField.GetValue(motor) is not Array psArr)
                        continue;
                    for (int i = 0; i < psArr.Length; i++)
                    {
                        if (psArr.GetValue(i) is not ParticleSystem ps || ps == null)
                            continue;
                        float s = ScorePs(ps);
                        if (s > bestScore)
                        {
                            bestScore = s;
                            best = ps;
                        }
                    }
                }
            }

            if (best != null)
                return best;

            ParticleSystem[] scene = missile.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < scene.Length; i++)
            {
                ParticleSystem ps = scene[i];
                if (ps == null || PrefabFactory.IsVisualRoot(ps.transform))
                    continue;
                float s = ScorePs(ps);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = ps;
                }
            }
            return best;
        }

        private static float ScorePs(ParticleSystem ps)
        {
            string n = (ps.gameObject.name ?? string.Empty).ToLowerInvariant();
            if (n.IndexOf("cone", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("shock", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("vapor", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("wing", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("jetstart", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("jet_start", StringComparison.Ordinal) >= 0)
                return -1f;
            if (n.IndexOf("start", StringComparison.Ordinal) >= 0 &&
                n.IndexOf("jet", StringComparison.Ordinal) >= 0)
                return -1f;

            float score = 1f;
            for (int i = 0; i < ExhaustParts.Length; i++)
            {
                if (n.IndexOf(ExhaustParts[i], StringComparison.Ordinal) >= 0)
                    score += 5f;
            }
            if (n.IndexOf("loop", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("idle", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("cruise", StringComparison.Ordinal) >= 0)
                score += 3f;
            return score;
        }
    }
}
