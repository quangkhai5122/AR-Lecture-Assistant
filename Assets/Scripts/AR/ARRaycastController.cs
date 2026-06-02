// ARRaycastController.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARRaycastController : MonoBehaviour
{
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private bool allowEstimatedPlaneFallback = true;

    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    private const TrackableType ExactPlaneTypes =
        TrackableType.PlaneWithinPolygon |
        TrackableType.PlaneWithinBounds;

    private const TrackableType EstimatedPlaneTypes =
        ExactPlaneTypes |
        TrackableType.PlaneEstimated;

    public bool TryRaycast(Vector2 screenPosition, out Pose hitPose)
    {
        hitPose = Pose.identity;

        if (!TryRaycastHit(screenPosition, out ARRaycastHit hit))
        {
            return false;
        }

        hitPose = hit.pose;
        return true;
    }

    public bool TryRaycastHit(Vector2 screenPosition, out ARRaycastHit hit)
    {
        return TryRaycastHit(screenPosition, out hit, null);
    }

    public bool TryRaycastHit(
        Vector2 screenPosition,
        out ARRaycastHit hit,
        Predicate<ARRaycastHit> hitFilter
    )
    {
        hit = default;

        if (raycastManager == null)
        {
            raycastManager = FindAnyObjectByType<ARRaycastManager>();
        }

        if (raycastManager == null)
        {
            return false;
        }

        if (raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon) &&
            TrySelectHit(hitFilter, out hit))
        {
            return true;
        }

        TrackableType fallbackTypes = allowEstimatedPlaneFallback
            ? EstimatedPlaneTypes
            : ExactPlaneTypes;
        if (raycastManager.Raycast(screenPosition, hits, fallbackTypes) &&
            TrySelectHit(hitFilter, out hit))
        {
            return true;
        }

        return false;
    }

    private bool TrySelectHit(Predicate<ARRaycastHit> hitFilter, out ARRaycastHit hit)
    {
        foreach (ARRaycastHit candidate in hits)
        {
            if (hitFilter == null || hitFilter(candidate))
            {
                hit = candidate;
                return true;
            }
        }

        hit = default;
        return false;
    }

    public bool TryRaycastFromCenter(out Pose hitPose)
    {
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        return TryRaycast(center, out hitPose);
    }

    public bool TryRaycastFromCenter(out Pose hitPose, out ARRaycastHit hit)
    {
        return TryRaycastFromCenter(out hitPose, out hit, null);
    }

    public bool TryRaycastFromCenter(
        out Pose hitPose,
        out ARRaycastHit hit,
        Predicate<ARRaycastHit> hitFilter
    )
    {
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        if (TryRaycastHit(center, out hit, hitFilter))
        {
            hitPose = hit.pose;
            return true;
        }

        hitPose = Pose.identity;
        return false;
    }
}
