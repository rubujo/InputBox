namespace InputBox.Core.Services;

/// <summary>
/// 輸入框命令鍵處理結果
/// </summary>
internal enum InputBoxCmdResult
{
    /// <summary>
    /// 未處理，交由呼叫端繼續判斷
    /// </summary>
    Unhandled,
    /// <summary>
    /// 已處理完成，不需再向下傳遞
    /// </summary>
    Handled,
    /// <summary>
    /// 需轉交基底類別處理。
    /// </summary>
    ForwardToBase
}

/// <summary>
/// ProcessCmdKey 的命令分派輔助器
/// </summary>
internal static class CmdKeyDispatcher
{
    /// <summary>
    /// 當右鍵選單顯示時，將鍵盤輸入轉譯為統一的選單操作命令，確保滑鼠、鍵盤與控制器可交替接手。
    /// </summary>
    /// <param name="keyData">目前命令鍵組合。</param>
    /// <param name="menuVisible">右鍵選單是否顯示中。</param>
    /// <param name="action">轉譯後的選單動作名稱。</param>
    /// <returns>若已對應到選單動作則回傳 true。</returns>
    public static bool TryGetContextMenuAction(Keys keyData, bool menuVisible, out string? action)
    {
        action = null;

        if (!menuVisible)
        {
            return false;
        }

        switch (keyData)
        {
            case Keys.Up:
                action = "Up";

                return true;
            case Keys.Down:
                action = "Down";

                return true;
            case Keys.Left:
                action = "Left";

                return true;
            case Keys.Right:
                action = "Right";

                return true;
            case Keys.Enter:
                action = "Confirm";

                return true;
            case Keys.Escape:
                action = "Cancel";

                return true;
            case Keys.PageUp:
                action = "PhrasePagePrevious";

                return true;
            case Keys.PageDown:
                action = "PhrasePageNext";

                return true;
            case Keys.Home:
                action = "PhrasePageFirst";

                return true;
            case Keys.End:
                action = "PhrasePageLast";

                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 處理全域命令鍵（不限定輸入框焦點）
    /// </summary>
    /// <param name="keyData">目前命令鍵組合。</param>
    /// <param name="onReturnPreviousWindow">觸發返回前景視窗的動作。</param>
    /// <param name="onAdjustOpacity">調整不透明度的動作。</param>
    /// <param name="onResetOpacity">重設不透明度的動作。</param>
    /// <param name="onTogglePrivacyMode">切換隱私模式的動作。</param>
    /// <param name="onShowContextMenu">顯示右鍵選單的動作。</param>
    /// <param name="canFocusInput">回傳輸入框是否可聚焦。</param>
    /// <param name="onFocusInput">聚焦輸入框的動作。</param>
    /// <param name="onAnnounceSkipNav">播報略過導覽訊息的動作。</param>
    /// <returns>若命令鍵已被處理則回傳 true。</returns>
    public static bool TryHandleGlobal(
        Keys keyData,
        Action onReturnPreviousWindow,
        Action<float> onAdjustOpacity,
        Action onResetOpacity,
        Action onTogglePrivacyMode,
        Action onShowContextMenu,
        Func<bool> canFocusInput,
        Action onFocusInput,
        Action onAnnounceSkipNav)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.B:
                onReturnPreviousWindow();

                return true;

            case Keys.Alt | Keys.Up:
                onAdjustOpacity(0.05f);

                return true;

            case Keys.Alt | Keys.Down:
                onAdjustOpacity(-0.05f);

                return true;

            case Keys.Alt | Keys.D0:
                onResetOpacity();

                return true;

            case Keys.Alt | Keys.P:
                onTogglePrivacyMode();

                return true;

            case Keys.F10:
            case Keys.Alt | Keys.M:
                onShowContextMenu();

                return true;

            case Keys.Control | Keys.M:
                if (canFocusInput())
                {
                    onFocusInput();
                    onAnnounceSkipNav();
                }

                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 處理輸入框焦點下的命令鍵（例如 Enter／Shift + Enter）
    /// </summary>
    /// <param name="keyData">目前命令鍵組合。</param>
    /// <param name="activeControl">目前作用中的控制項。</param>
    /// <param name="inputBox">主要輸入框控制項。</param>
    /// <param name="onShowTouchKeyboard">顯示觸控鍵盤的動作。</param>
    /// <param name="onConfirm">執行確認（複製）動作。</param>
    /// <param name="onAnnounceNewLine">播報換行訊息的動作。</param>
    /// <param name="onVibrateCursorMove">觸發游標移動震動的動作。</param>
    /// <returns>輸入框命令鍵處理結果。</returns>
    public static InputBoxCmdResult HandleInputBox(
        Keys keyData,
        Control? activeControl,
        TextBox inputBox,
        Action onShowTouchKeyboard,
        Action onConfirm,
        Action onAnnounceNewLine,
        Action onVibrateCursorMove)
    {
        if (activeControl != inputBox)
        {
            return InputBoxCmdResult.Unhandled;
        }

        if (keyData == Keys.Enter)
        {
            if (string.IsNullOrWhiteSpace(inputBox.Text))
            {
                onShowTouchKeyboard();
            }
            else
            {
                onConfirm();
            }

            return InputBoxCmdResult.Handled;
        }

        if (keyData == (Keys.Enter | Keys.Shift))
        {
            onAnnounceNewLine();
            onVibrateCursorMove();

            return InputBoxCmdResult.ForwardToBase;
        }

        return InputBoxCmdResult.Unhandled;
    }
}
