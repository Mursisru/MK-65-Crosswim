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
        private float _life;
        private bool _dockShed;
        private bool _engineFx;
        private Transform? _visual;
        private Unit? _target;
        private Vector3 _lead;

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
                ShedDockImmediate();
            }
            _phaseT = 0f;
            _life = 0f;
            _engineFx = false;
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

        private static Unit? ResolveTarget(Missile missile)
        {
            if (missile.targetID.IsValid &&
                UnitRegistry.TryGetUnit(new PersistentID?(missile.targetID), out Unit t))
                return t;
            return null;
        }

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
                    ShedDockImmediate();
                    _phase = CrosswimPhase.Ballistic;
                    _phaseT = 0f;
                    TickBallistic(dt);
                    break;
                case CrosswimPhase.VlsbLoft:
                    VlsbStep(dt);
                    break;
                case CrosswimPhase.Ballistic:
                    TickBallistic(dt);
                    break;
                case CrosswimPhase.Swim:
                    TickSwim(dt);
                    break;
            }
        }

        private void ShedDockImmediate()
        {
            if (_dockShed || _visual == null)
                return;
            _dockShed = true;
            Transform? dock = CrosswimVisualParts.FindByAliases(_visual, CrosswimConstants.DockAliases);
            if (dock == null)
                return;
            Vector3 vel = _rb != null ? _rb.velocity : Vector3.zero;
            dock.SetParent(null, true);
            Rigidbody rb = dock.gameObject.GetComponent<Rigidbody>() ?? dock.gameObject.AddComponent<Rigidbody>();
            rb.mass = CrosswimConstants.DockMassKg;
            rb.drag = 0.4f;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.velocity = vel - transform.forward * CrosswimConstants.DockEjectSpeed + Vector3.down * 3f;
            Object.Destroy(dock.gameObject, CrosswimConstants.DockDestroyS);
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
                80f);
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
            if (_missile == null)
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
            _rb.velocity = v;
            _rb.useGravity = false;
            _rb.drag = 0f;
            _rb.angularDrag = CrosswimConstants.SwimAngularDrag;
            _rb.detectCollisions = false;

            CrosswimShellPrep.Arm(_missile);
            CrosswimOpening.Play(_visual);
            _phase = CrosswimPhase.Swim;
            _phaseT = 0f;
            PlayEngineFx();
            CrosswimPlugin.ModLog?.LogInfo($"Crosswim water entry y={transform.position.y:F1} sea={Datum.LocalSeaY:F1}");
        }

        private void TickSwim(float dt)
        {
            if (_rb == null || _missile == null)
                return;

            _target = CrosswimHoming.SelectTarget(_missile, _target);
            Vector3 aim = CrosswimHoming.InterceptPoint(_missile.transform.position, _rb.velocity, _target, out _lead);
            CrosswimSwim.Apply(_missile, aim, dt, _phaseT);

            if (_target != null &&
                (transform.position - _target.transform.position).sqrMagnitude <=
                CrosswimConstants.DetonateProximityM * CrosswimConstants.DetonateProximityM)
            {
                TryDetonate();
            }
        }

        private void PlayEngineFx()
        {
            if (_engineFx || _missile == null)
                return;
            _engineFx = true;
            CrosswimMotorFx.Play(_missile, _visual);
        }

        private void TryDetonate()
        {
            if (_missile == null)
                return;
            CrosswimDetonateGate.Allow = true;
            try
            {
                _missile.Detonate(transform.forward, false, false);
            }
            finally
            {
                CrosswimDetonateGate.Allow = false;
            }
        }

        private void SoftKill()
        {
            if (_missile == null)
                return;
            Object.Destroy(_missile.gameObject);
        }
    }
}
