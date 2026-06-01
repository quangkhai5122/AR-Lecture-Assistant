// LabelBillboard.cs — Giúp label luôn hướng về camera
using UnityEngine;

public class LabelBillboard : MonoBehaviour
{
    private Camera arCamera;

    private void Start()
    {
        arCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (arCamera == null)
        {
            arCamera = Camera.main;
        }

        if (arCamera != null)
        {
            // Match the AR camera orientation so the world-space canvas remains readable
            // after the user looks away and comes back to the anchored label.
            transform.rotation = Quaternion.LookRotation(
                arCamera.transform.forward,
                arCamera.transform.up
            );
        }
    }
}
