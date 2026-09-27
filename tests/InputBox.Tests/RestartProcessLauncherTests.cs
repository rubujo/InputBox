using InputBox.Core.Services;
using System.Diagnostics;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證程式內重啟的啟動資訊與 WinForms Application.Restart() 行為一致，並正確保留命令列引數。
/// </summary>
public sealed class RestartProcessLauncherTests
{
    /// <summary>
    /// 應以目前執行檔啟動新執行個體，且不透過殼層啟動，才能直接取得新程序識別碼並對齊 Application.Restart()。
    /// </summary>
    [Fact]
    public void CreateStartInfo_UsesExecutablePathWithoutShellExecute()
    {
        const string exePath = @"C:\Apps\InputBox\InputBox.exe";

        ProcessStartInfo startInfo = RestartProcessLauncher.CreateStartInfo(exePath, [exePath]);

        Assert.Equal(exePath, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Empty(startInfo.ArgumentList);
    }

    /// <summary>
    /// 命令列第一個元素是執行檔本身，只應轉送其後的引數，並保持原本順序。
    /// </summary>
    [Fact]
    public void CreateStartInfo_ForwardsArgumentsAfterExecutableInOrder()
    {
        ProcessStartInfo startInfo = RestartProcessLauncher.CreateStartInfo(
            "InputBox.exe",
            ["InputBox.exe", "--first", "second"]);

        Assert.Equal(["--first", "second"], startInfo.ArgumentList);
    }

    /// <summary>
    /// 含空白或引號的引數應原樣保留，避免像字串拼接那樣在重啟後被拆開或轉義錯誤。
    /// </summary>
    [Fact]
    public void CreateStartInfo_PreservesArgumentsContainingSpacesAndQuotes()
    {
        const string pathWithSpace = @"D:\My Folder\a.txt";
        const string quoted = "say \"hi\"";

        ProcessStartInfo startInfo = RestartProcessLauncher.CreateStartInfo(
            "InputBox.exe",
            ["InputBox.exe", pathWithSpace, quoted]);

        Assert.Equal([pathWithSpace, quoted], startInfo.ArgumentList);
    }

    /// <summary>
    /// 執行檔路徑為空白時應立即擲出例外，避免以無效路徑嘗試重啟。
    /// </summary>
    [Fact]
    public void CreateStartInfo_BlankExecutablePath_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => RestartProcessLauncher.CreateStartInfo(" ", ["InputBox.exe"]));
    }

    /// <summary>
    /// 指定舊執行個體的程序識別碼時，應在轉送的引數之後附加交接參數，讓新執行個體等待接手 Mutex。
    /// </summary>
    [Fact]
    public void CreateStartInfo_WithHandoffProcessId_AppendsHandoffArgument()
    {
        ProcessStartInfo startInfo = RestartProcessLauncher.CreateStartInfo(
            "InputBox.exe",
            ["InputBox.exe", "--first"],
            handoffProcessId: 1234);

        Assert.Equal(["--first", "--restart-handoff=1234"], startInfo.ArgumentList);
    }

    /// <summary>
    /// 連續重啟時，舊的交接參數不得被重複轉送，只保留本次的交接參數。
    /// </summary>
    [Fact]
    public void CreateStartInfo_WhenAlreadyHandoffLaunch_ReplacesPreviousHandoffArgument()
    {
        ProcessStartInfo startInfo = RestartProcessLauncher.CreateStartInfo(
            "InputBox.exe",
            ["InputBox.exe", "--restart-handoff=111", "--first"],
            handoffProcessId: 222);

        Assert.Equal(["--first", "--restart-handoff=222"], startInfo.ArgumentList);
    }
}
