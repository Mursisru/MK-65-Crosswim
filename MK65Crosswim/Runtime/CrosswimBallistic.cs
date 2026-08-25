using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Air: weathercock. Swim: level roll (dorsal / DockingPlace side = world up).
    /// </summary>
    internal static class CrosswimBallistic
    {
        internal static void Apply(Missile missile, float dt)
        {
            if (missile?.rb == null || dt <= 0f)
                return;

            Rigidbody rb = missile.rb;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.detectCollisions = false;
            rb.drag = CrosswimConstants.BallisticDrag;
            rb.angularDrag = CrosswimConstants.BallisticAngularDrag;

            if (rb.angularVelocity.sqrMagnitude >
                CrosswimConstants.BallisticMaxAngVelRad * CrosswimConstants.BallisticMaxAngVelRad)
                rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, CrosswimConstants.BallisticMaxAngVelRad);
            rb.angularVelocity *= CrosswimConstants.BallisticAngVelDamp;

            if (rb.velocity.sqrMagnitude >= 4f)
                AlignNose(rb, missile.transform, rb.velocity, dt, CrosswimConstants.BallisticAlignDegS, false);
        }

        internal static void AlignNose(
            Rigidbody rb,
            Transform xform,
            Vector3 dir,
            float dt,
            float degPerSec,
            bool levelRoll)
        {
            if (rb == null || xform == null || dt <= 0f)
                return;
            if (dir.sqrMagnitude < 1e-6f)
                return;

            Vector3 n = dir.normalized;
            Vector3 up;
            if (levelRoll)
            {
                // Keep dorsal (DockingPlace / former DockingPort side) toward world up.
                up = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(n, up)) > 0.95f)
                    up = Mathf.Abs(Vector3.Dot(n, Vector3.forward)) < 0.9f ? Vector3.forward : Vector3.right;
            }
            else
            {
                up = xform.up;
                if (Mathf.Abs(Vector3.Dot(n, up)) > 0.92f)
                    up = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(n, up)) > 0.92f)
                    up = xform.right;
            }

            Quaternion want = Quaternion.LookRotation(n, up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, want, degPerSec * dt));
        }
    }
}
