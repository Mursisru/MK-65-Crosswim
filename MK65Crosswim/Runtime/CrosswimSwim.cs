using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// MK-88 pattern: thrust along forward, fin torque toward assigned aim.
    /// Weathercock uses flattened flow + entry heading — never LookRotation on near-vertical vel (180° flip).
    /// </summary>
    internal static class CrosswimSwim
    {
        internal static void Apply(
            Missile missile,
            Vector3 aim,
            float dt,
            float thrustTimeS,
            Vector3 entryHeading,
            bool hasAssignedTarget)
        {
            if (missile?.rb == null || dt <= 0f)
                return;

            Rigidbody rb = missile.rb;
            Transform xform = missile.transform;
            Vector3 pos = xform.position;
            float targetY = Datum.LocalSeaY - CrosswimConstants.SwimDepthM;
            bool bleeding = thrustTimeS < 0f;

            rb.useGravity = false;
            rb.isKinematic = false;
            rb.detectCollisions = false;
            rb.drag = 0f;
            rb.angularDrag = CrosswimConstants.SwimAngularDrag;

            Vector3 vel = rb.velocity;
            float speed = vel.magnitude;
            Vector3 forward = xform.forward;
            Vector3 heading = Horiz(entryHeading);
            if (heading.sqrMagnitude < 0.01f)
                heading = Horiz(forward);
            if (heading.sqrMagnitude < 0.01f)
                heading = Vector3.forward;

            float linearDrag = bleeding
                ? CrosswimConstants.SwimBleedLinearDrag
                : CrosswimConstants.SwimLinearDrag;
            if (speed > 0.05f)
            {
                float q = 0.5f * CrosswimConstants.WaterDensity * speed * speed;
                rb.AddForce(-vel.normalized * (q * CrosswimConstants.SwimCdArea), ForceMode.Force);
                if (linearDrag > 0f)
                    rb.AddForce(-vel.normalized * (linearDrag * speed), ForceMode.Force);
            }

            Vector3 localVel = xform.InverseTransformDirection(vel);
            rb.AddForce(xform.TransformDirection(new Vector3(
                -localVel.x * CrosswimConstants.SwimSideDamp,
                -localVel.y * CrosswimConstants.SwimHeaveDamp,
                0f)), ForceMode.Acceleration);

            float depthErr = targetY - pos.y;
            float buoyancy = bleeding
                ? CrosswimConstants.SwimBuoyancyGain * 0.35f
                : CrosswimConstants.SwimBuoyancyGain;
            rb.AddForce(Vector3.up * (depthErr * buoyancy), ForceMode.Acceleration);

            Vector3 to = aim - pos;
            Vector3 horiz = Horiz(to);
            Vector3 wantDir;
            if (horiz.sqrMagnitude > 1f)
            {
                wantDir = horiz.normalized;
                wantDir.y = Mathf.Clamp(depthErr * 0.08f, -0.35f, 0.35f);
                wantDir.Normalize();
            }
            else if (hasAssignedTarget)
            {
                // Close to aim in XY — keep depth chase, hold current yaw.
                wantDir = Horiz(forward).sqrMagnitude > 0.01f ? Horiz(forward).normalized : heading;
                wantDir.y = Mathf.Clamp(depthErr * 0.08f, -0.35f, 0.35f);
                wantDir.Normalize();
            }
            else
            {
                wantDir = heading;
                wantDir.y = Mathf.Clamp(depthErr * 0.05f, -0.25f, 0.25f);
                wantDir.Normalize();
            }

            if (!bleeding)
            {
                float ramp = CrosswimConstants.SwimThrustRampS > 0.01f
                    ? Mathf.SmoothStep(0f, 1f, thrustTimeS / CrosswimConstants.SwimThrustRampS)
                    : 1f;
                rb.AddForce(forward * (CrosswimConstants.SwimPropThrustN * ramp), ForceMode.Force);

                float dynQ = 0.5f * CrosswimConstants.WaterDensity * Mathf.Max(speed, 2f) * Mathf.Max(speed, 2f);
                Vector3 axis = Vector3.Cross(forward, wantDir);
                float sinAng = Mathf.Clamp(axis.magnitude, 0f, 1f);
                if (sinAng > 0.001f)
                {
                    axis /= sinAng;
                    float fin = dynQ * CrosswimConstants.SwimFinAuthority * sinAng;
                    rb.AddTorque(axis * fin, ForceMode.Acceleration);
                }
            }

            // Flattened weathercock — vertical dive must not invent a random yaw.
            if (speed > 2f)
            {
                Vector3 flatVel = Horiz(vel);
                Vector3 flowFlat = flatVel.sqrMagnitude > 0.05f ? flatVel.normalized : heading;
                Vector3 flow = flowFlat;
                flow.y = Mathf.Clamp(vel.y / Mathf.Max(speed, 0.01f), -0.35f, 0.2f);
                if (flow.sqrMagnitude > 1e-4f)
                    flow.Normalize();
                else
                    flow = heading;

                Quaternion flowRot = Quaternion.LookRotation(flow, Vector3.up);
                float align = (bleeding
                    ? CrosswimConstants.SwimBleedAlignDegS
                    : CrosswimConstants.SwimAlignDegS) * dt * 0.35f;
                rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, flowRot, align));
            }

            float maxW = CrosswimConstants.SwimMaxAngVelRad;
            if (bleeding)
                maxW *= 0.45f;
            if (rb.angularVelocity.sqrMagnitude > maxW * maxW)
                rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, maxW);

            if (!bleeding && rb.velocity.magnitude > CrosswimConstants.SwimSpeedMps)
                rb.velocity = rb.velocity.normalized * CrosswimConstants.SwimSpeedMps;
        }

        private static Vector3 Horiz(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
