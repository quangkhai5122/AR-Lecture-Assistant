using UnityEngine;

[DisallowMultipleComponent]
public class ARSurfaceOutlineRenderer : MonoBehaviour
{
    [SerializeField] private Color outlineColor = new Color(1f, 1f, 1f, 0.82f);
    [SerializeField] private float lineWidthMeters = 0.008f;
    [SerializeField] private bool visibleByDefault = false;

    private LineRenderer lineRenderer;

    private void Awake()
    {
        EnsureLineRenderer();
        Hide();
    }

    public void ShowSurface(Vector3[] worldCorners)
    {
        if (worldCorners == null || worldCorners.Length < 4)
        {
            Hide();
            return;
        }

        EnsureLineRenderer();
        lineRenderer.positionCount = 5;
        lineRenderer.SetPosition(0, worldCorners[0]);
        lineRenderer.SetPosition(1, worldCorners[1]);
        lineRenderer.SetPosition(2, worldCorners[2]);
        lineRenderer.SetPosition(3, worldCorners[3]);
        lineRenderer.SetPosition(4, worldCorners[0]);
        lineRenderer.enabled = true;
    }

    public void ShowLockedPose(Pose pose, float widthMeters = 0.7f, float heightMeters = 0.42f)
    {
        Vector3 right = pose.rotation * Vector3.right * Mathf.Max(0.05f, widthMeters * 0.5f);
        Vector3 surfaceUp = pose.rotation * Vector3.forward * Mathf.Max(0.05f, heightMeters * 0.5f);
        Vector3 center = pose.position;
        ShowSurface(new[]
        {
            center - right + surfaceUp,
            center + right + surfaceUp,
            center + right - surfaceUp,
            center - right - surfaceUp,
        });
    }

    public void Hide()
    {
        EnsureLineRenderer();
        lineRenderer.enabled = visibleByDefault;
    }

    private void EnsureLineRenderer()
    {
        if (lineRenderer != null) return;

        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = false;
        lineRenderer.widthMultiplier = lineWidthMeters;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.numCapVertices = 2;
        lineRenderer.startColor = outlineColor;
        lineRenderer.endColor = outlineColor;
        lineRenderer.positionCount = 0;

        if (lineRenderer.material == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                lineRenderer.material = new Material(shader);
            }
        }
    }
}
