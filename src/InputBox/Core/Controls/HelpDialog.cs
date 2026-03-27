using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Input;
using InputBox.Resources;
using System.ComponentModel;
using System.Diagnostics;

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
    /// 初始化說明對話框
    /// </summary>
    public HelpDialog()
    {
        DoubleBuffered = true;
        KeyPreview = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
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

        // 主版面：垂直堆疊（鍵盤表、手把表、關閉按鈕）。
        TableLayoutPanel tlpMain = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5
        };
        tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpMain.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // keyboard heading
        tlpMain.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // keyboard table
        tlpMain.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // gamepad heading
        tlpMain.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // gamepad table
        tlpMain.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // close button row

        // === 鍵盤快速鍵區段 ===
        Label lblKeyboardHeading = CreateSectionHeading(Strings.Help_Section_Keyboard);
        tlpMain.Controls.Add(lblKeyboardHeading, 0, 0);

        Panel pnlKeyboard = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AccessibleName = Strings.Help_A11y_Keyboard_Group,
            AccessibleRole = AccessibleRole.Grouping
        };

        _tlpKeyboard = CreateTablePanel();
        pnlKeyboard.Controls.Add(_tlpKeyboard);
        tlpMain.Controls.Add(pnlKeyboard, 0, 1);

        // === 手把按鍵對應區段 ===
        Label lblGamepadHeading = CreateSectionHeading(Strings.Help_Section_Gamepad);
        tlpMain.Controls.Add(lblGamepadHeading, 0, 2);

        Panel pnlGamepad = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AccessibleName = Strings.Help_A11y_Gamepad_Group,
            AccessibleRole = AccessibleRole.Grouping
        };

        _tlpGamepad = CreateTablePanel();
        pnlGamepad.Controls.Add(_tlpGamepad);
        tlpMain.Controls.Add(pnlGamepad, 0, 3);

        // === 關閉按鈕 ===
        _btnClose = new Button()
        {
            Text = Strings.Help_Btn_Close,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = Strings.Help_Btn_Close,
            DialogResult = DialogResult.Cancel
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

        FlowLayoutPanel flpButtons = new()
        {
            AutoSize = true,
            Dock = DockStyle.Right
        };
        flpButtons.Controls.Add(_btnClose);
        tlpMain.Controls.Add(flpButtons, 0, 4);

        CancelButton = _btnClose;
        Controls.Add(tlpMain);
    }

    /// <summary>
    /// 視窗建立完成後套用字型、填入資料列、計算最小尺寸。
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        ApplyFont();
        PopulateTable(_tlpKeyboard, Strings.Help_Col_Key, Strings.Help_Col_Action, Strings.Help_Keyboard_Rows);
        PopulateTable(_tlpGamepad, Strings.Help_Col_Button, Strings.Help_Col_Action, Strings.Help_Gamepad_Rows);
        BindGamepadEvents();
    }

    /// <summary>
    /// DPI 變更時重新套用字型並更新最小尺寸。
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        ApplyFont();
        ApplySmartPosition();
    }

    /// <summary>
    /// 視窗顯示後執行智慧定位。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        ApplySmartPosition();
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
        // 共享字體不由此對話框處置。
        Font? f = Interlocked.Exchange(ref _currentFont, null);
        // f 來自快取池，不處置，僅歸零欄位。
        _ = f;
    }

    /// <summary>
    /// 目前使用的字型（來自快取池，不由此對話框處置）
    /// </summary>
    private Font? _currentFont;

    /// <summary>
    /// 套用共享 A11y 字型到所有控制項。
    /// </summary>
    private void ApplyFont()
    {
        Font shared = MainForm.GetSharedA11yFont(DeviceDpi);

        Interlocked.Exchange(ref _currentFont, shared);

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
    /// 縮放後確保視窗不超出螢幕可視區域。
    /// </summary>
    private void ApplySmartPosition()
    {
        Rectangle workArea = Screen.GetWorkingArea(this);

        int x = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
        int y = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));

        if (x != Left || y != Top)
        {
            Location = new Point(x, y);
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
    }

    /// <summary>
    /// 手把 B / Back 關閉對話框。
    /// </summary>
    private void OnGamepadClose()
    {
        this.SafeInvoke(() =>
        {
            try
            {
                if (!IsDisposed)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[說明] 手把關閉失敗：{ex.Message}");
            }
        });
    }

    #endregion
}
