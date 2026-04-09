using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Utilities;
using System.Diagnostics;
using System.Threading.Channels;

namespace InputBox.Core.Services;

/// <summary>
/// A11y 廣播佇列服務：負責排隊、節流與背景處理，UI 實際朗讀由呼叫端提供委派
/// </summary>
internal sealed class AnnouncementService : IDisposable
{
    /// <summary>
    /// 廣播請求佇列
    /// </summary>
    private readonly Channel<AnnouncementRequest> _channel;

    /// <summary>
    /// 取消權杖來源
    /// </summary>
    private CancellationTokenSource? _cts = new();

    /// <summary>
    /// UI 執行緒廣播委派
    /// </summary>
    private readonly Func<string, bool, CancellationToken, Task> _announceOnUiAsync;

    /// <summary>
    /// 目前廣播序號
    /// </summary>
    private long _currentAnnouncementId;

    /// <summary>
    /// 上一次處理的廣播序號，用於丟棄過舊訊息的保護機制
    /// </summary>
    private long _lastProcessedAnnouncementId;

    /// <summary>
    /// 最新的 interrupt 廣播序號，用於準確判斷 interrupt 訊息是否已過期
    /// </summary>
    private long _latestInterruptId;

    /// <summary>
    /// 是否已發出釋放資源的訊號
    /// </summary>
    private int _disposeSignaled;

    /// <summary>
    /// 佇列中的單筆廣播請求
    /// </summary>
    /// <param name="Message">廣播訊息內容。</param>
    /// <param name="Interrupt">是否允許中斷較舊訊息。</param>
    /// <param name="Id">廣播請求的唯一識別碼。</param>
    private record AnnouncementRequest(string Message, bool Interrupt, long Id);

    /// <summary>
    /// 建立新的廣播服務執行個體
    /// </summary>
    /// <param name="announceOnUiAsync">UI 執行緒廣播委派。</param>
    public AnnouncementService(Func<string, bool, CancellationToken, Task> announceOnUiAsync)
    {
        _announceOnUiAsync = announceOnUiAsync;

        _channel = Channel.CreateUnbounded<AnnouncementRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        Task.Run(() => ProcessAnnouncementsAsync(_cts?.Token ?? CancellationToken.None)).SafeFireAndForget();
    }

    /// <summary>
    /// 將訊息加入廣播佇列
    /// </summary>
    /// <param name="message">廣播文字。</param>
    /// <param name="interrupt">是否允許中斷較舊訊息。</param>
    public void Enqueue(string message, bool interrupt = false)
    {
        if (string.IsNullOrEmpty(message) ||
            Volatile.Read(ref _disposeSignaled) != 0)
        {
            return;
        }

        long id = Interlocked.Increment(ref _currentAnnouncementId);

        if (interrupt)
        {
            Interlocked.Exchange(ref _latestInterruptId, id);
        }

        _channel.Writer.TryWrite(new AnnouncementRequest(message, interrupt, id));
    }

    /// <summary>
    /// 背景處理廣播佇列，負責節流、丟棄舊訊息與委派 UI 播報
    /// </summary>
    /// <param name="cancellationToken">背景工作取消權杖。</param>
    private async Task ProcessAnnouncementsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (AnnouncementRequest request in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // interrupt 過期檢查：使用獨立的 _latestInterruptId 而非
                    // _currentAnnouncementId + TryPeek，確保中間的 polite 訊息不會
                    // 誤使 interrupt 失效（A→polite B→interrupt C 場景中 A 仍應被 C 取代）。
                    if (request.Interrupt &&
                        Interlocked.Read(ref _latestInterruptId) > request.Id)
                    {
                        continue;
                    }

                    // 最終保護：忽略比 UI 已完成序號更舊的訊息。
                    if (request.Id <= Volatile.Read(ref _lastProcessedAnnouncementId))
                    {
                        continue;
                    }

                    try
                    {
                        int duckingDelay = AppSettings.AudioDuckingDelayMs;

                        await Task.Delay(
                            GaussianDelayHelper.NextDelay(duckingDelay, 60),
                            cancellationToken);

                        // Ducking 延遲後再次檢查：若有更新的 interrupt 訊息已加入佇列，
                        // 放棄播報此訊息，讓後來者負責播報最新內容。
                        if (request.Interrupt &&
                            Interlocked.Read(ref _latestInterruptId) > request.Id)
                        {
                            continue;
                        }

                        // 將實際 UI 朗讀邏輯委派給呼叫端，服務只負責佇列與節流。
                        await _announceOnUiAsync(
                            request.Message,
                            request.Interrupt,
                            cancellationToken);

                        Volatile.Write(ref _lastProcessedAnnouncementId, request.Id);

                        int waitDelay = request.Interrupt ?
                            100 :
                            300;

                        await Task.Delay(
                            GaussianDelayHelper.NextDelay(waitDelay, 40),
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[A11y] 廣播處理發生異常");

                        Debug.WriteLine($"[A11y] 廣播處理發生異常：{ex.Message}");
                    }
                }

                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "[A11y] 廣播工作者發生致命錯誤");

                Debug.WriteLine($"[A11y] 廣播工作者發生致命錯誤，嘗試重啟：{ex.Message}");

                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 停止背景工作並釋放資源
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeSignaled, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();

        Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();
    }
}