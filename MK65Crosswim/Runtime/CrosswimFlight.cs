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

    internal sealed class CrosswimFlight : MonoBehaviour
    {
        private Missile? _missile;
        private Rigidbody? _rb;
        private CrosswimPhase _phase;
        private float _phaseT;
        private float _life;
        private bool _dockShed;
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
                    BallisticStep(dt);
                    break;
                case CrosswimPhase.VlsbLoft:
                    VlsbStep(dt);
                    break;
                case CrosswimPhase.Ballistic:
                    BallisticStep(dt);
                    break;
                case CrosswimPhase.Swim:
                    SwimStep(dt);
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
            rb.velocity = vel - transform.forward * CrosswimConstants.DockEjectSpeed + Vector3.down * 3f;
            Object.Destroy(dock.gameObject, CrosswimConstants.DockDestroyS);
        }

        private void VlsbStep(float dt)
        {
            if (_rb == null)
                return;
            float alt = transform.position.y - Datum.LocalSeaY;
            _rb.AddForce(Vector3.up * CrosswimConstants.VlsbThrustMps2, ForceMode.Acceleration);
            AlignTo(_rb.velocity.sqrMagnitude > 1f ? _rb.velocity.normalized : Vector3.up, dt, 80f);
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
                    rb.velocity = vel + Vector3.down * 2f;
                    Object.Destroy(vlsb.gameObject, 10f);
                }
            }
            if (_missile != null)
                CrosswimMass.Apply(_missile, CrosswimConstants.MassKg);
            _phase = CrosswimPhase.Ballistic;
            _phaseT = 0f;
        }

        private void BallisticStep(float dt)
        {
            if (_rb == null)
                return;
            _rb.useGravity = true;
            _rb.drag = CrosswimConstants.BallisticDrag;
            _rb.angularDrag = CrosswimConstants.BallisticAngularDrag;
            if (_rb.velocity.sqrMagnitude > 0.05f)
                AlignTo(_rb.velocity.normalized, dt, CrosswimConstants.BallisticAlignDegS);
            if (transform.position.y <= Datum.LocalSeaY + CrosswimConstants.WaterEntrySubmergeM)
                EnterWater();
        }

        private void EnterWater()
        {
            if (_rb == null || _missile == null)
                return;
            Vector3 v = _rb.velocity;
            v.y = Mathf.Min(v.y, -2f);
            _rb.velocity = v;
            _rb.useGravity = false;
            CrosswimShellPrep.Arm(_missile);
            CrosswimOpening.Play(_visual);
            _phase = CrosswimPhase.Swim;
            _phaseT = 0f;
            PlayEngineFx();
        }

        private void SwimStep(float dt)
        {
            if (_rb == null || _missile == null)
                return;
            _target = CrosswimHoming.SelectTarget(_missile, _target);
            Vector3 aim = CrosswimHoming.InterceptPoint(_missile.transform.position, _rb.velocity, _target, out _lead);
            Vector3 to = aim - transform.position;
            if (to.sqrMagnitude < 0.01f)
                to = transform.forward;
            Vector3 dir = to.normalized;
            float depth = Datum.LocalSeaY - CrosswimConstants.SwimDepthM;
            if (transform.position.y > depth)
                dir = (dir + Vector3.down * 0.35f).normalized;

            float ramp = Mathf.Clamp01(_phaseT / CrosswimConstants.SwimThrustRampS);
            float speed = CrosswimConstants.SwimSpeedMps * ramp;
            Vector3 want = dir * speed;
            _rb.velocity = Vector3.MoveTowards(_rb.velocity, want, CrosswimConstants.SwimLinearDrag * dt * 10f);
            if (_rb.velocity.magnitude > CrosswimConstants.SwimSpeedMps)
                _rb.velocity = _rb.velocity.normalized * CrosswimConstants.SwimSpeedMps;
            AlignTo(dir, dt, CrosswimConstants.SwimTurnRateDeg);

            if (_target != null &&
                (transform.position - _target.transform.position).sqrMagnitude <=
                CrosswimConstants.DetonateProximityM * CrosswimConstants.DetonateProximityM)
            {
                TryDetonate();
            }
        }

        private void AlignTo(Vector3 dir, float dt, float degS)
        {
            if (dir.sqrMagnitude < 1e-6f)
                return;
            Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, degS * dt);
            if (_rb != null)
                _rb.angularVelocity *= 0.5f;
        }

        private void PlayEngineFx()
        {
            if (_visual == null)
                return;
            Transform? spawn = CrosswimVisualParts.FindByAliases(_visual, CrosswimConstants.MainEngineAliases);
            if (spawn == null)
                return;
            ParticleSystem[] ps = GetComponentsInChildren<ParticleSystem>(true);
            if (ps.Length == 0)
                return;
            ParticleSystem clone = Instantiate(ps[0], spawn.position, spawn.rotation, spawn);
            clone.Play(true);
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
