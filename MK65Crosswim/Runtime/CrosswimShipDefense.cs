using System;
using System.Reflection;
using Crosswim.Bootstrap;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Runtime Crosswim VLS on Dynamo/Argus.
    /// Donor: AShM-300 or AGM-99 (Argus); else synthetic MissileLauncher.
    /// Fire: wet intercept (6 s CD, 1 inbound/target) + ship every 45 s.
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
            if (!_ship.LocalSim || _ship.disabled)
                return;
            if (GameManager.gameState == GameState.Encyclopedia)
                return;
            if (NetworkManagerNuclearOption.i == null || !NetworkManagerNuclearOption.i.Server.Active)
                return;

            float now = Time.timeSinceLevelLoad;
            if (now < _nextScan)
                return;
            _nextScan = now + CrosswimConstants.ShipScanIntervalS;

            if (_launcher.GetAmmoLoaded() <= 0)
                return;

            // 1) Intercept wet missiles — standard CD, all targets, skip if already inbound.
            if (now >= _nextTorpFire)
            {
                Unit? wet = CrosswimShipTargeting.PickWet(_ship);
                if (wet != null && TryFire(wet, CrosswimConstants.ShipTorpFireIntervalS))
                {
                    _nextTorpFire = now + CrosswimConstants.ShipTorpFireIntervalS;
                    return;
                }
            }

            // 2) Anti-ship — one every 45 s while a hostile ship is in perimeter.
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
            GameObject go;
            MissileLauncher launcher;
            if (donor != null)
            {
                Transform parent = donor.transform.parent != null ? donor.transform.parent : _ship.transform;
                bool donorWasActive = donor.gameObject.activeSelf;
                donor.gameObject.SetActive(false);
                go = UnityEngine.Object.Instantiate(donor.gameObject, parent);
                donor.gameObject.SetActive(donorWasActive);
                go.name = "MK65_Crosswim_VLS";
                go.SetActive(false);
                if (go.GetComponent<CrosswimLauncherTag>() == null)
                    go.AddComponent<CrosswimLauncherTag>();
                launcher = go.GetComponent<MissileLauncher>();
                if (launcher == null)
                {
                    UnityEngine.Object.Destroy(go);
                    go = CreateSyntheticLauncherGo(_ship);
                    launcher = go.GetComponent<MissileLauncher>();
                }
            }
            else
            {
                CrosswimPlugin.ModLog?.LogWarning(
                    $"CrosswimShipDefense: no AShM/AGM donor on '{_ship.unitName}' — synthetic VLS.");
                go = CreateSyntheticLauncherGo(_ship);
                launcher = go.GetComponent<MissileLauncher>();
            }

            if (launcher == null)
            {
                CrosswimPlugin.ModLog?.LogError(
                    $"CrosswimShipDefense: failed to create launcher on '{_ship.unitName}'.");
                return; // retry next frame — do not mark installed
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
            LastFiredField?.SetValue(launcher, -CrosswimConstants.ShipTorpFireIntervalS);

            WeaponStation station = new WeaponStation(_ship, false, false, false, false);
            station.WeaponInfo = info;
            station.Weapons.Add(launcher);
            launcher.SetWeaponStation(station);
            station.Number = (byte)_ship.weaponStations.Count;
            _ship.RegisterWeaponStation(station);
            station.AccountAmmo();

            _station = station;
            _launcher = launcher;
            _installed = true;
            CrosswimPlugin.ModLog?.LogInfo(
                $"CrosswimShipDefense installed '{_ship.unitName}' ammo={station.Ammo}/{station.FullAmmo} stations={_ship.weaponStations.Count}");
        }

        private static GameObject CreateSyntheticLauncherGo(Ship ship)
        {
            GameObject go = new GameObject("MK65_Crosswim_VLS");
            go.transform.SetParent(ship.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
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
            int score = 10; // any MissileLauncher is usable
            string n = ml.info != null ? (ml.info.weaponName ?? string.Empty) : string.Empty;
            string go = ml.gameObject.name ?? string.Empty;
            string key = string.Empty;
            if (ml.missile != null)
                key = ml.missile.jsonKey ?? string.Empty;

            if (Contains(n, "AShM") || Contains(go, "AShM") || Contains(key, "AShM"))
                score += 100;
            // Argus VLS often AGM-99 / AGM — accept as equal donor.
            if (Contains(n, "AGM-99") || Contains(n, "AGM99") || Contains(go, "AGM-99") ||
                Contains(go, "AGM99") || Contains(key, "AGM-99") || Contains(key, "AGM99") ||
                Contains(n, "AGM") || Contains(go, "AGM") || Contains(key, "AGM"))
                score += 100;
            if (Contains(n, "Strato") || Contains(go, "Strato"))
                score -= 80;
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
}
