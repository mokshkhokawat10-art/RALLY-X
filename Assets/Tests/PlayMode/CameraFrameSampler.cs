using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>One rendered frame's camera-versus-car measurements.</summary>
    public readonly struct CameraFrame
    {
        public readonly float HorizontalDistance;
        public readonly float HeightAboveCar;
        public readonly float DistanceToCar;

        /// <summary>-1 when the camera is directly behind the car's heading, +1 when directly in front.</summary>
        public readonly float BehindDot;

        /// <summary>Camera yaw relative to the car's heading, degrees.</summary>
        public readonly float YawOffset;

        /// <summary>
        /// How far, in degrees, the camera looks outside the arc between the car's nose and its direction of travel.
        /// 0 means it is aimed somewhere between the two.
        /// </summary>
        public readonly float YawOutsideTravelArc;

        public readonly float FieldOfView;
        public readonly bool CarInView;
        public readonly bool InsideCollider;

        public CameraFrame(float horizontalDistance, float heightAboveCar, float distanceToCar, float behindDot, float yawOffset,
            float yawOutsideTravelArc, float fieldOfView, bool carInView, bool insideCollider)
        {
            HorizontalDistance = horizontalDistance;
            HeightAboveCar = heightAboveCar;
            DistanceToCar = distanceToCar;
            BehindDot = behindDot;
            YawOffset = yawOffset;
            YawOutsideTravelArc = yawOutsideTravelArc;
            FieldOfView = fieldOfView;
            CarInView = carInView;
            InsideCollider = insideCollider;
        }
    }

    /// <summary>
    /// Measures the camera against the car every frame, after the camera has moved. Sampling from test
    /// coroutines instead would compare a camera pose that is a frame older than the car's.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class CameraFrameSampler : MonoBehaviour
    {
        private const float CollisionProbeRadius = 0.1f;

        // Below this the direction of travel is too noisy to judge the camera against.
        private const float MinSpeedForTravelArc = 5f;

        private Camera sampledCamera;
        private VehicleController car;
        private float lookHeight;

        public CameraFrame Latest { get; private set; }
        public int Frames { get; private set; }
        public int FramesCarOutOfView { get; private set; }
        public int FramesInsideCollider { get; private set; }
        public float MaxHorizontalDistance { get; private set; }
        public float MinDistanceToCar { get; private set; }
        public float WorstBehindDot { get; private set; }
        public float MaxAbsYawOffset { get; private set; }
        public float MaxYawOutsideTravelArc { get; private set; }

        public void Initialize(Camera cameraToSample, VehicleController target, float aimHeight)
        {
            sampledCamera = cameraToSample;
            car = target;
            lookHeight = aimHeight;
            ResetStats();
        }

        public void ResetStats()
        {
            Frames = 0;
            FramesCarOutOfView = 0;
            FramesInsideCollider = 0;
            MaxHorizontalDistance = 0f;
            MinDistanceToCar = float.MaxValue;
            WorstBehindDot = -1f;
            MaxAbsYawOffset = 0f;
            MaxYawOutsideTravelArc = 0f;
        }

        private void LateUpdate()
        {
            if (sampledCamera == null || car == null)
                return;

            var frame = Measure();
            Latest = frame;
            Frames++;
            if (!frame.CarInView)
                FramesCarOutOfView++;
            if (frame.InsideCollider)
                FramesInsideCollider++;
            MaxHorizontalDistance = Mathf.Max(MaxHorizontalDistance, frame.HorizontalDistance);
            MinDistanceToCar = Mathf.Min(MinDistanceToCar, frame.DistanceToCar);
            WorstBehindDot = Mathf.Max(WorstBehindDot, frame.BehindDot);
            MaxAbsYawOffset = Mathf.Max(MaxAbsYawOffset, Mathf.Abs(frame.YawOffset));
            MaxYawOutsideTravelArc = Mathf.Max(MaxYawOutsideTravelArc, frame.YawOutsideTravelArc);
        }

        private CameraFrame Measure()
        {
            Transform cameraTransform = sampledCamera.transform;
            Transform carTransform = car.transform;
            Vector3 cameraPosition = cameraTransform.position;
            Vector3 carPosition = carTransform.position;
            Vector3 heading = Vector3.ProjectOnPlane(carTransform.forward, Vector3.up).normalized;
            Vector3 flatOffset = Vector3.ProjectOnPlane(cameraPosition - carPosition, Vector3.up);
            Vector3 cameraForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);

            Vector3 viewport = sampledCamera.WorldToViewportPoint(carPosition + Vector3.up * 0.7f);
            bool carInView = viewport.z > 0f && viewport.x is > 0f and < 1f && viewport.y is > 0f and < 1f;

            // All layers, including the car's own: the camera must never sit inside the vehicle either.
            bool insideCollider = Physics.CheckSphere(cameraPosition, CollisionProbeRadius, ~0, QueryTriggerInteraction.Ignore);

            float yawOffset = Vector3.SignedAngle(heading, cameraForward, Vector3.up);
            float yawOutsideTravelArc = 0f;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(car.Body.linearVelocity, Vector3.up);
            if (planarVelocity.magnitude >= MinSpeedForTravelArc)
            {
                float travelOffset = Vector3.SignedAngle(heading, planarVelocity, Vector3.up);
                float arcMin = Mathf.Min(0f, travelOffset);
                float arcMax = Mathf.Max(0f, travelOffset);
                yawOutsideTravelArc = Mathf.Max(0f, yawOffset - arcMax, arcMin - yawOffset);
            }

            return new CameraFrame(
                flatOffset.magnitude,
                cameraPosition.y - carPosition.y,
                Vector3.Distance(cameraPosition, carPosition + Vector3.up * lookHeight),
                Vector3.Dot(flatOffset.normalized, heading),
                yawOffset,
                yawOutsideTravelArc,
                sampledCamera.fieldOfView,
                carInView,
                insideCollider);
        }
    }
}
