using InputBox.Core.Controls;
using InputBox.Core.Services;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證動態計數標籤已鎖定寬度，避免數值變化時造成視覺抖動。
/// </summary>
public sealed class DialogLabelStabilityTests
{
    [Fact]
    public void PhraseEditDialog_CountLabels_UseFixedWidth()
    {
        using var dialog = new PhraseEditDialog("Name", "Content", null);

        Label nameCount = GetRequiredPrivateField<Label>(dialog, "_lblNameCount");
        Label contentCount = GetRequiredPrivateField<Label>(dialog, "_lblContentCount");

        Assert.False(nameCount.AutoSize);
        Assert.False(contentCount.AutoSize);
        Assert.True(nameCount.MinimumSize.Width > 0);
        Assert.True(contentCount.MinimumSize.Width > 0);
    }

    [Fact]
    public void PhraseManagerDialog_CountLabel_UsesFixedWidth()
    {
        var phraseService = new PhraseService();
        using var dialog = new PhraseManagerDialog(phraseService);

        Label phraseCount = GetRequiredPrivateField<Label>(dialog, "_lblPhraseCount");

        Assert.False(phraseCount.AutoSize);
        Assert.True(phraseCount.MinimumSize.Width > 0);
    }

    private static T GetRequiredPrivateField<T>(object instance, string fieldName) where T : class
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);

        return Assert.IsType<T>(field!.GetValue(instance));
    }
}
