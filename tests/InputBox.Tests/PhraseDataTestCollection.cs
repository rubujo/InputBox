using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 片語資料檔案測試需求常數，避免多個會讀寫 phrases.json 的測試類別並行執行而互相污染。
/// </summary>
internal static class PhraseDataTestRequirements
{
    /// <summary>
    /// 共享 phrases.json 檔案的測試集合名稱。
    /// </summary>
    public const string CollectionName = "Phrase Data";
}

/// <summary>
/// 將所有會讀寫片語資料檔的測試納入同一個非平行集合，避免備份與還原互相競爭。
/// </summary>
[CollectionDefinition(PhraseDataTestRequirements.CollectionName, DisableParallelization = true)]
public sealed class PhraseDataTestCollection
{
}
