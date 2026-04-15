using System.Globalization;

namespace InputBox.Core.Services;

/// <summary>
/// 協調「由本程式主動要求的重新啟動」之單次前景啟用需求。
/// <para>以短時效的一次性標記跨進程傳遞意圖，讓新執行個體在啟動後主動搶回前景與輸入焦點。</para>
/// </summary>
internal sealed class RestartActivationCoordinator
{
    /// <summary>
    /// 共用的一次性重啟啟用標記檔路徑。
    /// </summary>
    private static readonly string SharedMarkerPath = Path.Combine(
        Path.GetTempPath(),
        "InputBox",
        "restart-activation.flag");

    /// <summary>
    /// 目前協調器實際使用的標記檔路徑。
    /// </summary>
    private readonly string _markerPath;

    /// <summary>
    /// 標記有效時間，超過後即視為過期請求。
    /// </summary>
    private readonly TimeSpan _markerLifetime;

    /// <summary>
    /// 共用協調器執行個體。
    /// </summary>
    public static RestartActivationCoordinator Shared { get; } = new();

    /// <summary>
    /// 初始化重新啟動前景協調器。
    /// </summary>
    /// <param name="markerPath">標記檔路徑；未指定時使用系統暫存資料夾。</param>
    /// <param name="markerLifetime">標記有效時間；未指定時預設為 30 秒。</param>
    internal RestartActivationCoordinator(string? markerPath = null, TimeSpan? markerLifetime = null)
    {
        _markerPath = string.IsNullOrWhiteSpace(markerPath) ? SharedMarkerPath : markerPath;
        _markerLifetime = markerLifetime ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// 建立單次前景啟用請求，供下一個重啟後的執行個體消費。
    /// </summary>
    public void RequestActivationOnNextLaunch()
    {
        try
        {
            // 先解析標記檔所在資料夾，確保第一次執行時能安全建立儲存位置。
            string? directory = Path.GetDirectoryName(_markerPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 使用 UTC 到期時間戳，讓下一個執行個體能判斷這是否仍屬於有效的重啟請求。
            long expiryTicks = DateTime.UtcNow.Add(_markerLifetime).Ticks;

            File.WriteAllText(_markerPath, expiryTicks.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            TryDeleteMarkerSilently();
        }
    }

    /// <summary>
    /// 消費待處理的單次前景啟用請求。
    /// </summary>
    /// <returns>若存在且尚未過期的請求則回傳 true，並於消費後立即清除標記。</returns>
    public bool ConsumePendingActivationRequest()
    {
        try
        {
            if (!File.Exists(_markerPath))
            {
                return false;
            }

            // 讀取以 UTC Ticks 表示的有效期限，並在消費當下立即刪除，確保請求只會生效一次。
            string payload = File.ReadAllText(_markerPath).Trim();

            File.Delete(_markerPath);

            return long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out long expiryTicks) &&
                expiryTicks >= DateTime.UtcNow.Ticks;
        }
        catch
        {
            TryDeleteMarkerSilently();

            return false;
        }
    }

    /// <summary>
    /// 清除殘留標記（若存在）。
    /// </summary>
    internal void ClearPendingActivationRequest() => TryDeleteMarkerSilently();

    /// <summary>
    /// 安全刪除標記檔，不因暫存檔失敗而中斷主流程。
    /// </summary>
    private void TryDeleteMarkerSilently()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch
        {
            // 忽略清理失敗，避免影響主流程。
        }
    }
}