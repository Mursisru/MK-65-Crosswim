using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    internal enum CrosswimPhase
    {
        Drop,
        VlsbLoft,
        Ballistic,
        Swim
    }

    /// <summary>
    /// Drop → ballistic → water → swim. Physics live in Ballistic/Swim; no GC in FixedUpdate.
    /// </summary>
    internal sealed class CrosswimFlight : MonoBehaviour
    {
        private Missile? _missile;
        private Rigidbody? _rb;
        private CrosswimPhase _phase;
        private float _phaseT;
        private float _swimThrustT = -1f;
        private Vector3 _entryHeading = Vector3.forward;
        private float _life;
        private bool _dockShed;
        private bool _engineFx;
        private Transform? _visual;
        private Unit? _target;
        private Vector3 _lead;
        private Vector3 _loftHeading = Vector3.forward;
        private bool _detonated;
        private bool _inboundTracked;
        private PersistentID _inboundId;
        private float _swimFuelUsedM;
        private GameObject? _boosterFx;
        private float _vlsbFuelKg;

        internal static void Attach(Missile missile)
        {
            if (missile == null)
                return;
            CrosswimFlight? f = missile.GetComponent<CrosswimFlight>();
            if (f == null)
                f = missile.gameObject.AddComponent<CrosswimFlight>();
            f.Init(missile);
        }

        internal CrosswimPhase Phase => _phase;

        private void Init(Missile missile)
        {
            _missile = missile;
            _rb = missile.rb != null ? missile.rb : missile.GetComponent<Rigidbody>();
            _visual = PrefabFactory.FindVisual(missile.transform);
            _target = ResolveTarget(missile);
            bool ship = missile.owner is Ship;
            bool encyclopedia = GameManager.gameState == GameState.Encyclopedia;
            if (_visual != null)
                CrosswimVisualParts.ApplyCarrier(_visual, ship, encyclopedia);

            EnableFlyPhysics();

            if (encyclopedia)
            {
                enabled = false;
                return;
            }

            if (ship)
            {
                _phase = CrosswimPhase.VlsbLoft;
                // Stack = body + AShM VLS dry + fuel (Force thrust, same as VLSBooster).
                _vlsbFuelKg = CrosswimAshmVls.FuelMassKg;
                CrosswimMass.Apply(missile, CrosswimConstants.MassKg + CrosswimAshmVls.LaunchExtraMassKg);
                _loftHeading = ResolveLoftHeading(missile, _target);
                CrosswimMotorFx.SilenceStock(missile);
                missile.SetThrottle(0f);
                _boosterFx = CrosswimMotorFx.SpawnBooster(missile, _visual);
            }
            else
            {
                CrosswimMotorFx.SilenceStock(missile);
                _phase = CrosswimPhase.Drop;
                TryShedDock();
            }
            _phaseT = 0f;
            _life = 0f;
            _engineFx = false;
            _detonated = false;
            _swimFuelUsedM = 0f;
            // Track live lock only for wet-missile intercepts (ships use 45 s cadence).
            if (_target is Missile)
            {
                _inboundId = _target.persistentID;
                CrosswimInbound.Add(_inboundId);
                _inboundTracked = true;
            }
        }

        private void OnDestroy()
        {
            ReleaseInbound();
        }

        private void ReleaseInbound()
        {
            if (!_inboundTracked)
                return;
            CrosswimInbound.Remove(_inboundId);
            _inboundTracked = false;
            _inboundId = default;
        }

        private static Vector3 ResolveLoftHeading(Missile missile, Unit? target)
        {
            Vector3 flat;
            if (target != null)
            {
                Vector3 tgt = target.transform.position;
                flat = tgt - missile.transform.position;
                flat.y = 0f;

                // Miss course → aim 2 km short of target (intercept gate, not overshoot chase).
                Rigidbody? rb = missile.rb != null ? missile.rb : missile.GetComponent<Rigidbody>();
                if (rb != null && flat.sqrMagnitude > 1f)
                {
                    Vector3 vel = rb.velocity;
                    vel.y = 0f;
                    float dist = flat.magnitude;
                    if (vel.sqrMagnitude > 25f)
                    {
                        float align = Vector3.Dot(vel.normalized, flat.normalized);
                        bool pastCpa = Vector3.Dot(flat, vel) < 0f;
                        bool diverging = align < CrosswimConstants.VlsbMissAlignDot;
                        if ((pastCpa || diverging) && dist > CrosswimConstants.VlsbAimShortM + 500f)
                        {
                            Vector3 gate = tgt - flat.normalized * CrosswimConstants.VlsbAimShortM;
                            flat = gate - missile.transform.position;
                            flat.y = 0f;
                        }
                    }
                }
            }
            else
            {
                flat = missile.transform.forward;
                flat.y = 0f;
            }
            if (flat.sqrMagnitude < 0.01f)
            {
                Unit? owner = missile.owner;
                if (owner != null)
                {
                    flat = owner.transform.forward;
                    flat.y = 0f;
                }
            }
            if (flat.sqrMagnitude < 0.01f)
                flat = Vector3.forward;
            return flat.normalized;
        }

        private void EnableFlyPhysics()
        {
            if (_rb == null)
                return;
            Vector3 keep = _rb.velocity;
            if (keep.sqrMagnitude < 0.01f && _missile != null)
                keep = _missile.startingVelocity;
            _rb.isKinematic = false;
            _rb.detectCollisions = false;
            _rb.useGravity = true;
            _rb.velocity = keep;
            _rb.angularVelocity = Vector3.zero;
        }

        private static Unit? ResolveTarget(Missile missile) =>
            CrosswimHoming.ResolveAssigned(missile);

        private void FixedUpdate()
        {
            if (_missile == null || _rb == null)
                return;
            float dt = Time.fixedDeltaTime;
            _life += dt;
            _phaseT += dt;
            if (_life > CrosswimConstants.SoftKillTimeoutS)
            {
                SoftKill();
                return;
            }

            switch (_phase)
            {
                case CrosswimPhase.Drop:
                    TryShedDock();
                    _phase = CrosswimPhase.Ballistic;
                    _phaseT = 0f;
                    TickBallistic(dt);
                    break;
                case CrosswimPhase.VlsbLoft:
                    VlsbStep(dt);
                    break;
                case CrosswimPhase.Ballistic:
                    TryShedDock();
                    TickBallistic(dt);
                    break;
                case CrosswimPhase.Swim:
                    TickSwim(dt);
                    break;
            }
        }

        private void TryShedDock()
        {
            if (_dockShed)
                return;
            if (_visual == null && _missile != null)
                _visual = PrefabFactory.FindVisual(_missile.transform);
            if (CrosswimDockEject.TryEject(_missile, _visual))
                _dockShed = true;
            else if (_visual != null && !CrosswimDockEject.HasDockingPortLeft(_visual) &&
                     (_missile == null || !CrosswimDockEject.HasDockingPortLeft(_missile.transform)))
                _dockShed = true;
        }

        private void VlsbStep(float dt)
        {
            if (_rb == null)
                return;
            _rb.useGravity = true;
            if (_missile != null)
                _missile.SetThrottle(0f);

            float sea = Datum.LocalSeaY;
            float alt = transform.position.y - sea;

            // Hard floor — cruise missile skim, never dive under with VLSB attached.
            if (alt < CrosswimConstants.VlsbMinAltM)
            {
                Vector3 p = transform.position;
                p.y = sea + CrosswimConstants.VlsbMinAltM;
                _rb.position = p;
                Vector3 v = _rb.velocity;
                if (v.y < 0f)
                    v.y = 0f;
                _rb.velocity = v;
                alt = CrosswimConstants.VlsbMinAltM;
            }

            // Soft heading chase — AShM TerrainWaypoint ~10°/tick, not 22°/s.
            if (_target != null && !_target.disabled && _missile != null)
            {
                Vector3 desire = ResolveLoftHeading(_missile, _target);
                _loftHeading = Vector3.RotateTowards(
                    _loftHeading,
                    desire,
                    CrosswimConstants.VlsbHeadingRadPerTick,
                    0f);
            }

            float dist = DistHorizToTarget();
            float burnS = CrosswimAshmVls.BurnTimeS;
            if (dist <= CrosswimConstants.VlsbShedRangeM ||
                _phaseT >= burnS ||
                _vlsbFuelKg <= 0f)
            {
                ShedVlsb();
                return;
            }

            // Burn fuel → mass drop like vanilla VLSBooster.Thrust().
            float burnRate = burnS > 0.01f ? CrosswimAshmVls.FuelMassKg / burnS : CrosswimAshmVls.FuelMassKg;
            float dFuel = burnRate * dt;
            if (dFuel > _vlsbFuelKg)
                dFuel = _vlsbFuelKg;
            _vlsbFuelKg -= dFuel;
            if (_missile != null && dFuel > 0f)
                CrosswimMass.Apply(_missile, Mathf.Max(CrosswimConstants.MassKg + CrosswimAshmVls.DryMassKg, _rb.mass - dFuel));

            Vector3 want;
            if (_phaseT < CrosswimConstants.VlsbKickTimeS)
            {
                float u = _phaseT / CrosswimConstants.VlsbKickTimeS;
                u = u * u * (3f - 2f * u);
                want = Vector3.Slerp(Vector3.up, _loftHeading, u).normalized;
            }
            else
            {
                // AShM TerrainWaypoint: aim point ahead at sea + altitudeTarget (bends path down).
                float speed = _rb.velocity.magnitude;
                float look = Mathf.Max(speed, 100f) * CrosswimConstants.VlsbLookaheadSpeedMult;
                Vector3 aim = transform.position + _loftHeading * look;
                aim.y = sea + CrosswimConstants.VlsbCruiseAltM;
                want = (aim - transform.position).normalized;
                if (want.sqrMagnitude < 1e-4f)
                    want = _loftHeading;
            }

            // Nose follows aim at AShM maxTurnRate (no vel-blend that blocked dive).
            CrosswimBallistic.AlignNose(
                _rb, transform, want, dt, CrosswimAshmVls.MaxTurnRateDegS, levelRoll: true);
            // Lift substitute — actually changes velocity vector like ApplyAero.
            CrosswimBallistic.BendVelocityToNose(
                _rb, transform, dt, CrosswimAshmVls.MaxTurnRateDegS, CrosswimAshmVls.GLimit);
            _rb.AddForce(transform.forward * CrosswimAshmVls.EffectiveThrustN, ForceMode.Force);
        }

        private float DistHorizToTarget()
        {
            if (_target == null || _target.disabled)
                return float.MaxValue;
            Vector3 d = _target.transform.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private void ShedVlsb()
        {
            CrosswimMotorFx.DestroyBooster(ref _boosterFx);
            if (_missile != null)
                CrosswimMotorFx.SilenceStock(_missile);

            if (_visual != null)
            {
                Transform? vlsb = CrosswimVisualParts.FindExact(_visual, "VLSB");
                if (vlsb == null)
                    vlsb = CrosswimVisualParts.FindByAliases(_visual, CrosswimConstants.VlsbAliases);
                if (vlsb != null)
                {
                    Vector3 vel = _rb != null ? _rb.velocity : Vector3.zero;
                    CrosswimVisualParts.KillVlsbFxSubtree(vlsb);
                    vlsb.SetParent(null, true);
                    Rigidbody rb = vlsb.gameObject.GetComponent<Rigidbody>() ?? vlsb.gameObject.AddComponent<Rigidbody>();
                    rb.mass = CrosswimAshmVls.DryMassKg;
                    rb.useGravity = true;
                    rb.isKinematic = false;
                    rb.velocity = vel + Vector3.down * 2f;
                    Object.Destroy(vlsb.gameObject, 10f);
                }
                CrosswimVisualParts.KillVlsbFx(_visual);
            }
            if (_missile != null)
            {
                CrosswimMass.Apply(_missile, CrosswimConstants.MassKg);
                _missile.SetThrottle(0f);
                CrosswimMotorFx.SilenceStock(_missile);
            }
            _phase = CrosswimPhase.Ballistic;
            _phaseT = 0f;
        }

        private void TickBallistic(float dt)
        {
            if (_missile == null || _detonated)
                return;
            _missile.SetThrottle(0f);
            if (TryImpact("ballistic"))
                return;
            // Passive weathercock into velocity only — no target seek in air.
            CrosswimBallistic.Apply(_missile, dt);
            if (transform.position.y <= Datum.LocalSeaY)
                EnterWater();
        }

        private void EnterWater()
        {
            if (_rb == null || _missile == null)
                return;
            if (_phase == CrosswimPhase.Swim)
                return;
            // Never swim while VLSB is still the active phase — shed first.
            if (_phase == CrosswimPhase.VlsbLoft)
            {
                ShedVlsb();
                return;
            }

            Vector3 v = _rb.velocity;
            v.y = Mathf.Min(v.y, -4f);
            if (v.sqrMagnitude < 1f)
                v = transform.forward * 8f + Vector3.down * 4f;
            float entryCap = CrosswimConstants.SwimEntryMaxMps;
            if (v.magnitude > entryCap)
                v = v.normalized * entryCap;

            // Splash heading = horizontal velocity (fallback nose). Used so steep dive can't yaw-flip.
            _entryHeading = new Vector3(v.x, 0f, v.z);
            if (_entryHeading.sqrMagnitude < 0.25f)
                _entryHeading = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (_entryHeading.sqrMagnitude < 0.01f)
                _entryHeading = Vector3.forward;
            _entryHeading.Normalize();

            // Keep a forward splash component so weathercock has a stable yaw reference.
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            if (flat.sqrMagnitude < 4f)
                v = _entryHeading * Mathf.Max(flat.magnitude, 12f) + Vector3.down * Mathf.Abs(v.y);

            _rb.velocity = v;
            _rb.useGravity = false;
            _rb.drag = 0f;
            _rb.angularDrag = CrosswimConstants.SwimAngularDrag;
            _rb.detectCollisions = false;

            CrosswimShellPrep.Arm(_missile);
            CrosswimOpening.Play(_visual);
            _phase = CrosswimPhase.Swim;
            _phaseT = 0f;
            _swimThrustT = -1f;
            TryShedDock();
            CrosswimPlugin.ModLog?.LogInfo(
                $"Crosswim water entry y={transform.position.y:F1} sea={Datum.LocalSeaY:F1} spd={v.magnitude:F1} hdg={_entryHeading}");
        }

        private void TickSwim(float dt)
        {
            if (_rb == null || _missile == null || _detonated)
                return;

            if (TryImpact("swim"))
                return;

            TryShedDock();

            if (_swimThrustT < 0f)
            {
                float spd = _rb.velocity.magnitude;
                if (spd <= CrosswimConstants.SwimCoastMps || _phaseT >= CrosswimConstants.SwimBleedMaxS)
                {
                    _swimThrustT = 0f;
                    PlayEngineFx();
                }
            }
            else
                _swimThrustT += dt;

            if (_swimThrustT >= 0f)
            {
                Vector3 horiz = _rb.velocity;
                horiz.y = 0f;
                _swimFuelUsedM += horiz.magnitude * dt;
                if (_swimFuelUsedM >= CrosswimConstants.SwimFuelRangeM)
                {
                    SoftKill();
                    return;
                }
            }

            // Only the fire-assigned lock (targetID) — never pick opportunistic nearby units.
            _target = CrosswimHoming.ResolveAssigned(_missile);
            Vector3 aim = CrosswimHoming.InterceptPoint(
                _missile.transform.position, _rb.velocity, _target, _entryHeading, out _lead);
            aim.y = Datum.LocalSeaY - CrosswimConstants.SwimDepthM;
            CrosswimSwim.Apply(_missile, aim, dt, _swimThrustT, _entryHeading, _target != null);
        }

        private bool TryImpact(string tag)
        {
            if (_missile == null || _detonated)
                return false;
            if (!CrosswimImpact.ProbeHull(_missile, out RaycastHit hit))
                return false;
            Boom(hit.normal.sqrMagnitude > 0.01f ? hit.normal : Vector3.up, tag);
            return true;
        }

        private void Boom(Vector3 normal, string reason)
        {
            if (_missile == null || _detonated)
                return;
            _detonated = true;
            string why = reason;
            if (_target is Ship || (_target != null && _target.GetComponentInParent<Ship>() != null))
                why = "ship-" + reason;
            CrosswimImpact.DetonateNow(_missile, normal, why);
            ReleaseInbound();
            enabled = false;
        }

        private void PlayEngineFx()
        {
            if (_engineFx || _missile == null)
                return;
            _engineFx = true;
            CrosswimMotorFx.Play(_missile, _visual);
        }

        private void SoftKill()
        {
            if (_missile == null)
                return;
            ReleaseInbound();
            Object.Destroy(_missile.gameObject);
        }
    }
}
