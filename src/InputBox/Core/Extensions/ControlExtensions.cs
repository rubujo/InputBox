using System.Diagnostics;
using System.Globalization;
using InputBox.Core.Configuration;

namespace InputBox.Core.Extensions;

/// <summary>
/// Control 類別的擴充方法
/// </summary>
public static class ControlExtensions
{
    /// <summary>
    /// 安全的同步 Invoke
    /// </summary>
    /// <param name="control">要執行的控制項</param>
    /// <param name="action">要執行的動作</param>
    public static void SafeInvoke(
        this Control control,
        Action action)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return;
        }

        try
        {
            // 如果控制代碼尚未建立，無法使用 Invoke，直接在當前執行緒執行。
            // 但如果當前是背景執行緒，執行 UI 更新會導致崩潰或不可預期的行為，因此必須丟棄。
            if (!control.IsHandleCreated)
            {
                if (SynchronizationContext.Current is WindowsFormsSynchronizationContext)
                {
                    action();
                }

                return;
            }

            if (control.InvokeRequired)
            {
                control.Invoke(new MethodInvoker(() =>
                {
                    if (!control.IsDisposed &&
                        control.IsHandleCreated)
                    {
                        try
                        {
                            action();
                        }
                        catch (ObjectDisposedException)
                        {

                        }
                    }
                }));
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {

        }
        catch (InvalidOperationException)
        {

        }
    }

    /// <summary>
    /// 安全的非同步 Invoke（支援 Action）。
    /// 在 async 任務內優先使用此方法，內部封裝了 .NET 10 原生 InvokeAsync 並加入安全檢查。
    /// </summary>
    /// <param name="control">控制項</param>
    /// <param name="action">Action</param>
    /// <returns>Task</returns>
    public static async Task SafeInvokeAsync(this Control control, Action action)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return;
        }

        if (!control.IsHandleCreated)
        {
            if (SynchronizationContext.Current is WindowsFormsSynchronizationContext)
            {
                action();
            }

            return;
        }

        try
        {
            // 調用 .NET 10 原生非同步調度 API。
            await control.InvokeAsync(() =>
            {
                if (!control.IsDisposed && control.IsHandleCreated)
                {
                    action();
                }
            });
        }
        catch (ObjectDisposedException)
        {

        }
        catch (InvalidOperationException)
        {

        }

        // 其餘業務邏輯例外應讓它向上拋出，以便被全域例外處理器攔截。
    }

    /// <summary>
    /// 安全的非同步 Invoke（支援包含 await 的非同步邏輯）
    /// </summary>
    /// <param name="control">要執行的控制項</param>
    /// <param name="action">要執行的非同步動作</param>
    /// <returns>Task</returns>
    public static async Task SafeInvokeAsync(
        this Control control,
        Func<Task> action)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return;
        }

        // 如果控制代碼尚未建立，直接執行（因為此時通常還沒跨執行緒）。
        // 防護：若目前為背景執行緒，不可直接執行 UI 相關邏輯，直接放棄。
        if (!control.IsHandleCreated)
        {
            if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            {
                return;
            }

            try
            {
                await action();
            }
            catch (ObjectDisposedException)
            {
                // 忽略執行過程中的釋放錯誤。
            }
            catch (Exception)
            {
                // 重新拋出業務邏輯例外。
                throw;
            }

            return;
        }

        try
        {
            // 利用 .NET 10 原生支援的非同步調度，簡化邏輯並減少 TaskCompletionSource 的配置開銷。
            await control.InvokeAsync(async (ct) =>
            {
                // 進入 UI 執行緒後的最終安全檢查。
                if (!control.IsDisposed &&
                    control.IsHandleCreated)
                {
                    await action();
                }
            });
        }
        catch (ObjectDisposedException)
        {

        }
        catch (InvalidOperationException)
        {

        }
    }

    /// <summary>
    /// 安全的非同步 Invoke
    /// </summary>
    /// <remarks>
    /// 自動檢查 IsHandleCreated 與 IsDisposed，並捕捉 ObjectDisposedException。
    /// 適用於從背景執行緒更新 UI，且不希望阻塞背景執行緒的情境。
    /// </remarks>
    /// <param name="control">要執行的控制項</param>
    /// <param name="action">要執行的動作</param>
    public static void SafeBeginInvoke(
        this Control control,
        Action action)
    {
        // 第一層檢查：如果控制項已經無效，直接放棄，不要排程。
        if (control == null ||
            control.IsDisposed ||
            !control.IsHandleCreated)
        {
            return;
        }

        try
        {
            // 嘗試排程到 UI 執行緒。
            control.BeginInvoke(new MethodInvoker(() =>
            {
                // 第二層檢查：
                // 雖然排程當下控制項還在，但等到 UI 執行緒真正要跑這段程式碼時，
                // 視窗可能剛好被關掉了。所以這裡要再檢查一次。
                if (control.IsDisposed ||
                    !control.IsHandleCreated)
                {
                    return;
                }

                try
                {
                    action();
                }
                catch (ObjectDisposedException)
                {
                    // 忽略執行過程中的釋放錯誤。
                }
            }));
        }
        catch (ObjectDisposedException)
        {
            // 捕捉：在開啟 BeginInvoke 的瞬間視窗被釋放。
        }
        catch (InvalidOperationException)
        {
            // 捕捉：Handle 尚未建立或已失效。
        }
    }

    /// <summary>
    /// 依據語言習慣與內容產生包含助記鍵（Mnemonic）的文字
    /// </summary>
    /// <param name="text">原始文字</param>
    /// <param name="mnemonic">助記鍵字母</param>
    /// <returns>格式化後的文字</returns>
    public static string GetMnemonicText(string text, char mnemonic)
    {
        if (string.IsNullOrEmpty(text))
        {
            return $"(&{char.ToUpperInvariant(mnemonic)})";
        }

        char upperMnemonic = char.ToUpperInvariant(mnemonic);

        // 冪等性檢查：
        // 1. 如果字串中已經包含 '&' 標記（如 "確定(&A)"）。
        // 2. 如果字串結尾已經包含類似的括號提示（如 "確定 (A)" 或 "確定(A)"）。
        // 這能防止重複調用或資源檔本身自帶標記時產生的「確定 (A) (A)」問題。
        if (text.Contains('&') ||
            text.EndsWith($"({upperMnemonic})", StringComparison.OrdinalIgnoreCase) ||
            text.EndsWith($"(&{upperMnemonic})", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        // 核心設計：全語系一律採用後綴式提示。
        return $"{text} (&{upperMnemonic})";
    }

    /// <summary>
    /// 判斷目前控制項是否處於深色模式（Dark Mode）
    /// </summary>
    /// <param name="control">要檢查的控制項</param>
    /// <returns>若為深色模式則傳回 true</returns>
    public static bool IsDarkModeActive(this Control control)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return false;
        }

        // 高對比模式優先權高於深色模式，但在配色邏輯中通常分開處理。
        if (SystemInformation.HighContrast)
        {
            return false;
        }

        // .NET 10 官方 API：返回目前應用程式實際解析後的深色模式狀態。
        return Application.IsDarkModeEnabled;
    }

    /// <summary>
    /// 執行通用的眼動儀注視填滿動畫
    /// </summary>
    /// <param name="control">要執行動畫的控制項</param>
    /// <param name="animationIdField">用於追蹤目前動畫序號的欄位引用（需使用 Interlocked 操作）</param>
    /// <param name="id">本次動畫的目標序號</param>
    /// <param name="progressSetter">設定進度值（0.0 ~ 1.0）的回呼委派</param>
    /// <param name="durationMs">動畫總時長（毫秒），預設 1000ms</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>Task</returns>
    public static async Task RunDwellAnimationAsync(
        this Control control,
        long id,
        Func<long> animationIdGetter,
        Action<float> progressSetter,
        int durationMs = 1000,
        CancellationToken ct = default)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return;
        }

        // 系統動畫服從性：若使用者關閉了 UI 特效，直接跳至完成狀態。
        if (!SystemInformation.UIEffectsEnabled)
        {
            progressSetter(1.0f);

            control.Invalidate();

            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        // 根據規範：統一使用 60 FPS（16.6ms）物理頻率。
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(AppSettings.TargetFrameTimeMs));

        while (await timer.WaitForNextTickAsync(ct) &&
            animationIdGetter() == id &&
            !control.IsDisposed &&
            control.IsHandleCreated)
        {
            double elapsed = stopwatch.Elapsed.TotalMilliseconds;

            float progress = (float)Math.Min(1.0, elapsed / durationMs);

            progressSetter(progress);

            control.Invalidate();

            if (progress >= 1.0f)
            {
                break;
            }
        }
    }

    /// <summary>
    /// 遞歸更新控制項及其所有子控制項的背景色與前景色。
    /// </summary>
    /// <remarks>
    /// 針對複合控制項（如 NumericUpDown），透過強制遞歸確保內部的私有子元件（如 TextBox 編輯區）亦能同步配色，
    /// 杜絕 A11y 閃爍時產生的視覺殘留（Ghosting）。
    /// </remarks>
    /// <param name="parent">要開始更新的父控制項。</param>
    /// <param name="bg">新的背景顏色。</param>
    /// <param name="fg">新的前景顏色。</param>
    public static void UpdateRecursive(
        this Control parent,
        Color bg,
        Color fg)
    {
        if (parent == null ||
            parent.IsDisposed)
        {
            return;
        }

        // 更新本體。
        parent.BackColor = bg;
        parent.ForeColor = fg;

        // 針對複合控制項（如 NumericUpDown）的特殊強化：
        // NUD 內部的 TextBox 對於 BackColor 的變更可能存在渲染延遲，
        // 透過強制 Invalidate 確保內部的私有子元件立即重繪，防止閃爍殘留（Ghosting）。
        if (parent is NumericUpDown)
        {
            parent.Invalidate(true);
        }

        // 遞歸更新所有子控制項。
        // 對於 NumericUpDown 等複合控制項，其 Controls 集合包含了內部的 TextBox 與按鈕。
        foreach (Control child in parent.Controls)
        {
            UpdateRecursive(child, bg, fg);
        }
    }

    /// <summary>
    /// 將控制項及其所有子控制項的顏色屬性重設為 Color.Empty，
    /// 這將觸發 .NET 10 的原生主題引擎自動套用正確的系統配色。
    /// </summary>
    /// <param name="parent">父控制項。</param>
    public static void ResetThemeRecursive(this Control parent)
    {
        UpdateRecursive(parent, Color.Empty, Color.Empty);
    }

    /// <summary>
    /// 執行單字跳轉邏輯（智慧偵測空白、標點符號、字元類型轉換，支援全形與 IME 情境）
    /// </summary>
    /// <param name="tb">目標文字方塊</param>
    /// <param name="forward">是否向右跳轉</param>
    public static void WordJump(this TextBox tb, bool forward)
    {
        if (tb == null ||
            tb.IsDisposed)
        {
            return;
        }

        string text = tb.Text;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int pos = tb.SelectionStart,
            len = text.Length;

        if (forward)
        {
            if (pos >= len)
            {
                return;
            }

            // 取得起始字元的類別。
            CharType startType = GetCharType(text[pos]);

            // 持續移動，直到字元類型發生變化。
            // 若起始是空白，則會直接跳到下一個非空白單字的開始。
            // 若起始是文字，則會跳到該類型文字的邊界。
            while (pos < len &&
                GetCharType(text[pos]) == startType)
            {
                pos++;
            }

            // UX 補償：若跳轉後停在空白處，則繼續跳過後續所有空白，停在下一個實體單字的開始。
            if (pos < len &&
                GetCharType(text[pos]) == CharType.WhiteSpace)
            {
                while (pos < len &&
                    GetCharType(text[pos]) == CharType.WhiteSpace)
                {
                    pos++;
                }
            }

            tb.SelectionStart = pos;
        }
        else
        {
            if (pos <= 0)
            {
                return;
            }

            // 往回找，先跳過起始點左側的所有空白（如果有）。
            while (pos > 0 &&
                GetCharType(text[pos - 1]) == CharType.WhiteSpace)
            {
                pos--;
            }

            if (pos > 0)
            {
                // 取得目前單字末尾字元的類別。
                CharType targetType = GetCharType(text[pos - 1]);

                // 往回跳轉，直到字元類型變化（例如：從數字變回中文）。
                while (pos > 0 &&
                    GetCharType(text[pos - 1]) == targetType)
                {
                    pos--;
                }
            }

            tb.SelectionStart = pos;
        }

        tb.ScrollToCaret();
    }

    /// <summary>
    /// 內部定義字元類別，用於精準跳轉判定（支援全形感知）
    /// </summary>
    private enum CharType
    {
        /// <summary>
        /// 空白（含全形）
        /// </summary>
        WhiteSpace,
        /// <summary>
        /// 拉丁字母（含全形 Ａ-Ｚ）
        /// </summary>
        Latin,
        /// <summary>
        /// 中日韓文字
        /// </summary>
        CJK,
        /// <summary>
        /// 數字（含全形 ０-９）
        /// </summary>
        Digit,
        /// <summary>
        /// 標點符號（含全形）
        /// </summary>
        Punctuation,
        /// <summary>
        /// 特殊符號（含全形）
        /// </summary>
        Symbol,
        /// <summary>
        /// 其他
        /// </summary>
        Other
    }

    /// <summary>
    /// 取得字元的精確類別，特別針對全形與 CJK 環境優化
    /// </summary>
    private static CharType GetCharType(char c)
    {
        // 空白處理（.NET 已內建全形空格 U+3000 的支援）。
        if (char.IsWhiteSpace(c)) return CharType.WhiteSpace;

        // 數字處理（含全形）。
        if (char.IsDigit(c)) return CharType.Digit;

        // 標點與符號。
        if (char.IsPunctuation(c))
        {
            return CharType.Punctuation;
        }

        if (char.IsSymbol(c))
        {
            return CharType.Symbol;
        }

        // 字母與 CJK 判定（利用 Unicode 類別細分）。
        UnicodeCategory cat = char.GetUnicodeCategory(c);

        // CJK 漢字、假名、韓文通常歸類為 OtherLetter。
        if (cat == UnicodeCategory.OtherLetter)
        {
            return CharType.CJK;
        }

        // 拉丁字母、希臘文等歸類為 Uppercase／Lowercase Letter。
        if (cat == UnicodeCategory.UppercaseLetter ||
            cat == UnicodeCategory.LowercaseLetter ||
            cat == UnicodeCategory.TitlecaseLetter ||
            cat == UnicodeCategory.ModifierLetter)
        {
            return CharType.Latin;
        }

        return CharType.Other;
    }
}