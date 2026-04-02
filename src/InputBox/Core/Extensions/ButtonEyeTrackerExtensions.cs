using InputBox.Core.Configuration;
using InputBox.Resources;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace InputBox.Core.Extensions;

/// <summary>
/// 按鈕眼動儀回饋擴充方法，
/// 統一收斂對話框的 A11y Boilerplate
/// </summary>
public static class ButtonEyeTrackerExtensions
{
    /// <summary>
    /// 按鈕視覺狀態封裝類別，
    /// 包含當前是否懸停、按壓、凝視進度，以及相關的字型與描述資訊
    /// </summary>
    private sealed class ButtonVisualState
    {
        /// <summary>
        /// 是否懸停在按鈕上
        /// </summary>
        public bool IsHovered { get; set; }

        /// <summary>
        /// 是否按壓按鈕（包含實際的鼠標按壓或鍵盤焦點狀態下的強視覺反饋）
        /// </summary>
        public bool IsPressed { get; set; }

        /// <summary>
        /// 凝視進度（Dwell Progress），表示使用者注視按鈕的時間比例
        /// </summary>
        public float DwellProgress { get; set; }

        /// <summary>
        /// 動畫識別碼，用於取消過時的動畫回饋，確保視覺狀態與當前互動一致
        /// </summary>
        public long AnimId;

        /// <summary>
        /// 基礎無障礙描述，供不同互動狀態下的 AccessibleDescription 組合使用
        /// </summary>
        public string BaseDescription { get; set; } = string.Empty;

        /// <summary>
        /// 按鈕預設狀態下的字型（傳入 null 則 fallback 為按鈕現有字型）
        /// </summary>
        public Font? RegularFont { get; set; }

        /// <summary>
        /// 粗體字型，用於按鈕取得焦點或按壓時的強調視覺（傳入 null 則維持 RegularFont），建議外部預先建立好 Font 實例以避免頻繁產生造成效能問題
        /// </summary>
        public Font? BoldFont { get; set; }

        /// <summary>
        /// 當按鈕焦點狀態或懸停狀態改變時觸發的回呼，供外部連動處理（例如提示標籤顯示）
        /// </summary>
        public Action<bool>? OnFocusStateChanged { get; set; }

        /// <summary>
        /// 綁定 UI 表單生命週期的 CancellationToken，用於安全終止按鈕內部非同步動畫
        /// </summary>
        public CancellationToken FormCt { get; set; }

        /// <summary>
        /// 按鈕的原始內邊距，用於在視覺狀態改變時恢復原始佈局
        /// </summary>
        public Padding OriginalPadding { get; set; }
    }

    /// <summary>
    /// 按鈕與其視覺狀態的映射表，
    /// 用於管理按鈕的眼動儀回饋與無障礙狀態
    /// </summary>
    private static readonly ConditionalWeakTable<Button, ButtonVisualState> _btnStates = [];

    /// <summary>
    /// 為按鈕綁定眼動儀與無障礙完整事件（包含 Paint、Focus、Hover、Pressed 與 Dwell 凝視回饋）
    /// </summary>
    /// <param name="btn">要擴充的按鈕目標。</param>
    /// <param name="baseDescription">按鈕的基礎無障礙輔助描述（AccessibleDescription）。</param>
    /// <param name="regularFont">按鈕預設狀態下的字型（傳入 null 則 fallback 為按鈕現有字型）。</param>
    /// <param name="boldFont">按鈕取得焦點或按壓時的強調粗體字型。</param>
    /// <param name="formCt">綁定 UI 表單生命週期的 CancellationToken，用於安全終止按鈕內部非同步動畫。</param>
    /// <param name="onFocusStateChanged">當按鈕焦點狀態或懸停狀態改變時觸發的回呼，供外部連動處理（例如提示標籤顯示）。</param>
    public static void AttachEyeTrackerFeedback(
        this Button btn,
        string baseDescription,
        Font? regularFont,
        Font? boldFont,
        CancellationToken formCt,
        Action<bool>? onFocusStateChanged = null)
    {
        ButtonVisualState st = new()
        {
            BaseDescription = baseDescription,
            // Fallback。
            RegularFont = regularFont ?? btn.Font,
            BoldFont = boldFont,
            FormCt = formCt,
            OnFocusStateChanged = onFocusStateChanged,
            OriginalPadding = btn.Padding
        };
        _btnStates.AddOrUpdate(btn, st);

        // 避免重複綁定。
        btn.Paint -= Btn_Paint;
        btn.Paint += Btn_Paint;

        btn.GotFocus -= Btn_GotFocus;
        btn.GotFocus += Btn_GotFocus;

        btn.LostFocus -= Btn_LostFocus;
        btn.LostFocus += Btn_LostFocus;

        btn.MouseEnter -= Btn_MouseEnter;
        btn.MouseEnter += Btn_MouseEnter;

        btn.MouseLeave -= Btn_MouseLeave;
        btn.MouseLeave += Btn_MouseLeave;

        btn.MouseDown -= Btn_MouseDown;
        btn.MouseDown += Btn_MouseDown;

        btn.MouseUp -= Btn_MouseUp;
        btn.MouseUp += Btn_MouseUp;
    }

    /// <summary>
    /// 點擊後重置進度，
    /// 並執行視線重新接合（Gaze Re-engagement），
    /// 用於取代按鈕 Click 內部的手動清理
    /// </summary>
    /// <param name="btn">發生點擊的按鈕目標。</param>
    /// <returns>表示非同步重置與接合操作的工作任務。</returns>
    public static async Task CompleteClickAndReGazeAsync(this Button btn)
    {
        if (!_btnStates.TryGetValue(btn, out ButtonVisualState? st))
        {
            return;
        }

        Interlocked.Increment(ref st.AnimId);

        st.DwellProgress = 0f;

        btn.Invalidate();

        await Task.Yield();

        if (btn.IsDisposed ||
            !btn.Enabled)
        {
            return;
        }

        Point cursorPos = btn.PointToClient(Cursor.Position);

        if (btn.ClientRectangle.Contains(cursorPos))
        {
            st.IsHovered = true;

            StartAnimationFeedback(btn);
        }
        else
        {
            st.IsHovered = false;

            StopFeedback(btn);
        }
    }

    /// <summary>
    /// 取得鍵盤焦點時套用強視覺狀態，
    /// 並觸發焦點狀態改變回呼以連動提示標籤等 UI 元件的更新
    /// </summary>
    /// <param name="sender">事件觸發的按鈕</param>
    /// <param name="e">事件參數</param>
    private static void Btn_GotFocus(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            if (_btnStates.TryGetValue(btn, out var st))
            {
                st.OnFocusStateChanged?.Invoke(true);
            }

            // 如果同時處於懸停狀態，應維持懸停溫和視覺，不強制套用強烈視覺。
            if (_btnStates.TryGetValue(btn, out ButtonVisualState? state) &&
                state.IsHovered)
            {
                StartAnimationFeedback(btn);
            }
            else
            {
                ApplyStrongVisual(btn);
            }
        }
    }

    /// <summary>
    /// 失去鍵盤焦點時重置強視覺狀態，
    /// 並觸發焦點狀態改變回呼以連動提示標籤等 UI 元件的更新
    /// </summary>
    /// <param name="sender">事件觸發的按鈕</param>
    /// <param name="e">事件參數</param>
    private static void Btn_LostFocus(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            if (_btnStates.TryGetValue(btn, out ButtonVisualState? st))
            {
                st.IsPressed = false;
                st.OnFocusStateChanged?.Invoke(st.IsHovered);
            }

            StopFeedback(btn);
        }
    }

    /// <summary>
    /// 滑鼠進入按鈕區域時啟動懸停回饋，
    /// 並透過焦點狀態回呼讓外部 UI 可同步更新提示或邊框狀態，
    /// 提升眼動儀使用者的互動體驗
    /// </summary>
    /// <param name="sender">事件觸發的按鈕</param>
    /// <param name="e">事件參數</param>
    private static void Btn_MouseEnter(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            if (Form.ActiveForm != btn.FindForm())
            {
                return;
            }

            if (_btnStates.TryGetValue(btn, out var st))
            {
                st.IsHovered = true;
                st.OnFocusStateChanged?.Invoke(true);
            }

            StartAnimationFeedback(btn);
        }
    }

    /// <summary>
    /// 滑鼠離開按鈕區域時停止懸停回饋，
    /// 並透過焦點狀態回呼讓外部 UI 邏輯可依目前焦點狀態還原提示，
    /// 確保視覺反饋與使用者互動保持一致，
    /// 避免誤導眼動儀使用者的視線追蹤與互動判斷
    /// </summary>
    /// <param name="sender">事件觸發的按鈕</param>
    /// <param name="e">事件參數</param>
    private static void Btn_MouseLeave(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            if (_btnStates.TryGetValue(btn, out ButtonVisualState? st))
            {
                st.IsHovered = false;
                st.IsPressed = false;
                st.OnFocusStateChanged?.Invoke(btn.Focused);
            }

            StopFeedback(btn);
        }
    }

    /// <summary>
    /// 滑鼠左鍵按下時套用按壓狀態
    /// </summary>
    /// <param name="sender">事件觸發的按鈕</param>
    /// <param name="e">事件參數</param>
    private static void Btn_MouseDown(object? sender, MouseEventArgs e)
    {
        if (sender is Button btn &&
            e.Button == MouseButtons.Left)
        {
            if (_btnStates.TryGetValue(btn, out ButtonVisualState? st))
            {
                st.IsPressed = true;
            }

            StartAnimationFeedback(btn);
        }
    }

    /// <summary>
    /// 滑鼠左鍵放開時重置按壓狀態
    /// </summary>
    /// <param name="sender">事件觸發的按鈕</param>
    /// <param name="e">事件參數</param>
    private static void Btn_MouseUp(object? sender, MouseEventArgs e)
    {
        if (sender is Button btn &&
            e.Button == MouseButtons.Left)
        {
            if (_btnStates.TryGetValue(btn, out ButtonVisualState? st))
            {
                st.IsPressed = false;
            }

            StartAnimationFeedback(btn);
        }
    }

    /// <summary>
    /// 套用按壓或取得鍵盤焦點時的強視覺狀態（包含背景色彩切換與強調字型）
    /// </summary>
    /// <param name="btn">目標按鈕</param>
    private static void ApplyStrongVisual(Button btn)
    {
        if (!_btnStates.TryGetValue(btn, out ButtonVisualState? st))
        {
            return;
        }

        Interlocked.Increment(ref st.AnimId);

        st.DwellProgress = 0f;

        if (SystemInformation.HighContrast)
        {
            btn.BackColor = SystemColors.Highlight;
            btn.ForeColor = SystemColors.HighlightText;
        }
        else
        {
            bool isDark = btn.IsDarkModeActive();

            if (st.IsPressed)
            {
                btn.BackColor = isDark ?
                    Color.FromArgb(255, 200, 120) :
                    Color.FromArgb(28, 28, 28);
                btn.ForeColor = isDark ?
                    Color.Black :
                    Color.White;
            }
            else
            {
                btn.BackColor = isDark ?
                    Color.White :
                    Color.Black;
                btn.ForeColor = isDark ?
                    Color.Black :
                    Color.White;
            }
        }

        Font? bold = st.BoldFont;

        if (bold != null &&
            !ReferenceEquals(btn.Font, bold))
        {
            btn.Font = bold;
        }

        btn.AccessibleDescription = st.IsPressed ?
            $"{st.BaseDescription} ({Strings.A11y_State_Pressed})" :
            $"{st.BaseDescription} ({Strings.A11y_State_Focused})";

        btn.Padding = new Padding(0);
        btn.Invalidate();
    }

    /// <summary>
    /// 啟動眼動儀特化的懸停或注視進度條（Dwell Animation）回饋機制
    /// </summary>
    /// <param name="btn">目標按鈕</param>
    private static void StartAnimationFeedback(Button btn)
    {
        if (btn.IsDisposed ||
            !btn.Enabled)
        {
            return;
        }

        if (!_btnStates.TryGetValue(btn, out ButtonVisualState? st))
        {
            return;
        }

        if (st.IsPressed ||
            (btn.Focused && !st.IsHovered))
        {
            ApplyStrongVisual(btn);

            return;
        }

        long currentId = Interlocked.Increment(ref st.AnimId);

        st.DwellProgress = 0f;

        if (SystemInformation.HighContrast)
        {
            btn.BackColor = SystemColors.HotTrack;
            btn.ForeColor = SystemColors.HighlightText;
        }
        else
        {
            bool isDark = btn.IsDarkModeActive();

            btn.BackColor = isDark ?
                Color.FromArgb(60, 60, 60) :
                Color.FromArgb(220, 220, 220);
            btn.ForeColor = isDark ?
                Color.White :
                Color.Black;
        }

        Font? regular = st.RegularFont;

        if (regular != null &&
            !ReferenceEquals(btn.Font, regular))
        {
            btn.Font = regular;
        }

        btn.AccessibleDescription = $"{st.BaseDescription} ({Strings.A11y_State_Hover})";

        btn.Padding = new Padding(0);
        btn.Invalidate();

        btn.RunDwellAnimationAsync(
            id: currentId,
            animationIdGetter: () => Interlocked.Read(ref st.AnimId),
            progressSetter: p => st.DwellProgress = p,
            durationMs: 1000,
            ct: st.FormCt
        ).SafeFireAndForget();
    }

    /// <summary>
    /// 停止進度條回饋並恢復原本按鈕預設外觀或焦點狀態的外觀
    /// </summary>
    /// <param name="btn">目標按鈕</param>
    private static void StopFeedback(Button btn)
    {
        if (!_btnStates.TryGetValue(btn, out ButtonVisualState? st))
        {
            return;
        }

        Interlocked.Increment(ref st.AnimId);

        st.DwellProgress = 0f;

        if (btn.Focused)
        {
            ApplyStrongVisual(btn);

            return;
        }

        if (st.IsHovered)
        {
            StartAnimationFeedback(btn);

            return;
        }

        btn.BackColor = Color.Empty;
        btn.ForeColor = Color.Empty;

        Font? regular = st.RegularFont;

        if (regular != null &&
            !ReferenceEquals(btn.Font, regular))
        {
            btn.Font = regular;
        }

        btn.AccessibleDescription = st.BaseDescription;

        btn.Padding = st.OriginalPadding;
        btn.Invalidate();
    }

    /// <summary>
    /// 負責接管原生的 Paint 事件以繪製包含基礎邊框、
    /// 互動邊框、對比色、與 Dwell 進度條的所有按鈕客製化視覺
    /// </summary>
    /// <param name="sender">事件的發送者，通常是按鈕</param>
    /// <param name="e">包含繪圖資訊的事件參數</param>
    private static void Btn_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Button btn)
        {
            return;
        }

        try
        {
            _btnStates.TryGetValue(btn, out var st);

            Graphics g = e.Graphics;

            float scale = btn.DeviceDpi / AppSettings.BaseDpi;

            bool isDark = btn.IsDarkModeActive(),
                 isFocused = btn.Focused,
                 isHoveredOrDwell = (st?.IsHovered ?? false) ||
                    (st?.DwellProgress ?? 0f) > 0f;

            if (btn.TryDrawDisabledButtonCue(g, isDark, scale))
            {
                return;
            }

            Form? parentForm = btn.FindForm();

            bool isDefault = parentForm != null &&
                ReferenceEquals(parentForm.AcceptButton, btn) &&
                parentForm.ActiveControl is not Button &&
                btn.Enabled;

            if (!isFocused &&
                !isHoveredOrDwell &&
                !isDefault)
            {
                btn.DrawButtonBaseBorder(g, isDark, scale);
            }

            bool isStrongVisual = (st?.IsPressed ?? false) ||
                (isFocused && !(st?.IsHovered ?? false));

            if (isFocused ||
                isHoveredOrDwell ||
                isDefault)
            {
                Color borderColor = btn.GetButtonInteractiveBorderColor(isStrongVisual, isDark);

                btn.DrawButtonInteractiveBorder(
                    g,
                    borderColor,
                    scale,
                    out int inset,
                    out int borderThickness);

                if (!SystemInformation.HighContrast &&
                    (st?.IsPressed ?? false))
                {
                    btn.DrawPressedInnerCue(g, scale, inset, borderThickness);
                }
            }

            float progress = st?.DwellProgress ?? 0f;

            if (progress > 0f &&
                !(st?.IsPressed ?? false))
            {
                int barH = (int)(6 * scale),
                    barW = (int)(btn.Width * progress);

                if (barW > 0)
                {
                    Rectangle barRect = new(0, btn.Height - barH, barW, barH);

                    if (SystemInformation.HighContrast)
                    {
                        using Brush barBrush = new SolidBrush(SystemColors.HighlightText);

                        g.FillRectangle(barBrush, barRect);
                    }
                    else
                    {
                        Color baseColor = isDark ?
                                Color.LimeGreen :
                                Color.Green,
                              hatchColor = isDark ?
                                Color.DarkGreen :
                                Color.PaleGreen;

                        using Brush bgBrush = new SolidBrush(baseColor);
                        using Brush hatchBrush = new HatchBrush(
                            HatchStyle.BackwardDiagonal,
                            hatchColor,
                            Color.Transparent);

                        g.FillRectangle(bgBrush, barRect);
                        g.FillRectangle(hatchBrush, barRect);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EyeTrackerButton] Paint 失敗：{ex.Message}");
        }
    }
}