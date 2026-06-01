// ARAnchorPlacer.cs
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARAnchorPlacer : MonoBehaviour
{
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARRaycastController raycastController;

    /// <summary>
    /// Tạo anchor tại vị trí raycast hit.
    /// Anchor giúp text bám ổn định khi di chuyển điện thoại.
    /// </summary>
    public ARAnchor PlaceAnchor(Pose pose)
    {
        return PlaceAnchor(pose, null);
    }

    public ARAnchor PlaceAnchor(ARRaycastHit hit)
    {
        ARPlane hitPlane = hit.trackable as ARPlane ?? ResolvePlane(hit.trackableId);
        return PlaceAnchor(hit.pose, hitPlane);
    }

    public ARAnchor PlaceAnchor(Pose pose, ARPlane plane)
    {
        ResolveDependencies();

        ARAnchor attachedAnchor = TryAttachToPlane(plane, pose);
        if (attachedAnchor != null)
        {
            return attachedAnchor;
        }

        return CreateStandaloneAnchor(pose);
    }

    private void ResolveDependencies()
    {
        if (anchorManager == null)
        {
            anchorManager = FindAnyObjectByType<ARAnchorManager>();
        }

        if (planeManager == null)
        {
            planeManager = FindAnyObjectByType<ARPlaneManager>();
        }

        if (raycastController == null)
        {
            raycastController = FindAnyObjectByType<ARRaycastController>();
        }
    }

    private ARPlane ResolvePlane(TrackableId trackableId)
    {
        ResolveDependencies();
        if (planeManager == null)
        {
            return null;
        }

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane != null && plane.trackableId == trackableId)
            {
                return plane;
            }
        }

        return null;
    }

    private ARAnchor TryAttachToPlane(ARPlane plane, Pose pose)
    {
        if (anchorManager == null || plane == null)
        {
            return null;
        }

        try
        {
            ARAnchor anchor = anchorManager.AttachAnchor(plane, pose);
            if (anchor != null)
            {
                anchor.gameObject.name = "TranslationPlaneAnchor";
                Debug.Log($"[ARAnchorPlacer] Attached anchor to ARPlane {plane.trackableId}.");
            }
            return anchor;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ARAnchorPlacer] Could not attach anchor to ARPlane; using standalone anchor. {ex.Message}");
            return null;
        }
    }

    private ARAnchor CreateStandaloneAnchor(Pose pose)
    {
        if (anchorManager == null)
        {
            Debug.LogWarning("[ARAnchorPlacer] ARAnchorManager not found; cannot create a real AR anchor.");
            return null;
        }

        try
        {
#pragma warning disable CS0618
            ARAnchor anchor = anchorManager.AddAnchor(pose);
#pragma warning restore CS0618
            if (anchor == null)
            {
                Debug.LogWarning("[ARAnchorPlacer] ARAnchorManager rejected standalone anchor.");
                return null;
            }

            anchor.gameObject.name = "TranslationStandaloneAnchor";
            Debug.Log("[ARAnchorPlacer] Created standalone AR anchor.");
            return anchor;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ARAnchorPlacer] Could not create standalone AR anchor. {ex.Message}");
            return null;
        }
    }
}
