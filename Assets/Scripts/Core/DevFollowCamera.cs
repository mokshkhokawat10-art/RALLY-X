using UnityEngine;

namespace RallyX.Core
{
    /// <summary>
    /// Minimal follow camera for testing vehicle handling in the Sandbox.
    /// Temporary scaffolding: replaced by the chase camera in Phase 3.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(0f)] private float distance = 7.5f;
        [SerializeField] private float height = 2.8f;
        [SerializeField] private float lookHeight = 1.1f;
        [SerializeField, Min(0f)] private float positionSharpness = 6f;
        [SerializeField, Min(0f)] private float rotationSharpness = 10f;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            Vector3 heading = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (heading.sqrMagnitude < 1e-4f)
                return;

            float deltaTime = Time.deltaTime;
            Vector3 desiredPosition = target.position - heading.normalized * distance + Vector3.up * height;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, 1f - Mathf.Exp(-positionSharpness * deltaTime));

            Quaternion desiredRotation = Quaternion.LookRotation(target.position + Vector3.up * lookHeight - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotationSharpness * deltaTime));
        }
    }
}
