using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// <see cref="GamepadSignalEvaluator"/> 單元測試
/// <para>涵蓋整數與浮點數多載的 IsActive，以及浮點數多載的 IsIdle 邊界條件。</para>
/// </summary>
public class GamepadSignalEvaluatorTests
{
    // ── IsActive (int overload) ──────────────────────────────────────────

    /// <summary>
    /// 所有軸值為 0 且無按鈕時，應判定為無活動訊號（false）。
    /// </summary>
    [Fact]
    public void IsActive_Int_AllZeroNoButtons_ReturnsFalse()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: false,
            leftTrigger: 0, rightTrigger: 0,
            leftThumbX: 0, leftThumbY: 0,
            rightThumbX: 0, rightThumbY: 0,
            triggerThreshold: 30, thumbThreshold: 8000);

        Assert.False(result);
    }

    /// <summary>
    /// 有任一按鈕被按下時，無論軸值為何，應判定為有活動訊號（true）。
    /// </summary>
    [Fact]
    public void IsActive_Int_HasButtons_ReturnsTrue()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: true,
            leftTrigger: 0, rightTrigger: 0,
            leftThumbX: 0, leftThumbY: 0,
            rightThumbX: 0, rightThumbY: 0,
            triggerThreshold: 30, thumbThreshold: 8000);

        Assert.True(result);
    }

    /// <summary>
    /// 左板機值超過閾值時，應判定為有活動訊號（true）。
    /// </summary>
    [Fact]
    public void IsActive_Int_LeftTriggerAboveThreshold_ReturnsTrue()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: false,
            leftTrigger: 31, rightTrigger: 0,
            leftThumbX: 0, leftThumbY: 0,
            rightThumbX: 0, rightThumbY: 0,
            triggerThreshold: 30, thumbThreshold: 8000);

        Assert.True(result);
    }

    /// <summary>
    /// 右板機值超過閾值時，應判定為有活動訊號（true）。
    /// </summary>
    [Fact]
    public void IsActive_Int_RightTriggerAboveThreshold_ReturnsTrue()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: false,
            leftTrigger: 0, rightTrigger: 100,
            leftThumbX: 0, leftThumbY: 0,
            rightThumbX: 0, rightThumbY: 0,
            triggerThreshold: 30, thumbThreshold: 8000);

        Assert.True(result);
    }

    /// <summary>
    /// 左搖桿 X 軸為負值且絕對值超過閾值時，應判定為有活動訊號（true）。
    /// 驗證 IsActive 對負軸值取絕對值後比較。
    /// </summary>
    [Fact]
    public void IsActive_Int_LeftThumbXNegativeAboveThreshold_ReturnsTrue()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: false,
            leftTrigger: 0, rightTrigger: 0,
            leftThumbX: -9000, leftThumbY: 0,
            rightThumbX: 0, rightThumbY: 0,
            triggerThreshold: 30, thumbThreshold: 8000);

        Assert.True(result);
    }

    /// <summary>
    /// 所有觸發與搖桿軸值剛好等於閾值（不超過）時，應判定為無活動訊號（false）。
    /// 驗證 IsActive 使用嚴格大於（>）比較。
    /// </summary>
    [Fact]
    public void IsActive_Int_AllAtOrBelowThreshold_ReturnsFalse()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: false,
            leftTrigger: 30, rightTrigger: 30,
            leftThumbX: 8000, leftThumbY: -8000,
            rightThumbX: 8000, rightThumbY: -8000,
            triggerThreshold: 30, thumbThreshold: 8000);

        Assert.False(result);
    }

    // ── IsActive (float overload) ────────────────────────────────────────

    /// <summary>
    /// 浮點數多載：所有軸值為 0 且無按鈕時，應判定為無活動訊號（false）。
    /// </summary>
    [Fact]
    public void IsActive_Float_AllZeroNoButtons_ReturnsFalse()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: false,
            leftTrigger: 0f, rightTrigger: 0f,
            leftThumbX: 0f, leftThumbY: 0f,
            rightThumbX: 0f, rightThumbY: 0f,
            threshold: 0.1f);

        Assert.False(result);
    }

    /// <summary>
    /// 浮點數多載：右搖桿 Y 軸超過閾值時，應判定為有活動訊號（true）。
    /// </summary>
    [Fact]
    public void IsActive_Float_RightThumbYAboveThreshold_ReturnsTrue()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: false,
            leftTrigger: 0f, rightTrigger: 0f,
            leftThumbX: 0f, leftThumbY: 0f,
            rightThumbX: 0f, rightThumbY: 0.5f,
            threshold: 0.1f);

        Assert.True(result);
    }

    /// <summary>
    /// 浮點數多載：右搖桿 X 軸為負值且絕對值超過閾值時，應判定為有活動訊號（true）。
    /// </summary>
    [Fact]
    public void IsActive_Float_NegativeRightThumbAboveThreshold_ReturnsTrue()
    {
        bool result = GamepadSignalEvaluator.IsActive(
            hasButtons: false,
            leftTrigger: 0f, rightTrigger: 0f,
            leftThumbX: 0f, leftThumbY: 0f,
            rightThumbX: -0.5f, rightThumbY: 0f,
            threshold: 0.1f);

        Assert.True(result);
    }

    // ── IsIdle (float overload) ──────────────────────────────────────────

    /// <summary>
    /// 無按鈕且所有軸值絕對值低於閾值時，應判定為閒置（true）。
    /// </summary>
    [Fact]
    public void IsIdle_AllBelowThresholdNoButtons_ReturnsTrue()
    {
        bool result = GamepadSignalEvaluator.IsIdle(
            hasButtons: false,
            leftTrigger: 0f, rightTrigger: 0f,
            leftThumbX: 0.05f, leftThumbY: -0.05f,
            rightThumbX: 0f, rightThumbY: 0f,
            threshold: 0.1f);

        Assert.True(result);
    }

    /// <summary>
    /// 有按鈕被按下時，即使所有軸值為 0，也不應判定為閒置（false）。
    /// </summary>
    [Fact]
    public void IsIdle_HasButtons_ReturnsFalse()
    {
        bool result = GamepadSignalEvaluator.IsIdle(
            hasButtons: true,
            leftTrigger: 0f, rightTrigger: 0f,
            leftThumbX: 0f, leftThumbY: 0f,
            rightThumbX: 0f, rightThumbY: 0f,
            threshold: 0.1f);

        Assert.False(result);
    }

    /// <summary>
    /// 左板機值超過閾值時，不應判定為閒置（false）。
    /// </summary>
    [Fact]
    public void IsIdle_LeftTriggerAboveThreshold_ReturnsFalse()
    {
        bool result = GamepadSignalEvaluator.IsIdle(
            hasButtons: false,
            leftTrigger: 0.5f, rightTrigger: 0f,
            leftThumbX: 0f, leftThumbY: 0f,
            rightThumbX: 0f, rightThumbY: 0f,
            threshold: 0.1f);

        Assert.False(result);
    }

    /// <summary>
    /// IsIdle 使用嚴格小於（&lt;）比較：軸值剛好等於閾值時，不應判定為閒置（false）。
    /// </summary>
    [Fact]
    public void IsIdle_ThumbExactlyAtThreshold_ReturnsFalse()
    {
        bool result = GamepadSignalEvaluator.IsIdle(
            hasButtons: false,
            leftTrigger: 0f, rightTrigger: 0f,
            leftThumbX: 0.1f, leftThumbY: 0f,
            rightThumbX: 0f, rightThumbY: 0f,
            threshold: 0.1f);

        Assert.False(result);
    }
}
