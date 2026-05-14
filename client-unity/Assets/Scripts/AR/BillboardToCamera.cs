using UnityEngine;

namespace ARLectureTranslator.AR
{
    /// <summary>
    /// Làm label luôn hướng về camera để người dùng đọc được.
    /// </summary>
    public class BillboardToCamera : MonoBehaviour
    {
        public Transform targetCamera;

        private void LateUpdate()
        {
            if (targetCamera == null) return;

            Vector3 direction = transform.position - targetCamera.position;
            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }
}
