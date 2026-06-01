using UnityEngine;

public sealed class ARDocumentSurface
{
    public ARDocumentSurface(
        Vector2[] imageCorners,
        Vector3[] worldCorners,
        Quaternion rotation,
        float confidence,
        string method
    )
    {
        ImageCorners = imageCorners;
        WorldCorners = worldCorners;
        Rotation = rotation;
        Confidence = confidence;
        Method = method;
    }

    public Vector2[] ImageCorners { get; }
    public Vector3[] WorldCorners { get; }
    public Quaternion Rotation { get; }
    public float Confidence { get; }
    public string Method { get; }
}
