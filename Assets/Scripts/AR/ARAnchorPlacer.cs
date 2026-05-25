// ARAnchorPlacer.cs
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class ARAnchorPlacer : MonoBehaviour
{
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARRaycastController raycastController;

    /// <summary>
    /// Tạo anchor tại vị trí raycast hit.
    /// Anchor giúp text bám ổn định khi di chuyển điện thoại.
    /// </summary>
    public ARAnchor PlaceAnchor(Pose pose)
    {
        // Tạo GameObject rồi thêm ARAnchor component
        GameObject anchorGO = new GameObject("TranslationAnchor");
        anchorGO.transform.SetPositionAndRotation(pose.position, pose.rotation);

        ARAnchor anchor = anchorGO.AddComponent<ARAnchor>();
        return anchor;
    }
}