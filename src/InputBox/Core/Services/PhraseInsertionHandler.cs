using InputBox.Core.Configuration;
using InputBox.Resources;

namespace InputBox.Core.Services;

/// <summary>
/// 片語插入流程處理器：封裝文字插入、A11y 回饋與最近使用片語維護
/// </summary>
/// <remarks>
/// 建立片語插入處理器。
/// </remarks>
/// <param name="inputBox">目標輸入框。</param>
/// <param name="phraseService">片語服務。</param>
/// <param name="announce">A11y 廣播委派。</param>
/// <param name="maxRecent">最近使用片語上限。</param>
internal sealed class PhraseInsertionHandler(
    TextBox inputBox,
    PhraseService phraseService,
    Action<string, bool> announce,
    int maxRecent)
{
    /// <summary>
    /// 目標輸入框
    /// </summary>
    private readonly TextBox _inputBox = inputBox;

    /// <summary>
    /// PhraseService 實例（用於片語名稱解析與管理）
    /// </summary>
    private readonly PhraseService _phraseService = phraseService;

    /// <summary>
    /// A11y 廣播委派
    /// </summary>
    private readonly Action<string, bool> _announce = announce;

    /// <summary>
    /// 最近使用片語上限
    /// </summary>
    private readonly int _maxRecent = maxRecent;

    /// <summary>
    /// 最近使用片語清單
    /// </summary>
    private readonly List<PhraseService.PhraseEntry> _recentPhrases = [];

    /// <summary>
    /// 取得最近使用片語清單（唯讀）
    /// </summary>
    public IReadOnlyList<PhraseService.PhraseEntry> RecentPhrases => _recentPhrases;

    /// <summary>
    /// 清除已不存在於主片語清單的最近使用項目
    /// </summary>
    /// <param name="phrases">目前主片語清單。</param>
    public void PruneRecent(IReadOnlyList<PhraseService.PhraseEntry> phrases)
    {
        _recentPhrases.RemoveAll(r => !phrases.Any(p => p.Name == r.Name && p.Content == r.Content));
    }

    /// <summary>
    /// 將片語內容插入輸入框，並更新最近使用清單
    /// </summary>
    /// <param name="entry">要插入的片語項目。</param>
    public void InsertPhraseContent(PhraseService.PhraseEntry entry)
    {
        if (_inputBox.IsDisposed)
        {
            return;
        }

        int selectionStart = _inputBox.SelectionStart;

        // 若有選取的文字，取代之；否則在游標處插入。
        if (_inputBox.SelectionLength > 0)
        {
            _inputBox.SelectedText = entry.Content;
        }
        else
        {
            string text = _inputBox.Text;

            _inputBox.Text = text.Insert(selectionStart, entry.Content);
            _inputBox.SelectionStart = selectionStart + entry.Content.Length;
        }

        _inputBox.Focus();

        if (!string.IsNullOrEmpty(entry.Name))
        {
            _announce(
                AppSettings.Current.IsPrivacyMode ?
                    Strings.Phrase_A11y_Inserted_PrivacySafe :
                    string.Format(Strings.Phrase_A11y_Inserted, entry.Name),
                false);
        }

        RegisterRecentPhrase(entry);
    }

    /// <summary>
    /// 註冊最近使用的片語項目，
    /// 確保不重複且維持上限，
    /// 並嘗試從名稱或內容解析正式片語資訊
    /// </summary>
    /// <param name="entry">要註冊為最近使用項目的片語。</param>
    private void RegisterRecentPhrase(PhraseService.PhraseEntry entry)
    {
        PhraseService.PhraseEntry normalized = entry;

        // 從片語管理對話框插入時名稱可能為空，嘗試回查正式名稱。
        if (string.IsNullOrEmpty(normalized.Name) && !string.IsNullOrEmpty(normalized.Content))
        {
            IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;

            PhraseService.PhraseEntry? resolved = phrases.FirstOrDefault(p => p.Content == normalized.Content);

            if (resolved != null)
            {
                normalized = resolved;
            }
        }

        if (string.IsNullOrEmpty(normalized.Name) ||
            string.IsNullOrEmpty(normalized.Content))
        {
            return;
        }

        _recentPhrases.RemoveAll(p => p.Name == normalized.Name && p.Content == normalized.Content);
        _recentPhrases.Insert(0, normalized);

        if (_recentPhrases.Count > _maxRecent)
        {
            _recentPhrases.RemoveRange(_maxRecent, _recentPhrases.Count - _maxRecent);
        }
    }
}