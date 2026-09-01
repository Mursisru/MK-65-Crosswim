using System;
using System.Reflection;
using Crosswim.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Dynamo/Argus: convert existing AShM/AGM VLS launcher to Crosswim in-place
    /// (no Instantiate donor — that duplicated the cell bank). Block leftover vanilla VLS fire.
    /// </summary>
    internal sealed class CrosswimShipDefense : MonoBehaviour
    {
        private static readonly FieldInfo? MissileField =
            AccessTools.Field(typeof(MissileLauncher), "missile");
        private static readonly FieldInfo? FireIntervalField =
            AccessTools.Field(typeof(MissileLauncher), "fireInterval");
        private static readonly FieldInfo? MaxAmmoField =
            AccessTools.Field(typeof(MissileLauncher), "maxAmmo");
        private static readonly FieldInfo? LastFiredField =
            AccessTools.Field(typeof(Weapon), "lastFired");
        private static readonly FieldInfo? LaunchTransformsField =
            AccessTools.Field(typeof(MissileLauncher), "launchTransforms");

        internal static int FireGate;

        private Ship? _ship;
        private WeaponStation? _station;
        private MissileLauncher? _launcher;
        private float _nextScan = -1f;
        private float _nextTorpFire = -1f;
        private float _nextShipFire = -1f;
        private bool _subscribed;
        private bool _installed;

        private void Awake()
        {
            _ship = GetComponent<Ship>() ?? GetComponentInParent<Ship>();
            if (_ship == null)
                return;
            _ship.onInitialize += OnShipInitialized;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed && _ship != null)
                _ship.onInitialize -= OnShipInitialized;
        }

        private void Start()
        {
            if (_ship != null && _ship.weaponStations != null && _ship.weaponStations.Count > 0)
                InstallWeapon();
        }

        private void OnShipInitialized()
        {
            if (_ship != null)
                _ship.onInitialize -= OnShipInitialized;
            _subscribed = false;
            InstallWeapon();
        }

        private void Update()
        {
            if (!_installed)
                InstallWeapon();
            if (!_installed || _ship == null || _station == null || _launcher == null)
                return;
            if (_ship.disabled)
                return;
            if (!_ship.LocalSim && !_ship.IsServer)
                return;
            if (GameManager.gameState == GameState.Encyclopedia)
                return;

            float now = Time.timeSinceLevelLoad;
            if (now < _nextScan)
                return;
            _nextScan = now + CrosswimConstants.ShipScanIntervalS;

            if (_launcher.GetAmmoLoaded() <= 0)
                return;

            if (now >= _nextTorpFire)
            {
                Unit? wet = CrosswimShipTargeting.PickWet(_ship);
                if (wet != null && TryFire(wet, CrosswimConstants.ShipTorpFireIntervalS))
                {
                    _nextTorpFire = now + CrosswimConstants.ShipTorpFireIntervalS;
                    return;
                }
            }

            if (now >= _nextShipFire)
            {
                Unit? ship = CrosswimShipTargeting.PickShip(_ship);
                if (ship != null && TryFire(ship, CrosswimConstants.ShipShipFireIntervalS))
                    _nextShipFire = now + CrosswimConstants.ShipShipFireIntervalS;
            }
        }

        private void InstallWeapon()
        {
            if (_installed)
                return;
            _ship ??= GetComponent<Ship>() ?? GetComponentInParent<Ship>();
            if (_ship == null)
                return;

            DestroySyntheticClones(_ship);

            WeaponInfo? info = CrosswimBootstrap.Info;
            MissileDefinition? def = CrosswimBootstrap.Definition;
            if (info == null || def == null)
                return;

            for (int i = 0; i < _ship.weaponStations.Count; i++)
            {
                WeaponStation ws = _ship.weaponStations[i];
                if (ws == null || !CrosswimBootstrap.IsOurInfo(ws.WeaponInfo))
                    continue;
                _station = ws;
                _launcher = FindOurLauncher(ws);
                if (_launcher != null)
                {
                    ApplyAmmoCap(_launcher);
                    SilenceOtherVls(_ship, _launcher);
                    ws.AccountAmmo();
                    _installed = true;
                    CrosswimPlugin.ModLog?.LogInfo(
                        $"CrosswimShipDefense reuse '{_ship.unitName}' ammo={ws.Ammo}/{ws.FullAmmo}");
                    return;
                }
            }

            if (_ship.weaponStations.Count == 0)
                return;

            MissileLauncher? donor = FindDonorLauncher(_ship);
            MissileLauncher launcher;
            WeaponStation? existingStation = null;

            if (donor != null)
            {
                // Convert donor in-place — keeps one cell bank, no second VLS mesh.
                launcher = ConvertDonor(donor, def, info);
                existingStation = FindStationOwning(_ship, donor);
            }
            else
            {
                CrosswimPlugin.ModLog?.LogWarning(
                    $"CrosswimShipDefense: no AShM/AGM donor on '{_ship.unitName}' — synthetic.");
                GameObject go = CreateSyntheticLauncherGo(_ship, null);
                launcher = go.GetComponent<MissileLauncher>();
                if (launcher == null)
                {
                    UnityEngine.Object.Destroy(go);
                    return;
                }
                if (MissileField != null)
                    MissileField.SetValue(launcher, def);
                else
                    launcher.missile = def;
                launcher.info = info;
                ApplyAmmoCap(launcher);
                launcher.AttachToUnit(_ship);
                go.SetActive(true);
                ApplyAmmoCap(launcher);
            }

            LastFiredField?.SetValue(launcher, -CrosswimConstants.ShipTorpFireIntervalS);

            WeaponStation station;
            if (existingStation != null)
            {
                station = existingStation;
                station.WeaponInfo = info;
                if (!station.Weapons.Contains(launcher))
                    station.Weapons.Add(launcher);
                launcher.SetWeaponStation(station);
            }
            else
            {
                station = new WeaponStation(_ship, false, false, false, false);
                station.WeaponInfo = info;
                station.Weapons.Add(launcher);
                launcher.SetWeaponStation(station);
                station.Number = (byte)_ship.weaponStations.Count;
                _ship.RegisterWeaponStation(station);
            }

            station.AccountAmmo();
            SilenceOtherVls(_ship, launcher);

            _station = station;
            _launcher = launcher;
            _installed = true;
            CrosswimPlugin.ModLog?.LogInfo(
                $"CrosswimShipDefense converted '{_ship.unitName}' ammo={station.Ammo}/{station.FullAmmo}");
        }

        private static MissileLauncher ConvertDonor(
            MissileLauncher donor,
            MissileDefinition def,
            WeaponInfo info)
        {
            if (donor.GetComponent<CrosswimLauncherTag>() == null)
                donor.gameObject.AddComponent<CrosswimLauncherTag>();
            donor.gameObject.name = "MK65_Crosswim_VLS";

            if (MissileField != null)
                MissileField.SetValue(donor, def);
            else
                donor.missile = def;
            donor.info = info;
            ApplyAmmoCap(donor);
            return donor;
        }

        /// <summary>Zero ammo on other AShM/AGM VLS so AI / FireControl cannot dump vanilla cruise.</summary>
        private static void SilenceOtherVls(Ship ship, MissileLauncher keep)
        {
            MissileLauncher[] all = ship.GetComponentsInChildren<MissileLauncher>(true);
            for (int i = 0; i < all.Length; i++)
            {
                MissileLauncher ml = all[i];
                if (ml == null || ReferenceEquals(ml, keep))
                    continue;
                if (ml.GetComponent<CrosswimLauncherTag>() != null)
                    continue;
                if (!IsAshmOrAgmVls(ml))
                    continue;
                ml.ammo = 0;
                MaxAmmoField?.SetValue(ml, 0);
                if (ml.GetComponent<CrosswimSilencedVls>() == null)
                    ml.gameObject.AddComponent<CrosswimSilencedVls>();
            }
        }

        internal static bool IsSilencedVanillaVls(MissileLauncher ml)
        {
            if (ml == null)
                return false;
            if (ml.GetComponent<CrosswimLauncherTag>() != null)
                return false;
            if (CrosswimBootstrap.IsOurInfo(ml.info))
                return false;
            return ml.GetComponent<CrosswimSilencedVls>() != null || IsAshmOrAgmVls(ml);
        }

        private static bool IsAshmOrAgmVls(MissileLauncher ml)
        {
            string n = ml.info != null ? (ml.info.weaponName ?? string.Empty) : string.Empty;
            string go = ml.gameObject.name ?? string.Empty;
            string key = ml.missile != null ? (ml.missile.jsonKey ?? string.Empty) : string.Empty;
            // Dynamo AShM-300 + Argus AGM-99 VLS only — not every AGM on the hull.
            return Contains(n, "AShM") || Contains(go, "AShM") || Contains(key, "AShM") ||
                   Contains(n, "AGM-99") || Contains(n, "AGM99") || Contains(go, "AGM-99") ||
                   Contains(go, "AGM99") || Contains(key, "AGM-99") || Contains(key, "AGM99");
        }

        private static void DestroySyntheticClones(Ship ship)
        {
            Transform[] all = ship.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform t = all[i];
                if (t == null)
                    continue;
                if (t.name.IndexOf("MK65_Crosswim", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // Only destroy empty synthetic roots (no launchTransforms children from real VLS).
                MissileLauncher? ml = t.GetComponent<MissileLauncher>();
                if (ml == null)
                {
                    UnityEngine.Object.Destroy(t.gameObject);
                    continue;
                }
                // Real converted donor keeps launchTransforms — leave it.
                if (LaunchTransformsField?.GetValue(ml) is Transform[] cells && cells.Length > 0)
                    continue;
                // Synthetic: no cell array — if it also has Crosswim tag and we will convert donor, drop extras.
                if (t.GetComponent<CrosswimLauncherTag>() != null &&
                    t.childCount == 0)
                {
                    // May still be the only launcher — keep until convert decides.
                }
            }
        }

        private static GameObject CreateSyntheticLauncherGo(Ship ship, MissileLauncher? donor)
        {
            GameObject go = new GameObject("MK65_Crosswim_VLS");
            Transform parent = ship.transform;
            if (donor != null && donor.transform.parent != null)
                parent = donor.transform.parent;
            go.transform.SetParent(parent, false);
            if (donor != null)
            {
                go.transform.position = donor.transform.position;
                go.transform.rotation = donor.transform.rotation;
            }
            else
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
            }
            go.SetActive(false);
            go.AddComponent<MissileLauncher>();
            go.AddComponent<CrosswimLauncherTag>();
            return go;
        }

        private static void ApplyAmmoCap(MissileLauncher launcher)
        {
            if (launcher == null)
                return;
            launcher.ammo = CrosswimConstants.ShipVlsAmmo;
            MaxAmmoField?.SetValue(launcher, CrosswimConstants.ShipVlsAmmo);
            FireIntervalField?.SetValue(launcher, CrosswimConstants.ShipTorpFireIntervalS);
        }

        private static WeaponStation? FindStationOwning(Ship ship, MissileLauncher launcher)
        {
            for (int i = 0; i < ship.weaponStations.Count; i++)
            {
                WeaponStation ws = ship.weaponStations[i];
                if (ws?.Weapons == null)
                    continue;
                for (int w = 0; w < ws.Weapons.Count; w++)
                {
                    if (ReferenceEquals(ws.Weapons[w], launcher))
                        return ws;
                }
            }
            return null;
        }

        private static MissileLauncher? FindOurLauncher(WeaponStation ws)
        {
            if (ws.Weapons == null)
                return null;
            for (int i = 0; i < ws.Weapons.Count; i++)
            {
                if (ws.Weapons[i] is MissileLauncher ml &&
                    (ml.GetComponent<CrosswimLauncherTag>() != null || CrosswimBootstrap.IsOurInfo(ml.info)))
                    return ml;
            }
            return null;
        }

        private static MissileLauncher? FindDonorLauncher(Ship ship)
        {
            MissileLauncher[] all = ship.GetComponentsInChildren<MissileLauncher>(true);
            MissileLauncher? best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < all.Length; i++)
            {
                MissileLauncher ml = all[i];
                if (ml == null || ml.GetComponent<CrosswimLauncherTag>() != null)
                    continue;
                if (CrosswimBootstrap.IsOurInfo(ml.info))
                    continue;
                int score = ScoreDonor(ml);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = ml;
                }
            }
            return best;
        }

        private static int ScoreDonor(MissileLauncher ml)
        {
            int score = 10;
            string n = ml.info != null ? (ml.info.weaponName ?? string.Empty) : string.Empty;
            string go = ml.gameObject.name ?? string.Empty;
            string key = ml.missile != null ? (ml.missile.jsonKey ?? string.Empty) : string.Empty;

            if (Contains(n, "AShM") || Contains(go, "AShM") || Contains(key, "AShM"))
                score += 100;
            if (Contains(n, "AGM-99") || Contains(n, "AGM99") || Contains(go, "AGM-99") ||
                Contains(go, "AGM99") || Contains(key, "AGM-99") || Contains(key, "AGM99"))
                score += 100;
            if (Contains(n, "Strato") || Contains(go, "Strato"))
                score -= 80;
            // Prefer real cell banks over empty synthetics.
            if (LaunchTransformsField?.GetValue(ml) is Transform[] cells && cells.Length > 0)
                score += 50;
            return score;
        }

        private static bool Contains(string s, string part) =>
            !string.IsNullOrEmpty(s) &&
            s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        private bool TryFire(Unit target, float interval)
        {
            if (_ship == null || _station == null || _launcher == null || target == null)
                return false;
            int ammoBefore = _launcher.GetAmmoLoaded();
            if (ammoBefore <= 0)
                return false;
            if (target is Missile m && !CrosswimInbound.HasRoom(m))
                return false;

            FireIntervalField?.SetValue(_launcher, interval);
            LastFiredField?.SetValue(_launcher, Time.timeSinceLevelLoad - interval);

            FireGate++;
            try
            {
                Vector3 vel = _ship.rb != null ? _ship.rb.velocity : Vector3.zero;
                _launcher.Fire(_ship, target, vel, _station, default);
            }
            finally
            {
                if (FireGate > 0)
                    FireGate--;
            }

            if (_launcher.GetAmmoLoaded() >= ammoBefore)
                return false;

            _station.AccountAmmo();
            return true;
        }
    }

    /// <summary>Marks a vanilla AShM/AGM VLS silenced after Crosswim took over.</summary>
    internal sealed class CrosswimSilencedVls : MonoBehaviour
    {
    }
}
