using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using System.Runtime.CompilerServices;
using Xunit;

namespace InputBox.Tests;

public sealed class FeedbackButtonLifetimeTests
{
    [Fact]
    public void AttachAfterDispose_DoesNotRetainButtonThroughLiveToken()
    {
        using CancellationTokenSource lifetime = new();
        WeakReference reference = CreateDisposedButton(lifetime.Token);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
        GC.KeepAlive(lifetime);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDisposedButton(CancellationToken token)
    {
        FeedbackButton button = new();
        button.Dispose();
        button.AttachEyeTrackerFeedback("disposed", null, null, token);
        return new WeakReference(button);
    }
}
