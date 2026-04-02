using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 說明對話框（WCAG 3.3.5 Help）
/// <para>提供鍵盤快速鍵與控制器按鍵對應表，可透過 F1 或右鍵選單開啟。</para>
/// </summary>
internal sealed class HelpDialog : Form
{
    /// <summary>
    /// 說明視窗的基準最小寬度（96 DPI）
    /// </summary>
    private const int BaseDialogMinWidth = 760;

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
    /// 控制器按鍵對應表格面板
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
    /// 已套用的 DPI 快取，避免重複計算最小尺寸
    /// </summary>
    private float _lastAppliedDpi;

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
        AccessibleDescription = Strings.Help_A11y_Dialog_Desc;

        // 繼承圖示：優先從主視窗繼承，保持應用程式視覺識別的一致性。
        Icon = Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.Icon ??
            ActiveForm?.Icon;
        AccessibleRole = AccessibleRole.Dialog;

        // 內容版面：垂直堆疊（鍵盤表頭、鍵盤表、控制器表頭、控制器表）。
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
        // keyboard heading。
        _tlpContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // keyboard table。
        _tlpContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // gamepad heading。
        _tlpContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // gamepad table。
        _tlpContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 鍵盤快速鍵區段。
        Label lblKeyboardHeading = CreateSectionHeading(Strings.Help_Section_Keyboard);

        _tlpContent.Controls.Add(lblKeyboardHeading, 0, 0);

        Panel pnlKeyboard = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AccessibleName = Strings.Help_A11y_Keyboard_Group,
            AccessibleDescription = Strings.Help_A11y_Keyboard_Group_Desc,
            AccessibleRole = AccessibleRole.Grouping,
            Padding = new Padding(0, 0, 0, 4)
        };

        _tlpKeyboard = CreateTablePanel();

        pnlKeyboard.Controls.Add(_tlpKeyboard);

        _tlpContent.Controls.Add(pnlKeyboard, 0, 1);

        // 控制器按鍵對應區段。
        Label lblGamepadHeading = CreateSectionHeading(Strings.Help_Section_Gamepad);

        _tlpContent.Controls.Add(lblGamepadHeading, 0, 2);

        Panel pnlGamepad = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AccessibleName = Strings.Help_A11y_Gamepad_Group,
            AccessibleDescription = Strings.Help_A11y_Gamepad_Group_Desc,
            AccessibleRole = AccessibleRole.Grouping,
            Padding = new Padding(0, 0, 0, 4)
        };

        _tlpGamepad = CreateTablePanel();
        pnlGamepad.Controls.Add(_tlpGamepad);
        _tlpContent.Controls.Add(pnlGamepad, 0, 3);

        // 可捲動內容面板。
        _pnlScroll = new Panel()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AutoScrollMargin = new Size(0, 16),
            Margin = Padding.Empty
        };
        _pnlScroll.Controls.Add(_tlpContent);

        // 關閉按鈕（底部固定，不隨內容捲動）。
        _btnClose = new Button()
        {
            Text = Strings.Help_Btn_Close,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty, // 移除預設外距避免被底部面板裁切
            FlatStyle = FlatStyle.Flat,
            AccessibleName = Strings.Help_Btn_Close,
            AccessibleRole = AccessibleRole.PushButton,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.Empty,
            ForeColor = Color.Empty
        };
        _btnClose.FlatAppearance.BorderSize = 0;

        _btnClose.Click += (s, e) =>
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[說明] 關閉失敗：{ex.Message}");
            }
        };

        _pnlFooter = new Panel()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Height = 44,
            Padding = new Padding(8, 6, 8, 6),
        };

        // TableLayoutPanel 讓按鈕右對齊且垂直置中（Anchor=Right 在 TLP cell 中自動垂直置中）。
        TableLayoutPanel tlpFooter = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
        };
        tlpFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlpFooter.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tlpFooter.Controls.Add(_btnClose, 0, 0);
        _pnlFooter.Controls.Add(tlpFooter);

        CancelButton = _btnClose;

        // 使用 Root TableLayoutPanel 徹底解決 WinForms Docking 重疊與高度被 Margin 吃掉的問題。
        TableLayoutPanel tlpRoot = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        tlpRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlpRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tlpRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        tlpRoot.Controls.Add(_pnlScroll, 0, 0);
        tlpRoot.Controls.Add(_pnlFooter, 0, 1);

        Controls.Add(tlpRoot);

        // 應用程式切回前景時恢復控制器輪詢（防止 alt-tab 後控制器停滯）。
        // 加入 50ms 延遲確保系統焦點切換完成後再呼叫 Resume，與 NumericInputDialog 一致。
        Activated += (s, e) =>
        {
            Task.Run(async () =>
            {
                try
                {
                    CancellationToken token = _cts?.Token ?? CancellationToken.None;

                    token.ThrowIfCancellationRequested();

                    await Task.Delay(50, token);

                    await this.SafeInvokeAsync(() =>
                    {
                        try
                        {
                            GamepadController?.Resume();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[說明] 恢復控制器失敗：{ex.Message}");
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不進行報錯。
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[說明] Activated 恢復控制器失敗：{ex.Message}");
                }
            },
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
        };

        // 應用程式切至背景時暫停控制器輪詢，防止背景誤觸。
        Deactivate += (s, e) =>
        {
            try
            {
                this.SafeBeginInvoke(() =>
                {
                    try
                    {
                        GamepadController?.Pause();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[說明] 暫停控制器失敗：{ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[說明] Deactivate 處理失敗：{ex.Message}");
            }
        };
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

            string keyboardRows = string.Format(
                Strings.Help_Keyboard_Rows,
                GlobalHotKeyService.GetHotKeyDisplayString(" + "));

            PopulateTable(_tlpKeyboard, Strings.Help_Col_Key, Strings.Help_Col_Action, keyboardRows);
            PopulateTable(_tlpGamepad, Strings.Help_Col_Button, Strings.Help_Col_Action, Strings.Help_Gamepad_Rows);
            BindGamepadEvents();
            UpdateButtonMinimumSize();
            UpdateFooterHeight();
            UpdateMinimumSize();

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
                    UpdateMinimumSize();
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

        // 明確將焦點指向關閉按鈕。
        // WinForms 開啟對話框時雖會自動聚焦唯一可聚焦控制項，但不保證在所有
        // 主題引擎與協助技術環境下皆如此。顯式呼叫確保 UIA 焦點事件觸發，
        // 讓螢幕閱讀器能正確播報按鈕名稱與 AccessibleDescription。
        if (_btnClose.CanFocus &&
            !_btnClose.Focused)
        {
            // Focus() 會觸發 GotFocus → ApplyStrongCloseVisual()，正確呈現聚焦強烈視覺。
            // 不可在 Focus() 後手動重置顏色至 Color.Empty：在深色模式下 Color.Empty 可能回退
            // 為系統預設 ButtonFace（淺灰）+ ControlText（黑字），造成按鈕在深色對話框中
            // 呈現淺色外觀，且 Paint 選用的 MediumBlue 邊框在淺灰底對深色背景中對比失準。
            _btnClose.Focus();
        }
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
    /// 在 ProcessDialogKey（方向鍵焦點導航）之前攔截，確保 ↑↓ 可捲動內容面板。
    /// PageUp／Down／Home／End 由 OnKeyDown 處理（不受 ProcessDialogKey 干擾）。
    /// F1／Esc 由 <see cref="OnKeyDown"/> 處理。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        const int WM_KEYDOWN = 0x0100;

        if (msg.Msg == WM_KEYDOWN)
        {
            Keys key = keyData & Keys.KeyCode;

            if (key is Keys.Up or Keys.Down)
            {
                float scale = DeviceDpi / AppSettings.BaseDpi;

                int step = (int)Math.Max(20, 40 * scale),
                    maxY = Math.Max(
                        0,
                        _pnlScroll.DisplayRectangle.Height - _pnlScroll.ClientSize.Height),
                    current = -_pnlScroll.AutoScrollPosition.Y,
                    newY = Math.Clamp(current + (key == Keys.Up ? -step : step), 0, maxY);

                _pnlScroll.AutoScrollPosition = new Point(0, newY);

                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.F1 or Keys.Escape)
        {
            e.SuppressKeyPress = true;

            Close();

            return;
        }

        // PageUp／PageDown／Home／End 捲動內容面板。
        // ↑↓ 已由 ProcessCmdKey 處理（避免被 ProcessDialogKey 焦點導航消耗）。
        float scale = DeviceDpi / AppSettings.BaseDpi;

        int step = (int)Math.Max(20, 40 * scale),
            pageStep = Math.Max(100, _pnlScroll.ClientSize.Height - step);

        int delta = e.KeyCode switch
        {
            Keys.PageUp => -pageStep,
            Keys.PageDown => pageStep,
            Keys.Home => int.MinValue,
            Keys.End => int.MaxValue,
            _ => 0,
        };

        if (delta == 0)
        {
            return;
        }

        e.SuppressKeyPress = true;

        int maxY = Math.Max(
                0,
                _pnlScroll.DisplayRectangle.Height - _pnlScroll.ClientSize.Height),
            current = -_pnlScroll.AutoScrollPosition.Y,
            newY = Math.Clamp(
                delta == int.MinValue ? 0 :
                delta == int.MaxValue ? maxY :
                current + delta,
                0,
                maxY);

        _pnlScroll.AutoScrollPosition = new Point(0, newY);
    }

    /// <summary>
    /// 關閉時解除控制器事件。
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

        // 關閉按鈕字型來自共享快取池，僅歸零引用。
        _ = Interlocked.Exchange(ref _closeRegularFont, null);
        _ = Interlocked.Exchange(ref _closeBoldFont, null);
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
    /// 關閉按鈕目前使用的基礎字型（來自共享快取）
    /// </summary>
    private Font? _closeRegularFont;

    /// <summary>
    /// 關閉按鈕 Bold 狀態字型（焦點/按壓時使用，來自共享快取）
    /// </summary>
    private Font? _closeBoldFont;

    // 關閉按鈕視覺狀態已由 ButtonEyeTrackerExtensions 統一接管。

    /// <summary>
    /// 套用共享 A11y 字型到所有控制項，並同步取得按鈕用 Regular／Bold 共享字型
    /// </summary>
    private void ApplyFont()
    {
        Font shared = MainForm.GetSharedA11yFont(DeviceDpi);

        Interlocked.Exchange(ref _currentFont, shared);

        FontFamily family = shared.FontFamily;

        _ = Interlocked.Exchange(
            ref _closeBoldFont,
            MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, family));

        Font = shared;

        _btnClose.Font = shared;
        _btnClose.AttachEyeTrackerFeedback(
            baseDescription: Strings.Help_Btn_Close,
            regularFont: _closeRegularFont,
            boldFont: _closeBoldFont,
            formCt: _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// 建立區段標題 Label
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
    /// 建立表格面板（含表頭列）
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
    /// 填入表格的表頭與資料列
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

            string keyPart = trimmed[..tabPos].Trim(),
                actionPart = trimmed[(tabPos + 1)..].Trim();

            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(CreateCell(keyPart), 0, rowIndex);
            table.Controls.Add(CreateCell(actionPart), 1, rowIndex);

            rowIndex++;
        }

        table.RowCount = rowIndex;
    }

    /// <summary>
    /// 建立單一表格儲存格 Label
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
            // 繼承表格字型，表頭由 Paint 事件加粗。
            Font = isHeader ? null : null
        };
    }

    /// <summary>
    /// 根據內容自然大小與螢幕工作區域，動態計算並套用視窗尺寸
    /// 確保捲動條只在真正需要時出現，且關閉按鈕永遠可見
    /// </summary>
    private void UpdateMinimumSize(bool forceRecalculate = false)
    {
        float currentDpi = DeviceDpi;

        if (!DialogLayoutHelper.TryBeginDpiLayout(currentDpi, ref _lastAppliedDpi, forceRecalculate))
        {
            return;
        }

        Rectangle workArea = Screen.GetWorkingArea(this);

        // 強制完成一次版面計算，以取得正確的偏好尺寸。
        _pnlScroll.SuspendLayout();
        _tlpContent.PerformLayout();
        _pnlScroll.ResumeLayout(false);

        Size contentPref = _tlpContent.GetPreferredSize(new Size(workArea.Width, 0));

        // 計算邊框與裝飾所需的額外空間。
        int frameW = SystemInformation.FrameBorderSize.Width * 2,
            frameH = SystemInformation.FrameBorderSize.Height * 2,
            captionH = SystemInformation.CaptionHeight,
            scrollBarW = SystemInformation.VerticalScrollBarWidth,
            // 已由 UpdateFooterHeight() 動態計算。
            footerH = _pnlFooter.Height,
            // 視窗寬度：內容寬度 + 表單 Padding + 捲動條預留空間 + 框架。
            formW = contentPref.Width + Padding.Horizontal + scrollBarW + frameW + 8;

        float scale = DeviceDpi / AppSettings.BaseDpi;

        int desiredMinWidth = (int)(BaseDialogMinWidth * scale);

        (int maxFitWidth, _) = DialogLayoutHelper.GetMaxFitSize(workArea);

        formW = Math.Max(formW, desiredMinWidth);
        formW = Math.Min(formW, maxFitWidth);

        // 視窗高度：內容高度 + 底部按鈕列 + 表單 Padding + 標題列 + 框架。
        // 上限設為可用高度的 45%，確保在 ROG Ally X 等小螢幕裝置（約 760px 高）開啟 OSK 時仍能完整顯示。
        int naturalH = contentPref.Height + footerH + Padding.Vertical + captionH + frameH + 8,
            maxH = Math.Max(320, (int)(workArea.Height * 0.45f)),
            desiredMinHeight = (int)(300 * scale),
            // 邊界檢查：確保最小值不超過最大值，防止 Math.Clamp 拋出異常。
            finalMaxH = Math.Max(desiredMinHeight, maxH),
            formH = Math.Clamp(naturalH, desiredMinHeight, finalMaxH);

        Size = new Size(formW, formH);
    }

    /// <summary>
    /// 依據目前 DPI 與按鈕偏好高度，動態更新底部列高度
    /// 確保關閉按鈕在任何 DPI 縮放等級下都不會被裁切
    /// </summary>
    private void UpdateFooterHeight()
    {
        float scale = DeviceDpi / AppSettings.BaseDpi;

        // 取得按鈕在目前字型下的偏好高度，加上 Panel 所需的上下留白（自動 DPI 縮放值）
        // 因為已將按鈕的 Margin 設為 Empty，我們只需要考慮 _pnlFooter 的垂直 Padding
        int btnPrefH = _btnClose.GetPreferredSize(Size.Empty).Height,
            needed = btnPrefH + _pnlFooter.Padding.Vertical,
            // 基準高度同樣隨 DPI 縮放，確保在高解析度下不顯得過於緊縮。
            baseline = (int)(44 * scale);

        _pnlFooter.Height = Math.Max(baseline, needed);
    }

    /// <summary>
    /// 縮放後確保視窗不超出螢幕可視區域
    /// </summary>
    private void ApplySmartPosition()
    {
        if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
        {
            Location = clampedLocation;
        }
    }

    /// <summary>
    /// 系統偏好設定變更時重新量測按鈕、頁尾與對話框尺寸
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">系統偏好設定事件參數。</param>
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
                        UpdateMinimumSize(forceRecalculate: true);
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

    #region 控制器事件

    /// <summary>
    /// 綁定控制器事件（B／Back 關閉；Start／A 確認關閉；LT／RT 翻頁捲動；↑↓ 行捲動）
    /// </summary>
    private void BindGamepadEvents()
    {
        if (GamepadController == null)
        {
            return;
        }

        GamepadController.BPressed += OnGamepadClose;
        GamepadController.BackPressed += OnGamepadClose;
        GamepadController.StartPressed += OnGamepadClose;
        GamepadController.APressed += OnGamepadClose;
        GamepadController.LeftTriggerPressed += OnGamepadPageUp;
        GamepadController.RightTriggerPressed += OnGamepadPageDown;
        GamepadController.UpPressed += OnGamepadScrollUp;
        GamepadController.UpRepeat += OnGamepadScrollUp;
        GamepadController.DownPressed += OnGamepadScrollDown;
        GamepadController.DownRepeat += OnGamepadScrollDown;
    }

    /// <summary>
    /// 解除控制器事件
    /// </summary>
    private void UnbindGamepadEvents()
    {
        if (GamepadController == null)
        {
            return;
        }

        GamepadController.BPressed -= OnGamepadClose;
        GamepadController.BackPressed -= OnGamepadClose;
        GamepadController.StartPressed -= OnGamepadClose;
        GamepadController.APressed -= OnGamepadClose;
        GamepadController.LeftTriggerPressed -= OnGamepadPageUp;
        GamepadController.RightTriggerPressed -= OnGamepadPageDown;
        GamepadController.UpPressed -= OnGamepadScrollUp;
        GamepadController.UpRepeat -= OnGamepadScrollUp;
        GamepadController.DownPressed -= OnGamepadScrollDown;
        GamepadController.DownRepeat -= OnGamepadScrollDown;
    }

    /// <summary>
    /// 控制器 LS 上／D-pad 上：向上捲動內容面板（行捲動）
    /// </summary>
    private void OnGamepadScrollUp()
    {
        this.SafeBeginInvoke(() =>
        {
            if (IsDisposed ||
                !_pnlScroll.IsHandleCreated)
            {
                return;
            }

            int step = (int)Math.Max(20, 40 * (DeviceDpi / AppSettings.BaseDpi)),
                newY = Math.Max(0, -_pnlScroll.AutoScrollPosition.Y - step);

            _pnlScroll.AutoScrollPosition = new Point(0, newY);
        });
    }

    /// <summary>
    /// 控制器 LS 下／D-pad 下：向下捲動內容面板（行捲動）
    /// </summary>
    private void OnGamepadScrollDown()
    {
        this.SafeBeginInvoke(() =>
        {
            if (IsDisposed ||
                !_pnlScroll.IsHandleCreated)
            {
                return;
            }

            int step = (int)Math.Max(20, 40 * (DeviceDpi / AppSettings.BaseDpi)),
                maxY = Math.Max(
                    0,
                    _pnlScroll.DisplayRectangle.Height - _pnlScroll.ClientSize.Height),
                newY = Math.Min(maxY, -_pnlScroll.AutoScrollPosition.Y + step);

            _pnlScroll.AutoScrollPosition = new Point(0, newY);
        });
    }

    /// <summary>
    /// 控制器 LT：向上翻頁捲動（等同 PageUp）
    /// </summary>
    private void OnGamepadPageUp()
    {
        this.SafeBeginInvoke(() =>
        {
            if (IsDisposed ||
                !_pnlScroll.IsHandleCreated)
            {
                return;
            }

            float scale = DeviceDpi / AppSettings.BaseDpi;

            int step = (int)Math.Max(20, 40 * scale),
                pageStep = Math.Max(100, _pnlScroll.ClientSize.Height - step),
                newY = Math.Max(0, -_pnlScroll.AutoScrollPosition.Y - pageStep);

            _pnlScroll.AutoScrollPosition = new Point(0, newY);
        });
    }

    /// <summary>
    /// 控制器 RT：向下翻頁捲動（等同 PageDown）。
    /// </summary>
    private void OnGamepadPageDown()
    {
        this.SafeBeginInvoke(() =>
        {
            if (IsDisposed ||
                !_pnlScroll.IsHandleCreated)
            {
                return;
            }

            float scale = DeviceDpi / AppSettings.BaseDpi;

            int step = (int)Math.Max(20, 40 * scale),
                pageStep = Math.Max(100, _pnlScroll.ClientSize.Height - step),
                maxY = Math.Max(
                    0,
                    _pnlScroll.DisplayRectangle.Height - _pnlScroll.ClientSize.Height),
                newY = Math.Min(maxY, -_pnlScroll.AutoScrollPosition.Y + pageStep);

            _pnlScroll.AutoScrollPosition = new Point(0, newY);
        });
    }

    /// <summary>
    /// 控制器 B／Back／Start／A：關閉對話框
    /// </summary>
    private void OnGamepadClose()
    {
        this.SafeBeginInvoke(() =>
        {
            if (IsDisposed)
            {
                return;
            }

            Close();
        });
    }

    #endregion

    #region 關閉按鈕視覺特效

    /// <summary>
    /// 預先計算按鈕 Bold 文字最大寬度，鎖定 MinimumSize 防止字型加粗時佈局抖動。
    /// 同時強制套用 DPI 感知的 44px 下限，確保符合 WCAG 2.5.5 AAA 觸控目標尺寸。
    /// </summary>
    private void UpdateButtonMinimumSize()
    {
        Font? regularFont = _closeRegularFont,
            boldFont = _closeBoldFont;

        if (regularFont == null ||
            boldFont == null)
        {
            return;
        }

        using Graphics g = _btnClose.CreateGraphics();

        SizeF regularSize = g.MeasureString(_btnClose.Text, regularFont),
            boldSize = g.MeasureString(_btnClose.Text, boldFont);

        int maxW = (int)Math.Ceiling(Math.Max(regularSize.Width, boldSize.Width)) +
                _btnClose.Padding.Horizontal + 12,
            maxH = (int)Math.Ceiling(Math.Max(regularSize.Height, boldSize.Height)) +
                _btnClose.Padding.Vertical + 6,
            // 強制套用 WCAG 2.5.5 AAA 觸控目標尺寸下限（44×44 邏輯像素，隨 DPI 等比縮放）。
            a11yMin = (int)Math.Ceiling(44.0 * DeviceDpi / AppSettings.BaseDpi);

        maxW = Math.Max(maxW, a11yMin);
        maxH = Math.Max(maxH, a11yMin);

        _btnClose.MinimumSize = new Size(maxW, maxH);
    }

    #endregion
}