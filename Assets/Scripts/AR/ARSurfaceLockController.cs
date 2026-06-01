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
    [SerializeField] private bool disablePlaneDetectionAfterLock = true;
    [SerializeField] private PlaneDetectionMode requestedDetectionMode =
        PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;

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

        if (raycastController != null && raycastController.TryRaycastFromCenter(out Pose hitPose, out ARRaycastHit hit))
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            LockSurface(hit);
            return;
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

    public void ObservePlaneFound(ARPlane plane)
    {
        ResolveDependencies();

        if (raycastController != null && raycastController.TryRaycastFromCenter(out Pose hitPose, out ARRaycastHit hit))
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            LockSurface(hit, plane);
            return;
        }

        if (plane != null)
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            LockSurface(new Pose(plane.transform.position, plane.transform.rotation), plane);
        }
    }

    public void LockSurface(Pose pose)
    {
        LockSurface(pose, null);
    }

    public void LockSurface(ARRaycastHit hit)
    {
        LockSurface(hit, null);
    }

    private void LockSurface(ARRaycastHit hit, ARPlane fallbackPlane)
    {
        ARPlane hitPlane = hit.trackable as ARPlane ?? ResolvePlane(hit.trackableId) ?? fallbackPlane;
        LockSurface(hit.pose, hitPlane);
    }

    public void LockSurface(Pose pose, ARPlane plane)
    {
        ResolveDependencies();
        plane = plane != null ? plane : ResolveNearestPlane(pose.position);
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

    private ARPlane ResolveNearestPlane(Vector3 position)
    {
        if (planeManager == null) return null;

        ARPlane nearest = null;
        float nearestDistanceSquared = float.PositiveInfinity;
        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane == null) continue;

            float distanceSquared = (plane.transform.position - position).sqrMagnitude;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = plane;
            }
        }

        return nearest;
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
