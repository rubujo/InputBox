using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Diagnostics;
using System.Media;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語管理對話框（片語清單操作分部）。
/// <para>本分部檔案包含片語清單重新整理、新增、編輯、刪除、排序與插入等成員。</para>
/// </summary>
internal sealed partial class PhraseManagerDialog
{
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

        UpdatePhraseCountLabelMinimumWidth();

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
    /// 預先鎖定片語數量提示標籤的寬度，避免數字變化時擠壓旁邊元件。
    /// </summary>
    private void UpdatePhraseCountLabelMinimumWidth()
    {
        if (_lblPhraseCount == null ||
            _lblPhraseCount.IsDisposed)
        {
            return;
        }

        string widestText = $"{Strings.Phrase_A11y_List_Name}：{AppSettings.MaxPhraseCount}/{AppSettings.MaxPhraseCount}";
        Size measured = TextRenderer.MeasureText(
            widestText,
            _lblPhraseCount.Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        int horizontalPadding = (int)Math.Ceiling(12 * (DeviceDpi / AppSettings.BaseDpi));
        int width = Math.Max(_lblPhraseCount.MinimumSize.Width, measured.Width + horizontalPadding);
        int height = Math.Max(_lblPhraseCount.MinimumSize.Height, measured.Height + 2);

        _lblPhraseCount.AutoSize = false;
        _lblPhraseCount.MinimumSize = new Size(width, height);
        _lblPhraseCount.Size = new Size(Math.Max(_lblPhraseCount.Width, width), height);
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
}
