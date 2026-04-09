using InputBox.Core.Extensions;
using InputBox.Core.Interop;
using InputBox.Core.Utilities;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace InputBox.Core.Services;

/// <summary>
/// 觸控式鍵盤服務
/// </summary>
internal static partial class TouchKeyboardService
{
    /// <summary>
    /// 是否正在啟動中（原子旗標：0=否, 1=是）
    /// </summary>
    private static volatile int _isOpening = 0;

    /// <summary>
    /// 觸控鍵盤視窗類別名稱
    /// </summary>
    private const string IPTipWindowClassName = "IPTip_Main_Window";

    /// <summary>
    /// COM 介面實例
    /// </summary>
    private static Ole32.ITipInvocation? _tipInvocation;

    /// <summary>
    /// COM 包裝器實例
    /// </summary>
    private static readonly StrategyBasedComWrappers _wrappers = new();

    /// <summary>
    /// 偵測觸控式鍵盤目前是否可見
    /// </summary>
    /// <returns>若可見則回傳 true</returns>
    public static bool IsVisible()
    {
        nint hWnd = User32.FindWindow(IPTipWindowClassName, null);

        if (hWnd == 0)
        {
            return false;
        }

        // 檢查視窗是否可見。
        return User32.IsWindowVisible(hWnd);
    }

    /// <summary>
    /// 嘗試開啟觸控式鍵盤
    /// </summary>
    /// <remarks>必須在 UI 執行緒呼叫。</remarks>
    /// <returns>是否成功啟動程序或鍵盤已顯示</returns>
    public static bool TryOpen()
    {
        // 關鍵修正：若鍵盤已經可見，則不執行 Toggle，避免「閃一下就關閉」的問題。
        if (IsVisible())
        {
            Debug.WriteLine("觸控式鍵盤已顯示，略過開啟動作。");
            return true;
        }

        // 進入保護，防止並發觸發。
        if (Interlocked.CompareExchange(ref _isOpening, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            // 策略 1：優先使用 COM 介面（ITipInvocation）。
            // 在 Windows 10 週年更新之後，使用 COM 介面能更穩定地向背景服務發送 Toggle 指令。
            if (TryOpenViaCOM())
            {
                return true;
            }

            // 策略 2：Fallback 至啟動 TabTip.exe 程式。
            string? strTabTipPath = SystemHelper.GetTabTipPath();

            if (string.IsNullOrEmpty(strTabTipPath))
            {
                Debug.WriteLine("無法取得 TabTip.exe 路徑。");

                return false;
            }

            // 使用 Process.Start 啟動。
            using Process? process = Process.Start(new ProcessStartInfo(strTabTipPath)
            {
                UseShellExecute = true
            });

            return true;
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine($"無法啟動觸控式鍵盤程序（Win32）：{ex.Message}");

            return false;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "啟動觸控式鍵盤程序失敗");

            Debug.WriteLine($"無法啟動觸控式鍵盤程序：{ex.Message}");

            return false;
        }
        finally
        {
            // 500ms 後重置原子旗標，使用 SafeFireAndForget 確保例外不被靜默吞沒。
            ResetIsOpeningAfterDelayAsync().SafeFireAndForget();
        }
    }

    /// <summary>
    /// 使用 COM 介面 ITipInvocation 嘗試開啟觸控鍵盤
    /// </summary>
    /// <returns>是否成功切換狀態</returns>
    private static bool TryOpenViaCOM()
    {
        try
        {
            // 如果尚未初始化 COM 物件，才進行建立。
            if (_tipInvocation == null)
            {
                // UIHostNoLaunch CLSID：{4ce576fa-83dc-4f88-951c-9d0782b4e376}。
                Guid clsid_UIHostNoLaunch = new("4ce576fa-83dc-4f88-951c-9d0782b4e376"),
                     iid_IUnknown = new("00000000-0000-0000-C000-000000000046");

                // CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER。
                uint clsContext = Ole32.ClsCtx.InprocServer |
                    Ole32.ClsCtx.LocalServer;

                int hr = Ole32.CoCreateInstance(in clsid_UIHostNoLaunch, 0, clsContext, in iid_IUnknown, out nint ppv);

                if (hr < 0 ||
                    ppv == 0)
                {
                    Debug.WriteLine($"CoCreateInstance 建立觸控鍵盤 COM 物件失敗（HRESULT：0x{hr:X}）。");

                    return false;
                }

                try
                {
                    // 取得來源生成的 COM 介面。
                    _tipInvocation = (Ole32.ITipInvocation)_wrappers.GetOrCreateObjectForComInstance(ppv, CreateObjectFlags.None);
                }
                finally
                {
                    // 釋放 ppv，因為 _wrappers 已接管並增加參考計數。
                    Marshal.Release(ppv);
                }
            }

            if (_tipInvocation != null)
            {
                // 再次檢查可見性，避免在 COM 初始化期間發生的並發變更。
                if (!IsVisible())
                {
                    _tipInvocation.Toggle(User32.GetDesktopWindow());

                    Debug.WriteLine("已透過 ITipInvocation（COM）切換觸控鍵盤顯示。");
                }

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"透過 ITipInvocation 切換觸控式鍵盤失敗：{ex.Message}");

            _tipInvocation = null;

            return false;
        }
    }

    /// <summary>
    /// 延遲 500ms 後重置「正在開啟中」的原子旗標
    /// </summary>
    /// <returns>Task</returns>
    private static async Task ResetIsOpeningAfterDelayAsync()
    {
        await Task.Delay(500).ConfigureAwait(false);

        Interlocked.Exchange(ref _isOpening, 0);
    }

    /// <summary>
    /// 釋放靜態 COM 介面參考，防止應用程式關閉時發生 COM 物件洩漏
    /// </summary>
    internal static void Cleanup()
    {
        Interlocked.Exchange(ref _tipInvocation, null);
    }
}