using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Underwater: rb.drag=0, quadratic drag as Force, thrust along forward, level to aim.
    /// </summary>
    internal static class CrosswimSwim
    {
        internal static void Apply(Missile missile, Vector3 aim, float dt, float swimTimeS)
        {
            if (missile?.rb == null || dt <= 0f)
                return;

            Rigidbody rb = missile.rb;
            Transform xform = missile.transform;
            Vector3 pos = xform.position;
            float targetY = Datum.LocalSeaY - CrosswimConstants.SwimDepthM;

            rb.useGravity = false;
            rb.isKinematic = false;
            rb.detectCollisions = false;
            rb.drag = 0f;
            rb.angularDrag = CrosswimConstants.SwimAngularDrag;

            Vector3 vel = rb.velocity;
            float speed = vel.magnitude;
            Vector3 forward = xform.forward;

            if (speed > 0.05f)
            {
                float q = 0.5f * CrosswimConstants.WaterDensity * speed * speed;
                rb.AddForce(-vel.normalized * (q * CrosswimConstants.SwimCdArea), ForceMode.Force);
                if (CrosswimConstants.SwimLinearDrag > 0f)
                    rb.AddForce(-vel.normalized * (CrosswimConstants.SwimLinearDrag * speed), ForceMode.Force);
            }

            Vector3 localVel = xform.InverseTransformDirection(vel);
            Vector3 dampLocal = new Vector3(
                -localVel.x * CrosswimConstants.SwimSideDamp,
                -localVel.y * CrosswimConstants.SwimHeaveDamp,
                0f);
            rb.AddForce(xform.TransformDirection(dampLocal), ForceMode.Acceleration);

            float depthErr = targetY - pos.y;
            rb.AddForce(Vector3.up * (depthErr * CrosswimConstants.SwimBuoyancyGain), ForceMode.Acceleration);

            Vector3 to = aim - pos;
            Vector3 wantDir;
            if (to.sqrMagnitude > 0.01f)
            {
                wantDir = to.normalized;
                wantDir.y = Mathf.Clamp(wantDir.y + depthErr * 0.08f, -0.55f, 0.55f);
                if (wantDir.sqrMagnitude > 0.01f)
                    wantDir.Normalize();
            }
            else
                wantDir = forward;

            float ramp = CrosswimConstants.SwimThrustRampS > 0.01f
                ? Mathf.SmoothStep(0f, 1f, swimTimeS / CrosswimConstants.SwimThrustRampS)
                : 1f;
            rb.AddForce(forward * (CrosswimConstants.SwimPropThrustN * ramp), ForceMode.Force);

            float dynQ = 0.5f * CrosswimConstants.WaterDensity * Mathf.Max(speed, 2f) * Mathf.Max(speed, 2f);
            Vector3 axis = Vector3.Cross(forward, wantDir);
            float sinAng = Mathf.Clamp(axis.magnitude, 0f, 1f);
            if (sinAng > 0.001f)
            {
                axis /= sinAng;
                rb.AddTorque(axis * (dynQ * CrosswimConstants.SwimFinAuthority * sinAng), ForceMode.Acceleration);
            }

            CrosswimBallistic.AlignNose(rb, xform, wantDir, dt, CrosswimConstants.SwimAlignDegS);

            if (rb.velocity.magnitude > CrosswimConstants.SwimSpeedMps)
                rb.velocity = rb.velocity.normalized * CrosswimConstants.SwimSpeedMps;

            float maxW = CrosswimConstants.SwimMaxAngVelRad;
            if (rb.angularVelocity.sqrMagnitude > maxW * maxW)
                rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, maxW);
        }
    }
}
