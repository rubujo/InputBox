using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InputBox.Tests;

/// <summary>
/// 先由 Win32 取得受測行程的可見頂層 HWND，再交給 UIA，避免其他程式無回應的 provider 阻塞桌面搜尋。
/// </summary>
internal static partial class UiWindowDiscovery
{
    /// <summary>
    /// 列舉指定行程的可見頂層視窗與浮動選單。
    /// </summary>
    /// <param name="automation">目前測試的 UIA 連線。</param>
    /// <param name="processId">受測 InputBox 行程識別碼。</param>
    /// <returns>可供 UIA 搜尋的受測視窗。</returns>
    public static unsafe IEnumerable<AutomationElement> FindRoots(AutomationBase automation, int processId)
    {
        WindowSearch search = new(processId);
        GCHandle state = GCHandle.Alloc(search);
        try
        {
            _ = EnumWindows(&CollectWindow, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        return search.Handles.Select(automation.FromHandle);
    }

    /// <summary>
    /// 僅收集指定行程的可見 HWND，不向其他應用程式的 UIA provider 查詢。
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectWindow(nint window, nint state)
    {
        WindowSearch search = (WindowSearch)GCHandle.FromIntPtr(state).Target!;
        _ = GetWindowThreadProcessId(window, out uint processId);
        if (processId == search.ProcessId && IsWindowVisible(window) != 0)
        {
            search.Handles.Add(window);
        }

        return 1;
    }

    /// <summary>
    /// 單次同步列舉使用的狀態，生命週期由 GCHandle 保護。
    /// </summary>
    private sealed class WindowSearch(int processId)
    {
        public uint ProcessId { get; } = checked((uint)processId);
        public List<nint> Handles { get; } = [];
    }

    [LibraryImport("user32.dll")]
    private static unsafe partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(nint window);
}