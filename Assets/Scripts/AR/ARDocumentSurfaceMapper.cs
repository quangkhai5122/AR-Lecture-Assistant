using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public sealed class ARDocumentSurfaceMapper
{
    private const float MaxProjectedSurfaceSideMeters = 6.0f;
    private const float MaxProjectedSurfaceAreaMeters = 18.0f;
    private const float MaxProjectedSurfaceAspectRatio = 12.0f;

    private readonly ARDocumentSurface surface;
    private readonly double[] imageToUvHomography;

    private ARDocumentSurfaceMapper(ARDocumentSurface surface, double[] imageToUvHomography)
    {
        this.surface = surface;
        this.imageToUvHomography = imageToUvHomography;
    }

    public ARDocumentSurface Surface => surface;
    public Vector3[] WorldCorners => surface.WorldCorners;

    public static ARDocumentSurfaceMapper TryCreate(
        PipelineResponse response,
        ARRaycastController raycastController,
        Func<Vector2, PipelineResponse, Vector2> imageToScreenPoint,
        Pose? fallbackPlanePose = null,
        ARPlane fallbackPlane = null)
    {
        if (response?.document_surface?.corners == null ||
            response.document_surface.corners.Length < 8 ||
            raycastController == null ||
            imageToScreenPoint == null)
        {
            return null;
        }

        if (response.document_surface.confidence > 0f && response.document_surface.confidence < 0.20f)
        {
            return null;
        }

        Vector2[] imageCorners = ParseCorners(response.document_surface.corners);
        if (!HasUsableQuad(imageCorners) ||
            !TryBuildImageToUvHomography(imageCorners, out double[] homography))
        {
            return null;
        }

        Vector2[] screenCorners = new Vector2[4];
        for (int i = 0; i < imageCorners.Length; i++)
        {
            screenCorners[i] = imageToScreenPoint(imageCorners[i], response);
        }

        Vector3[] worldCorners;
        Pose planePose;
        Quaternion rotation;
        ARPlane commonPlane;
        if (fallbackPlanePose.HasValue)
        {
            planePose = fallbackPlanePose.Value;
            rotation = planePose.rotation;
            commonPlane = fallbackPlane;
            if (!TryProjectScreenCornersToPlane(screenCorners, planePose, out worldCorners))
            {
                return null;
            }
        }
        else if (TryResolveProjectionPlane(screenCorners, raycastController, out planePose, out commonPlane))
        {
            rotation = planePose.rotation;
            if (!TryProjectScreenCornersToPlane(screenCorners, planePose, out worldCorners))
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        if (!HasUsableQuad(worldCorners))
        {
            return null;
        }

        var surface = new ARDocumentSurface(
            imageCorners,
            screenCorners,
            worldCorners,
            planePose,
            rotation,
            commonPlane,
            response.document_surface.confidence,
            response.document_surface.method
        );
        return new ARDocumentSurfaceMapper(surface, homography);
    }

    public bool TryMapImagePointToPose(Vector2 imagePoint, out Pose pose)
    {
        pose = Pose.identity;
        if (!TryMapImagePointToWorld(imagePoint, out Vector3 position))
        {
            return false;
        }

        pose = new Pose(position, surface.Rotation);
        return true;
    }

    public bool TryMapImageRectToSurfacePose(Rect imageRect, out Pose pose, out Vector2 sizeMeters)
    {
        pose = Pose.identity;
        sizeMeters = Vector2.zero;

        if (imageRect.width <= 0f || imageRect.height <= 0f)
        {
            return false;
        }

        Vector2[] imagePoints =
        {
            new Vector2(imageRect.xMin, imageRect.yMin),
            new Vector2(imageRect.xMax, imageRect.yMin),
            new Vector2(imageRect.xMax, imageRect.yMax),
            new Vector2(imageRect.xMin, imageRect.yMax),
        };
        Vector3[] worldPoints = new Vector3[4];
        for (int i = 0; i < imagePoints.Length; i++)
        {
            if (!TryMapImagePointToWorld(imagePoints[i], out worldPoints[i]))
            {
                return false;
            }
        }

        Vector3 center = (worldPoints[0] + worldPoints[1] + worldPoints[2] + worldPoints[3]) * 0.25f;
        Vector3 right = ((worldPoints[1] - worldPoints[0]) + (worldPoints[2] - worldPoints[3])) * 0.5f;
        Vector3 down = ((worldPoints[3] - worldPoints[0]) + (worldPoints[2] - worldPoints[1])) * 0.5f;
        float width = Mathf.Max((worldPoints[1] - worldPoints[0]).magnitude, (worldPoints[2] - worldPoints[3]).magnitude);
        float height = Mathf.Max((worldPoints[3] - worldPoints[0]).magnitude, (worldPoints[2] - worldPoints[1]).magnitude);
        if (width <= 0.0001f || height <= 0.0001f)
        {
            return false;
        }

        Vector3 normal = surface.Rotation * Vector3.up;
        if (normal.sqrMagnitude < 0.000001f)
        {
            normal = Vector3.Cross(right, down);
        }
        normal.Normalize();
        Camera camera = Camera.main;
        if (camera != null && Vector3.Dot(normal, camera.transform.position - center) < 0f)
        {
            normal = -normal;
        }

        down = Vector3.ProjectOnPlane(down, normal);
        if (down.sqrMagnitude < 0.000001f)
        {
            down = Vector3.ProjectOnPlane(surface.Rotation * Vector3.forward, normal);
        }
        if (down.sqrMagnitude < 0.000001f)
        {
            return false;
        }
        down.Normalize();

        Quaternion rotation = Quaternion.LookRotation(-down, normal);
        pose = new Pose(center, rotation);
        sizeMeters = new Vector2(width, height);
        return true;
    }

    private bool TryMapImagePointToWorld(Vector2 imagePoint, out Vector3 position)
    {
        position = Vector3.zero;
        if (!TryImagePointToSurfaceUv(imagePoint, out Vector2 uv))
        {
            return false;
        }

        Vector3 top = Vector3.Lerp(surface.WorldCorners[0], surface.WorldCorners[1], uv.x);
        Vector3 bottom = Vector3.Lerp(surface.WorldCorners[3], surface.WorldCorners[2], uv.x);
        position = Vector3.Lerp(top, bottom, uv.y);
        return true;
    }

    private bool TryImagePointToSurfaceUv(Vector2 imagePoint, out Vector2 uv)
    {
        uv = Vector2.zero;
        double[] h = imageToUvHomography;

        double denominator = h[6] * imagePoint.x + h[7] * imagePoint.y + 1.0;
        if (Math.Abs(denominator) < 0.000001)
        {
            return false;
        }

        float u = (float)((h[0] * imagePoint.x + h[1] * imagePoint.y + h[2]) / denominator);
        float v = (float)((h[3] * imagePoint.x + h[4] * imagePoint.y + h[5]) / denominator);
        if (u < -0.08f || u > 1.08f || v < -0.08f || v > 1.08f)
        {
            return false;
        }

        uv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        return true;
    }

    private static Vector2[] ParseCorners(float[] corners)
    {
        return new[]
        {
            new Vector2(corners[0], corners[1]),
            new Vector2(corners[2], corners[3]),
            new Vector2(corners[4], corners[5]),
            new Vector2(corners[6], corners[7]),
        };
    }

    private static bool TryBuildImageToUvHomography(Vector2[] corners, out double[] homography)
    {
        double[,] matrix =
        {
            { corners[0].x, corners[0].y, 1.0, 0.0, 0.0, 0.0, -0.0 * corners[0].x, -0.0 * corners[0].y },
            { 0.0, 0.0, 0.0, corners[0].x, corners[0].y, 1.0, -0.0 * corners[0].x, -0.0 * corners[0].y },
            { corners[1].x, corners[1].y, 1.0, 0.0, 0.0, 0.0, -1.0 * corners[1].x, -1.0 * corners[1].y },
            { 0.0, 0.0, 0.0, corners[1].x, corners[1].y, 1.0, -0.0 * corners[1].x, -0.0 * corners[1].y },
            { corners[2].x, corners[2].y, 1.0, 0.0, 0.0, 0.0, -1.0 * corners[2].x, -1.0 * corners[2].y },
            { 0.0, 0.0, 0.0, corners[2].x, corners[2].y, 1.0, -1.0 * corners[2].x, -1.0 * corners[2].y },
            { corners[3].x, corners[3].y, 1.0, 0.0, 0.0, 0.0, -0.0 * corners[3].x, -0.0 * corners[3].y },
            { 0.0, 0.0, 0.0, corners[3].x, corners[3].y, 1.0, -1.0 * corners[3].x, -1.0 * corners[3].y },
        };
        double[] values = { 0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0 };

        return SolveLinearSystem(matrix, values, out homography);
    }

    private static bool TryProjectScreenPointToPlane(Vector2 screenPoint, Pose planePose, out Vector3 position)
    {
        position = Vector3.zero;
        Camera camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        Ray ray = camera.ScreenPointToRay(screenPoint);
        Vector3 normal = planePose.rotation * Vector3.up;
        float denominator = Vector3.Dot(normal, ray.direction);
        if (Mathf.Abs(denominator) < 0.0001f)
        {
            return false;
        }

        float distance = Vector3.Dot(planePose.position - ray.origin, normal) / denominator;
        if (distance <= 0f)
        {
            return false;
        }

        position = ray.origin + ray.direction * distance;
        return true;
    }

    private static bool TryProjectScreenCornersToPlane(
        Vector2[] screenCorners,
        Pose planePose,
        out Vector3[] worldCorners
    )
    {
        worldCorners = new Vector3[4];
        if (screenCorners == null || screenCorners.Length < 4)
        {
            return false;
        }

        for (int i = 0; i < 4; i++)
        {
            if (!TryProjectScreenPointToPlane(screenCorners[i], planePose, out worldCorners[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveProjectionPlane(
        Vector2[] screenCorners,
        ARRaycastController raycastController,
        out Pose planePose,
        out ARPlane plane
    )
    {
        planePose = Pose.identity;
        plane = null;
        if (screenCorners == null || screenCorners.Length < 4 || raycastController == null)
        {
            return false;
        }

        Vector2 center = Vector2.zero;
        for (int i = 0; i < 4; i++)
        {
            center += screenCorners[i];
        }
        center *= 0.25f;

        if (TryResolvePlaneFromScreenPoint(center, raycastController, out planePose, out plane))
        {
            return true;
        }

        for (int i = 0; i < 4; i++)
        {
            if (TryResolvePlaneFromScreenPoint(screenCorners[i], raycastController, out planePose, out plane))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolvePlaneFromScreenPoint(
        Vector2 screenPoint,
        ARRaycastController raycastController,
        out Pose planePose,
        out ARPlane plane
    )
    {
        planePose = Pose.identity;
        plane = null;
        if (raycastController == null ||
            !raycastController.TryRaycastHit(screenPoint, out ARRaycastHit hit))
        {
            return false;
        }

        plane = hit.trackable as ARPlane;
        Quaternion rotation = plane != null ? plane.transform.rotation : hit.pose.rotation;
        planePose = new Pose(hit.pose.position, rotation);
        return true;
    }

    private static bool HasUsableQuad(Vector2[] corners)
    {
        if (corners == null || corners.Length < 4) return false;

        float area = 0f;
        for (int i = 0; i < 4; i++)
        {
            Vector2 current = corners[i];
            Vector2 next = corners[(i + 1) % 4];
            area += current.x * next.y - next.x * current.y;
        }

        return Mathf.Abs(area) * 0.5f > 25f;
    }

    private static bool HasUsableQuad(Vector3[] corners)
    {
        if (corners == null || corners.Length < 4) return false;

        float topWidth = (corners[1] - corners[0]).magnitude;
        float bottomWidth = (corners[2] - corners[3]).magnitude;
        float leftHeight = (corners[3] - corners[0]).magnitude;
        float rightHeight = (corners[2] - corners[1]).magnitude;
        float width = Mathf.Max(topWidth, bottomWidth);
        float height = Mathf.Max(leftHeight, rightHeight);
        if (width <= 0.005f || height <= 0.005f)
        {
            return false;
        }

        if (width > MaxProjectedSurfaceSideMeters || height > MaxProjectedSurfaceSideMeters)
        {
            return false;
        }

        float aspect = width / Mathf.Max(0.0001f, height);
        if (aspect > MaxProjectedSurfaceAspectRatio || aspect < 1f / MaxProjectedSurfaceAspectRatio)
        {
            return false;
        }

        float area =
            Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).magnitude * 0.5f +
            Vector3.Cross(corners[2] - corners[0], corners[3] - corners[0]).magnitude * 0.5f;
        return area > 0.0001f && area <= MaxProjectedSurfaceAreaMeters;
    }

    private static bool SolveLinearSystem(double[,] matrix, double[] values, out double[] solution)
    {
        int size = values.Length;
        solution = new double[size];
        double[,] augmented = new double[size, size + 1];

        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                augmented[row, column] = matrix[row, column];
            }
            augmented[row, size] = values[row];
        }

        for (int pivot = 0; pivot < size; pivot++)
        {
            int bestRow = pivot;
            double bestValue = Math.Abs(augmented[pivot, pivot]);
            for (int row = pivot + 1; row < size; row++)
            {
                double candidate = Math.Abs(augmented[row, pivot]);
                if (candidate > bestValue)
                {
                    bestValue = candidate;
                    bestRow = row;
                }
            }

            if (bestValue < 0.0000001)
            {
                return false;
            }

            if (bestRow != pivot)
            {
                for (int column = pivot; column <= size; column++)
                {
                    (augmented[pivot, column], augmented[bestRow, column]) =
                        (augmented[bestRow, column], augmented[pivot, column]);
                }
            }

            double divisor = augmented[pivot, pivot];
            for (int column = pivot; column <= size; column++)
            {
                augmented[pivot, column] /= divisor;
            }

            for (int row = 0; row < size; row++)
            {
                if (row == pivot) continue;

                double factor = augmented[row, pivot];
                for (int column = pivot; column <= size; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }

        for (int row = 0; row < size; row++)
        {
            solution[row] = augmented[row, size];
        }

        return true;
    }
}
