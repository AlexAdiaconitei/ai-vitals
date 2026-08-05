using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AIVitals.Platform.Windows;

public enum TrayMenuIcon
{
    None,
    Dashboard,
    Widget,
    Layout,
    Pin,
    Lock,
    Refresh,
    Move,
    Link,
    Theme,
    Settings,
    Exit
}

public sealed record TrayMenuAction(
    string Text,
    Func<Task>? ExecuteAsync = null,
    TrayMenuIcon Icon = TrayMenuIcon.None,
    bool IsChecked = false,
    IReadOnlyList<TrayMenuAction>? Children = null);

public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SynchronizationContext _uiContext;
    private readonly System.Windows.Forms.Timer _leftClickTimer;

    public TrayIconHost(
        Action showQuickView,
        Action showDashboard,
        Action showContextMenu)
    {
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayIconHost must be created on the UI thread.");

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "AI Vitals",
            Visible = true
        };
        _leftClickTimer = new System.Windows.Forms.Timer
        {
            Interval = SystemInformation.DoubleClickTime
        };
        _leftClickTimer.Tick += (_, _) =>
        {
            _leftClickTimer.Stop();
            OnUi(showQuickView);
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                _leftClickTimer.Stop();
                _leftClickTimer.Start();
            }
            if (eventArgs.Button == MouseButtons.Right)
            {
                _leftClickTimer.Stop();
                OnUi(showContextMenu);
            }
        };
        _notifyIcon.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Left) return;
            _leftClickTimer.Stop();
            OnUi(showDashboard);
        };
    }

    public void SetStatus(string text)
    {
        var tooltip = text.Length > 63 ? text[..63] : text;
        OnUi(() => _notifyIcon.Text = tooltip);
    }

    public void Dispose()
    {
        _leftClickTimer.Stop();
        _leftClickTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var outer = RingPen(Color.FromArgb(49, 214, 198), 3.5f);
        using var middle = RingPen(Color.FromArgb(61, 139, 255), 3.5f);
        using var inner = RingPen(Color.FromArgb(255, 155, 47), 3.5f);
        graphics.DrawArc(outer, 2.5f, 2.5f, 27, 27, -90, 304);
        graphics.DrawArc(middle, 7.5f, 7.5f, 17, 17, -90, 266);
        graphics.DrawArc(inner, 12.5f, 12.5f, 7, 7, -90, 226);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private static Pen RingPen(Color color, float width) => new(color, width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round
    };

    private static Bitmap CreateMenuIcon(TrayMenuIcon icon)
    {
        var glyph = icon switch
        {
            TrayMenuIcon.Dashboard => "\uE80F",
            TrayMenuIcon.Widget => "\uE7F4",
            TrayMenuIcon.Layout => "\uECA5",
            TrayMenuIcon.Pin => "\uE718",
            TrayMenuIcon.Lock => "\uE72E",
            TrayMenuIcon.Refresh => "\uE72C",
            TrayMenuIcon.Move => "\uE7C2",
            TrayMenuIcon.Link => "\uE71B",
            TrayMenuIcon.Theme => "\uE790",
            TrayMenuIcon.Settings => "\uE713",
            TrayMenuIcon.Exit => "\uE7E8",
            _ => string.Empty
        };
        var bitmap = new Bitmap(20, 20);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Point);
        using var brush = new SolidBrush(icon is TrayMenuIcon.Dashboard or TrayMenuIcon.Refresh ? Color.FromArgb(61, 139, 255) : Color.FromArgb(225, 232, 241));
        var size = graphics.MeasureString(glyph, font);
        graphics.DrawString(glyph, font, brush, (20 - size.Width) / 2, (20 - size.Height) / 2);
        return bitmap;
    }

    private void OnUi(Action action) => _uiContext.Post(_ => action(), null);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new TrayMenuColorTable()) { RoundedEdges = true; }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs eventArgs)
        {
            var middle = new Point(eventArgs.ArrowRectangle.Left + eventArgs.ArrowRectangle.Width / 2,
                eventArgs.ArrowRectangle.Top + eventArgs.ArrowRectangle.Height / 2);
            var points = eventArgs.Direction == ArrowDirection.Right
                ? new[] { new Point(middle.X - 2, middle.Y - 4), new Point(middle.X + 2, middle.Y), new Point(middle.X - 2, middle.Y + 4) }
                : new[] { new Point(middle.X - 4, middle.Y - 2), new Point(middle.X, middle.Y + 2), new Point(middle.X + 4, middle.Y - 2) };
            using var pen = new Pen(Color.FromArgb(225, 232, 241), 1.8f) { LineJoin = LineJoin.Round };
            eventArgs.Graphics.DrawLines(pen, points);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs eventArgs)
        {
            var bounds = eventArgs.ImageRectangle;
            using var pen = new Pen(Color.FromArgb(49, 214, 198), 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            eventArgs.Graphics.DrawLines(pen,
            [
                new Point(bounds.Left + 3, bounds.Top + bounds.Height / 2),
                new Point(bounds.Left + 7, bounds.Bottom - 4),
                new Point(bounds.Right - 2, bounds.Top + 3)
            ]);
        }
    }

    private sealed class TrayMenuColorTable : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(23, 30, 40);
        private static readonly Color Hover = Color.FromArgb(38, 49, 63);
        private static readonly Color Line = Color.FromArgb(58, 69, 83);
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuBorder => Line;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Surface;
        public override Color CheckBackground => Color.FromArgb(24, 70, 99);
        public override Color CheckSelectedBackground => Color.FromArgb(27, 84, 117);
    }
}
