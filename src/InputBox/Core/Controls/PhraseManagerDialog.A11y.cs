using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語管理對話框（無障礙輔助功能分部）。
/// <para>本分部檔案包含 A11y 廣播成員。</para>
/// </summary>
internal sealed partial class PhraseManagerDialog
{
    /// <summary>
    /// 內部 A11y 廣播
    /// </summary>
    /// <param name="message">要廣播的無障礙訊息文字。</param>
    /// <param name="interrupt">設為 <see langword="true"/> 可中斷目前朗讀（需設定允許中斷）。</param>
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
}
