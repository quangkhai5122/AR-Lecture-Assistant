// AppState.cs
public enum AppState
{
    Idle,           // Chờ, chưa làm gì
    Scanning,       // Đang scan plane / tìm bảng
    PlaneDetected,  // Đã phát hiện plane, sẵn sàng
    Translating,    // Đang dịch (chờ kết quả)
    Anchored,       // Text đã được anchor, hiển thị ổn định
    Error           // Lỗi (mất tracking, API fail, v.v.)
}