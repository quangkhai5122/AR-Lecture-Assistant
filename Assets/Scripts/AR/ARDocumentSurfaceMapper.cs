using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public sealed class ARDocumentSurfaceMapper
{
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
        Vector3[] worldCorners = new Vector3[4];
        Pose planePose = fallbackPlanePose ?? Pose.identity;
        Quaternion rotation = fallbackPlanePose?.rotation ?? Quaternion.identity;
        ARPlane commonPlane = fallbackPlane;
        bool planeMismatch = false;
        bool hasRaycastHit = false;

        for (int i = 0; i < imageCorners.Length; i++)
        {
            Vector2 screenPoint = imageToScreenPoint(imageCorners[i], response);
            screenCorners[i] = screenPoint;
            if (raycastController.TryRaycastHit(screenPoint, out ARRaycastHit hit))
            {
                worldCorners[i] = hit.pose.position;
                if (!hasRaycastHit)
                {
                    planePose = hit.pose;
                    rotation = hit.pose.rotation;
                    hasRaycastHit = true;
                }

                ARPlane hitPlane = hit.trackable as ARPlane;
                if (!planeMismatch && hitPlane != null)
                {
                    if (commonPlane == null)
                    {
                        commonPlane = hitPlane;
                    }
                    else if (commonPlane.trackableId != hitPlane.trackableId)
                    {
                        commonPlane = null;
                        planeMismatch = true;
                    }
                }
            }
            else if (fallbackPlanePose.HasValue &&
                     TryProjectScreenPointToPlane(screenPoint, fallbackPlanePose.Value, out Vector3 projectedPosition))
            {
                worldCorners[i] = projectedPosition;
            }
            else
            {
                return null;
            }
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
            planeMismatch ? null : commonPlane,
            response.document_surface.confidence,
            response.document_surface.method
        );
        return new ARDocumentSurfaceMapper(surface, homography);
    }

    public bool TryMapImagePointToPose(Vector2 imagePoint, out Pose pose)
    {
        pose = Pose.identity;
        if (!TryImagePointToSurfaceUv(imagePoint, out Vector2 uv))
        {
            return false;
        }

        Vector3 top = Vector3.Lerp(surface.WorldCorners[0], surface.WorldCorners[1], uv.x);
        Vector3 bottom = Vector3.Lerp(surface.WorldCorners[3], surface.WorldCorners[2], uv.x);
        Vector3 position = Vector3.Lerp(top, bottom, uv.y);
        pose = new Pose(position, surface.Rotation);
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

        float area =
            Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).magnitude * 0.5f +
            Vector3.Cross(corners[2] - corners[0], corners[3] - corners[0]).magnitude * 0.5f;
        return area > 0.0001f;
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
