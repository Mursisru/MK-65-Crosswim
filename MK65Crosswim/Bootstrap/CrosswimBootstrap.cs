using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Crosswim.Blueprinter;
using Crosswim.Patches;
using Crosswim.Runtime;
using UnityEngine;

namespace Crosswim.Bootstrap
{
    internal static class CrosswimBootstrap
    {
        private static bool _done;
        internal static MissileDefinition? Definition { get; private set; }
        internal static WeaponMount? Mount { get; private set; }
        internal static WeaponInfo? Info { get; private set; }

        private static readonly FieldInfo? UnitDisabled =
            typeof(UnitDefinition).GetField("disabled", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? MountDisabled =
            typeof(WeaponMount).GetField("disabled", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static bool IsOurMissile(Missile? missile)
        {
            if (missile == null)
                return false;
            if (missile.GetComponent<CrosswimTag>() != null)
                return true;
            if (Definition != null && missile.definition == Definition)
                return true;
            return missile.definition != null &&
                   string.Equals(missile.definition.jsonKey, CrosswimConstants.MissileJsonKey, StringComparison.Ordinal);
        }

        internal static IEnumerator Run(Encyclopedia enc)
        {
            if (_done || enc == null)
                yield break;

            yield return BlueprinterGate.WaitUntilReady();

            try
            {
                NobpContent.TryLoad();
                MissileDefinition? shell = ResolveShellMissile(enc);
                if (shell?.unitPrefab != null)
                    VisualShader.PrimeFrom(shell.unitPrefab);

                CrosswimMotorFx.Capture(enc);

                if (Encyclopedia.Lookup != null &&
                    Encyclopedia.Lookup.TryGetValue(CrosswimConstants.MissileJsonKey, out UnitDefinition existing) &&
                    existing is MissileDefinition md && md.unitPrefab != null)
                    Definition = md;
                else
                    Definition = CreateDefinition(enc, shell);

                if (Definition != null && shell?.unitPrefab != null)
                    Definition.unitPrefab = shell.unitPrefab;

                if (Encyclopedia.WeaponLookup != null &&
                    Encyclopedia.WeaponLookup.TryGetValue(CrosswimConstants.MountJsonKey, out WeaponMount existingMount) &&
                    existingMount.prefab != null)
                {
                    Mount = existingMount;
                    RefreshMount(enc, Mount, Definition);
                }
                else
                    Mount = CreateMount(enc, Definition);

                Info = Mount?.info;
                if (Mount != null)
                    HardpointInjector.InjectAshmSlots(enc, Mount);
                ShipLauncherInjector.InjectDynamoArgus(enc, Definition);

                _done = Definition != null && Mount != null;
                CrosswimPlugin.ModLog?.LogInfo(_done
                    ? $"Crosswim ready def={CrosswimConstants.MissileJsonKey} visual={(NobpContent.VisualPrefab != null)}"
                    : "Crosswim bootstrap incomplete.");
            }
            catch (Exception ex)
            {
                CrosswimPlugin.ModLog?.LogError($"CrosswimBootstrap: {ex}");
            }
        }

        private static MissileDefinition? ResolveShellMissile(Encyclopedia enc)
        {
            return PrefabFactory.FindMissileByExactKey(enc, CrosswimConstants.ShellMissileKey) ??
                   PrefabFactory.FindMissileByExactKey(enc, CrosswimConstants.ShellMissileKeyAlt);
        }

        private static WeaponMount? ResolveShellMount(Encyclopedia enc)
        {
            return PrefabFactory.FindMountByExactKey(enc, CrosswimConstants.ShellMountKey) ??
                   PrefabFactory.FindMountByExactKey(enc, CrosswimConstants.ShellMountKeyAlt);
        }

        private static MissileDefinition? CreateDefinition(Encyclopedia enc, MissileDefinition? shell)
        {
            if (shell?.unitPrefab == null)
            {
                CrosswimPlugin.ModLog?.LogError("No AShM shell MissileDefinition.");
                return null;
            }

            MissileDefinition def = ScriptableObject.CreateInstance<MissileDefinition>();
            def.name = "MissilePack_MK65_Definition";
            def.jsonKey = CrosswimConstants.MissileJsonKey;
            PrefabFactory.CopyUnitDefScalars(shell, def);
            PrefabFactory.CopyMapIdentity(shell, def);
            def.unitName = CrosswimConstants.UnitName;
            def.bogeyName = CrosswimConstants.BogeyName;
            def.description = CrosswimConstants.Description;
            def.value = CrosswimConstants.Cost;
            def.mass = CrosswimConstants.MassKg;
            def.length = CrosswimConstants.LengthM;
            def.width = CrosswimConstants.WidthM;
            def.height = CrosswimConstants.HeightM;
            def.radarSize = CrosswimConstants.RadarSize;
            def.code = "MSL";
            def.IsObstacle = false;
            UnitDisabled?.SetValue(def, false);
            def.unitPrefab = shell.unitPrefab;

            enc.missiles ??= new List<MissileDefinition>();
            if (!enc.missiles.Contains(def))
                enc.missiles.Add(def);
            Encyclopedia.Lookup ??= new Dictionary<string, UnitDefinition>(StringComparer.Ordinal);
            Encyclopedia.Lookup[def.jsonKey] = def;
            if (enc.IndexLookup != null && !PrefabFactory.ContainsNet(enc.IndexLookup, def))
            {
                enc.IndexLookup.Add(def);
                ((INetworkDefinition)def).LookupIndex = enc.IndexLookup.Count - 1;
            }
            return def;
        }

        private static WeaponMount? CreateMount(Encyclopedia enc, MissileDefinition? def)
        {
            WeaponMount? shell = ResolveShellMount(enc);
            if (shell?.prefab == null || shell.info == null || def?.unitPrefab == null)
            {
                CrosswimPlugin.ModLog?.LogError("No AShM shell WeaponMount.");
                return null;
            }

            string shellKey = shell.jsonKey;
            WeaponMount mount = ScriptableObject.CreateInstance<WeaponMount>();
            mount.name = "MissilePack_MK65_Mount";
            mount.jsonKey = CrosswimConstants.MountJsonKey;
            mount.mountName = CrosswimConstants.MountDisplayName;
            PrefabFactory.CopyMountScalars(shell, mount);
            mount.ammo = 1;
            mount.emptyMass = 20f;
            mount.mass = mount.emptyMass + CrosswimConstants.MassKg;
            MountDisabled?.SetValue(mount, false);

            WeaponInfo info = ScriptableObject.CreateInstance<WeaponInfo>();
            info.name = "MissilePack_MK65_Info";
            PrefabFactory.CopyWeaponInfoScalars(shell.info, info);
            Sprite? icon = CrosswimWeaponIcon.Get();
            if (icon != null)
                info.weaponIcon = icon;
            info.weaponName = CrosswimConstants.WeaponInfoName;
            info.shortName = CrosswimConstants.ShortName;
            info.description = CrosswimConstants.Description;
            info.weaponPrefab = def.unitPrefab;
            info.massPerRound = CrosswimConstants.MassKg;
            info.costPerRound = CrosswimConstants.Cost;
            info.blastDamage = CrosswimConstants.BlastYieldKg;
            info.pK = CrosswimConstants.Pk;
            info.fireInterval = CrosswimConstants.FireIntervalS;
            info.maxSpeed = 250f;
            info.gravMult = 1f;
            // Free-fall CCIP (CombatHUD BombingUI), not missile lock range.
            info.missile = false;
            info.bomb = true;
            info.glideBomb = false;
            ApplyTargetProfile(info);
            mount.info = info;

            GameObject mountGo = PrefabFactory.CloneAsPrefab(shell.prefab, "MissilePack_MK65_MountPrefab");
            KeepSingleMounted(mountGo);
            PrefabFactory.StampVisual(mountGo, NobpContent.VisualPrefab);
            mount.prefab = mountGo;
            foreach (MountedMissile mm in mountGo.GetComponentsInChildren<MountedMissile>(true))
            {
                if (mm != null)
                    mm.info = info;
            }

            if (!string.Equals(shell.jsonKey, shellKey, StringComparison.Ordinal))
                CrosswimPlugin.ModLog?.LogError($"AShM shell jsonKey mutated: '{shellKey}' -> '{shell.jsonKey}'");

            enc.weaponMounts ??= new List<WeaponMount>();
            if (!enc.weaponMounts.Contains(mount))
                enc.weaponMounts.Add(mount);
            Encyclopedia.WeaponLookup ??= new Dictionary<string, WeaponMount>(StringComparer.Ordinal);
            Encyclopedia.WeaponLookup[mount.jsonKey] = mount;
            if (enc.IndexLookup != null && !PrefabFactory.ContainsNet(enc.IndexLookup, mount))
            {
                enc.IndexLookup.Add(mount);
                ((INetworkDefinition)mount).LookupIndex = enc.IndexLookup.Count - 1;
            }

            try
            {
                mount.Initialize();
            }
            catch (Exception ex)
            {
                CrosswimPlugin.ModLog?.LogWarning($"WeaponMount.Initialize: {ex.Message}");
            }

            mount.info = info;
            mount.jsonKey = CrosswimConstants.MountJsonKey;
            mount.mountName = CrosswimConstants.MountDisplayName;
            mount.ammo = 1;
            info.weaponPrefab = def.unitPrefab;
            Info = info;
            return mount;
        }

        private static void RefreshMount(Encyclopedia enc, WeaponMount mount, MissileDefinition? def)
        {
            NobpContent.TryLoad();
            if (mount.prefab != null && NobpContent.VisualPrefab != null)
                PrefabFactory.StampVisual(mount.prefab, NobpContent.VisualPrefab);
            WeaponInfo info = mount.info ?? ScriptableObject.CreateInstance<WeaponInfo>();
            info.weaponName = CrosswimConstants.WeaponInfoName;
            info.shortName = CrosswimConstants.ShortName;
            info.massPerRound = CrosswimConstants.MassKg;
            info.blastDamage = CrosswimConstants.BlastYieldKg;
            info.maxSpeed = 250f;
            info.gravMult = 1f;
            info.missile = false;
            info.bomb = true;
            info.glideBomb = false;
            ApplyTargetProfile(info);
            if (def?.unitPrefab != null)
                info.weaponPrefab = def.unitPrefab;
            Sprite? icon = CrosswimWeaponIcon.Get();
            if (icon != null)
                info.weaponIcon = icon;
            mount.info = info;
            Info = info;
        }

        private static void ApplyTargetProfile(WeaponInfo info)
        {
            info.effectiveness = new RoleIdentity
            {
                antiSurface = 0.55f,
                antiAir = 0f,
                antiMissile = 1f,
                antiRadar = 0f
            };
            TargetRequirements tr = info.targetRequirements;
            tr.lineOfSight = false;
            tr.minAltitude = -50f;
            tr.maxAltitude = 20000f;
            tr.minRange = 0f;
            // Ballistic free-fall envelope (CCIP uses kinematics; this caps AI / HUD ring).
            tr.maxRange = CrosswimConstants.AiMaxRangeM;
            tr.maxSpeed = 200f;
            tr.minAlignment = 180f;
            tr.minOwnerSpeed = 0f;
            info.targetRequirements = tr;
        }

        private static void KeepSingleMounted(GameObject mountGo)
        {
            MountedMissile[] mounted = mountGo.GetComponentsInChildren<MountedMissile>(true);
            for (int i = mounted.Length - 1; i >= 1; i--)
            {
                if (mounted[i] != null)
                    UnityEngine.Object.DestroyImmediate(mounted[i].gameObject);
            }
        }
    }
}
