using System;
using UnityEngine;

public sealed class ARDocumentSurfaceMapper
{
    private readonly ARDocumentSurface surface;

    private ARDocumentSurfaceMapper(ARDocumentSurface surface)
    {
        this.surface = surface;
    }

    public ARDocumentSurface Surface => surface;
    public Vector3[] WorldCorners => surface.WorldCorners;

    public static ARDocumentSurfaceMapper TryCreate(
        PipelineResponse response,
        ARRaycastController raycastController,
        Func<Vector2, PipelineResponse, Vector2> imageToScreenPoint)
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
        Vector3[] worldCorners = new Vector3[4];
        Quaternion rotation = Quaternion.identity;

        for (int i = 0; i < imageCorners.Length; i++)
        {
            Vector2 screenPoint = imageToScreenPoint(imageCorners[i], response);
            if (!raycastController.TryRaycast(screenPoint, out Pose hitPose))
            {
                return null;
            }

            worldCorners[i] = hitPose.position;
            if (i == 0) rotation = hitPose.rotation;
        }

        var surface = new ARDocumentSurface(
            imageCorners,
            worldCorners,
            rotation,
            response.document_surface.confidence,
            response.document_surface.method
        );
        return new ARDocumentSurfaceMapper(surface);
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
        Vector2[] corners = surface.ImageCorners;
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

        if (!SolveLinearSystem(matrix, values, out double[] h))
        {
            return false;
        }

        double denominator = h[6] * imagePoint.x + h[7] * imagePoint.y + 1.0;
        if (Math.Abs(denominator) < 0.000001)
        {
            return false;
        }

        float u = (float)((h[0] * imagePoint.x + h[1] * imagePoint.y + h[2]) / denominator);
        float v = (float)((h[3] * imagePoint.x + h[4] * imagePoint.y + h[5]) / denominator);
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
