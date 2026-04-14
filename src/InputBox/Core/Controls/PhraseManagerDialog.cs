using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Runtime.CompilerServices;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語管理對話框
/// <para>提供片語的新增、編輯、刪除、排序功能，支援遊戲控制器操作、深色／淺色模式與無障礙。</para>
/// </summary>
internal sealed class PhraseManagerDialog : Form
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
    /// 設定控制器實作，並訂閱事件
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

            SubscribeGamepadEvents();
        }
    }

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

        _btnAdd = CreateActionButton(Strings.Phrase_Btn_Add, Strings.Phrase_A11y_Btn_Add_Desc, 'Y');
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

        _btnDelete = CreateActionButton(Strings.Phrase_Btn_Delete, Strings.Phrase_A11y_Btn_Delete_Desc, 'X');
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
            Text = ControlExtensions.GetMnemonicText(Strings.Phrase_Btn_Close, 'B'),
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
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 12, 0, 0),
            AccessibleRole = AccessibleRole.StaticText
        };

        tlpFooter.Controls.Add(_lblPhraseCount, 0, 0);
        tlpFooter.Controls.Add(_btnClose, 1, 0);

        _tlpMain.Controls.Add(tlpFooter, 0, 1);
        _tlpMain.SetColumnSpan(tlpFooter, 2);

        CancelButton = _btnClose;

        Controls.Add(_tlpMain);

        UpdatePhraseCountHint();

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
                    // 不應暫停同一支控制器，避免子視窗手把操作失效。
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

    protected override void OnHandleCreated(EventArgs e)
    {
        try
        {
            base.OnHandleCreated(e);

            ApplyFont();
            RefreshList();
            UpdateButtonStates();
            UpdateButtonMinimumSizes();
            UpdateMinimumSize();

            this.SafeBeginInvoke(() =>
            {
                try
                {
                    UpdateOpacity();
                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "PhraseManagerDialog.OnHandleCreated 延遲邏輯失敗");

                    Debug.WriteLine($"[片語] OnHandleCreated 延遲邏輯失敗：{ex.Message}");
                }
            });

            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "PhraseManagerDialog.OnHandleCreated 失敗");

            Debug.WriteLine($"[片語] OnHandleCreated 失敗：{ex.Message}");
        }
    }

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
                    UpdateButtonMinimumSizes();
                    UpdateMinimumSize();
                    ApplySmartPosition();
                    InvalidateAllButtons();
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "PhraseManagerDialog.OnDpiChanged 延遲邏輯失敗");

                    Debug.WriteLine($"[片語] OnDpiChanged 失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "PhraseManagerDialog.OnDpiChanged 失敗");

            Debug.WriteLine($"[片語] OnDpiChanged 失敗：{ex.Message}");
        }
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

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);

        ApplySmartPosition();
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

    /// <summary>
    /// Handle 銷毀時解除靜態系統事件訂閱，避免遺留參考。
    /// </summary>
    /// <param name="e">控制項事件參數。</param>
    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    #region 清單操作

    /// <summary>
    /// 重新載入清單內容
    /// </summary>
    private void RefreshList()
    {
        int selectedIndex = _lstPhrases.SelectedIndex;

        _lstPhrases.BeginUpdate();

        try
        {
            _lstPhrases.Items.Clear();

            foreach (PhraseService.PhraseEntry entry in _phraseService.Phrases)
            {
                _lstPhrases.Items.Add(entry.Name);
            }
        }
        finally
        {
            _lstPhrases.EndUpdate();
        }

        // 還原選取位置。
        if (selectedIndex >= 0 && selectedIndex < _lstPhrases.Items.Count)
        {
            _lstPhrases.SelectedIndex = selectedIndex;
        }
        else if (_lstPhrases.Items.Count > 0)
        {
            _lstPhrases.SelectedIndex = Math.Min(selectedIndex, _lstPhrases.Items.Count - 1);
        }

        UpdateButtonStates();
        UpdatePhraseCountHint();
    }

    /// <summary>
    /// 更新按鈕啟用狀態
    /// </summary>
    private void UpdateButtonStates()
    {
        bool hasSelection = _lstPhrases.SelectedIndex >= 0,
            canAdd = _phraseService.Count < AppSettings.MaxPhraseCount;

        _btnAdd.Enabled = canAdd;
        _btnEdit.Enabled = hasSelection;
        _btnDelete.Enabled = hasSelection;
        _btnMoveUp.Enabled = hasSelection &&
            _lstPhrases.SelectedIndex > 0;
        _btnMoveDown.Enabled = hasSelection &&
            _lstPhrases.SelectedIndex < _lstPhrases.Items.Count - 1;

        UpdatePhraseCountHint();
    }

    /// <summary>
    /// 更新片語數量提示（例如：片語數量：12/50）。
    /// </summary>
    private void UpdatePhraseCountHint()
    {
        if (_lblPhraseCount == null ||
            _lblPhraseCount.IsDisposed)
        {
            return;
        }

        string countText = $"{Strings.Phrase_A11y_List_Name}：{_phraseService.Count}/{AppSettings.MaxPhraseCount}";

        _lblPhraseCount.Text = countText;
        _lblPhraseCount.AccessibleName = countText;
        _lblPhraseCount.AccessibleDescription =
            $"{Strings.Phrase_A11y_List_Desc} {countText}";

        if (SystemInformation.HighContrast)
        {
            _lblPhraseCount.ForeColor = Color.Empty;

            return;
        }

        bool isNearLimit = _phraseService.Count >= AppSettings.MaxPhraseCount - 5;

        _lblPhraseCount.ForeColor = isNearLimit ?
            Color.DarkOrange :
            Color.Empty;
    }

    /// <summary>
    /// 新增片語
    /// </summary>
    private void AddPhrase()
    {
        if (_phraseService.Count >= AppSettings.MaxPhraseCount)
        {
            FeedbackService.PlaySound(SystemSounds.Beep);

            AnnounceA11y(Strings.Phrase_A11y_Full);

            return;
        }

        // 暫時解除本對話框的控制器事件，防止子對話框開啟期間事件同時觸發。
        UnsubscribeGamepadEvents();

        try
        {
            using PhraseEditDialog dlg = new(string.Empty, string.Empty, _a11yFont)
            {
                GamepadController = _gamepadController
            };
            dlg.StartPosition = FormStartPosition.Manual;
            dlg.Location = new Point(Left + 20, Top + 20);

            if (dlg.ShowDialog(this) == DialogResult.OK &&
                !string.IsNullOrWhiteSpace(dlg.PhraseName) &&
                !string.IsNullOrWhiteSpace(dlg.PhraseContent))
            {
                _phraseService.Add(dlg.PhraseName, dlg.PhraseContent);

                RefreshList();

                _lstPhrases.SelectedIndex = _lstPhrases.Items.Count - 1;

                FeedbackService.PlaySound(SystemSounds.Asterisk);

                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                    Strings.Phrase_A11y_Added_PrivacySafe :
                    string.Format(Strings.Phrase_A11y_Added, dlg.PhraseName));
            }
        }
        finally
        {
            // 子對話框關閉後重新訂閱控制器事件。
            SubscribeGamepadEvents();

            // 防止 Owner／Child 失焦競態導致控制器殘留在 Pause 狀態。
            if (ActiveForm == this)
            {
                try
                {
                    _gamepadController?.Resume();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語] AddPhrase Resume 失敗：{ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 編輯選取的片語
    /// </summary>
    private void EditSelectedPhrase()
    {
        int idx = _lstPhrases.SelectedIndex;

        if (idx < 0)
        {
            return;
        }

        IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;

        if (idx >= phrases.Count)
        {
            return;
        }

        PhraseService.PhraseEntry entry = phrases[idx];

        // 暫時解除本對話框的控制器事件，防止子對話框開啟期間事件同時觸發。
        UnsubscribeGamepadEvents();

        try
        {
            using PhraseEditDialog dlg = new(entry.Name, entry.Content, _a11yFont)
            {
                GamepadController = _gamepadController
            };
            dlg.StartPosition = FormStartPosition.Manual;
            dlg.Location = new Point(Left + 20, Top + 20);

            if (dlg.ShowDialog(this) == DialogResult.OK &&
                !string.IsNullOrWhiteSpace(dlg.PhraseName) &&
                !string.IsNullOrWhiteSpace(dlg.PhraseContent))
            {
                _phraseService.Update(idx, dlg.PhraseName, dlg.PhraseContent);

                RefreshList();

                FeedbackService.PlaySound(SystemSounds.Asterisk);

                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                    Strings.Phrase_A11y_Updated_PrivacySafe :
                    string.Format(Strings.Phrase_A11y_Updated, dlg.PhraseName));
            }
        }
        finally
        {
            // 子對話框關閉後重新訂閱控制器事件。
            SubscribeGamepadEvents();

            // 防止 Owner／Child 失焦競態導致控制器殘留在 Pause 狀態。
            if (ActiveForm == this)
            {
                try
                {
                    _gamepadController?.Resume();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語] EditSelectedPhrase Resume 失敗：{ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 刪除選取的片語
    /// </summary>
    private void DeleteSelectedPhrase()
    {
        int idx = _lstPhrases.SelectedIndex;

        if (idx < 0)
        {
            return;
        }

        IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;

        if (idx >= phrases.Count)
        {
            return;
        }

        string name = phrases[idx].Name;

        // 暫時解除本對話框的控制器事件，防止子對話框開啟期間事件同時觸發。
        UnsubscribeGamepadEvents();

        DialogResult confirmResult;

        try
        {
            // 確認刪除對話框（與應用程式其他訊息框使用相同風格）
            confirmResult = GamepadMessageBox.Show(
                this,
                string.Format(Strings.Msg_ConfirmDeletePhrase, name),
                Strings.Wrn_Title,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                gamepad: _gamepadController);
        }
        finally
        {
            // 子對話框關閉後重新訂閱控制器事件。
            SubscribeGamepadEvents();

            // 防止 Owner／Child 失焦競態導致控制器殘留在 Pause 狀態。
            if (ActiveForm == this)
            {
                try
                {
                    _gamepadController?.Resume();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語] DeleteSelectedPhrase Resume 失敗：{ex.Message}");
                }
            }
        }

        if (confirmResult != DialogResult.Yes)
        {
            return;
        }

        _phraseService.Remove(idx);

        RefreshList();

        FeedbackService.PlaySound(SystemSounds.Asterisk);

        FeedbackService.VibrateAsync(
            _gamepadController,
            VibrationPatterns.ClearInput,
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();

        AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
            Strings.Phrase_A11y_Deleted_PrivacySafe :
            string.Format(Strings.Phrase_A11y_Deleted, name));
    }

    /// <summary>
    /// 移動選取的片語
    /// </summary>
    /// <param name="direction">-1 為上移，1 為下移</param>
    private void MoveSelectedPhrase(int direction)
    {
        int idx = _lstPhrases.SelectedIndex;

        if (idx < 0)
        {
            return;
        }

        bool success = direction < 0 ?
            _phraseService.MoveUp(idx) :
            _phraseService.MoveDown(idx);

        if (success)
        {
            RefreshList();

            _lstPhrases.SelectedIndex = idx + direction;

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();

            AnnounceA11y(string.Format(Strings.Phrase_A11y_Moved, _lstPhrases.SelectedIndex + 1));
        }
        else
        {
            FeedbackService.PlaySound(SystemSounds.Beep);
        }
    }

    /// <summary>
    /// 插入選取的片語（設定結果並關閉）
    /// </summary>
    private void InsertSelectedPhrase()
    {
        int idx = _lstPhrases.SelectedIndex;

        if (idx < 0)
        {
            return;
        }

        IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;

        if (idx >= phrases.Count)
        {
            return;
        }

        SelectedPhraseContent = phrases[idx].Content;

        DialogResult = DialogResult.OK;

        Close();
    }

    #endregion

    #region 控制器事件處理

    /// <summary>
    /// 控制器向上輸入時在清單項目或按鈕焦點之間移動
    /// </summary>
    private void HandleUp() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveForm != this)
            {
                return;
            }

            // 焦點在按鈕上時，D-Pad 上下切換焦點。
            if (IsButtonAreaFocused())
            {
                CycleFocus(forward: false);
                return;
            }

            if (_lstPhrases.Items.Count > 0)
            {
                int newIdx = _lstPhrases.SelectedIndex <= 0 ?
                    _lstPhrases.Items.Count - 1 :
                    _lstPhrases.SelectedIndex - 1;

                _lstPhrases.SelectedIndex = newIdx;

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleUp 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器向下輸入時在清單項目或按鈕焦點之間移動
    /// </summary>
    private void HandleDown() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveForm != this)
            {
                return;
            }

            // 焦點在按鈕上時，D-Pad 上下切換焦點。
            if (IsButtonAreaFocused())
            {
                CycleFocus(forward: true);

                return;
            }

            if (_lstPhrases.Items.Count > 0)
            {
                int newIdx = _lstPhrases.SelectedIndex >= _lstPhrases.Items.Count - 1 ?
                    0 :
                    _lstPhrases.SelectedIndex + 1;

                _lstPhrases.SelectedIndex = newIdx;

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleDown 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器 A 鍵依目前焦點執行按鈕、插入片語或新增片語
    /// </summary>
    private void HandleGamepadA() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveForm != this)
            {
                return;
            }

            // 如果焦點在按鈕上，執行該按鈕的動作。
            if (TryGetFocusedButton(out Button? focusedBtn) &&
                focusedBtn is { Enabled: true })
            {
                focusedBtn.PerformClick();
            }
            else if (_lstPhrases.Focused &&
            _lstPhrases.SelectedIndex >= 0)
            {
                InsertSelectedPhrase();
            }
            else
            {
                AddPhrase();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleGamepadA 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器取消動作時關閉片語管理對話框
    /// </summary>
    private void HandleClose() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveForm != this)
            {
                return;
            }

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleClose 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器刪除動作時移除目前選取的片語
    /// </summary>
    private void HandleDelete() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveForm != this)
            {
                return;
            }

            DeleteSelectedPhrase();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleDelete 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器新增動作時開啟片語建立流程
    /// </summary>
    private void HandleAdd() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveForm != this)
            {
                return;
            }

            AddPhrase();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleAdd 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器左向輸入時回到清單或將片語上移
    /// </summary>
    private void HandleMoveUp() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveForm != this)
            {
                return;
            }

            // 左方向鍵：若目前在右側按鈕區，回到清單。
            if (IsButtonAreaFocused())
            {
                _lstPhrases.Focus();

                if (_lstPhrases.SelectedIndex >= 0)
                {
                    IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;

                    if (_lstPhrases.SelectedIndex < phrases.Count)
                    {
                        AnnounceA11y(
                            AppSettings.Current.IsPrivacyMode ?
                                Strings.Phrase_A11y_Selected_PrivacySafe :
                                string.Format(Strings.Phrase_A11y_Selected, phrases[_lstPhrases.SelectedIndex].Name),
                            interrupt: true);
                    }
                }

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                return;
            }

            MoveSelectedPhrase(-1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleMoveUp 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器右向輸入時進入按鈕區或將片語下移
    /// </summary>
    private void HandleMoveDown() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveForm != this)
            {
                return;
            }

            // 右方向鍵：由清單進入右側按鈕區；若已在按鈕區則向後循環。
            if (_lstPhrases.Focused ||
                _lstPhrases.ContainsFocus)
            {
                FocusFirstActionButtonOrClose();

                return;
            }

            if (IsButtonAreaFocused())
            {
                CycleFocus(forward: true);

                return;
            }

            MoveSelectedPhrase(1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleMoveDown 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 在清單與按鈕之間循環焦點
    /// </summary>
    /// <param name="forward">是否向前循環焦點</param>
    private void CycleFocus(bool forward)
    {
        // 建立焦點順序：清單 → 各按鈕（依面板順序）→ 關閉按鈕。
        List<Control> focusOrder = [_lstPhrases];

        foreach (Control ctrl in _flpButtons.Controls)
        {
            if (ctrl is Button btn &&
                btn.Enabled &&
                btn.Visible)
            {
                focusOrder.Add(btn);
            }
        }

        if (_btnClose.Enabled && _btnClose.Visible)
        {
            focusOrder.Add(_btnClose);
        }

        if (focusOrder.Count == 0)
        {
            return;
        }

        // 找到目前焦點在列表中的位置。
        int currentIdx = -1;

        for (int i = 0; i < focusOrder.Count; i++)
        {
            if (focusOrder[i].Focused ||
                focusOrder[i].ContainsFocus)
            {
                currentIdx = i;

                break;
            }
        }

        int nextIdx;

        if (currentIdx < 0)
        {
            nextIdx = forward ?
                0 :
                focusOrder.Count - 1;
        }
        else
        {
            nextIdx = forward ?
                (currentIdx + 1) % focusOrder.Count :
                (currentIdx - 1 + focusOrder.Count) % focusOrder.Count;
        }

        focusOrder[nextIdx].Focus();

        // 播報焦點目標。
        string? name = focusOrder[nextIdx].AccessibleName ?? focusOrder[nextIdx].Text;

        if (!string.IsNullOrEmpty(name))
        {
            AnnounceA11y(name, interrupt: true);
        }

        FeedbackService.VibrateAsync(
            _gamepadController,
            VibrationPatterns.CursorMove,
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
    }

    /// <summary>
    /// 嘗試找出目前在按鈕區取得焦點的按鈕
    /// </summary>
    /// <param name="focusedBtn">輸出的焦點按鈕。</param>
    /// <returns>若找到焦點按鈕則回傳 true。</returns>
    private bool TryGetFocusedButton(out Button? focusedBtn)
    {
        if (_btnClose.Focused ||
            _btnClose.ContainsFocus)
        {
            focusedBtn = _btnClose;

            return true;
        }

        foreach (Control ctrl in _flpButtons.Controls)
        {
            if (ctrl is Button btn &&
                (btn.Focused || btn.ContainsFocus))
            {
                focusedBtn = btn;

                return true;
            }
        }

        focusedBtn = null;

        return false;
    }

    /// <summary>
    /// 判斷目前焦點是否位於按鈕區
    /// </summary>
    /// <returns>若按鈕區有焦點則回傳 true。</returns>
    private bool IsButtonAreaFocused() => TryGetFocusedButton(out _);

    /// <summary>
    /// 將焦點移至第一個可用動作按鈕，否則移至關閉按鈕。
    /// </summary>
    private void FocusFirstActionButtonOrClose()
    {
        foreach (Control ctrl in _flpButtons.Controls)
        {
            if (ctrl is Button btn &&
                btn.Enabled &&
                btn.Visible)
            {
                btn.Focus();

                AnnounceA11y(btn.AccessibleName ?? btn.Text, interrupt: true);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                return;
            }
        }

        if (_btnClose.Enabled &&
            _btnClose.Visible)
        {
            _btnClose.Focus();

            AnnounceA11y(_btnClose.AccessibleName ?? _btnClose.Text, interrupt: true);

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
    }

    /// <summary>
    /// 控制器連線狀態變更時更新恢復狀態並播報結果
    /// </summary>
    /// <param name="connected">新的控制器連線狀態。</param>
    private void HandleGamepadConnectionChanged(bool connected)
    {
        try
        {
            if (connected)
            {
                _gamepadController?.Resume();
            }

            AnnounceA11y(connected ?
                string.Format(Strings.A11y_Gamepad_Connected, _gamepadController?.DeviceName) :
                string.Format(Strings.A11y_Gamepad_Disconnected, _gamepadController?.DeviceName));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] 控制器連線變更處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 訂閱片語管理對話框使用的控制器事件
    /// </summary>
    private void SubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                _gamepadController.UpPressed += HandleUp;
                _gamepadController.UpRepeat += HandleUp;
                _gamepadController.DownPressed += HandleDown;
                _gamepadController.DownRepeat += HandleDown;
                _gamepadController.APressed += HandleGamepadA;
                _gamepadController.StartPressed += HandleGamepadA;
                _gamepadController.BPressed += HandleClose;
                _gamepadController.BackPressed += HandleClose;
                _gamepadController.XPressed += HandleDelete;
                _gamepadController.YPressed += HandleAdd;
                _gamepadController.LeftPressed += HandleMoveUp;
                _gamepadController.LeftRepeat += HandleMoveUp;
                _gamepadController.RightPressed += HandleMoveDown;
                _gamepadController.RightRepeat += HandleMoveDown;
                _gamepadController.ConnectionChanged += HandleGamepadConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] SubscribeGamepadEvents 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 解除片語管理對話框使用的控制器事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                _gamepadController.UpPressed -= HandleUp;
                _gamepadController.UpRepeat -= HandleUp;
                _gamepadController.DownPressed -= HandleDown;
                _gamepadController.DownRepeat -= HandleDown;
                _gamepadController.APressed -= HandleGamepadA;
                _gamepadController.StartPressed -= HandleGamepadA;
                _gamepadController.BPressed -= HandleClose;
                _gamepadController.BackPressed -= HandleClose;
                _gamepadController.XPressed -= HandleDelete;
                _gamepadController.YPressed -= HandleAdd;
                _gamepadController.LeftPressed -= HandleMoveUp;
                _gamepadController.LeftRepeat -= HandleMoveUp;
                _gamepadController.RightPressed -= HandleMoveDown;
                _gamepadController.RightRepeat -= HandleMoveDown;
                _gamepadController.ConnectionChanged -= HandleGamepadConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] UnsubscribeGamepadEvents 失敗：{ex.Message}");
        }
    }

    #endregion

    #region 視覺與佈局


    /// <summary>
    /// 建立動作按鈕
    /// </summary>
    /// <param name="text">按鈕文字</param>
    /// <param name="a11yDesc">輔助功能描述</param>
    /// <param name="mnemonic">快捷鍵字元</param>
    /// <returns>Button</returns>
    private static Button CreateActionButton(
        string text,
        string a11yDesc,
        char mnemonic)
    {
        Button btn = new()
        {
            Text = ControlExtensions.GetMnemonicText(text, mnemonic),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = text,
            AccessibleDescription = a11yDesc,
            AccessibleRole = AccessibleRole.PushButton,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            Margin = new Padding(2, 2, 2, 2),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        btn.FlatAppearance.BorderSize = 0;

        // 暫存 base description 到 Tag 供後續 AttachEyeTrackerFeedback 使用。
        btn.Tag = a11yDesc;

        return btn;
    }

    /// <summary>
    /// 套用字型（Regular + Bold）並掛載眼動儀擴充
    /// </summary>
    private void ApplyFont()
    {
        _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Regular);
        _boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

        Font = _a11yFont;

        _lstPhrases.Font = _a11yFont;

        foreach (Control ctrl in _flpButtons.Controls)
        {
            if (ctrl is Button btn)
            {
                btn.Font = _a11yFont;
                btn.AttachEyeTrackerFeedback(
                    baseDescription: btn.Tag?.ToString() ?? string.Empty,
                    regularFont: _a11yFont,
                    boldFont: _boldFont,
                    formCt: _cts?.Token ?? CancellationToken.None);
            }
        }

        _btnClose.Font = _a11yFont;
        _btnClose.AttachEyeTrackerFeedback(
            baseDescription: Strings.Phrase_A11y_Btn_Close_Desc,
            regularFont: _a11yFont,
            boldFont: _boldFont,
            formCt: _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// 更新每個按鈕的最小尺寸（抗抖動 + WCAG 2.5.5 AAA 44×44）
    /// </summary>
    private void UpdateButtonMinimumSizes()
    {
        float scale = DeviceDpi / AppSettings.BaseDpi;

        UpdateSingleButtonMinimumSize(_btnAdd, scale);
        UpdateSingleButtonMinimumSize(_btnEdit, scale);
        UpdateSingleButtonMinimumSize(_btnDelete, scale);
        UpdateSingleButtonMinimumSize(_btnMoveUp, scale);
        UpdateSingleButtonMinimumSize(_btnMoveDown, scale);
        UpdateSingleButtonMinimumSize(_btnClose, scale);
    }

    /// <summary>
    /// 更新單一按鈕的最小尺寸
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateSingleButtonMinimumSize(Button btn, float scale)
    {
        try
        {
            if (btn.IsDisposed)
            {
                return;
            }
            Font boldFont = _boldFont ?? MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

            DialogLayoutHelper.UpdateButtonMinimumSize(btn, boldFont, scale, 44, 44, 24, 16);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] UpdateSingleButtonMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新最小尺寸（依按鈕面板實際內容高度動態計算）
    /// </summary>
    private void UpdateMinimumSize(bool forceRecalculate = false)
    {
        float currentDpi = DeviceDpi;

        if (!DialogLayoutHelper.TryBeginDpiLayout(currentDpi, ref _lastAppliedDpi, forceRecalculate))
        {
            return;
        }

        float scale = currentDpi / AppSettings.BaseDpi;

        // 加上非客戶區（標題列＋邊框）高度，MinimumSize 是外框尺寸。
        // OnHandleCreated 時 Height == ClientSize.Height（非客戶區尚未就緒），
        // 故以 SystemInformation 估算作為最低保底值。
        int nonClientH = DialogLayoutHelper.GetEstimatedNonClientHeight(this);

        // 以主版面實際偏好尺寸作為基準，避免最後一顆按鈕在 row 0 被裁切。
        _tlpMain.PerformLayout();

        Size preferred = _tlpMain.GetPreferredSize(Size.Empty);

        int desiredMinWidth = (int)(BaseDialogMinWidth * scale),
            minW = Math.Max(desiredMinWidth, preferred.Width + Padding.Horizontal),
            baseClientH = Math.Max(
                (int)(300 * scale) - nonClientH,
                preferred.Height + Padding.Vertical + (int)(8 * scale)),
            minH = baseClientH + nonClientH;

        Rectangle workArea = Screen.GetWorkingArea(this);

        (int maxFitW, int maxFitH) = DialogLayoutHelper.GetMaxFitSize(workArea);

        minW = Math.Min(minW, maxFitW);
        minH = Math.Min(minH, maxFitH);

        DialogLayoutHelper.ClampFormSize(this, minW, minH, maxFitW, maxFitH);
    }

    /// <summary>
    /// 更新視窗不透明度
    /// </summary>
    private void UpdateOpacity()
    {
        if (SystemInformation.HighContrast)
        {
            Opacity = 1.0;

            return;
        }

        Opacity = AppSettings.Current.WindowOpacity;
    }

    /// <summary>
    /// 智慧定位
    /// </summary>
    private void ApplySmartPosition()
    {
        if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
        {
            Location = clampedLocation;
        }
    }

    /// <summary>
    /// 強制重繪所有按鈕
    /// </summary>
    private void InvalidateAllButtons()
    {
        _btnAdd.Invalidate();
        _btnEdit.Invalidate();
        _btnDelete.Invalidate();
        _btnMoveUp.Invalidate();
        _btnMoveDown.Invalidate();
        _btnClose.Invalidate();
    }

    /// <summary>
    /// 系統偏好設定變更時同步更新尺寸、定位與按鈕視覺。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">系統偏好設定事件參數。</param>
    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            if (e.Category is UserPreferenceCategory.Accessibility or
                UserPreferenceCategory.Color or
                UserPreferenceCategory.General)
            {
                this.SafeInvoke(() =>
                {
                    try
                    {
                        UpdateButtonMinimumSizes();
                        UpdateMinimumSize(forceRecalculate: true);
                        ApplySmartPosition();
                        InvalidateAllButtons();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[片語] SystemEvents 更新失敗：{ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] SystemEvents 處理失敗：{ex.Message}");
        }
    }

    #endregion

    #region A11y

    /// <summary>
    /// 內部 A11y 廣播
    /// </summary>
    private void AnnounceA11y(string message, bool interrupt = false)
    {
        if (IsDisposed ||
            string.IsNullOrEmpty(message))
        {
            return;
        }

        if (Owner is MainForm mainForm)
        {
            mainForm.AnnounceA11y(message, interrupt);
        }
        else
        {
            long currentId = Interlocked.Increment(ref _a11yDebounceId);

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(AppSettings.AudioDuckingDelayMs, _cts?.Token ?? CancellationToken.None);

                    if (Interlocked.Read(ref _a11yDebounceId) == currentId &&
                        !IsDisposed &&
                        IsHandleCreated)
                    {
                        await this.SafeInvokeAsync(() =>
                            _announcer.Announce(message, interrupt && AppSettings.Current.A11yInterruptEnabled));
                    }
                }
                catch (OperationCanceledException)
                {

                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語] A11y 廣播失敗：{ex.Message}");
                }
            },
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
        }
    }

    #endregion
}