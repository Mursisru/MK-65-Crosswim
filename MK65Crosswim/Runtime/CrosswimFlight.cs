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
        private bool _boosterLit;
        private float _stallT;
        private float _submergedLoftT;
        private PersistentID _selfId;

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

        /// <summary>False when drowned/stalled — ship defense may treat inbound as free.</summary>
        internal bool CoversThreat
        {
            get
            {
                if (!enabled || _missile == null || _missile.disabled)
                    return false;
                float sea = Datum.LocalSeaY;
                if (transform.position.y < sea - 0.5f && _phase != CrosswimPhase.Swim)
                    return false;
                if (_rb == null)
                    return true;
                float spd = _rb.velocity.magnitude;
                if (_phase == CrosswimPhase.VlsbLoft &&
                    _phaseT > CrosswimAshmVls.DelayTimeS + CrosswimConstants.VlsbKickTimeS &&
                    spd < CrosswimConstants.VlsbStallSpeedMps)
                    return false;
                if (_phase == CrosswimPhase.Swim && _swimThrustT >= 0f &&
                    spd < CrosswimConstants.SwimStallSpeedMps)
                    return false;
                return true;
            }
        }

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
                _boosterLit = false;
                CrosswimMass.Apply(missile, CrosswimConstants.MassKg + CrosswimAshmVls.LaunchExtraMassKg);
                _loftHeading = ResolveLoftHeading(missile, _target);
                CrosswimMotorFx.SilenceStock(missile);
                missile.SetThrottle(0f);
                // Idle FX until tube delay — AShM delayTimer then Activate.
                _boosterFx = CrosswimMotorFx.SpawnBooster(missile, _visual);
                // Spawn must clear sea immediately — tube may open near waterline.
                if (_rb != null)
                {
                    float floorY = Datum.LocalSeaY + CrosswimConstants.VlsbMinAltM;
                    Vector3 p = missile.transform.position;
                    if (p.y < floorY)
                    {
                        p.y = floorY;
                        _rb.position = p;
                        missile.transform.position = p;
                    }
                    Vector3 v = _rb.velocity;
                    v.y = Mathf.Max(v.y, CrosswimConstants.VlsbTubeClimbMps);
                    _rb.velocity = v;
                    _rb.useGravity = false;
                }
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
            _stallT = 0f;
            _submergedLoftT = 0f;
            _selfId = missile.persistentID;
            TryClaimInbound();
        }

        private void OnDestroy()
        {
            ReleaseInbound();
        }

        private void TryClaimInbound()
        {
            if (_inboundTracked || _missile == null || _target is not Missile)
                return;
            _selfId = _missile.persistentID;
            if (!_selfId.IsValid || !_target.persistentID.IsValid)
                return;
            _inboundId = _target.persistentID;
            CrosswimInbound.Add(_inboundId, _selfId);
            _inboundTracked = true;
        }

        private void ReleaseInbound()
        {
            if (!_inboundTracked)
                return;
            CrosswimInbound.RemoveInterceptor(_selfId);
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
            TryClaimInbound();
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
            if (_missile != null)
                _missile.SetThrottle(0f);

            float sea = Datum.LocalSeaY;
            float delay = CrosswimAshmVls.DelayTimeS;
            float alt = transform.position.y - sea;

            // Tube coast: no gravity sink — rise clear of the deck/water until ignition.
            bool tube = _phaseT < delay;
            _rb.useGravity = !tube;

            // SoftKill only if somehow still pinned under after clamps (safety net).
            if (alt < -0.5f)
            {
                _submergedLoftT += dt;
                if (_submergedLoftT >= CrosswimConstants.VlsbDrownSoftKillS)
                {
                    SoftKill();
                    return;
                }
            }
            else if (!tube &&
                     _phaseT > delay + CrosswimConstants.VlsbKickTimeS &&
                     _rb.velocity.magnitude < CrosswimConstants.VlsbStallSpeedMps)
            {
                _submergedLoftT += dt;
                if (_submergedLoftT >= CrosswimConstants.VlsbStallSoftKillS)
                {
                    SoftKill();
                    return;
                }
            }
            else
                _submergedLoftT = 0f;

            EnforceVlsbAboveSea(sea, climb: tube || alt < CrosswimConstants.VlsbCruiseAltM);

            if (tube)
            {
                Vector3 v = _rb.velocity;
                v.y = Mathf.Max(v.y, CrosswimConstants.VlsbTubeClimbMps);
                _rb.velocity = v;
                Vector3 wantUp = Vector3.up;
                if (_rb.velocity.sqrMagnitude > 4f)
                    wantUp = Vector3.Slerp(Vector3.up, _rb.velocity.normalized, 0.2f).normalized;
                CrosswimBallistic.AlignNose(
                    _rb, transform, wantUp, dt, CrosswimConstants.VlsbTubeAlignDegS, levelRoll: true);
                EnforceVlsbAboveSea(sea, climb: true);
                return;
            }

            if (!_boosterLit)
            {
                CrosswimMotorFx.ActivateBooster(_boosterFx, _missile!);
                _boosterLit = true;
            }

            float burnT = _phaseT - delay;

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
                burnT >= burnS ||
                _vlsbFuelKg <= 0f)
            {
                ShedVlsb();
                return;
            }

            float burnRate = burnS > 0.01f ? CrosswimAshmVls.FuelMassKg / burnS : CrosswimAshmVls.FuelMassKg;
            float dFuel = burnRate * dt;
            if (dFuel > _vlsbFuelKg)
                dFuel = _vlsbFuelKg;
            _vlsbFuelKg -= dFuel;
            if (_missile != null && dFuel > 0f)
                CrosswimMass.Apply(_missile, Mathf.Max(CrosswimConstants.MassKg + CrosswimAshmVls.DryMassKg, _rb.mass - dFuel));

            Vector3 want;
            float thrustMul = 1f;
            float turnMul = 1f;
            alt = transform.position.y - sea;
            if (burnT < CrosswimConstants.VlsbKickTimeS)
            {
                float u = burnT / CrosswimConstants.VlsbKickTimeS;
                u = u * u * (3f - 2f * u);
                // Hold climb until above cruise — don't pitch into the sea on soft kick.
                float pitchHold = alt < CrosswimConstants.VlsbCruiseAltM
                    ? Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(alt / CrosswimConstants.VlsbCruiseAltM))
                    : 0f;
                Vector3 loft = Vector3.Slerp(Vector3.up, _loftHeading, u).normalized;
                want = Vector3.Slerp(loft, Vector3.up, pitchHold).normalized;
                thrustMul = Mathf.Lerp(CrosswimConstants.VlsbKickThrustMin, 1f, u);
                turnMul = Mathf.Lerp(CrosswimConstants.VlsbKickTurnMin, 1f, u);
            }
            else
            {
                float speed = _rb.velocity.magnitude;
                float look = Mathf.Max(speed, 100f) * CrosswimConstants.VlsbLookaheadSpeedMult;
                Vector3 aim = transform.position + _loftHeading * look;
                aim.y = sea + CrosswimConstants.VlsbCruiseAltM;
                // Never aim below the skim floor while still climbing out.
                if (aim.y < sea + CrosswimConstants.VlsbMinAltM)
                    aim.y = sea + CrosswimConstants.VlsbMinAltM;
                want = (aim - transform.position).normalized;
                if (want.sqrMagnitude < 1e-4f)
                    want = _loftHeading;
                if (alt < CrosswimConstants.VlsbMinAltM + CrosswimConstants.VlsbFloorBandM && want.y < 0.15f)
                {
                    want.y = 0.15f;
                    want.Normalize();
                }
            }

            float turnDeg = CrosswimAshmVls.MaxTurnRateDegS * turnMul;
            float gLim = CrosswimAshmVls.GLimit * turnMul;
            CrosswimBallistic.AlignNose(_rb, transform, want, dt, turnDeg, levelRoll: true);
            CrosswimBallistic.BendVelocityToNose(_rb, transform, dt, turnDeg, gLim);
            _rb.AddForce(transform.forward * (CrosswimAshmVls.EffectiveThrustN * thrustMul), ForceMode.Force);

            // After thrust/bend — re-clamp so physics can't bury us this tick.
            EnforceVlsbAboveSea(sea, climb: alt < CrosswimConstants.VlsbCruiseAltM);
        }

        /// <summary>Hard floor for entire VLSB loft — never cross LocalSeaY.</summary>
        private void EnforceVlsbAboveSea(float sea, bool climb)
        {
            if (_rb == null)
                return;

            float floorY = sea + CrosswimConstants.VlsbMinAltM;
            Vector3 p = transform.position;
            if (p.y < floorY)
            {
                p.y = floorY;
                _rb.position = p;
                transform.position = p;
                _rb.AddForce(Vector3.up * CrosswimConstants.VlsbSurfaceRescueMps2, ForceMode.Acceleration);
            }

            Vector3 v = _rb.velocity;
            float band = floorY + CrosswimConstants.VlsbFloorBandM;
            if (p.y <= band)
            {
                if (v.y < 0f)
                    v.y = 0f;
                if (climb)
                    v.y = Mathf.Max(v.y, CrosswimConstants.VlsbFloorClimbMps);
                _rb.velocity = v;
            }
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
            {
                CrosswimMotorFx.SilenceStock(_missile);
                _missile.boosterIsAttached = false;
            }

            Vector3 vel = _rb != null ? _rb.velocity : Vector3.zero;
            Vector3 aft = transform.forward;
            CrosswimVlsbShed.Detach(_visual, vel, aft, CrosswimAshmVls.DryMassKg);

            if (_missile != null)
            {
                CrosswimMass.Apply(_missile, CrosswimConstants.MassKg);
                _missile.SetThrottle(0f);
                CrosswimMotorFx.SilenceStock(_missile);
                _missile.boosterIsAttached = false;
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
            CrosswimStealth.OnSubmerged(_missile);
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

                // Drowned / stalled near launch — free inbound so destroyer can shoot again.
                if (_rb.velocity.magnitude < CrosswimConstants.SwimStallSpeedMps)
                {
                    _stallT += dt;
                    if (_stallT >= CrosswimConstants.SwimStallSoftKillS)
                    {
                        SoftKill();
                        return;
                    }
                }
                else
                    _stallT = 0f;
            }

            // Only the fire-assigned lock (targetID) — never pick opportunistic nearby units.
            _target = CrosswimHoming.ResolveAssigned(_missile);
            Vector3 aim = CrosswimHoming.InterceptPoint(
                _missile.transform.position, _rb.velocity, _target, _entryHeading, out _lead);

            float cruiseY = Datum.LocalSeaY - CrosswimConstants.SwimDepthM;
            float horizDist = float.MaxValue;
            if (_target != null)
            {
                Vector3 d = _target.transform.position - transform.position;
                d.y = 0f;
                horizDist = d.magnitude;
            }

            bool terminal = _target != null && horizDist <= CrosswimConstants.SwimTerminalRangeM;
            if (terminal)
            {
                // Soft depth blend: cruise → target keel over range (no snap pitch-up).
                float tgtY = aim.y;
                float minUw = Datum.LocalSeaY - 1f;
                if (tgtY > minUw)
                    tgtY = minUw;

                float span = CrosswimConstants.SwimTerminalRangeM - CrosswimConstants.SwimTerminalNearM;
                float u = span > 1f
                    ? 1f - Mathf.Clamp01((horizDist - CrosswimConstants.SwimTerminalNearM) / span)
                    : 1f;
                u = u * u * (3f - 2f * u);
                aim.y = Mathf.Lerp(cruiseY, tgtY, u);
            }
            else
                aim.y = cruiseY;

            CrosswimSwim.Apply(_missile, aim, dt, _swimThrustT, _entryHeading, _target != null, terminal);
        }

        private bool TryImpact(string tag)
        {
            if (_missile == null || _detonated)
                return false;
            if (!CrosswimImpact.ProbeAny(_missile, out RaycastHit hit, out string why, out Missile? victim))
                return false;
            string reason = string.IsNullOrEmpty(why) ? tag : why + "-" + tag;
            Boom(hit.normal.sqrMagnitude > 0.01f ? hit.normal : Vector3.up, reason, victim);
            return true;
        }

        private void Boom(Vector3 normal, string reason, Missile? victim = null)
        {
            if (_missile == null || _detonated)
                return;
            _detonated = true;
            string why = reason;
            if (_target is Ship || (_target != null && _target.GetComponentInParent<Ship>() != null))
                why = "ship-" + reason;
            CrosswimImpact.DetonateNow(_missile, normal, why, victim);
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
            CrosswimPlugin.ModLog?.LogInfo(
                $"Crosswim SoftKill phase={_phase} life={_life:F1}s y={transform.position.y:F1}");
            Object.Destroy(_missile.gameObject);
        }
    }
}
