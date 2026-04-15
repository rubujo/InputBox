using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證重新啟動前景協調器的單次標記與過期清理行為，避免設定重啟後遺失主視窗焦點。
/// </summary>
public sealed class RestartActivationCoordinatorTests : IDisposable
{
    /// <summary>
    /// 此測試使用的暫存標記檔路徑。
    /// </summary>
    private readonly string _markerPath = Path.Combine(
        Path.GetTempPath(),
        $"InputBox.Tests.{Guid.NewGuid():N}.restart.flag");

    /// <summary>
    /// 清理測試產生的暫存檔。
    /// </summary>
    public void Dispose()
    {
        if (File.Exists(_markerPath))
        {
            File.Delete(_markerPath);
        }
    }

    /// <summary>
    /// 尚未要求重新啟動前景時，不應誤判為有待處理的啟用請求。
    /// </summary>
    [Fact]
    public void ConsumePendingActivationRequest_WithoutMarker_ReturnsFalse()
    {
        RestartActivationCoordinator coordinator = new(_markerPath, TimeSpan.FromSeconds(5));

        Assert.False(coordinator.ConsumePendingActivationRequest());
    }

    /// <summary>
    /// 一次性標記建立後，下一個啟動執行個體應能成功消費並立即清除標記。
    /// </summary>
    [Fact]
    public void RequestActivationOnNextLaunch_ThenConsume_ReturnsTrueOnce()
    {
        RestartActivationCoordinator coordinator = new(_markerPath, TimeSpan.FromSeconds(5));

        coordinator.RequestActivationOnNextLaunch();

        Assert.True(coordinator.ConsumePendingActivationRequest());
        Assert.False(coordinator.ConsumePendingActivationRequest());
        Assert.False(File.Exists(_markerPath));
    }

    /// <summary>
    /// 已過期的標記不可再觸發前景啟用，並應於讀取時被清除。
    /// </summary>
    [Fact]
    public void ConsumePendingActivationRequest_WhenMarkerExpired_ReturnsFalseAndDeletesMarker()
    {
        RestartActivationCoordinator coordinator = new(_markerPath, TimeSpan.FromMilliseconds(-1));

        coordinator.RequestActivationOnNextLaunch();

        Assert.False(coordinator.ConsumePendingActivationRequest());
        Assert.False(File.Exists(_markerPath));
    }
}