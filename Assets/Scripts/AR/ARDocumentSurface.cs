using UnityEngine;
using UnityEngine.XR.ARFoundation;

public sealed class ARDocumentSurface
{
    public ARDocumentSurface(
        Vector2[] imageCorners,
        Vector2[] screenCorners,
        Vector3[] worldCorners,
        Pose planePose,
        Quaternion rotation,
        ARPlane plane,
        float confidence,
        string method
    )
    {
        ImageCorners = imageCorners;
        ScreenCorners = screenCorners;
        WorldCorners = worldCorners;
        PlanePose = planePose;
        Rotation = rotation;
        Plane = plane;
        Confidence = confidence;
        Method = method;
    }

    public Vector2[] ImageCorners { get; }
    public Vector2[] ScreenCorners { get; }
    public Vector3[] WorldCorners { get; }
    public Pose PlanePose { get; }
    public Quaternion Rotation { get; }
    public ARPlane Plane { get; }
    public float Confidence { get; }
    public string Method { get; }
}
