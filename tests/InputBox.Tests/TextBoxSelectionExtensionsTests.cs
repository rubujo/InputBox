using InputBox.Core.Extensions;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證右搖桿延伸選取共用的 TextBox 擴充方法：錨點解析與單字跳轉目標計算。
/// <para>這些邏輯原本在主視窗、數值輸入與片語編輯三處各有一份複本，集中後以此測試保護行為一致。</para>
/// </summary>
public sealed class TextBoxSelectionExtensionsTests
{
    /// <summary>
    /// 沒有選取範圍時，應以目前游標位置作為新錨點，活動邊緣與錨點相同。
    /// </summary>
    [Fact]
    public void ResolveSelectionAnchor_NoSelection_UsesCaretAsAnchor()
    {
        using TextBox textBox = new() { Text = "hello world" };
        textBox.Select(3, 0);

        (int anchor, int activeEdge) = textBox.ResolveSelectionAnchor(currentAnchor: 7);

        Assert.Equal(3, anchor);
        Assert.Equal(3, activeEdge);
    }

    /// <summary>
    /// 錨點位於選取左端時，活動邊緣應在右端，並沿用既有錨點。
    /// </summary>
    [Fact]
    public void ResolveSelectionAnchor_AnchorAtStart_ActiveEdgeIsSelectionEnd()
    {
        using TextBox textBox = new() { Text = "hello world" };
        textBox.Select(2, 4);

        (int anchor, int activeEdge) = textBox.ResolveSelectionAnchor(currentAnchor: 2);

        Assert.Equal(2, anchor);
        Assert.Equal(6, activeEdge);
    }

    /// <summary>
    /// 錨點位於選取右端（反向選取）時，活動邊緣應在左端，並沿用既有錨點。
    /// </summary>
    [Fact]
    public void ResolveSelectionAnchor_AnchorAtEnd_ActiveEdgeIsSelectionStart()
    {
        using TextBox textBox = new() { Text = "hello world" };
        textBox.Select(2, 4);

        (int anchor, int activeEdge) = textBox.ResolveSelectionAnchor(currentAnchor: 6);

        Assert.Equal(6, anchor);
        Assert.Equal(2, activeEdge);
    }

    /// <summary>
    /// 選取範圍兩端都不等於既有錨點（例如使用者以滑鼠改變過選取）時，應以選取起點重新作為錨點。
    /// </summary>
    [Fact]
    public void ResolveSelectionAnchor_StaleAnchor_ResetsToSelectionStart()
    {
        using TextBox textBox = new() { Text = "hello world" };
        textBox.Select(2, 4);

        (int anchor, int activeEdge) = textBox.ResolveSelectionAnchor(currentAnchor: 9);

        Assert.Equal(2, anchor);
        Assert.Equal(6, activeEdge);
    }

    /// <summary>
    /// 計算單字跳轉目標時不得改變文字方塊目前的選取範圍。
    /// </summary>
    [Fact]
    public void GetWordJumpTarget_DoesNotChangeCurrentSelection()
    {
        using TextBox textBox = new() { Text = "hello world again" };
        textBox.Select(1, 3);

        int target = textBox.GetWordJumpTarget(caret: 0, forward: true);

        Assert.True(target > 0);
        Assert.Equal(1, textBox.SelectionStart);
        Assert.Equal(3, textBox.SelectionLength);
    }

    /// <summary>
    /// 向右與向左的單字跳轉應分別往對應方向移動，且在文字邊界時不越界。
    /// </summary>
    [Fact]
    public void GetWordJumpTarget_MovesInRequestedDirectionWithinBounds()
    {
        using TextBox textBox = new() { Text = "hello world again" };

        int forward = textBox.GetWordJumpTarget(caret: 0, forward: true);
        int backward = textBox.GetWordJumpTarget(caret: textBox.TextLength, forward: false);

        Assert.InRange(forward, 1, textBox.TextLength);
        Assert.InRange(backward, 0, textBox.TextLength - 1);
    }
}
