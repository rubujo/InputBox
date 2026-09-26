using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using System.ComponentModel;
using System.Diagnostics;
using System.Media;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 顯示遊戲控制器校準狀態的視覺化診斷對話框（遊戲控制器與輸入分部）。
/// <para>本分部檔案包含遊戲控制器事件訂閱、確認與取消、按鈕焦點導覽，以及校正狀態重設等成員。</para>
/// </summary>
internal sealed partial class GamepadCalibrationDialog
{
    /// <summary>
    /// 指派控制器實例，並同步診斷快照與事件訂閱。
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IGamepadController? GamepadController
    {
        get => _gamepadController;
        set
        {
            if (ReferenceEquals(_gamepadController, value))
            {
                return;
            }

            UnsubscribeGamepadEvents();
            _gamepadController = value;

            if (_gamepadController != null)
            {
                SubscribeGamepadEvents();
            }

            UpdateSnapshotFromController();
        }
    }

    /// <summary>
    /// 訂閱目前控制器的所有輸入事件。
    /// </summary>
    private void SubscribeGamepadEvents()
    {
        if (_gamepadController == null)
        {
            return;
        }

        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        _gamepadController.APressed += profile.ConfirmOnSouth ? HandleGamepadConfirm : HandleGamepadCancel;
        _gamepadController.StartPressed += HandleGamepadConfirm;
        _gamepadController.BPressed += profile.ConfirmOnSouth ? HandleGamepadCancel : HandleGamepadConfirm;
        _gamepadController.BackPressed += HandleGamepadCancel;
        _gamepadController.YPressed += HandleGamescopeSurfaceRecovery;
        _gamepadController.LeftPressed += HandleDPadPrevious;
        _gamepadController.LeftRepeat += HandleDPadPrevious;
        _gamepadController.UpPressed += HandleDPadPrevious;
        _gamepadController.UpRepeat += HandleDPadPrevious;
        _gamepadController.RightPressed += HandleDPadNext;
        _gamepadController.RightRepeat += HandleDPadNext;
        _gamepadController.DownPressed += HandleDPadNext;
        _gamepadController.DownRepeat += HandleDPadNext;
        _gamepadController.ConnectionChanged += HandleGamepadConnectionChanged;
    }

    /// <summary>
    /// 取消訂閱目前控制器的所有輸入事件。
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController == null)
            {
                return;
            }

            _gamepadController.APressed -= HandleGamepadConfirm;
            _gamepadController.APressed -= HandleGamepadCancel;
            _gamepadController.StartPressed -= HandleGamepadConfirm;
            _gamepadController.BPressed -= HandleGamepadConfirm;
            _gamepadController.BPressed -= HandleGamepadCancel;
            _gamepadController.BackPressed -= HandleGamepadCancel;
            _gamepadController.YPressed -= HandleGamescopeSurfaceRecovery;
            _gamepadController.LeftPressed -= HandleDPadPrevious;
            _gamepadController.LeftRepeat -= HandleDPadPrevious;
            _gamepadController.UpPressed -= HandleDPadPrevious;
            _gamepadController.UpRepeat -= HandleDPadPrevious;
            _gamepadController.RightPressed -= HandleDPadNext;
            _gamepadController.RightRepeat -= HandleDPadNext;
            _gamepadController.DownPressed -= HandleDPadNext;
            _gamepadController.DownRepeat -= HandleDPadNext;
            _gamepadController.ConnectionChanged -= HandleGamepadConnectionChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] 取消訂閱控制器事件失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 控制器連線狀態變更時更新快照並播報連線訊息。
    /// </summary>
    /// <param name="isConnected">控制器是否已連線。</param>
    private void HandleGamepadConnectionChanged(bool isConnected)
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                UpdateSnapshotFromController();
                _announcer?.Announce(FormatConnectionAnnouncement(isConnected, _gamepadController?.DeviceName), true);
            });
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "GamepadCalibrationDialog.HandleGamepadConnectionChanged 失敗");
            Debug.WriteLine($"[GamepadCalibrationDialog] 控制器連線變更處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理控制器確認按鍵，觸發目前焦點按鈕或預設按鈕的點擊。
    /// </summary>
    private void HandleGamepadConfirm()
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    if (ActiveControl is Button activeButton &&
                        activeButton.Enabled)
                    {
                        activeButton.PerformClick();
                    }
                    else
                    {
                        (_btnReset ?? AcceptButton as Button)?.PerformClick();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadCalibrationDialog] HandleGamepadConfirm UI 失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] HandleGamepadConfirm 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理控制器取消按鍵，觸發關閉按鈕或直接關閉對話框。
    /// </summary>
    private void HandleGamepadCancel()
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    if (_btnClose != null &&
                        !_btnClose.IsDisposed)
                    {
                        _btnClose.PerformClick();
                    }
                    else
                    {
                        DialogResult = DialogResult.Cancel;
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadCalibrationDialog] HandleGamepadCancel UI 失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] HandleGamepadCancel 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理 Gamescope 專用 surface recovery 組合鍵。
    /// </summary>
    private void HandleGamescopeSurfaceRecovery()
    {
        GamescopeSurfaceRecovery.TryRecoverFromGamepadChord(
            this,
            RecreateHandle,
            _gamepadController,
            context: "GamepadCalibrationDialog Gamescope surface recovery 失敗");
    }

    /// <summary>
    /// 處理 D-Pad 向前（左/上）輸入，在搖桿不干擾時移動焦點至前一個按鈕。
    /// </summary>
    private void HandleDPadPrevious()
    {
        if (ShouldHandleDirectionalFocusNavigation(_gamepadController?.CurrentCalibrationSnapshot ?? _snapshot))
        {
            FocusPreviousButton();
        }
    }

    /// <summary>
    /// 處理 D-Pad 向後（右/下）輸入，在搖桿不干擾時移動焦點至下一個按鈕。
    /// </summary>
    private void HandleDPadNext()
    {
        if (ShouldHandleDirectionalFocusNavigation(_gamepadController?.CurrentCalibrationSnapshot ?? _snapshot))
        {
            FocusNextButton();
        }
    }

    /// <summary>
    /// 將焦點移至前一個按鈕。
    /// </summary>
    private void FocusPreviousButton() => MoveButtonFocus(-1);

    /// <summary>
    /// 將焦點移至下一個按鈕。
    /// </summary>
    private void FocusNextButton() => MoveButtonFocus(+1);

    /// <summary>
    /// 依方向在重設與關閉按鈕之間移動焦點。
    /// </summary>
    /// <param name="direction">方向值；負值移往重設按鈕，正值移往關閉按鈕。</param>
    private void MoveButtonFocus(int direction)
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                if (IsDisposed || _btnReset == null || _btnClose == null)
                {
                    return;
                }

                Button nextButton = direction < 0 ?
                    ActiveControl == _btnClose ? _btnReset : _btnClose :
                    ActiveControl == _btnReset ? _btnClose : _btnReset;

                nextButton.Focus();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] MoveButtonFocus 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 從控制器取得最新校準快照，更新狀態文字並觸發畫面重繪。
    /// </summary>
    private void UpdateSnapshotFromController()
    {
        GamepadCalibrationSnapshot snapshot = _gamepadController?.CurrentCalibrationSnapshot ?? new GamepadCalibrationSnapshot
        {
            IsConnected = false,
            ThumbDeadzoneEnter = AppSettings.Current.ThumbDeadzoneEnter,
            ThumbDeadzoneExit = AppSettings.Current.ThumbDeadzoneExit,
            TimestampUtc = DateTime.UtcNow
        };

        _snapshot = snapshot;
        UpdateStatusText();
        _surface?.Invalidate();
    }

    /// <summary>
    /// 判斷目前搖桿是否靜止到足以安全處理方向焦點移動，避免搖桿偏移誤觸焦點導覽。
    /// </summary>
    /// <param name="snapshot">目前的校準狀態快照。</param>
    /// <returns>若搖桿偏移量低於安全閾值（或控制器未連線）則回傳 true。</returns>
    internal static bool ShouldHandleDirectionalFocusNavigation(GamepadCalibrationSnapshot snapshot)
    {
        if (!snapshot.IsConnected)
        {
            return true;
        }

        float normalizedDeadzone = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(
            Math.Max(snapshot.ThumbDeadzoneEnter, snapshot.ThumbDeadzoneExit));

        // 門檻必須低於 normalizedDeadzone，確保 LS 剛跨越 ThumbDeadzoneEnter
        // 觸發搖桿→D-Pad 映射的瞬間，保護邏輯已阻止方向焦點移動。
        float navigationThreshold = Math.Clamp(normalizedDeadzone * 0.75f, 0.06f, 0.18f);

        return MathF.Abs(snapshot.RawLeftX) <= navigationThreshold &&
               MathF.Abs(snapshot.RawLeftY) <= navigationThreshold &&
               MathF.Abs(snapshot.CorrectedLeftX) <= navigationThreshold &&
               MathF.Abs(snapshot.CorrectedLeftY) <= navigationThreshold;
    }

    /// <summary>
    /// 重設控制器校準資料，播放音效與震動回饋並更新快照。
    /// </summary>
    private void ResetCalibration()
    {
        try
        {
            _gamepadController?.ResetCalibration();
            SystemSounds.Asterisk.Play();
            PlayResetCalibrationFeedbackAsync().SafeFireAndForget();
            UpdateSnapshotFromController();
            _announcer?.Announce(Strings.A11y_Gamepad_CalibrationReset, true);
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "重設校準視覺化狀態失敗");
            Debug.WriteLine($"[GamepadCalibrationDialog] ResetCalibration 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 播放重設校準的三段式震動序列回饋。
    /// </summary>
    /// <returns>代表震動序列播放過程的非同步工作。</returns>
    private async Task PlayResetCalibrationFeedbackAsync()
    {
        IGamepadController? controller = _gamepadController;

        if (controller == null)
        {
            return;
        }

        CancellationToken token = _cts?.Token ?? CancellationToken.None;

        VibrationProfile[] sequence =
        [
            new(22000, 26, 1.0f, 0.18f, 0.52f, 0.04f),
            new(22000, 26, 0.18f, 1.0f, 0.04f, 0.52f),
            new(18000, 22, 0.62f, 0.62f, 0.18f, 0.18f)
        ];

        foreach (VibrationProfile profile in sequence)
        {
            token.ThrowIfCancellationRequested();
            await controller.VibrateAsync(profile, VibrationPriority.Normal, token);
            await Task.Delay(20, token);
        }
    }
}
