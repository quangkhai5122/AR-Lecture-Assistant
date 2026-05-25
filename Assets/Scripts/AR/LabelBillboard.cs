// LabelBillboard.cs — Giúp label luôn hướng về camera
using UnityEngine;

public class LabelBillboard : MonoBehaviour
{
    private Camera arCamera;

    void Start()
    {
        arCamera = Camera.main;
    }

    void LateUpdate()
    {
        if (arCamera != null)
        {
            // Xoay label để hướng về camera
            transform.LookAt(
                transform.position + arCamera.transform.forward);
        }
    }
}