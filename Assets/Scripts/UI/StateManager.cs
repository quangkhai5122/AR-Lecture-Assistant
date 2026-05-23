// StateManager.cs
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

    public AppState CurrentState => currentState;

    public void SetState(AppState newState)
    {
        if (currentState == newState) return;

        currentState = newState;
        UpdateStatusUI();
        OnStateChanged?.Invoke(newState);
    }

    private void UpdateStatusUI()
    {
        switch (currentState)
        {
            case AppState.Idle:
                statusText.text = "Sẵn sàng";
                statusBackground.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                break;
            case AppState.Scanning:
                statusText.text = "🔍 Đang quét...";
                statusBackground.color = new Color(0.2f, 0.6f, 1f, 0.7f);
                break;
            case AppState.PlaneDetected:
                statusText.text = "✅ Đã phát hiện bảng/slide";
                statusBackground.color = new Color(0.2f, 0.8f, 0.4f, 0.7f);
                break;
            case AppState.Translating:
                statusText.text = "⏳ Đang dịch...";
                statusBackground.color = new Color(1f, 0.8f, 0.2f, 0.7f);
                break;
            case AppState.Anchored:
                statusText.text = "📌 Đã ghim bản dịch";
                statusBackground.color = new Color(0.2f, 0.8f, 0.4f, 0.7f);
                break;
            case AppState.Error:
                statusText.text = "❌ Lỗi — Thử lại";
                statusBackground.color = new Color(1f, 0.3f, 0.3f, 0.7f);
                break;
        }
    }
}