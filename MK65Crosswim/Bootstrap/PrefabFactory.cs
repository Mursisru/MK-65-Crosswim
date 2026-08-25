using System;
using System.Collections.Generic;
using System.Reflection;
using Crosswim.Runtime;
using Mirage;
using UnityEngine;

namespace Crosswim.Bootstrap
{
    internal static class PrefabFactory
    {
        private static readonly FieldInfo? UnitDisabled =
            typeof(UnitDefinition).GetField("disabled", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? MountDisabled =
            typeof(WeaponMount).GetField("disabled", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void EnableUnit(UnitDefinition def) => UnitDisabled?.SetValue(def, false);
        internal static void EnableMount(WeaponMount mount) => MountDisabled?.SetValue(mount, false);

        internal static GameObject CloneAsPrefab(GameObject source, string name)
        {
            GameObject clone = UnityEngine.Object.Instantiate(source);
            clone.name = name;
            NetworkPrefabPrep.PrepareTemplate(clone);
            UnityEngine.Object.DontDestroyOnLoad(clone);
            clone.hideFlags = HideFlags.None;
            clone.transform.SetParent(null, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            FreezeTemplatePhysics(clone);
            clone.SetActive(false);
            NetworkPrefabPrep.PrepareTemplate(clone);
            return clone;
        }

        internal static void FreezeTemplatePhysics(GameObject root)
        {
            if (root == null)
                return;
            Rigidbody[] rbs = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                Rigidbody rb = rbs[i];
                if (rb == null)
                    continue;
                rb.detectCollisions = false;
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        internal static void ActivateMountedInstance(GameObject instance, bool internalBay)
        {
            if (instance == null)
                return;
            instance.hideFlags = HideFlags.None;
            instance.SetActive(true);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            FreezeTemplatePhysics(instance);
            if (internalBay)
                HideBayVisuals(instance);
            else
            {
                EnsureVisualRenderers(instance);
                Transform? vis = FindVisual(instance.transform);
                if (vis != null)
                {
                    VisualFit.Apply(vis);
                    CrosswimOpening.PoseClosed(vis);
                }
            }
        }

        internal static void HideBayVisuals(GameObject host)
        {
            HideStockRenderers(host);
            Transform? vis = FindVisual(host.transform);
            if (vis == null)
                return;
            Renderer[] rs = vis.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] != null)
                    rs[i].enabled = false;
            }
        }

        internal static void EnsureVisualRenderers(GameObject host)
        {
            Transform? vis = FindVisual(host.transform);
            if (vis == null)
                return;
            vis.gameObject.SetActive(true);
            Renderer[] rs = vis.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] != null)
                    rs[i].enabled = true;
            }
        }

        internal static void StampVisual(GameObject host, GameObject? visualPrefab)
        {
            if (host == null)
                return;
            DestroyExistingVisuals(host);
            if (visualPrefab == null)
                return;
            int n = StampOnMounted(host, visualPrefab);
            if (n > 0)
                HideStockRenderers(host);
            host.SetActive(false);
            NetworkPrefabPrep.PrepareTemplate(host);
        }

        internal static void StampVisualLive(GameObject host, GameObject? visualPrefab)
        {
            if (host == null || visualPrefab == null)
                return;
            DestroyExistingVisuals(host);
            if (StampOnMounted(host, visualPrefab) > 0)
                HideStockRenderers(host);
        }

        private static int StampOnMounted(GameObject host, GameObject visualPrefab)
        {
            MountedMissile[] mms = host.GetComponentsInChildren<MountedMissile>(true);
            int stamped = 0;
            if (mms.Length > 0)
            {
                for (int i = 0; i < mms.Length; i++)
                {
                    if (mms[i] != null && StampOne(mms[i].transform, host, visualPrefab, shipContext: false, encyclopedia: false))
                        stamped++;
                }
                return stamped;
            }
            return StampOne(host.transform, host, visualPrefab, shipContext: false, encyclopedia: false) ? 1 : 0;
        }

        internal static bool StampOne(
            Transform parent,
            GameObject host,
            GameObject visualPrefab,
            bool shipContext,
            bool encyclopedia)
        {
            if (parent == null || visualPrefab == null)
                return false;

            GameObject vis = UnityEngine.Object.Instantiate(visualPrefab, parent, false);
            vis.name = CrosswimConstants.VisualRootName;
            vis.hideFlags = HideFlags.None;
            vis.SetActive(true);
            VisualMaterials.StripSceneJunk(vis);
            VisualMaterials.DestroySpawnedJunk(vis);
            VisualMaterials.MatchHostDrawState(vis, host);
            CrosswimVisualParts.ApplyCarrier(vis.transform, shipContext, encyclopedia);
            VisualFit.Apply(vis.transform);
            CrosswimOpening.PoseClosed(vis.transform);
            VisualShader.PrimeFrom(host);
            VisualMaterials.ApplyFbxLook(vis);
            return true;
        }

        internal static void HideStockRenderers(GameObject root)
        {
            if (root == null)
                return;
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || IsVisualRoot(rs[i].transform))
                    continue;
                if (rs[i] is ParticleSystemRenderer)
                    continue;
                rs[i].enabled = false;
            }
        }

        internal static bool IsVisualRoot(Transform t)
        {
            while (t != null)
            {
                if (t.name == CrosswimConstants.VisualRootName)
                    return true;
                t = t.parent;
            }
            return false;
        }

        internal static Transform? FindVisual(Transform root)
        {
            if (root == null)
                return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == CrosswimConstants.VisualRootName)
                    return all[i];
            }
            return null;
        }

        private static void DestroyExistingVisuals(GameObject host)
        {
            Transform[] all = host.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                if (all[i] != null && all[i].name == CrosswimConstants.VisualRootName)
                    UnityEngine.Object.DestroyImmediate(all[i].gameObject);
            }
        }

        internal static WeaponMount? FindMountByExactKey(Encyclopedia enc, string jsonKey)
        {
            if (string.IsNullOrEmpty(jsonKey))
                return null;
            if (Encyclopedia.WeaponLookup != null &&
                Encyclopedia.WeaponLookup.TryGetValue(jsonKey, out WeaponMount m) &&
                m != null)
                return m;
            if (enc?.weaponMounts == null)
                return null;
            foreach (WeaponMount w in enc.weaponMounts)
            {
                if (w != null && string.Equals(w.jsonKey, jsonKey, StringComparison.Ordinal))
                    return w;
            }
            return null;
        }

        internal static MissileDefinition? FindMissileByExactKey(Encyclopedia enc, string jsonKey)
        {
            if (string.IsNullOrEmpty(jsonKey))
                return null;
            if (Encyclopedia.Lookup != null &&
                Encyclopedia.Lookup.TryGetValue(jsonKey, out UnitDefinition u) &&
                u is MissileDefinition md)
                return md;
            if (enc?.missiles == null)
                return null;
            foreach (MissileDefinition m in enc.missiles)
            {
                if (m != null && string.Equals(m.jsonKey, jsonKey, StringComparison.Ordinal))
                    return m;
            }
            return null;
        }

        internal static void CopyMountScalars(WeaponMount src, WeaponMount dst)
        {
            dst.ammo = src.ammo;
            dst.turret = src.turret;
            dst.missileBay = src.missileBay;
            dst.radar = false;
            dst.tailHook = false;
            dst.slingloadHook = false;
            dst.countermeasure = false;
            dst.colorable = src.colorable;
            dst.Cargo = false;
            dst.Troops = false;
            dst.sortWeapons = src.sortWeapons;
            dst.GearSafety = src.GearSafety;
            dst.GroundSafety = src.GroundSafety;
            dst.GunAmmo = false;
            dst.emptyCost = src.emptyCost;
            dst.emptyMass = src.emptyMass;
            dst.mass = src.mass;
            dst.drag = src.drag;
            dst.emptyDrag = src.emptyDrag;
            dst.RCS = src.RCS;
            dst.emptyRCS = src.emptyRCS;
            dst.dontAutomaticallyAddToEncyclopedia = false;
        }

        internal static void CopyWeaponInfoScalars(WeaponInfo src, WeaponInfo dst)
        {
            dst.effectiveness = src.effectiveness;
            dst.targetRequirements = src.targetRequirements;
            dst.pK = src.pK;
            dst.fireInterval = src.fireInterval;
            dst.muzzleVelocity = src.muzzleVelocity;
            dst.maxSpeed = src.maxSpeed;
            dst.dragCoef = src.dragCoef;
            dst.gravMult = src.gravMult;
            dst.pierceDamage = src.pierceDamage;
            dst.blastDamage = src.blastDamage;
            dst.weaponIcon = src.weaponIcon;
            dst.armorTierEffectiveness = src.armorTierEffectiveness;
            dst.airburstHeight = src.airburstHeight;
            dst.visibilityWhenFired = src.visibilityWhenFired;
            dst.useWeaponDoors = src.useWeaponDoors;
            dst.boresight = src.boresight;
            dst.laserGuided = false;
            dst.missile = false;
            dst.bomb = true;
            dst.glideBomb = false;
            dst.gun = false;
            dst.overHorizon = false;
            dst.nuclear = false;
            dst.strategic = false;
            dst.energy = false;
            dst.jammer = false;
            dst.troops = false;
            dst.hideInDisplay = false;
            dst.cargo = false;
            dst.rearmGround = src.rearmGround;
            dst.rearmShip = src.rearmShip;
            dst.sling = false;
        }

        internal static void CopyUnitDefScalars(UnitDefinition src, UnitDefinition dst)
        {
            dst.visibleRange = src.visibleRange;
            dst.iconRange = src.iconRange;
            dst.iconSize = src.iconSize;
            dst.mapIconSize = src.mapIconSize;
            dst.captureStrength = 0f;
            dst.captureDefense = 0f;
            dst.manpower = 0f;
            dst.armorTier = src.armorTier;
            dst.damageTolerance = src.damageTolerance;
            dst.minEditorHeight = src.minEditorHeight;
            dst.maxEditorHeight = src.maxEditorHeight;
            dst.code = src.code;
        }

        internal static void CopyMapIdentity(UnitDefinition src, UnitDefinition dst)
        {
            dst.mapIcon = src.mapIcon;
            dst.friendlyIcon = src.friendlyIcon;
            dst.hostileIcon = src.hostileIcon;
            dst.mapOrient = src.mapOrient;
            dst.mapIconSize = src.mapIconSize;
            dst.typeIdentity = src.typeIdentity;
            dst.roleIdentity = src.roleIdentity;
            dst.IsObstacle = false;
        }

        internal static bool ContainsNet(List<INetworkDefinition> list, INetworkDefinition item)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                    return true;
            }
            return false;
        }

        internal static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                for (int i = 0; i < s.Length; i++)
                    h = h * 31 + s[i];
                if (h == 0)
                    h = 0x4D504B31;
                if (h < 0)
                    h = -h;
                return h;
            }
        }
    }
}
