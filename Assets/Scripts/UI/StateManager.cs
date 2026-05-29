// StateManager.cs
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using UnityEngine.UI;

public class StateManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Image statusBackground;

    public UnityEvent<AppState> OnStateChanged;

    private AppState currentState = AppState.Idle;
    private Coroutine statusTransition;
    private bool hasInitializedState;

    public AppState CurrentState => currentState;

    public void SetState(AppState newState)
    {
        if (hasInitializedState && currentState == newState) return;

        hasInitializedState = true;
        currentState = newState;
        UpdateStatusUI();
        OnStateChanged?.Invoke(newState);
    }

    /// <summary>
    /// Hiện lỗi chi tiết trên thanh status (thay vì "Có lỗi, hãy thử lại" mặc định)
    /// </summary>
    public void SetError(string errorMessage)
    {
        hasInitializedState = true;
        currentState = AppState.Error;
        if (statusText != null) statusText.text = errorMessage;
        Color errorColor = new Color(0.92f, 0.22f, 0.22f, 0.88f);
        if (statusBackground != null)
        {
            if (statusTransition != null) StopCoroutine(statusTransition);
            statusTransition = StartCoroutine(AnimateStatus(errorColor));
        }
        OnStateChanged?.Invoke(AppState.Error);
    }

    private void UpdateStatusUI()
    {
        string message = "Sẵn sàng";
        Color targetColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);

        switch (currentState)
        {
            case AppState.Idle:
                message = "Sẵn sàng";
                targetColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);
                break;
            case AppState.Scanning:
                message = "Đang quét bảng/slide...";
                targetColor = new Color(0.10f, 0.42f, 0.88f, 0.82f);
                break;
            case AppState.PlaneDetected:
                message = "Đã phát hiện bảng/slide";
                targetColor = new Color(0.08f, 0.68f, 0.52f, 0.82f);
                break;
            case AppState.Translating:
                message = "Đang dịch nội dung...";
                targetColor = new Color(0.38f, 0.34f, 0.95f, 0.84f);
                break;
            case AppState.Anchored:
                message = "Đã ghim bản dịch";
                targetColor = new Color(0.08f, 0.68f, 0.52f, 0.84f);
                break;
            case AppState.Error:
                message = "Có lỗi, hãy thử lại";
                targetColor = new Color(0.92f, 0.22f, 0.22f, 0.88f);
                break;
        }

        if (statusText != null) statusText.text = message;
        if (statusBackground == null) return;

        if (statusTransition != null) StopCoroutine(statusTransition);
        statusTransition = StartCoroutine(AnimateStatus(targetColor));
    }

    private IEnumerator AnimateStatus(Color targetColor)
    {
        RectTransform rect = statusBackground.transform as RectTransform;
        Color startColor = statusBackground.color;
        Vector3 startScale = rect != null ? rect.localScale : Vector3.one;
        Vector3 pulseScale = Vector3.one * 1.035f;
        const float duration = 0.22f;

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            statusBackground.color = Color.Lerp(startColor, targetColor, t);
            if (rect != null)
            {
                rect.localScale = Vector3.Lerp(startScale, pulseScale, Mathf.Sin(t * Mathf.PI));
            }

            yield return null;
        }

        statusBackground.color = targetColor;
        if (rect != null) rect.localScale = Vector3.one;
        statusTransition = null;
    }
}
