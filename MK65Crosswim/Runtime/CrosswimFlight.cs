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
        private bool _detonated;

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
                CrosswimMass.Apply(missile, CrosswimConstants.MassKg + CrosswimConstants.VlsbDryMassKg);
            }
            else
            {
                _phase = CrosswimPhase.Drop;
                TryShedDock();
            }
            _phaseT = 0f;
            _life = 0f;
            _engineFx = false;
            _detonated = false;
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
            float alt = transform.position.y - Datum.LocalSeaY;
            _rb.AddForce(Vector3.up * CrosswimConstants.VlsbThrustMps2, ForceMode.Acceleration);
            CrosswimBallistic.AlignNose(
                _rb,
                transform,
                _rb.velocity.sqrMagnitude > 1f ? _rb.velocity : Vector3.up,
                dt,
                80f,
                levelRoll: false);
            if (alt >= CrosswimConstants.VlsbLoftAltM || _phaseT >= CrosswimConstants.VlsbMaxTimeS)
                ShedVlsb();
        }

        private void ShedVlsb()
        {
            if (_visual != null)
            {
                Transform? vlsb = CrosswimVisualParts.FindByAliases(_visual, CrosswimConstants.VlsbAliases);
                if (vlsb != null)
                {
                    Vector3 vel = _rb != null ? _rb.velocity : Vector3.zero;
                    vlsb.SetParent(null, true);
                    Rigidbody rb = vlsb.gameObject.GetComponent<Rigidbody>() ?? vlsb.gameObject.AddComponent<Rigidbody>();
                    rb.mass = CrosswimConstants.VlsbDryMassKg;
                    rb.useGravity = true;
                    rb.isKinematic = false;
                    rb.velocity = vel + Vector3.down * 2f;
                    Object.Destroy(vlsb.gameObject, 10f);
                }
            }
            if (_missile != null)
                CrosswimMass.Apply(_missile, CrosswimConstants.MassKg);
            _phase = CrosswimPhase.Ballistic;
            _phaseT = 0f;
        }

        private void TickBallistic(float dt)
        {
            if (_missile == null || _detonated)
                return;
            if (TryImpact("ballistic"))
                return;
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
            Object.Destroy(_missile.gameObject);
        }
    }
}
