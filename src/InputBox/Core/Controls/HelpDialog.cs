using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Input;
using InputBox.Resources;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 說明對話框（WCAG 3.3.5 Help）
/// <para>提供鍵盤快速鍵與手把按鍵對應表，可透過 F1 或右鍵選單開啟。</para>
/// </summary>
internal sealed class HelpDialog : Form
{
    /// <summary>
    /// 遊戲控制器（由主視窗傳入，不由此對話框管理生命週期）
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IGamepadController? GamepadController { get; set; }

    /// <summary>
    /// 關閉按鈕
    /// </summary>
    private readonly Button _btnClose;

    /// <summary>
    /// 鍵盤快速鍵表格面板
    /// </summary>
    private readonly TableLayoutPanel _tlpKeyboard;

    /// <summary>
    /// 手把按鍵對應表格面板
    /// </summary>
    private readonly TableLayoutPanel _tlpGamepad;

    /// <summary>
    /// 可捲動內容區域（用於動態計算視窗高度）
    /// </summary>
    private readonly Panel _pnlScroll;

    /// <summary>
    /// 內容主版面（用於計算偏好尺寸）
    /// </summary>
    private readonly TableLayoutPanel _tlpContent;

    /// <summary>
    /// 底部按鈕列（Dock=Bottom，高度隨 DPI 動態調整）
    /// </summary>
    private readonly Panel _pnlFooter;

    /// <summary>
    /// 初始化說明對話框
    /// </summary>
    public HelpDialog()
    {
        DoubleBuffered = true;
        KeyPreview = true;
        AutoSize = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = Strings.Help_Title;
        Padding = new Padding(12);
        AccessibleName = Strings.Help_Title;
        AccessibleRole = AccessibleRole.Dialog;

        // 內容版面：垂直堆疊（鍵盤表頭、鍵盤表、手把表頭、手把表）。
        // 使用 Anchor=Top|Left 而非 Dock=Fill，讓捲動面板可正確計算捲動範圍。
        _tlpContent = new TableLayoutPanel()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Location = new Point(0, 0),
            ColumnCount = 1,
            RowCount = 4
        };
        _tlpContent.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _tlpContent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // keyboard heading
        _tlpContent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // keyboard table
        _tlpContent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // gamepad heading
        _tlpContent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // gamepad table

        // === 鍵盤快速鍵區段 ===
        Label lblKeyboardHeading = CreateSectionHeading(Strings.Help_Section_Keyboard);
        _tlpContent.Controls.Add(lblKeyboardHeading, 0, 0);

        Panel pnlKeyboard = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AccessibleName = Strings.Help_A11y_Keyboard_Group,
            AccessibleRole = AccessibleRole.Grouping
        };

        _tlpKeyboard = CreateTablePanel();
        pnlKeyboard.Controls.Add(_tlpKeyboard);
        _tlpContent.Controls.Add(pnlKeyboard, 0, 1);

        // === 手把按鍵對應區段 ===
        Label lblGamepadHeading = CreateSectionHeading(Strings.Help_Section_Gamepad);
        _tlpContent.Controls.Add(lblGamepadHeading, 0, 2);

        Panel pnlGamepad = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AccessibleName = Strings.Help_A11y_Gamepad_Group,
            AccessibleRole = AccessibleRole.Grouping
        };

        _tlpGamepad = CreateTablePanel();
        pnlGamepad.Controls.Add(_tlpGamepad);
        _tlpContent.Controls.Add(pnlGamepad, 0, 3);

        // === 可捲動內容面板 ===
        _pnlScroll = new Panel()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };
        _pnlScroll.Controls.Add(_tlpContent);

        // === 關閉按鈕（底部固定，不隨內容捲動）===
        _btnClose = new Button()
        {
            Text = Strings.Help_Btn_Close,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = Strings.Help_Btn_Close,
            AccessibleRole = AccessibleRole.PushButton,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.Empty,
            ForeColor = Color.Empty
        };
        _btnClose.FlatAppearance.BorderSize = 0;

        // Click：打斷目前動畫後關閉。
        _btnClose.Click += (s, e) =>
        {
            Interlocked.Increment(ref _closeAnimId);
            _closeDwellProgress = 0f;
            _btnClose.Invalidate();

            try
            {
                Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[說明] 關閉失敗：{ex.Message}");
            }
        };

        // MouseEnter / Leave：懸停視覺回饋。
        _btnClose.MouseEnter += (s, e) =>
        {
            if (ActiveForm != this)
            {
                return;
            }

            _closeIsHovered = true;
            StartCloseAnimationFeedback();
        };
        _btnClose.MouseLeave += (s, e) =>
        {
            _closeIsHovered = false;
            _closeIsPressed = false;
            StopCloseFeedback();
        };

        // MouseDown / Up：按壓狀態追蹤。
        _btnClose.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _closeIsPressed = true;
                StartCloseAnimationFeedback();
            }
        };
        _btnClose.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _closeIsPressed = false;
                StartCloseAnimationFeedback();
            }
        };

        // GotFocus / LostFocus：鍵盤焦點強烈視覺回饋。
        _btnClose.GotFocus += (s, e) => ApplyStrongCloseVisual();
        _btnClose.LostFocus += (s, e) =>
        {
            _closeIsPressed = false;
            StopCloseFeedback();
        };

        // Paint：自訂邊框與注視進度條。
        _btnClose.Paint += BtnClose_Paint;

        _pnlFooter = new Panel()
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(0, 8, 0, 0),
        };
        _pnlFooter.Controls.Add(_btnClose);

        CancelButton = _btnClose;

        // Dock=Bottom 必須比 Dock=Fill 先加入 Controls，
        // 才能正確佔據底部空間，讓 Fill 面板填滿剩餘區域。
        Controls.Add(_pnlScroll);
        Controls.Add(_pnlFooter);
    }

    /// <summary>
    /// 視窗建立完成後套用字型、填入資料列、計算最小尺寸。
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        try
        {
            base.OnHandleCreated(e);

            ApplyFont();
            PopulateTable(_tlpKeyboard, Strings.Help_Col_Key, Strings.Help_Col_Action, Strings.Help_Keyboard_Rows);
            PopulateTable(_tlpGamepad, Strings.Help_Col_Button, Strings.Help_Col_Action, Strings.Help_Gamepad_Rows);
            BindGamepadEvents();
            UpdateButtonMinimumSize();
            UpdateFooterHeight();
            UpdateFormSize();

            // 延遲位置修正，確保 Handle 完全建立後再執行。
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[說明] OnHandleCreated 延遲邏輯失敗：{ex.Message}");
                }
            });

            // 先解除再訂閱靜態事件，防止 Handle 重建時產生重複訂閱。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[說明] OnHandleCreated 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// DPI 變更時重新套用字型並更新最小尺寸。
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);

            this.SafeInvoke(() =>
            {
                try
                {
                    ApplyFont();
                    UpdateButtonMinimumSize();
                    UpdateFooterHeight();
                    UpdateFormSize();
                    ApplySmartPosition();
                    _btnClose.Invalidate();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[說明] OnDpiChanged 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[說明] OnDpiChanged 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 視窗顯示後執行智慧定位。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        ApplySmartPosition();

        // 對話框開啟時，WinForms 會自動聚焦 _btnClose（唯一可聚焦控制項），
        // 導致 GotFocus → ApplyStrongCloseVisual() → 背景色反轉（黑底）。
        // 此時使用者尚未主動導航到按鈕，應呈現中性外觀，僅靠 Paint 繪製焦點邊框。
        Interlocked.Increment(ref _closeAnimId);
        _closeDwellProgress = 0f;
        _btnClose.BackColor = Color.Empty;
        _btnClose.ForeColor = Color.Empty;

        Font? regularFont = _closeRegularFont;
        if (regularFont != null)
        {
            _btnClose.Font = regularFont;
        }

        _btnClose.Invalidate();
    }

    /// <summary>
    /// 視窗大小改變後重新定位。
    /// </summary>
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);

        ApplySmartPosition();
    }

    /// <summary>
    /// F1 / Esc 關閉對話框。
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.F1 or Keys.Escape)
        {
            e.SuppressKeyPress = true;
            Close();
        }
    }

    /// <summary>
    /// 關閉時解除手把事件。
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        UnbindGamepadEvents();
        Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();
        // 共享字體不由此對話框處置。
        Font? f = Interlocked.Exchange(ref _currentFont, null);
        // f 來自快取池，不處置，僅歸零欄位。
        _ = f;
        // 關閉按鈕字型（非快取池，需處置）。
        Interlocked.Exchange(ref _closeRegularFont, null)?.Dispose();
        Interlocked.Exchange(ref _closeBoldFont, null)?.Dispose();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            // 確保靜態事件在視窗控制項控制代碼銷毀時被絕對釋放。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    /// <summary>
    /// 目前使用的字型（來自快取池，不由此對話框處置）
    /// </summary>
    private Font? _currentFont;

    /// <summary>
    /// 對話框存續期間的取消權杖（用於 Dwell 動畫等背景任務）
    /// </summary>
    private CancellationTokenSource? _cts = new();

    /// <summary>
    /// 關閉按鈕目前使用的基礎字型（不含 Bold）
    /// </summary>
    private Font? _closeRegularFont;

    /// <summary>
    /// 關閉按鈕 Bold 狀態字型（焦點/按壓時使用，需獨立管理生命週期）
    /// </summary>
    private Font? _closeBoldFont;

    // ──── 關閉按鈕視覺狀態 ────
    private float _closeDwellProgress;
    private long _closeAnimId;
    private bool _closeIsHovered;
    private bool _closeIsPressed;

    /// <summary>
    /// 套用共享 A11y 字型到所有控制項，並同步建立按鈕用 Regular / Bold 字型副本。
    /// </summary>
    private void ApplyFont()
    {
        Font shared = MainForm.GetSharedA11yFont(DeviceDpi);

        Interlocked.Exchange(ref _currentFont, shared);

        // Regular 字型（供懸停與一般狀態使用）。
        Font? oldRegular = Interlocked.Exchange(ref _closeRegularFont,
            new Font(shared, FontStyle.Regular));
        oldRegular?.Dispose();

        // Bold 字型（供焦點/按壓強烈視覺使用）。
        Font? oldBold = Interlocked.Exchange(ref _closeBoldFont,
            new Font(shared, FontStyle.Bold));
        oldBold?.Dispose();

        Font = shared;
    }

    /// <summary>
    /// 建立區段標題 Label。
    /// </summary>
    private static Label CreateSectionHeading(string text)
    {
        return new Label()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
            AccessibleRole = AccessibleRole.StaticText
        };
    }

    /// <summary>
    /// 建立表格面板（含表頭列）。
    /// </summary>
    private static TableLayoutPanel CreateTablePanel()
    {
        return new TableLayoutPanel()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
    }

    /// <summary>
    /// 填入表格的表頭與資料列。
    /// </summary>
    /// <param name="table">目標 TableLayoutPanel</param>
    /// <param name="colHeader">第一欄標題（按鍵／按鈕）</param>
    /// <param name="actionHeader">第二欄標題（功能）</param>
    /// <param name="rowData">Tab 分隔的資料列字串，換行區隔各列</param>
    private static void PopulateTable(TableLayoutPanel table, string colHeader, string actionHeader, string rowData)
    {
        table.Controls.Clear();
        table.RowStyles.Clear();
        table.ColumnStyles.Clear();

        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 表頭列。
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(CreateCell(colHeader, isHeader: true), 0, 0);
        table.Controls.Add(CreateCell(actionHeader, isHeader: true), 1, 0);

        // 資料列。
        string[] rows = rowData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int rowIndex = 1;

        foreach (string row in rows)
        {
            string trimmed = row.TrimEnd('\r');
            int tabPos = trimmed.IndexOf('\t');

            if (tabPos < 0)
            {
                continue;
            }

            string keyPart = trimmed[..tabPos].Trim();
            string actionPart = trimmed[(tabPos + 1)..].Trim();

            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(CreateCell(keyPart), 0, rowIndex);
            table.Controls.Add(CreateCell(actionPart), 1, rowIndex);

            rowIndex++;
        }

        table.RowCount = rowIndex;
    }

    /// <summary>
    /// 建立單一表格儲存格 Label。
    /// </summary>
    private static Label CreateCell(string text, bool isHeader = false)
    {
        return new Label()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(6, 4, 6, 4),
            AccessibleRole = AccessibleRole.Cell,
            AccessibleName = text,
            Font = isHeader ? null : null // 繼承表格字型，表頭由 Paint 事件加粗
        };
    }

    /// <summary>
    /// 根據內容自然大小與螢幕工作區域，動態計算並套用視窗尺寸。
    /// 確保捲動條只在真正需要時出現，且關閉按鈕永遠可見。
    /// </summary>
    private void UpdateFormSize()
    {
        Rectangle workArea = Screen.GetWorkingArea(this);

        // 強制完成一次版面計算，以取得正確的偏好尺寸。
        _pnlScroll.SuspendLayout();
        _tlpContent.PerformLayout();
        _pnlScroll.ResumeLayout(false);

        Size contentPref = _tlpContent.GetPreferredSize(new Size(workArea.Width, 0));

        // 計算邊框與裝飾所需的額外空間。
        int frameW = SystemInformation.FrameBorderSize.Width * 2;
        int captionH = SystemInformation.CaptionHeight;
        int scrollBarW = SystemInformation.VerticalScrollBarWidth;
        int footerH = _pnlFooter.Height; // 已由 UpdateFooterHeight() 動態計算。

        // 視窗寬度：內容寬度 + 表單 Padding + 捲動條預留空間 + 框架。
        int formW = contentPref.Width + Padding.Horizontal + scrollBarW + frameW + 8;
        formW = Math.Max(formW, 360);
        formW = Math.Min(formW, workArea.Width - 40);

        // 視窗高度：內容高度 + 底部按鈕列 + 表單 Padding + 標題列 + 框架。
        // 上限設為可用高度的 70%，確保在 ROG Ally X 等小螢幕裝置（約 760px 高）上仍能舒適使用。
        int naturalH = contentPref.Height + footerH + Padding.Vertical + captionH + frameW + 8;
        int maxH = (int)(workArea.Height * 0.60f);
        int formH = Math.Clamp(naturalH, 200, maxH);

        Size = new Size(formW, formH);
    }

    /// <summary>
    /// 依據目前 DPI 與按鈕偏好高度，動態更新底部列高度，
    /// 確保關閉按鈕在任何 DPI 縮放等級下都不會被裁切。
    /// </summary>
    private void UpdateFooterHeight()
    {
        float scale = DeviceDpi / AppSettings.BaseDpi;

        // 取得按鈕在目前字型下的偏好高度，加上上方 Padding（8px × scale）與邊距（4px × scale）。
        int btnPrefH = _btnClose.GetPreferredSize(Size.Empty).Height;
        int needed = btnPrefH + (int)(12 * scale);

        // 基準高度同樣隨 DPI 縮放，確保在高解析度下不顯得過於緊縮。
        int baseline = (int)(44 * scale);

        _pnlFooter.Height = Math.Max(baseline, needed);
    }

    /// <summary>
    /// 縮放後確保視窗不超出螢幕可視區域。
    /// </summary>
    private void ApplySmartPosition()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        Rectangle workArea = Screen.GetWorkingArea(this);

        int x = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
        int y = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));

        if (x != Left || y != Top)
        {
            Location = new Point(x, y);
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            if (e.Category == UserPreferenceCategory.Accessibility ||
                e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.General)
            {
                this.SafeInvoke(() =>
                {
                    try
                    {
                        UpdateButtonMinimumSize();
                        UpdateFooterHeight();
                        UpdateFormSize();
                        ApplySmartPosition();
                        _btnClose.Invalidate();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[說明] SystemEvents 更新失敗：{ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[說明] SystemEvents 處理失敗：{ex.Message}");
        }
    }

    #region 手把事件

    /// <summary>
    /// 綁定手把事件（B / Back 關閉對話框）。
    /// </summary>
    private void BindGamepadEvents()
    {
        if (GamepadController == null)
        {
            return;
        }

        GamepadController.BPressed += OnGamepadClose;
        GamepadController.BackPressed += OnGamepadClose;
        GamepadController.UpPressed += OnGamepadScrollUp;
        GamepadController.UpRepeat += OnGamepadScrollUp;
        GamepadController.DownPressed += OnGamepadScrollDown;
        GamepadController.DownRepeat += OnGamepadScrollDown;
    }

    /// <summary>
    /// 解除手把事件。
    /// </summary>
    private void UnbindGamepadEvents()
    {
        if (GamepadController == null)
        {
            return;
        }

        GamepadController.BPressed -= OnGamepadClose;
        GamepadController.BackPressed -= OnGamepadClose;
        GamepadController.UpPressed -= OnGamepadScrollUp;
        GamepadController.UpRepeat -= OnGamepadScrollUp;
        GamepadController.DownPressed -= OnGamepadScrollDown;
        GamepadController.DownRepeat -= OnGamepadScrollDown;
    }

    /// <summary>
    /// 手把 LS 上 / D-pad 上：向上捲動內容面板。
    /// </summary>
    private void OnGamepadScrollUp()
    {
        this.SafeBeginInvoke(() =>
        {
            if (IsDisposed || !_pnlScroll.IsHandleCreated)
            {
                return;
            }

            int step = (int)Math.Max(20, 40 * (DeviceDpi / AppSettings.BaseDpi));
            int newY = Math.Max(0, -_pnlScroll.AutoScrollPosition.Y - step);
            _pnlScroll.AutoScrollPosition = new Point(0, newY);
        });
    }

    /// <summary>
    /// 手把 LS 下 / D-pad 下：向下捲動內容面板。
    /// </summary>
    private void OnGamepadScrollDown()
    {
        this.SafeBeginInvoke(() =>
        {
            if (IsDisposed || !_pnlScroll.IsHandleCreated)
            {
                return;
            }

            int step = (int)Math.Max(20, 40 * (DeviceDpi / AppSettings.BaseDpi));
            int maxY = Math.Max(0,
                _pnlScroll.DisplayRectangle.Height - _pnlScroll.ClientSize.Height);
            int newY = Math.Min(maxY, -_pnlScroll.AutoScrollPosition.Y + step);
            _pnlScroll.AutoScrollPosition = new Point(0, newY);
        });
    }

    /// <summary>
    /// 手把 B / Back 關閉對話框。
    /// </summary>
    private void OnGamepadClose()
    {
        this.SafeBeginInvoke(async () =>
        {
            try
            {
                if (IsDisposed)
                {
                    return;
                }

                ApplyStrongCloseVisual();
                await Task.Delay(80, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
                await this.InvokeAsync(() =>
                {
                    if (!IsDisposed)
                    {
                        Close();
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[說明] 手把關閉失敗：{ex.Message}");
            }
        });
    }

    #endregion

    #region 關閉按鈕視覺特效

    /// <summary>
    /// 預先計算按鈕 Bold 文字最大寬度，鎖定 MinimumSize 防止字型加粗時佈局抖動。
    /// </summary>
    private void UpdateButtonMinimumSize()
    {
        Font? regularFont = _closeRegularFont;
        Font? boldFont = _closeBoldFont;

        if (regularFont == null || boldFont == null)
        {
            return;
        }

        using Graphics g = _btnClose.CreateGraphics();

        SizeF regularSize = g.MeasureString(_btnClose.Text, regularFont);
        SizeF boldSize = g.MeasureString(_btnClose.Text, boldFont);

        int maxW = (int)Math.Ceiling(Math.Max(regularSize.Width, boldSize.Width)) +
                   _btnClose.Padding.Horizontal + 12;
        int maxH = (int)Math.Ceiling(Math.Max(regularSize.Height, boldSize.Height)) +
                   _btnClose.Padding.Vertical + 6;

        _btnClose.MinimumSize = new Size(maxW, maxH);
    }

    /// <summary>
    /// 焦點或按壓時套用強烈靜態視覺（主題感知反轉 + Bold 字型）。
    /// </summary>
    private void ApplyStrongCloseVisual()
    {
        if (_btnClose.IsDisposed)
        {
            return;
        }

        Interlocked.Increment(ref _closeAnimId);
        _closeDwellProgress = 0f;

        if (SystemInformation.HighContrast)
        {
            _btnClose.BackColor = SystemColors.Highlight;
            _btnClose.ForeColor = SystemColors.HighlightText;
        }
        else
        {
            bool isDark = _btnClose.IsDarkModeActive();
            _btnClose.BackColor = isDark ? Color.White : Color.Black;
            _btnClose.ForeColor = isDark ? Color.Black : Color.White;
        }

        // 使用預建 Bold 字型，避免每次呼叫建立新物件洩漏 GDI 資源。
        Font? boldFont = _closeBoldFont;
        if (boldFont != null)
        {
            _btnClose.Font = boldFont;
        }

        _btnClose.Invalidate();
    }

    /// <summary>
    /// 懸停（Hover）或一般互動時啟動 Dwell 動畫。
    /// </summary>
    private void StartCloseAnimationFeedback()
    {
        if (_btnClose.IsDisposed)
        {
            return;
        }

        // 按壓中（MouseDown）或純鍵盤焦點（未懸停）：套用強烈靜態視覺，不啟動 Dwell。
        if (_closeIsPressed || (_btnClose.Focused && !_closeIsHovered))
        {
            ApplyStrongCloseVisual();
            return;
        }

        if (SystemInformation.HighContrast)
        {
            _btnClose.BackColor = SystemColors.HotTrack;
            _btnClose.ForeColor = SystemColors.HighlightText;
        }
        else
        {
            bool isDark = _btnClose.IsDarkModeActive();
            _btnClose.BackColor = isDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220);
            _btnClose.ForeColor = isDark ? Color.White : Color.Black;
        }

        Font? regularFont = _closeRegularFont;
        if (regularFont != null)
        {
            _btnClose.Font = regularFont;
        }

        long currentId = Interlocked.Increment(ref _closeAnimId);
        _closeDwellProgress = 0f;
        _btnClose.Invalidate();

        CancellationToken ct = _cts?.Token ?? CancellationToken.None;
        _btnClose.RunDwellAnimationAsync(
            id: currentId,
            animationIdGetter: () => Interlocked.Read(ref _closeAnimId),
            progressSetter: p => _closeDwellProgress = p,
            durationMs: 1000,
            ct: ct
        ).SafeFireAndForget();
    }

    /// <summary>
    /// 停止回饋：恢復預設外觀或依焦點/懸停狀態調整。
    /// </summary>
    private void StopCloseFeedback()
    {
        Interlocked.Increment(ref _closeAnimId);
        _closeDwellProgress = 0f;

        if (_btnClose.Focused)
        {
            ApplyStrongCloseVisual();
            return;
        }

        if (_closeIsHovered)
        {
            StartCloseAnimationFeedback();
            return;
        }

        // 恢復預設（由主題引擎決定）。
        _btnClose.BackColor = Color.Empty;
        _btnClose.ForeColor = Color.Empty;

        Font? regularFont = _closeRegularFont;
        if (regularFont != null)
        {
            _btnClose.Font = regularFont;
        }

        _btnClose.Invalidate();
    }

    /// <summary>
    /// 自訂繪製：基礎邊框 → 焦點/懸停邊框 → Dwell 進度條。
    /// </summary>
    private void BtnClose_Paint(object? sender, PaintEventArgs e)
    {
        Button btn = _btnClose;
        Graphics g = e.Graphics;

        // 動態存取最新 DPI，避免靜態捕獲舊 DPI 導致跨螢幕拖曳時繪圖偏移。
        float scale = btn.DeviceDpi / AppSettings.BaseDpi;
        bool isDark = btn.IsDarkModeActive();
        bool isFocused = btn.Focused;
        bool isHoveredOrDwell = _closeIsHovered || (_closeDwellProgress > 0f);

        // ── 基礎邊框（只在非焦點、非懸停時繪製，與焦點邊框互斥）──
        // 確保按鈕在靜態狀態下仍具備物理辨識度，不融入背景。
        if (!isFocused && !isHoveredOrDwell)
        {
            int thickness = (int)Math.Max(1, scale);

            using Pen basePen = new(
                SystemInformation.HighContrast ?
                    SystemColors.WindowFrame :
                    (isDark ? Color.DimGray : Color.DarkGray),
                thickness);

            g.DrawRectangle(basePen, 0, 0, btn.Width - 1, btn.Height - 1);
        }

        // ── 焦點 / 懸停邊框（3px，與 BtnCopy 完全對齊）──
        // 淺色模式（黑底控制項）用 Cyan；深色模式（白底控制項）用 RoyalBlue。
        if (isFocused || isHoveredOrDwell)
        {
            int borderThickness = (int)Math.Max(3, 3 * scale);
            int inset = (int)Math.Max(2, 2 * scale);

            using Pen focusPen = new(
                SystemInformation.HighContrast ?
                    SystemColors.HighlightText :
                    (isDark ? Color.RoyalBlue : Color.Cyan),
                borderThickness);

            g.DrawRectangle(focusPen,
                inset,
                inset,
                btn.Width - (inset * 2) - 1,
                btn.Height - (inset * 2) - 1);
        }

        // ── Dwell 進度條（懸停中且非按壓狀態）──
        // 雙重編碼（CVD 補償）：實心背景 + BackwardDiagonal 條紋紋理。
        float progress = _closeDwellProgress;
        if (progress > 0f && !_closeIsPressed)
        {
            int barH = (int)(6 * scale);
            int barW = (int)(btn.Width * progress);

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
                    // 懸停灰底對比：淺色懸停（#DCDCDC）→ SaddleBrown 5.18:1；深色懸停（#3C3C3C）→ DarkOrange 4.73:1。
                    // 均符合 WCAG 1.4.11 非文字 UI 組件最低 3:1 需求。
                    Color baseColor = isDark ? Color.DarkOrange : Color.SaddleBrown;
                    Color hatchColor = isDark ? Color.OrangeRed : Color.DarkOrange;

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

    #endregion
}
