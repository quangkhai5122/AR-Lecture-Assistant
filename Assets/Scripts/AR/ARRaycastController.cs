// ARRaycastController.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARRaycastController : MonoBehaviour
{
    [SerializeField] private ARRaycastManager raycastManager;

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();

    // Mở rộng trackable types: polygon + bounds + estimated → hit rate tăng mạnh
    private const TrackableType AllPlaneTypes =
        TrackableType.PlaneWithinPolygon |
        TrackableType.PlaneWithinBounds |
        TrackableType.PlaneEstimated;

    /// <summary>
    /// Thực hiện raycast từ vị trí screen vào các plane đã detect
    /// Thử polygon trước (chính xác nhất), rồi fallback sang bounds + estimated
    /// </summary>
    public bool TryRaycast(Vector2 screenPosition, out Pose hitPose)
    {
        hitPose = Pose.identity;

        if (raycastManager == null)
            raycastManager = FindAnyObjectByType<ARRaycastManager>();
        if (raycastManager == null) return false;

        // Thử chính xác nhất trước
        if (raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            hitPose = hits[0].pose;
            return true;
        }

        // Fallback: bounds + estimated (phạm vi lớn hơn)
        if (raycastManager.Raycast(screenPosition, hits, AllPlaneTypes))
        {
            hitPose = hits[0].pose;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Raycast từ trung tâm màn hình
    /// </summary>
    public bool TryRaycastFromCenter(out Pose hitPose)
    {
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        return TryRaycast(center, out hitPose);
    }
}