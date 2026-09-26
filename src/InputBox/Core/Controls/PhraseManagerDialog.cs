using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語管理對話框
/// <para>提供片語的新增、編輯、刪除、排序功能，支援遊戲控制器操作、深色／淺色模式與無障礙。</para>
/// </summary>
internal sealed partial class PhraseManagerDialog : Form
{
    /// <summary>
    /// 片語管理視窗的基準最小寬度（96 DPI）
    /// </summary>
    private const int BaseDialogMinWidth = 760;

    /// <summary>
    /// 片語服務（由主視窗傳入，不由此對話框管理生命周期）
    /// </summary>
    private readonly PhraseService _phraseService;

    /// <summary>
    /// 片語清單控制項
    /// </summary>
    private readonly ListBox _lstPhrases;

    /// <summary>
    /// 新增按鈕
    /// </summary>
    private readonly Button _btnAdd;

    /// <summary>
    /// 編輯按鈕
    /// </summary>
    private readonly Button _btnEdit;

    /// <summary>
    /// 刪除按鈕
    /// </summary>
    private readonly Button _btnDelete;

    /// <summary>
    /// 上移按鈕
    /// </summary>
    private readonly Button _btnMoveUp;

    /// <summary>
    /// 下移按鈕
    /// </summary>
    private readonly Button _btnMoveDown;

    /// <summary>
    /// 關閉按鈕
    /// </summary>
    private readonly Button _btnClose;

    /// <summary>
    /// 片語數量提示標籤
    /// </summary>
    private readonly Label _lblPhraseCount;

    /// <summary>
    /// A11y 廣播用的 Label
    /// </summary>
    private readonly AnnouncerLabel _announcer;

    /// <summary>
    /// 主版面容器
    /// </summary>
    private readonly TableLayoutPanel _tlpMain;

    /// <summary>
    /// 按鈕面板
    /// </summary>
    private readonly FlowLayoutPanel _flpButtons;

    /// <summary>
    /// 用於管理對話框生命週期內非同步任務的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _cts = new();

    /// <summary>
    /// A11y 廣播防抖用的序號
    /// </summary>
    private long _a11yDebounceId;

    /// <summary>
    /// 遊戲控制器
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 統一放大的 A11y 字型（來自共享快取）
    /// </summary>
    private Font? _a11yFont;

    /// <summary>
    /// A11y Bold 字型（來自共享快取，用於焦點加粗）
    /// </summary>
    private Font? _boldFont;

    /// <summary>
    /// 已套用的 DPI 快取，避免重複計算最小尺寸
    /// </summary>
    private float _lastAppliedDpi;

    /// <summary>
    /// 對話框的結果：使用者選取要插入的片語內容（null 表示未選取）
    /// </summary>
    public string? SelectedPhraseContent { get; private set; }

    /// <summary>
    /// 初始化片語管理對話框
    /// </summary>
    /// <param name="phraseService">片語服務實例</param>
    public PhraseManagerDialog(PhraseService phraseService)
    {
        _phraseService = phraseService ?? throw new ArgumentNullException(nameof(phraseService));

        DoubleBuffered = true;
        KeyPreview = true;
        AutoSize = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = Strings.Phrase_Title;
        Padding = new Padding(12);
        AccessibleName = Strings.Phrase_Title;
        AccessibleDescription = Strings.Phrase_A11y_Dialog_Desc;
        AccessibleRole = AccessibleRole.Dialog;

        Icon = Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.Icon ??
            ActiveForm?.Icon;

        // A11y 廣播器。
        _announcer = new AnnouncerLabel()
        {
            AccessibleName = "\u200B",
            Dock = DockStyle.Bottom,
            Height = 1,
            TabStop = false,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
        };
        Controls.Add(_announcer);

        // 主版面：左側清單 + 右側按鈕列。
        _tlpMain = new TableLayoutPanel()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0),
        };
        _tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _tlpMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _tlpMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 片語清單。
        _lstPhrases = new ListBox()
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            AccessibleName = Strings.Phrase_A11y_List_Name,
            AccessibleDescription = Strings.Phrase_A11y_List_Desc,
            TabIndex = 0,
            AccessibleRole = AccessibleRole.List
        };
        _lstPhrases.SelectedIndexChanged += (s, e) =>
        {
            try
            {
                UpdateButtonStates();

                if (_lstPhrases.SelectedIndex >= 0)
                {
                    IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;

                    if (_lstPhrases.SelectedIndex < phrases.Count)
                    {
                        PhraseService.PhraseEntry entry = phrases[_lstPhrases.SelectedIndex];

                        AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                            Strings.Phrase_A11y_Selected_PrivacySafe :
                            string.Format(Strings.Phrase_A11y_Selected, entry.Name));
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.SelectedIndexChanged 失敗");

                Debug.WriteLine($"[片語] SelectedIndexChanged 失敗：{ex.Message}");
            }
        };
        _lstPhrases.DoubleClick += (s, e) =>
        {
            try
            {
                InsertSelectedPhrase();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.DoubleClick 失敗");

                Debug.WriteLine($"[片語] DoubleClick 失敗：{ex.Message}");
            }
        };
        _lstPhrases.KeyDown += (s, e) =>
        {
            try
            {
                if (e.KeyCode == Keys.Enter)
                {
                    InsertSelectedPhrase();

                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    DeleteSelectedPhrase();

                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.KeyDown 失敗");

                Debug.WriteLine($"[片語] KeyDown 失敗：{ex.Message}");
            }
        };

        _tlpMain.Controls.Add(_lstPhrases, 0, 0);
        _tlpMain.SetRowSpan(_lstPhrases, 1);

        // 右側按鈕列（Grouping）：補齊 Name／Description／Role，與其他對話框一致。
        _flpButtons = new FlowLayoutPanel()
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 0, 0, 0),
            WrapContents = false,
            AccessibleName = Strings.Phrase_A11y_ButtonArea,
            AccessibleDescription = Strings.Phrase_A11y_ButtonArea_Desc,
            AccessibleRole = AccessibleRole.Grouping
        };

        // profile 用於同步新增、刪除與關閉等操作按鈕的控制器標示與助記詞。
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        _btnAdd = CreateActionButton(profile.FormatMenuButtonText(Strings.Phrase_Btn_Add), Strings.Phrase_A11y_Btn_Add_Desc, profile.MenuMnemonic);
        _btnAdd.TabIndex = 1;
        _btnAdd.Click += (s, e) =>
        {
            try
            {
                AddPhrase();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.AddPhrase 失敗");

                Debug.WriteLine($"[片語] Add 失敗：{ex.Message}");
            }
        };

        _btnEdit = CreateActionButton(Strings.Phrase_Btn_Edit, Strings.Phrase_A11y_Btn_Edit_Desc, 'E');
        _btnEdit.TabIndex = 2;
        _btnEdit.Click += (s, e) =>
        {
            try
            {
                EditSelectedPhrase();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.EditSelectedPhrase 失敗");

                Debug.WriteLine($"[片語] Edit 失敗：{ex.Message}");
            }
        };

        _btnDelete = CreateActionButton(profile.FormatDeleteButtonText(Strings.Phrase_Btn_Delete), Strings.Phrase_A11y_Btn_Delete_Desc, profile.DeleteMnemonic);
        _btnDelete.TabIndex = 3;
        _btnDelete.Click += (s, e) =>
        {
            try
            {
                DeleteSelectedPhrase();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.DeleteSelectedPhrase 失敗");

                Debug.WriteLine($"[片語] Delete 失敗：{ex.Message}");
            }
        };

        _btnMoveUp = CreateActionButton(Strings.Phrase_Btn_MoveUp, Strings.Phrase_A11y_Btn_MoveUp_Desc, 'U');
        _btnMoveUp.TabIndex = 4;
        _btnMoveUp.Click += (s, e) =>
        {
            try
            {
                MoveSelectedPhrase(-1);
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.MoveUp 失敗");

                Debug.WriteLine($"[片語] MoveUp 失敗：{ex.Message}");
            }
        };

        _btnMoveDown = CreateActionButton(Strings.Phrase_Btn_MoveDown, Strings.Phrase_A11y_Btn_MoveDown_Desc, 'W');
        _btnMoveDown.TabIndex = 5;
        _btnMoveDown.Click += (s, e) =>
        {
            try
            {
                MoveSelectedPhrase(1);
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.MoveDown 失敗");

                Debug.WriteLine($"[片語] MoveDown 失敗：{ex.Message}");
            }
        };

        _flpButtons.Controls.Add(_btnAdd);
        _flpButtons.Controls.Add(_btnEdit);
        _flpButtons.Controls.Add(_btnDelete);
        _flpButtons.Controls.Add(_btnMoveUp);
        _flpButtons.Controls.Add(_btnMoveDown);

        _tlpMain.Controls.Add(_flpButtons, 1, 0);

        // 底部關閉按鈕。
        _btnClose = new Button()
        {
            Text = profile.FormatCancelButtonText(Strings.Phrase_Btn_Close),
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = Strings.Phrase_Btn_Close,
            AccessibleDescription = Strings.Phrase_A11y_Btn_Close_Desc,
            AccessibleRole = AccessibleRole.PushButton,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            TabIndex = 6,
            Margin = new Padding(4, 8, 0, 0)
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
                LoggerService.LogException(ex, "PhraseManagerDialog.Close 失敗");

                Debug.WriteLine($"[片語] Close 失敗：{ex.Message}");
            }
        };

        TableLayoutPanel tlpFooter = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        tlpFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlpFooter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpFooter.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblPhraseCount = new Label()
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoEllipsis = false,
            UseMnemonic = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 12, 12, 0),
            AccessibleRole = AccessibleRole.StaticText
        };

        tlpFooter.Controls.Add(_lblPhraseCount, 0, 0);
        tlpFooter.Controls.Add(_btnClose, 1, 0);

        _tlpMain.Controls.Add(tlpFooter, 0, 1);
        _tlpMain.SetColumnSpan(tlpFooter, 2);

        CancelButton = _btnClose;

        Controls.Add(_tlpMain);

        UpdatePhraseCountHint();
        UpdatePhraseCountLabelMinimumWidth();

        // 應用程式焦點切換：暫停／恢復控制器。
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
                            LoggerService.LogException(ex, "PhraseManagerDialog.Resume 控制器失敗");

                            Debug.WriteLine($"[片語] Resume 失敗：{ex.Message}");
                        }
                    });
                }
                catch (OperationCanceledException)
                {

                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語] Activated 失敗：{ex.Message}");
                }
            },
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
        };

        Deactivate += (s, e) =>
        {
            try
            {
                this.SafeBeginInvoke(() =>
                {
                    // 若目前仍有其他本應用程式視窗（例如子對話框）處於作用中，
                    // 不應暫停同一支控制器，避免子視窗控制器操作失效。
                    if (ActiveForm != null)
                    {
                        return;
                    }

                    try
                    {
                        GamepadController?.Pause();
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "PhraseManagerDialog.Pause 控制器失敗");

                        Debug.WriteLine($"[片語] Pause 失敗：{ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "PhraseManagerDialog.Deactivate 失敗");

                Debug.WriteLine($"[片語] Deactivate 失敗：{ex.Message}");
            }
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Handle 與 Layout 完整建立後再重新計算，可避免底部按鈕被裁切。
        UpdateMinimumSize();

        ApplySmartPosition();

        if (_lstPhrases.Items.Count > 0)
        {
            _lstPhrases.SelectedIndex = 0;
            _lstPhrases.Focus();
        }
        else
        {
            _btnAdd.Focus();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        const int WM_KEYDOWN = 0x0100;

        if (msg.Msg == WM_KEYDOWN)
        {
            Keys key = keyData &
                Keys.KeyCode;

            if (key == Keys.F1 ||
                key == Keys.Escape)
            {
                Close();

                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 關閉片語管理對話框時解除事件訂閱並釋放暫用資源。
    /// </summary>
    /// <param name="e">表單關閉事件參數。</param>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (e.Cancel)
        {
            return;
        }

        try
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

            UnsubscribeGamepadEvents();

            Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();

            _a11yFont = null;
            _boldFont = null;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "PhraseManagerDialog.OnFormClosing 失敗");

            Debug.WriteLine($"[片語] OnFormClosing 失敗：{ex.Message}");
        }
    }
}
