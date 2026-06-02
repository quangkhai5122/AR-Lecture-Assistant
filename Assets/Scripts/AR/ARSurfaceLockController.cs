using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public enum ARSurfaceLockState
{
    SearchingPlane,
    PlaneFound,
    SurfaceLocked,
    TrackingLimited,
    Lost
}

[DisallowMultipleComponent]
public class ARSurfaceLockController : MonoBehaviour
{
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARRaycastController raycastController;
    [SerializeField] private ARLabelPlacer labelPlacer;
    [SerializeField] private ARSurfaceOutlineRenderer outlineRenderer;
    [SerializeField] private bool disablePlaneDetectionAfterLock = false;
    [SerializeField] private PlaneDetectionMode requestedDetectionMode =
        PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
    [SerializeField] private bool preferVerticalSurfaces = false;
    [SerializeField] private float maxVerticalSurfaceNormalY = 1f;
    [SerializeField] private bool usePlaneRotationForLock = true;
    [SerializeField] private bool allowEstimatedSurfaceFallback = true;
    [SerializeField] private float estimatedSurfaceFallbackDelaySeconds = 0f;

    public event Action<ARSurfaceLockState> TrackingStateChanged;
    public event Action<Pose> SurfaceLocked;

    private ARSurfaceLockState currentState = ARSurfaceLockState.Lost;
    private Pose? lockedPose;
    private ARPlane lockedPlane;
    private float searchStartedAt = -1f;

    public ARSurfaceLockState CurrentState => currentState;
    public bool HasLockedSurface => lockedPose.HasValue;
    public Pose LockedPose => lockedPose ?? Pose.identity;
    public ARPlane LockedPlane => lockedPlane;

    private void Awake()
    {
        ResolveDependencies();
        outlineRenderer?.Hide();
    }

    private void OnEnable()
    {
        ARSession.stateChanged += OnSessionStateChanged;
    }

    private void OnDisable()
    {
        ARSession.stateChanged -= OnSessionStateChanged;
    }

    private void Update()
    {
        if (currentState != ARSurfaceLockState.SearchingPlane &&
            currentState != ARSurfaceLockState.TrackingLimited)
        {
            return;
        }

        if (raycastController != null &&
            raycastController.TryRaycastFromCenter(out Pose hitPose, out ARRaycastHit hit, IsAcceptableRaycastHit))
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            if (LockSurface(hit))
            {
                return;
            }

            RestoreSearchStateAfterRejectedLock();
        }

        if (currentState == ARSurfaceLockState.SearchingPlane &&
            searchStartedAt > 0f &&
            Time.time - searchStartedAt > 4f)
        {
            TransitionTo(ARSurfaceLockState.TrackingLimited);
        }
    }

    public void BeginSearch()
    {
        ResolveDependencies();
        lockedPose = null;
        lockedPlane = null;
        searchStartedAt = Time.time;
        outlineRenderer?.Hide();
        SetPlaneDetection(true, false);
        TransitionTo(ARSurfaceLockState.SearchingPlane);
    }

    public bool ObservePlaneFound(ARPlane plane)
    {
        ResolveDependencies();

        if (raycastController != null &&
            raycastController.TryRaycastFromCenter(out Pose hitPose, out ARRaycastHit hit, IsAcceptableRaycastHit))
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            if (LockSurface(hit, plane))
            {
                return true;
            }

            RestoreSearchStateAfterRejectedLock();
        }

        if (plane != null && IsAcceptableSurface(plane, new Pose(plane.transform.position, plane.transform.rotation)))
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            if (LockSurface(new Pose(plane.transform.position, plane.transform.rotation), plane))
            {
                return true;
            }

            RestoreSearchStateAfterRejectedLock();
        }

        return HasLockedSurface;
    }

    public bool LockSurface(Pose pose)
    {
        return LockSurface(pose, null);
    }

    public bool LockSurface(ARRaycastHit hit)
    {
        return LockSurface(hit, null);
    }

    private bool LockSurface(ARRaycastHit hit, ARPlane fallbackPlane)
    {
        ARPlane hitPlane = hit.trackable as ARPlane ?? ResolvePlane(hit.trackableId);
        if (hitPlane == null &&
            fallbackPlane != null &&
            IsAcceptableSurface(fallbackPlane, new Pose(fallbackPlane.transform.position, fallbackPlane.transform.rotation)))
        {
            hitPlane = fallbackPlane;
        }

        Pose stablePose = BuildStableSurfacePose(hit.pose, hitPlane);
        if (!IsAcceptableSurface(hitPlane, stablePose))
        {
            Debug.LogWarning("[ARSurfaceLockController] Ignored non-vertical or unstable surface lock candidate.");
            return false;
        }

        return LockSurface(stablePose, hitPlane);
    }

    public bool LockSurface(Pose pose, ARPlane plane)
    {
        ResolveDependencies();
        plane = plane != null ? plane : ResolveNearestAcceptablePlane(pose.position);
        pose = BuildStableSurfacePose(pose, plane);
        if (!IsAcceptableSurface(plane, pose))
        {
            Debug.LogWarning("[ARSurfaceLockController] Ignored non-vertical or unstable surface pose.");
            return false;
        }

        lockedPose = pose;
        lockedPlane = plane;
        labelPlacer?.CachePlanePose(pose);
        outlineRenderer?.ShowLockedPose(pose);
        Debug.Log(lockedPlane != null
            ? $"[ARSurfaceLockController] Locked surface on ARPlane {lockedPlane.trackableId}."
            : "[ARSurfaceLockController] Locked surface pose without ARPlane; anchors will use standalone mode.");
        TransitionTo(ARSurfaceLockState.SurfaceLocked);
        SurfaceLocked?.Invoke(pose);

        if (disablePlaneDetectionAfterLock)
        {
            SetPlaneDetection(false, true);
        }

        return true;
    }

    public void ClearLock()
    {
        lockedPose = null;
        lockedPlane = null;
        searchStartedAt = -1f;
        outlineRenderer?.Hide();
        SetPlaneDetection(false, false);
        TransitionTo(ARSurfaceLockState.Lost);
    }

    public void PausePlaneDetection()
    {
        ResolveDependencies();
        searchStartedAt = -1f;
        SetPlaneDetection(false, lockedPlane != null);
    }

    public void ResumePlaneDetection()
    {
        ResolveDependencies();

        if (lockedPose.HasValue)
        {
            if (disablePlaneDetectionAfterLock)
            {
                SetPlaneDetection(false, lockedPlane != null);
            }
            else
            {
                SetPlaneDetection(true, lockedPlane != null);
            }

            TransitionTo(ARSurfaceLockState.SurfaceLocked);
            return;
        }

        BeginSearch();
    }

    public void ShowDocumentSurface(Vector3[] worldCorners)
    {
        ResolveDependencies();
        outlineRenderer?.ShowSurface(worldCorners);
    }

    private void RestoreSearchStateAfterRejectedLock()
    {
        if (lockedPose.HasValue)
        {
            TransitionTo(ARSurfaceLockState.SurfaceLocked);
            return;
        }

        bool timedOut = searchStartedAt > 0f && Time.time - searchStartedAt > 4f;
        TransitionTo(timedOut ? ARSurfaceLockState.TrackingLimited : ARSurfaceLockState.SearchingPlane);
    }

    private void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        if (args.state == ARSessionState.SessionTracking)
        {
            if (currentState == ARSurfaceLockState.TrackingLimited ||
                (currentState == ARSurfaceLockState.Lost && lockedPose.HasValue))
            {
                TransitionTo(lockedPose.HasValue
                    ? ARSurfaceLockState.SurfaceLocked
                    : ARSurfaceLockState.SearchingPlane);
            }
            return;
        }

        if (currentState == ARSurfaceLockState.SearchingPlane ||
            currentState == ARSurfaceLockState.PlaneFound)
        {
            TransitionTo(ARSurfaceLockState.TrackingLimited);
            return;
        }

        if (currentState == ARSurfaceLockState.SurfaceLocked)
        {
            TransitionTo(ARSurfaceLockState.TrackingLimited);
        }
    }

    private void ResolveDependencies()
    {
        if (planeManager == null) planeManager = FindAnyObjectByType<ARPlaneManager>();
        if (raycastController == null) raycastController = FindAnyObjectByType<ARRaycastController>();
        if (labelPlacer == null) labelPlacer = FindAnyObjectByType<ARLabelPlacer>();
        if (outlineRenderer == null)
        {
            outlineRenderer = FindAnyObjectByType<ARSurfaceOutlineRenderer>();
            if (outlineRenderer == null)
            {
                outlineRenderer = gameObject.AddComponent<ARSurfaceOutlineRenderer>();
            }
        }
    }

    private void SetPlaneDetection(bool enabled, bool preserveLockedPlane)
    {
        if (planeManager == null) return;
        bool shouldPreserveLockedPlane = preserveLockedPlane && lockedPlane != null;

        if (enabled)
        {
            planeManager.requestedDetectionMode = requestedDetectionMode;
        }
        else
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.None;
        }

        planeManager.enabled = enabled || shouldPreserveLockedPlane;
        foreach (ARPlane plane in planeManager.trackables)
        {
            bool keepLockedPlane = shouldPreserveLockedPlane && plane == lockedPlane;
            plane.gameObject.SetActive(enabled || keepLockedPlane);
            SetPlaneVisualsVisible(plane, enabled);
        }
    }

    private ARPlane ResolvePlane(TrackableId trackableId)
    {
        ResolveDependencies();
        if (planeManager == null) return null;

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane != null && plane.trackableId == trackableId)
            {
                return plane;
            }
        }

        return null;
    }

    private ARPlane ResolveNearestAcceptablePlane(Vector3 position)
    {
        if (planeManager == null) return null;

        ARPlane nearest = null;
        float nearestDistanceSquared = float.PositiveInfinity;
        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane == null) continue;
            if (!IsAcceptableSurface(plane, new Pose(plane.transform.position, plane.transform.rotation))) continue;

            float distanceSquared = (plane.transform.position - position).sqrMagnitude;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = plane;
            }
        }

        return nearest;
    }

    private bool IsAcceptableRaycastHit(ARRaycastHit hit)
    {
        ARPlane hitPlane = hit.trackable as ARPlane ?? ResolvePlane(hit.trackableId);
        Pose stablePose = BuildStableSurfacePose(hit.pose, hitPlane);
        return IsAcceptableSurface(hitPlane, stablePose);
    }

    private bool IsAcceptableSurface(ARPlane plane, Pose pose)
    {
        if (!preferVerticalSurfaces)
        {
            return true;
        }

        if (plane == null && allowEstimatedSurfaceFallback && !CanUseEstimatedSurfaceFallback())
        {
            return false;
        }

        Vector3 normal = plane != null
            ? plane.transform.up
            : pose.rotation * Vector3.up;
        if (normal.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        normal.Normalize();
        return Mathf.Abs(normal.y) <= Mathf.Clamp01(maxVerticalSurfaceNormalY);
    }

    private Pose BuildStableSurfacePose(Pose pose, ARPlane plane)
    {
        if (plane == null)
        {
            return TryBuildCameraFacingVerticalPose(pose.position, out Pose fallbackPose)
                ? fallbackPose
                : pose;
        }

        if (!usePlaneRotationForLock)
        {
            return pose;
        }

        return new Pose(pose.position, plane.transform.rotation);
    }

    private bool CanUseEstimatedSurfaceFallback()
    {
        if (!allowEstimatedSurfaceFallback) return false;
        if (searchStartedAt <= 0f) return false;

        return Time.time - searchStartedAt >= Mathf.Max(0f, estimatedSurfaceFallbackDelaySeconds);
    }

    private bool TryBuildCameraFacingVerticalPose(Vector3 position, out Pose pose)
    {
        pose = Pose.identity;

        Camera camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        Vector3 normal = camera.transform.position - position;
        normal = Vector3.ProjectOnPlane(normal, Vector3.up);
        if (normal.sqrMagnitude < 0.000001f)
        {
            normal = Vector3.ProjectOnPlane(-camera.transform.forward, Vector3.up);
        }

        if (normal.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        normal.Normalize();
        pose = new Pose(position, Quaternion.LookRotation(Vector3.up, normal));
        return true;
    }

    private static void SetPlaneVisualsVisible(ARPlane plane, bool visible)
    {
        if (plane == null) return;

        foreach (Renderer renderer in plane.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }

        foreach (LineRenderer lineRenderer in plane.GetComponentsInChildren<LineRenderer>(true))
        {
            lineRenderer.enabled = visible;
        }
    }

    private void TransitionTo(ARSurfaceLockState nextState)
    {
        if (currentState == nextState) return;

        currentState = nextState;
        TrackingStateChanged?.Invoke(currentState);
    }
}
