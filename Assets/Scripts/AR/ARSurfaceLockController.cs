using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

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

    public event Action<ARSurfaceLockState> TrackingStateChanged;
    public event Action<Pose> SurfaceLocked;

    private ARSurfaceLockState currentState = ARSurfaceLockState.Lost;
    private Pose? lockedPose;
    private float searchStartedAt = -1f;

    public ARSurfaceLockState CurrentState => currentState;
    public bool HasLockedSurface => lockedPose.HasValue;
    public Pose LockedPose => lockedPose ?? Pose.identity;

    private void Awake()
    {
        ResolveDependencies();
        outlineRenderer?.Hide();
    }

    private void Update()
    {
        if (currentState != ARSurfaceLockState.SearchingPlane) return;

        if (raycastController != null && raycastController.TryRaycastFromCenter(out Pose hitPose))
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            LockSurface(hitPose);
            return;
        }

        if (searchStartedAt > 0f && Time.time - searchStartedAt > 4f)
        {
            TransitionTo(ARSurfaceLockState.TrackingLimited);
        }
    }

    public void BeginSearch()
    {
        ResolveDependencies();
        lockedPose = null;
        searchStartedAt = Time.time;
        outlineRenderer?.Hide();
        SetPlaneDetection(true);
        TransitionTo(ARSurfaceLockState.SearchingPlane);
    }

    public void ObservePlaneFound(ARPlane plane)
    {
        ResolveDependencies();

        if (raycastController != null && raycastController.TryRaycastFromCenter(out Pose hitPose))
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            LockSurface(hitPose);
            return;
        }

        if (plane != null)
        {
            TransitionTo(ARSurfaceLockState.PlaneFound);
            LockSurface(new Pose(plane.transform.position, plane.transform.rotation));
        }
    }

    public void LockSurface(Pose pose)
    {
        ResolveDependencies();
        lockedPose = pose;
        labelPlacer?.CachePlanePose(pose);
        outlineRenderer?.ShowLockedPose(pose);
        TransitionTo(ARSurfaceLockState.SurfaceLocked);
        SurfaceLocked?.Invoke(pose);

        if (disablePlaneDetectionAfterLock)
        {
            SetPlaneDetection(false);
        }
    }

    public void ClearLock()
    {
        lockedPose = null;
        searchStartedAt = -1f;
        outlineRenderer?.Hide();
        SetPlaneDetection(false);
        TransitionTo(ARSurfaceLockState.Lost);
    }

    public void ShowDocumentSurface(Vector3[] worldCorners)
    {
        ResolveDependencies();
        outlineRenderer?.ShowSurface(worldCorners);
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

    private void SetPlaneDetection(bool enabled)
    {
        if (planeManager == null) return;

        planeManager.enabled = enabled;
        foreach (ARPlane plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(enabled);
        }
    }

    private void TransitionTo(ARSurfaceLockState nextState)
    {
        if (currentState == nextState) return;

        currentState = nextState;
        TrackingStateChanged?.Invoke(currentState);
    }
}
