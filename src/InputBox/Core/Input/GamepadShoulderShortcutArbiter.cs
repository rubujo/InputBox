namespace InputBox.Core.Input;

/// <summary>
/// 仲裁肩鍵在單按翻頁、長按連發、單字跳轉修飾鍵與雙肩鍵組合之間的優先序，避免同一輪互動中出現重複或衝突行為。
/// </summary>
internal sealed class GamepadShoulderShortcutArbiter
{
    /// <summary>
    /// 左肩鍵在本輪互動中是否仍保留單按候選資格。
    /// </summary>
    private bool _leftTapArmed;

    /// <summary>
    /// 右肩鍵在本輪互動中是否仍保留單按候選資格。
    /// </summary>
    private bool _rightTapArmed;

    /// <summary>
    /// 左肩鍵是否已被長按連發消耗，避免放開時再次補發單按。
    /// </summary>
    private bool _leftRepeatConsumed;

    /// <summary>
    /// 右肩鍵是否已被長按連發消耗，避免放開時再次補發單按。
    /// </summary>
    private bool _rightRepeatConsumed;

    /// <summary>
    /// 本輪肩鍵是否已被其他修飾鍵語意占用。
    /// </summary>
    private bool _modifierUsed;

    /// <summary>
    /// 本輪互動是否已保留給雙肩鍵組合使用。
    /// </summary>
    private bool _dualShoulderComboReserved;

    /// <summary>
    /// 記錄本輪肩鍵按下，等待在放開時判定是否應視為單按。
    /// </summary>
    /// <param name="direction">方向：負值代表左肩鍵，正值代表右肩鍵。</param>
    public void ArmTap(int direction)
    {
        if (direction < 0)
        {
            _leftTapArmed = true;
            _leftRepeatConsumed = false;
            return;
        }

        if (direction > 0)
        {
            _rightTapArmed = true;
            _rightRepeatConsumed = false;
        }
    }

    /// <summary>
    /// 當肩鍵已觸發長按連發後，標記本輪互動不應再在放開時補發一次單按。
    /// </summary>
    /// <param name="direction">方向：負值代表左肩鍵，正值代表右肩鍵。</param>
    public void MarkRepeatConsumed(int direction)
    {
        if (direction < 0)
        {
            _leftTapArmed = false;
            _leftRepeatConsumed = true;
            return;
        }

        if (direction > 0)
        {
            _rightTapArmed = false;
            _rightRepeatConsumed = true;
        }
    }

    /// <summary>
    /// 當肩鍵被用作單字跳轉等修飾鍵時，阻止本輪互動再觸發翻頁。
    /// </summary>
    public void MarkModifierUsed()
    {
        _modifierUsed = true;
        _leftTapArmed = false;
        _rightTapArmed = false;
    }

    /// <summary>
    /// 當偵測到 LB + RB 同時成立時，保留給雙肩鍵組合，不再允許個別肩鍵翻頁。
    /// </summary>
    public void ReserveDualShoulderCombo()
    {
        _dualShoulderComboReserved = true;
        _leftTapArmed = false;
        _rightTapArmed = false;
    }

    /// <summary>
    /// 判斷目前是否應整體抑制肩鍵翻頁。
    /// </summary>
    /// <param name="isLeftHeld">目前左肩鍵是否仍處於按住狀態。</param>
    /// <param name="isRightHeld">目前右肩鍵是否仍處於按住狀態。</param>
    /// <returns>若目前應抑制肩鍵翻頁則回傳 true。</returns>
    public bool ShouldSuppressPaging(bool isLeftHeld, bool isRightHeld)
    {
        if (isLeftHeld && isRightHeld)
        {
            _dualShoulderComboReserved = true;
        }

        return _modifierUsed ||
               _dualShoulderComboReserved ||
               (isLeftHeld && isRightHeld);
    }

    /// <summary>
    /// 在肩鍵放開時判定是否應提交單按翻頁動作。
    /// </summary>
    /// <param name="direction">方向：負值代表左肩鍵，正值代表右肩鍵。</param>
    /// <param name="isLeftStillHeld">放開事件後左肩鍵是否仍按住。</param>
    /// <param name="isRightStillHeld">放開事件後右肩鍵是否仍按住。</param>
    /// <returns>若應提交單按翻頁則回傳 true。</returns>
    public bool TryConsumeTapOnRelease(int direction, bool isLeftStillHeld, bool isRightStillHeld)
    {
        bool shouldEmitTap = false;

        try
        {
            if (!_modifierUsed &&
                !_dualShoulderComboReserved &&
                !(isLeftStillHeld && isRightStillHeld))
            {
                shouldEmitTap = direction < 0 ?
                    _leftTapArmed && !_leftRepeatConsumed :
                    _rightTapArmed && !_rightRepeatConsumed;
            }

            return shouldEmitTap;
        }
        finally
        {
            if (direction < 0)
            {
                _leftTapArmed = false;
                _leftRepeatConsumed = false;
            }
            else if (direction > 0)
            {
                _rightTapArmed = false;
                _rightRepeatConsumed = false;
            }

            if (!isLeftStillHeld &&
                !isRightStillHeld)
            {
                _modifierUsed = false;
                _dualShoulderComboReserved = false;
            }
        }
    }

    /// <summary>
    /// 清除所有暫態狀態，供失焦、重新初始化或控制器重連後重新開始判定。
    /// </summary>
    public void Reset()
    {
        _leftTapArmed = false;
        _rightTapArmed = false;
        _leftRepeatConsumed = false;
        _rightRepeatConsumed = false;
        _modifierUsed = false;
        _dualShoulderComboReserved = false;
    }
}