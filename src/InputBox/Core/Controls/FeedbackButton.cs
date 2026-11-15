using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Resources;
using System.Drawing.Drawing2D;

namespace InputBox.Core.Controls;

/// <summary>原生 Standard／Latest 表面與專案必要的焦點、按壓及凝視提示。</summary>
public class FeedbackButton : Button
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)Math.Round(AppSettings.TargetFrameTimeMs) };
    private bool _hovered;
    private bool _pointerPressed;
    private bool _keyboardPressed;
    private long _started;
    private float _progress;
    private Font? _regular;
    private Font? _bold;
    private bool _configured;
    private string _description = string.Empty;
    private Action<bool>? _onFocusStateChanged;
    private CancellationToken _formToken;
    private CancellationTokenRegistration _formRegistration;
    private int _lifetimeVersion;
    private int _interactionVersion;

    public FeedbackButton()
    {
        FlatStyle = FlatStyle.Standard;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        _timer.Tick += (_, _) =>
        {
            if (!_hovered || Pressed || !Enabled || IsDisposed || !IsHandleCreated || _formToken.IsCancellationRequested)
            {
                StopDwell();
                return;
            }
            _progress = Math.Min(1, (Environment.TickCount64 - _started) / 1000f);
            Invalidate();
            if (_progress >= 1)
            {
                _timer.Stop();
            }
        };
    }

    /// <summary>目前的按壓視覺，滑鼠與鍵盤來源分開清理。</summary>
    internal bool Pressed => _pointerPressed || _keyboardPressed;
    /// <summary>供畫面回歸驗證讀取，不以進度推導互動狀態。</summary>
    internal float Progress => _progress;

    /// <summary>初始化或更新字型／在地化設定，不重新建立互動狀態或動畫。</summary>
    internal void ConfigureFeedback(string description, Font? regular, Font? bold,
        Action<bool>? onFocusStateChanged, CancellationToken formToken)
    {
        _regular = regular ?? Font;
        _bold = bold;
        _description = description;
        _onFocusStateChanged = onFocusStateChanged;
        FlatStyle = FlatStyle.Standard;
        bool replaceLifetime = !_configured || _formToken != formToken;
        _configured = true;
        if (replaceLifetime)
        {
            int version = ++_lifetimeVersion;
            _formRegistration.Dispose();
            _formToken = formToken;
            _formRegistration = formToken.Register(() =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated) { return; }
                this.SafeBeginInvoke(() =>
                {
                    if (_configured && version == _lifetimeVersion && _formToken.IsCancellationRequested)
                    {
                        CancelDwell();
                    }
                });
            });
        }
        Reconcile();
    }

    /// <summary>初始化版面時在置中文字上下保留固定空間，讓底部 Dwell 不遮住字形。</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        Size preferred = base.GetPreferredSize(proposedSize);
        Font measuringFont = _bold ?? _regular ?? Font;
        Size text = TextRenderer.MeasureText(Text, measuringFont);
        float scale = DeviceDpi / AppSettings.BaseDpi;
        int dwellSpace = (int)Math.Ceiling(7 * scale) + (int)Math.Ceiling(6 * scale) +
            2 * (int)Math.Max(1, Math.Ceiling(scale)) + (int)Math.Ceiling(2 * scale);
        return new Size(Math.Max(preferred.Width, text.Width + Padding.Horizontal + (int)Math.Ceiling(8 * scale)),
            Math.Max(preferred.Height, text.Height + Padding.Vertical + 2 * dwellSpace));
    }
    /// <summary>更新呼叫端提供的共享字型，保留描述、回呼與正在進行的互動。</summary>
    internal void UpdateFeedbackFonts(Font regular, Font? bold)
    {
        _regular = regular;
        _bold = bold;
        Reconcile();
    }
    internal void CancelDwell()
    {
        ++_interactionVersion;
        StopDwell();
        Reconcile();
    }

    /// <summary>點擊結束後依實際游標重接視線；新的互動會使舊接合失效。</summary>
    internal async Task ReGazeAfterClickAsync()
    {
        int version = ++_interactionVersion;
        StopDwell();
        Invalidate();
        await Task.Yield();
        if (IsDisposed || !Enabled || _formToken.IsCancellationRequested || version != _interactionVersion)
        {
            return;
        }
        _hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
        RestartDwell();
        Reconcile();
    }
    private void RestartDwell()
    {
        StopDwell();
        if (!_configured || !_hovered || Pressed || !Enabled || _formToken.IsCancellationRequested)
        {
            return;
        }
        _started = Environment.TickCount64;
        if (SystemInformation.UIEffectsEnabled)
        {
            _timer.Start();
        }
        else
        {
            _progress = 1;
        }
    }

    private void StopDwell()
    {
        _timer.Stop();
        bool visibleProgress = _progress > 0;
        _progress = 0;
        if (visibleProgress && IsHandleCreated && !IsDisposed && !Disposing) { Invalidate(); }
    }

    private void Reconcile()
    {
        if (!_configured || IsDisposed || Disposing) { return; }
        Font? desired = Pressed || (Focused && !_hovered) ? _bold : _regular;
        if (desired is not null && !ReferenceEquals(Font, desired))
        {
            Font = desired;
        }
        AccessibleDescription = !Enabled ? _description : Pressed ?
            $"{_description} ({Strings.A11y_State_Pressed})" : _hovered ?
            $"{_description} ({Strings.A11y_State_Hover})" : Focused ?
            $"{_description} ({Strings.A11y_State_Focused})" : _description;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        ++_interactionVersion;
        _hovered = true;
        RestartDwell();
        Reconcile();
        _onFocusStateChanged?.Invoke(true);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        ++_interactionVersion;
        _hovered = false;
        _pointerPressed = false;
        StopDwell();
        Reconcile();
        _onFocusStateChanged?.Invoke(Focused);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        ++_interactionVersion;
        if (Enabled && e.Button == MouseButtons.Left)
        {
            _pointerPressed = true;
            StopDwell();
            Reconcile();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        ++_interactionVersion;
        if (e.Button == MouseButtons.Left)
        {
            _pointerPressed = false;
            RestartDwell();
            Reconcile();
        }
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ++_interactionVersion;
        if (Enabled && Focused && e.KeyCode == Keys.Space)
        {
            _keyboardPressed = true;
            StopDwell();
            Reconcile();
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        ++_interactionVersion;
        if (e.KeyCode == Keys.Space)
        {
            _keyboardPressed = false;
            RestartDwell();
            Reconcile();
        }
        base.OnKeyUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        ++_interactionVersion;
        Reconcile();
        _onFocusStateChanged?.Invoke(true);
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        ++_interactionVersion;
        _pointerPressed = false;
        _keyboardPressed = false;
        RestartDwell();
        Reconcile();
        _onFocusStateChanged?.Invoke(_hovered);
        base.OnLostFocus(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture && _pointerPressed)
        {
            ++_interactionVersion;
            _pointerPressed = false;
            RestartDwell();
            Reconcile();
        }
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        ++_interactionVersion;
        if (!Enabled)
        {
            _hovered = false;
            _pointerPressed = false;
            _keyboardPressed = false;
            StopDwell();
        }
        Reconcile();
        base.OnEnabledChanged(e);
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        Invalidate();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ++_interactionVersion;
        StopDwell();
        _hovered = false;
        _pointerPressed = false;
        _keyboardPressed = false;
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _configured = false;
            ++_lifetimeVersion;
            _formRegistration.Dispose();
            StopDwell();
            _regular = null;
            _bold = null;
            _onFocusStateChanged = null;
        }
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing) { _timer.Dispose(); }
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }
        base.OnPaint(e);
        if (!_configured) { return; }
        Graphics graphics = e.Graphics;
        bool dark = this.IsDarkModeActive();
        bool highContrast = SystemInformation.HighContrast;
        bool strong = Enabled && (Pressed || (Focused && !_hovered));
        bool isDefault = Enabled && FindForm() is Form form &&
            ReferenceEquals(form.AcceptButton, this) && form.ActiveControl is not Button;
        float scale = DeviceDpi / AppSettings.BaseDpi;
        Color back = Color.Empty;
        Color fore = ForeColor;
        if (strong)
        {
            (back, fore) = highContrast
                ? Pressed ? (SystemColors.HighlightText, SystemColors.Highlight) : (SystemColors.Highlight, SystemColors.HighlightText)
                : Pressed ? (dark ? Color.FromArgb(255, 200, 120) : Color.FromArgb(28, 28, 28), dark ? Color.Black : Color.White)
                : dark ? (Color.White, Color.Black) : (Color.Black, Color.White);
            using Brush surface = new SolidBrush(back);
            graphics.FillRectangle(surface, ClientRectangle);
            int inset = (int)Math.Ceiling(4 * scale);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak | TextFormatFlags.PreserveGraphicsClipping;
            if (!UseMnemonic)
            {
                flags |= TextFormatFlags.NoPrefix;
            }
            if (RightToLeft == RightToLeft.Yes)
            {
                flags |= TextFormatFlags.RightToLeft;
            }
            if (!ShowKeyboardCues) { flags |= TextFormatFlags.HidePrefix; }
            Rectangle textBounds = Rectangle.FromLTRB(inset + Padding.Left, inset + Padding.Top,
                Width - inset - Padding.Right, Height - inset - Padding.Bottom);
            TextRenderer.DrawText(graphics, Text, Font, textBounds, fore, flags);
        }

        if (Enabled && (strong || _hovered || isDefault))
        {
            (Color border, Color backing) = strong
                ? highContrast ? (fore, back) : (dark ? Color.MediumBlue : Color.Cyan, back)
                : ControlExtensions.GetContextualFocusColors(isDefault ? GetInputContext()?.BackColor ?? Color.Empty : Color.Empty, dark);
            this.DrawButtonInteractiveBorder(graphics, border, scale, out int inset, out int thickness, backing);
            if (Pressed)
            {
                // 高對比也保留固定位置的內框，提供不只依賴色彩的按壓線索。
                int inner = inset + thickness + (int)Math.Ceiling(scale);
                using Pen line = new(fore, Math.Max(1, 2 * scale));
                graphics.DrawRectangle(line, inner, inner, Width - 2 * inner - 1, Height - 2 * inner - 1);
            }
        }

        if (Enabled && _hovered && !Pressed && _progress > 0)
        {
            int inset = (int)Math.Ceiling(7 * scale);
            int backing = (int)Math.Max(1, Math.Ceiling(scale));
            int height = (int)Math.Ceiling(6 * scale) + 2 * backing;
            Rectangle track = new(inset, Height - inset - height, Width - 2 * inset, height);
            Color trackColor = highContrast ? SystemColors.Highlight : dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220);
            Color fillColor = highContrast ? SystemColors.HighlightText : dark ? Color.LimeGreen : Color.Green;
            using Brush trackBrush = new SolidBrush(trackColor);
            using Brush fillBrush = new SolidBrush(fillColor);
            graphics.FillRectangle(trackBrush, track);
            Rectangle fill = Rectangle.Inflate(track, -backing, -backing);
            fill.Width = (int)(fill.Width * _progress);
            graphics.FillRectangle(fillBrush, fill);
            if (!highContrast)
            {
                using HatchBrush texture = new(HatchStyle.BackwardDiagonal, dark ? Color.DarkGreen : Color.PaleGreen, Color.Transparent);
                graphics.FillRectangle(texture, fill);
            }
        }

    }

    /// <summary>沿巢狀容器尋找實際輸入焦點，與輸入框提示共用情境選色。</summary>
    private Control? GetInputContext()
    {
        Control? context = FindForm()?.ActiveControl;
        while (context is ContainerControl { ActiveControl: not null } container)
        {
            context = container.ActiveControl;
        }
        return context;
    }
}
