using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 專門用於數值輸入的對話框（無障礙數值輸入控制項分部）。
/// <para>本分部檔案包含內嵌的 AccessibleNumericUpDown 控制項。</para>
/// </summary>
internal sealed partial class NumericInputDialog
{
    /// <summary>
    /// 繼承自 NumericUpDown 以公開受保護的成員方法
    /// </summary>
    /// <param name="parent">父對話框實例</param>
    private sealed class AccessibleNumericUpDown(NumericInputDialog parent) : NumericUpDown
    {
        /// <summary>
        /// 父 NumericInputDialog 實例，用於回呼邊界撞牆通知。
        /// </summary>
        private readonly NumericInputDialog _parent = parent;

        /// <summary>
        /// 遞增數值，若已達上限則通知父對話框播放邊界回饋。
        /// </summary>
        public override void UpButton()
        {
            try
            {
                decimal oldValue = Value;

                base.UpButton();

                if (Value == oldValue)
                {
                    _parent.HandleBoundaryHit(true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] UpButton 失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 遞減數值，若已達下限則通知父對話框播放邊界回饋。
        /// </summary>
        public override void DownButton()
        {
            try
            {
                decimal oldValue = Value;

                base.DownButton();

                if (Value == oldValue)
                {
                    _parent.HandleBoundaryHit(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] DownButton 失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 主動觸發無障礙狀態變更通知
        /// </summary>
        public void NotifyAccessibilityChange()
        {
            try
            {
                // 主動通知輔助科技（AT）數值已變更。
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] NotifyAccessibilityChange 失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 強制驗證編輯文字，確保 Value 屬性與目前輸入內容同步
        /// </summary>
        public void ValidateValue()
        {
            try
            {
                ValidateEditText();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] ValidateValue 失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 覆寫滑鼠滾輪行為，確保「一格跳一格」
        /// </summary>
        /// <param name="e">MouseEventArgs</param>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            try
            {
                // 強制攔截並阻斷 Windows 系統的「一次捲動多行」設定（預設為 3）。
                if (e is HandledMouseEventArgs hme)
                {
                    hme.Handled = true;
                }

                // 手動精確執行單次增減，不調用 base.OnMouseWheel(e)。
                if (e.Delta > 0)
                {
                    UpButton();
                }
                else if (e.Delta < 0)
                {
                    DownButton();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] OnMouseWheel 失敗：{ex.Message}");
            }
        }
    }
}
