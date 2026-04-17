using System.Runtime.CompilerServices;

namespace InputBox.Tests;

/// <summary>
/// 為測試行程建立獨立的設定目錄，避免任何測試讀寫到使用者真正的 AppData 資料。
/// </summary>
internal static class TestEnvironmentSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        string isolatedConfigDirectory = Path.Combine(
            Path.GetTempPath(),
            "InputBox.Tests",
            $"config-{Guid.NewGuid():N}");

        Directory.CreateDirectory(isolatedConfigDirectory);
        Environment.SetEnvironmentVariable("INPUTBOX_CONFIG_DIRECTORY", isolatedConfigDirectory);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(isolatedConfigDirectory))
                {
                    Directory.Delete(isolatedConfigDirectory, recursive: true);
                }
            }
            catch
            {
            }
        };
    }
}
