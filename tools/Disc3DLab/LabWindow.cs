#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Disc3DLab
{
    /// <summary>Colours and fonts for the dark theme.</summary>
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(14, 16, 19);
        public static readonly Color Side = Color.FromArgb(11, 13, 16);
        public static readonly Color Panel = Color.FromArgb(22, 26, 32);
        public static readonly Color Panel2 = Color.FromArgb(30, 35, 43);
        public static readonly Color Line = Color.FromArgb(39, 45, 55);
        public static readonly Color Line2 = Color.FromArgb(54, 62, 75);
        public static readonly Color Text = Color.FromArgb(232, 236, 242);
        public static readonly Color Muted = Color.FromArgb(139, 149, 165);
        public static readonly Color Faint = Color.FromArgb(100, 110, 125);
        public static readonly Color Accent = Color.FromArgb(106, 168, 255);
        public static readonly Color Ok = Color.FromArgb(76, 210, 144);
        public static readonly Color Bad = Color.FromArgb(255, 107, 107);
        public static readonly Color Warn = Color.FromArgb(255, 181, 71);
        public static readonly Color[] Spectrum =
        {
            Color.FromArgb(255, 94, 126), Color.FromArgb(255, 181, 71), Color.FromArgb(246, 240, 90), Color.FromArgb(76, 210, 144),
            Color.FromArgb(69, 200, 255), Color.FromArgb(106, 124, 255), Color.FromArgb(196, 107, 255),
        };
        public static readonly Color[] LayerColors =
        {
            Color.FromArgb(230, 25, 75), Color.FromArgb(245, 130, 49), Color.FromArgb(255, 225, 25), Color.FromArgb(191, 239, 69),
        };

        public static Font UI(float size, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style, GraphicsUnit.Point);
        public static readonly Font Body = UI(9.75f);
        public static readonly Font Small = UI(8.5f);
        public static readonly Font Bold = UI(9.75f, FontStyle.Bold);
        public static readonly Font Section = UI(8f, FontStyle.Bold);
        public static readonly Font Title = UI(14f, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);

        public static LinearGradientBrush SpectrumBrush(Rectangle r)
        {
            var brush = new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)), Color.Black, Color.Black, LinearGradientMode.Horizontal);
            var blend = new ColorBlend { Colors = Spectrum, Positions = Enumerable.Range(0, Spectrum.Length).Select(i => i / (float)(Spectrum.Length - 1)).ToArray() };
            brush.InterpolationColors = blend;
            return brush;
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // ───────────────────────── small custom controls ─────────────────────────

    /// <summary>Flat button: Normal (gray), Primary (blue) or Spectrum (rainbow gradient).</summary>
    internal sealed class LabButton : Button
    {
        public enum Kind { Normal, Primary, Spectrum, Nav }
        private readonly Kind _kind;
        private bool _hover;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
        public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
        private bool _selected;

        public LabButton(string text, Kind kind = Kind.Normal)
        {
            _kind = kind;
            Text = text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = kind == Kind.Primary || kind == Kind.Spectrum ? Theme.Bold : Theme.Body;
            ForeColor = Theme.Text;
            BackColor = Theme.Panel2;
            Cursor = Cursors.Hand;
            Height = 34;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fore = Theme.Text;
            using (GraphicsPath path = Theme.Rounded(r, _kind == Kind.Nav ? 7 : 8))
            {
                switch (_kind)
                {
                    case Kind.Spectrum:
                        using (LinearGradientBrush b = Theme.SpectrumBrush(r)) g.FillPath(b, path);
                        if (_hover && Enabled) using (var w = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillPath(w, path);
                        fore = Color.FromArgb(11, 13, 16);
                        break;
                    case Kind.Primary:
                        using (var b = new SolidBrush(_hover && Enabled ? ControlPaint.Light(Theme.Accent, .15f) : Theme.Accent)) g.FillPath(b, path);
                        fore = Color.FromArgb(7, 17, 31);
                        break;
                    case Kind.Nav:
                        Color nav = _selected ? Theme.Panel2 : _hover ? Theme.Panel : Theme.Side;
                        using (var b = new SolidBrush(nav)) g.FillPath(b, path);
                        if (_selected) using (var a = new SolidBrush(Theme.Accent)) g.FillRectangle(a, 0, 6, 3, Height - 12);
                        fore = _selected || _hover ? Theme.Text : Theme.Muted;
                        break;
                    default:
                        using (var b = new SolidBrush(_selected ? Theme.Line2 : _hover && Enabled ? Theme.Line : Theme.Panel2)) g.FillPath(b, path);
                        using (var p = new Pen(Theme.Line2)) g.DrawPath(p, path);
                        break;
                }
            }
            if (!Enabled) using (var dim = new SolidBrush(Color.FromArgb(140, Parent?.BackColor ?? Theme.Back))) g.FillRectangle(dim, r);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                        (_kind == Kind.Nav ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
            Rectangle textRect = _kind == Kind.Nav ? new Rectangle(14, 0, Width - 18, Height) : r;
            int lineBreak = Text.IndexOf('\n');
            if (_kind == Kind.Nav && lineBreak > 0)
            {
                // Two-line entry: the name, and a smaller note underneath.
                TextRenderer.DrawText(g, Text.Substring(0, lineBreak), Font, new Rectangle(14, 6, Width - 18, 22), Enabled ? fore : Theme.Faint,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Text.Substring(lineBreak + 1), Theme.Small, new Rectangle(14, 27, Width - 18, 20), _selected ? Theme.Muted : Theme.Faint,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? fore : Theme.Faint, flags);
        }
    }

    /// <summary>Thin rainbow progress bar.</summary>
    internal sealed class ProgressStrip : Control
    {
        private double _value;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
        public Color? Solid { get => _solid; set { _solid = value; Invalidate(); } }
        private Color? _solid;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
        public double Value { get => _value; set { _value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public ProgressStrip() { Height = 6; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Line);
            int w = (int)Math.Round(Width * _value);
            if (w <= 0) return;
            var r = new Rectangle(0, 0, w, Height);
            if (_solid.HasValue) using (var b = new SolidBrush(_solid.Value)) e.Graphics.FillRectangle(b, r);
            else using (LinearGradientBrush b = Theme.SpectrumBrush(new Rectangle(0, 0, Width, Height))) e.Graphics.FillRectangle(b, r);
        }
    }

    /// <summary>A row of mutually exclusive buttons.</summary>
    internal sealed class Segmented : TableLayoutPanel
    {
        private readonly List<LabButton> _buttons = new List<LabButton>();
        private readonly string[] _values;
        public event EventHandler Changed;
        public string Value { get; private set; }

        public Segmented(params (string Value, string Label)[] options) : this(options.Length, options) { }

        /// <summary>Buttons laid out in rows of <paramref name="columns"/>.</summary>
        public Segmented(int columns, params (string Value, string Label)[] options)
        {
            _values = options.Select(o => o.Value).ToArray();
            Value = _values[0];
            columns = Math.Max(1, Math.Min(columns, options.Length));
            int rows = (options.Length + columns - 1) / columns;
            ColumnCount = columns;
            RowCount = rows;
            Height = 6 + 32 * rows;
            BackColor = Theme.Panel;
            Padding = new Padding(3);
            for (int c = 0; c < columns; c++) ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int r = 0; r < rows; r++) RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
            for (int i = 0; i < options.Length; i++)
            {
                var b = new LabButton(options[i].Label) { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Panel };
                string v = options[i].Value;
                b.Click += (s, e) => { Set(v); Changed?.Invoke(this, EventArgs.Empty); };
                _buttons.Add(b);
                Controls.Add(b, i % columns, i / columns);
            }
            Set(Value);
        }

        /// <summary>Selects a value as if it were clicked (raises Changed). Ignores unknown values.</summary>
        public void Choose(string value)
        {
            if (!_values.Contains(value)) return;
            Set(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Set(string value)
        {
            Value = value;
            for (int i = 0; i < _buttons.Count; i++) _buttons[i].Selected = _values[i] == value;
        }
    }

    /// <summary>Labelled slider with a live value read-out. Works in doubles over an integer TrackBar.</summary>
    internal sealed class Slider : Panel
    {
        private readonly TrackBar _track;
        private readonly Label _value, _name;
        /// <summary>The note shown under the slider. It's added to the page as an ordinary note (see Add),
        /// so it wraps like every other note at any display scaling.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
        public string HintText { get; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
        public Label HintLabel { get; set; }

        // Showing or hiding the slider shows or hides its note too.
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
            if (HintLabel != null) HintLabel.Visible = value;
        }
        private Func<double, string> _nameFormat;
        private readonly double _min, _step;
        private readonly Func<double, string> _format;
        public event EventHandler Changed;

        public Slider(string name, double min, double max, double step, double value, Func<double, string> format, string hint = null)
        {
            _min = min; _step = step; _format = format;
            Height = 58;
            HintText = hint;
            BackColor = Theme.Back;

            var label = _name = new Label { UseMnemonic = false, Text = name, ForeColor = Theme.Text, Font = Theme.Body, AutoSize = true, Location = new Point(0, 2) };
            _value = new Label { UseMnemonic = false, ForeColor = Theme.Muted, Font = Theme.Body, AutoSize = false, TextAlign = ContentAlignment.TopRight, Anchor = AnchorStyles.Top | AnchorStyles.Right, Height = 20 };
            _track = new WheelSafeTrackBar
            {
                Minimum = 0, Maximum = (int)Math.Round((max - min) / step), TickStyle = TickStyle.None, AutoSize = false, Height = 28,
                BackColor = Theme.Back, Location = new Point(-6, 22), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                SmallChange = 1, LargeChange = Math.Max(1, (int)((max - min) / step / 10)),
            };
            Value = value;
            _track.ValueChanged += (s, e) => { _value.Text = _format(Value); if (_nameFormat != null) _name.Text = _nameFormat(Value); Changed?.Invoke(this, EventArgs.Empty); };
            Controls.Add(label); Controls.Add(_value); Controls.Add(_track);
            _value.Text = _format(Value);
        }

        /// <summary>Shows the value in the name too (e.g. "Views:  4"), kept up to date.</summary>
        public void ShowValueInName(Func<double, string> format)
        {
            _nameFormat = format;
            _name.Font = Theme.Bold;
            _name.Text = format(Value);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_value == null) return;
            _value.SetBounds(Width - 110, 2, 110, 20);
            _track.Width = Width + 12;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
        public double Value
        {
            get => _min + _track.Value * _step;
            set => _track.Value = Math.Max(_track.Minimum, Math.Min(_track.Maximum, (int)Math.Round((value - _min) / _step)));
        }
    }

    /// <summary>
    /// The mouse wheel always scrolls the page, never a slider's or list's value, so scrolling past
    /// a control can't change it by accident. (Drag a slider, or click a list, to change it.)
    /// </summary>
    internal static class PageScroll
    {
        public const int WM_MOUSEWHEEL = 0x020A;

        public static int Delta(Message m) => (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);

        public static void Forward(Control control, int delta)
        {
            for (Control p = control.Parent; p != null; p = p.Parent)
            {
                if (p is ScrollableControl s && s.AutoScroll)
                {
                    Point at = s.AutoScrollPosition; // negative offsets
                    int y = Math.Max(0, -at.Y - delta * 60 / 120);
                    s.AutoScrollPosition = new Point(-at.X, y);
                    return;
                }
            }
        }
    }

    internal sealed class WheelSafeTrackBar : TrackBar
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PageScroll.WM_MOUSEWHEEL) { PageScroll.Forward(this, PageScroll.Delta(m)); return; }
            base.WndProc(ref m);
        }
    }

    internal sealed class WheelSafeComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            // An open list still scrolls itself; a closed one passes the wheel to the page.
            if (m.Msg == PageScroll.WM_MOUSEWHEEL && !DroppedDown) { PageScroll.Forward(this, PageScroll.Delta(m)); return; }
            base.WndProc(ref m);
        }
    }

    /// <summary>Dashed box you can click or drop a file onto.</summary>
    internal sealed class DropZone : Panel
    {
        private string _text = "Drop a file here, or click to choose";
        private string _sub = "";
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
        public string Filter { get; set; } = "All files|*.*";
        public event Action<string> FileChosen;
        /// <summary>With Multiple set, several files can be picked or dropped at once.</summary>
        public event Action<string[]> FilesChosen;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
        public bool Multiple { get; set; }
        private bool _over;
        private static bool s_opened;

        public DropZone()
        {
            Height = 74;
            AllowDrop = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Back;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetText(string main, string sub) { _text = main; _sub = sub; Invalidate(); }

        /// <summary>Picks a file as if it were dropped here (raises FileChosen).</summary>
        public void Choose(string path) => FileChosen?.Invoke(path);

        /// <summary>Picks several files as if they were dropped here (raises FilesChosen).</summary>
        public void ChooseMany(string[] paths) => FilesChosen?.Invoke(paths);

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            string samples = Path.Combine(AppContext.BaseDirectory, "samples");
            using (var dialog = new OpenFileDialog { Filter = Filter, Multiselect = Multiple })
            {
                // The first time, start in the bundled samples folder; after that Windows remembers.
                if (Directory.Exists(samples) && !s_opened) dialog.InitialDirectory = samples;
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    s_opened = true;
                    if (Multiple) FilesChosen?.Invoke(dialog.FileNames);
                    else FileChosen?.Invoke(dialog.FileName);
                }
            }
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            _over = true; Invalidate();
        }
        protected override void OnDragLeave(EventArgs e) { _over = false; Invalidate(); }
        protected override void OnDragDrop(DragEventArgs e)
        {
            _over = false; Invalidate();
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (Multiple) FilesChosen?.Invoke(files);
                else FileChosen?.Invoke(files[0]);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Back);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = Theme.Rounded(r, 9))
            {
                using (var b = new SolidBrush(_over ? Color.FromArgb(24, 32, 44) : Theme.Panel)) g.FillPath(b, path);
                using (var p = new Pen(_over ? Theme.Accent : Theme.Line2, 1.5f) { DashStyle = DashStyle.Dash }) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, _text, Theme.Bold, new Rectangle(8, 10, Width - 16, 24), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _sub, Theme.Small, new Rectangle(8, 36, Width - 16, 30), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    // ───────────────────────── the window ─────────────────────────

    public sealed class LabWindow : Form
    {
        private readonly ILabBackend _backend;
        private readonly Dictionary<string, string> _settings = new Dictionary<string, string>();
        private readonly System.Windows.Forms.Timer _saveTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        private bool _restoring, _resetting;
        private const int ColumnWidth = 336;

        // shared
        private readonly List<TextBox> _folderBoxes = new List<TextBox>();
        private readonly List<ComboBox> _presetBoxes = new List<ComboBox>();
        private readonly Dictionary<string, Panel> _pages = new Dictionary<string, Panel>();
        private readonly Dictionary<string, LabButton> _nav = new Dictionary<string, LabButton>();
        private Label _title, _subtitle, _sideFolder;

        // hidden-3D
        private Segmented _source, _media, _show;
        private ComboBox _demo;
        private DropZone _stDrop;
        private CheckBox _invert, _guides;
        private Panel _meshPanel;
        private Slider _yaw, _pitch, _diameter, _separation, _depthFactor, _dot;
        private TextBox _stName;
        private LabButton _stSave;
        private PictureBox _stPicture;
        private Label _stStatus, _stTitle;
        private string _stFile;
        private int _seed = 1, _stSequence;
        private Bitmap _stStereo, _stDepth;
        private readonly System.Windows.Forms.Timer _stTimer = new System.Windows.Forms.Timer { Interval = 350 };

        // test disc
        private PictureBox _testPicture;
        private TextBox _testKey, _testName;
        private LabButton _testStart;
        private bool _testLoaded;

        // tilt
        private readonly TiltLayerRequest[] _layers = new TiltLayerRequest[4];
        private readonly PictureBox[] _thumbs = new PictureBox[4];
        private readonly Label[] _layerNames = new Label[4];
        private readonly ComboBox[] _layerGratings = new ComboBox[4];
        private readonly LabButton[] _layerRemove = new LabButton[4];
        private Segmented _dither;
        private Slider _threshold;
        private PictureBox _tiltPicture;
        private TextBox _tiltKey, _tiltName;
        private LabButton _tiltStart;
        private readonly System.Windows.Forms.Timer _tiltTimer = new System.Windows.Forms.Timer { Interval = 300 };
        private int _tiltSequence;

        // hologram mode (page id "pair")
        private Segmented _pairMode, _pairDither, _pairSize, _pairDetail;
        private DropZone _pairSbs, _pairLeft, _pairRight, _pairColor, _pairDepth, _pairModel, _pairFrames;
        private Slider _pairViews, _pairYaw, _pairPitch, _pairTurn, _pairZoom, _pairMoveX, _pairMoveY;
        private Segmented _pairCopies;
        private CheckBox _pairUpright;
        private Segmented _pairOff;
        private LabButton _pairPatches;
        private string _pairModelFile;
        private string[] _pairFrameFiles;
        private Label _pairChoiceLabel, _pairViewsHint, _pairFramesHint;
        private CheckBox _pairSqueezed, _pairCross, _pairSwap, _pairTest, _pairInvert;
        private Slider _pairDistance, _pairWidth, _pairStrength, _pairFocus;
        private string _pairColorFile, _pairDepthFile;
        private ComboBox _pairChoice;
        private Label _pairSummary;
        private PictureBox _pairPicture;
        private TextBox _pairKey, _pairName;
        private LabButton _pairStart;
        private string _pairSbsFile, _pairLeftFile, _pairRightFile;
        private readonly System.Windows.Forms.Timer _pairTimer = new System.Windows.Forms.Timer { Interval = 300 };
        private int _pairSequence;

        // stage 1: measure (disc-probe)
        private ComboBox _mDrive;
        private CheckBox _mPractice, _mReplace;
        private TextBox _mSectors, _mName, _mLog, _mCommand;
        private Label _mVerdict, _mStatus;
        private FlowLayoutPanel _mSuggestions;
        private LabButton _mCancel, _mInstall;
        private readonly List<LabButton> _mButtons = new List<LabButton>();
        private CancellationTokenSource _mCancelSource;
        private Task _mTask;
        private List<string> _mLastExport;

        // job bar
        private Panel _jobBar;
        private Label _jobTitle, _jobStatus;
        private ProgressStrip _jobProgress;
        private LabButton _jobCancel, _jobShow, _jobClose;
        private CancellationTokenSource _jobCancelSource;
        private Task _jobTask;
        private string _jobOutput;

        public static int Run(ILabBackend backend)
        {
#if NET
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LabWindow(backend));
            return 0;
        }

        public LabWindow(ILabBackend backend)
        {
            _backend = backend;
            LoadSettings();
            SuspendLayout();
            Text = "HoloStudio3D";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            MinimumSize = new Size(1100, 720);
            ClientSize = new Size(1360, 880);
            StartPosition = FormStartPosition.CenterScreen;
            try { if (Environment.OSVersion.Platform == PlatformID.Win32NT) Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { /* icon is cosmetic */ }

            // Docking: fill first, then edges (later-added controls dock first).
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
            Controls.Add(content);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Line });
            Controls.Add(BuildSidebar());

            var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
            content.Controls.Add(pages);
            content.Controls.Add(BuildHeader());
            content.Controls.Add(BuildJobBar());

            AddPage(pages, "measure", BuildMeasurePage());
            AddPage(pages, "stereo", BuildStereoPage());
            AddPage(pages, "test", BuildTestPage());
            AddPage(pages, "tilt", BuildTiltPage());
            AddPage(pages, "pair", BuildPairPage());
            AddPage(pages, "help", BuildHelpPage());
            AddPage(pages, "about", BuildAboutPage());

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();

            _stTimer.Tick += (s, e) => { _stTimer.Stop(); RenderStereo(); };
            _tiltTimer.Tick += (s, e) => { _tiltTimer.Stop(); RenderTilt(); };
            _pairTimer.Tick += (s, e) => { _pairTimer.Stop(); RenderPair(); };
            // Use the folder the person picked before; if they never picked one, the default (top of C:).
            if (Setting("outPicked", "") == "1") SetFolder(Setting("out", ""), picked: true);
            else SetFolder(_backend.DefaultOutputFolder, picked: false);
            RestoreWindow();
            RestoreControls();
            HookRemember();
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); RememberNow(); };
            ShowPage(Setting("view", "pair"));
            Shown += (s, e) => QueueStereo();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Dark title bar on Windows 10 20H1+ / 11.
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    int on = 1;
                    if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4);
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // ───────────── frame ─────────────

        private Control BuildSidebar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = 232, BackColor = Theme.Side, Padding = new Padding(12, 16, 12, 12) };
            var brand = new BrandMark { Location = new Point(18, 18), Size = new Size(36, 36) };
            var name = new Label { UseMnemonic = false, Text = "HoloStudio3D", Font = Theme.UI(12f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(62, 17), BackColor = Theme.Side };
            var tag = new Label { UseMnemonic = false, Text = "for Disc Image Studio", Font = Theme.Small, ForeColor = Theme.Faint, AutoSize = true, Location = new Point(63, 39), BackColor = Theme.Side };
            side.Controls.Add(brand); side.Controls.Add(name); side.Controls.Add(tag);
            int y = 78;
            foreach ((string id, string label) in new[]
            {
                ("pair", "Hologram mode   ·  exp"), ("measure", "Stage 1 · Measure discs\nfirst time, or a new disc stack"), ("stereo", "Magic Eye-style picture"), ("test", "Rainbow test disc   ·  exp"), ("tilt", "Tilt-image disc   ·  exp"), ("help", "How to burn & view"), ("about", "Info, instructions & credits"),
            })
            {
                int height = label.Contains("\n") ? 52 : 38;
                var b = new LabButton(label, LabButton.Kind.Nav) { Location = new Point(10, y), Size = new Size(210, height), BackColor = Theme.Side };
                b.Click += (s, e) => ShowPage(id);
                _nav[id] = b;
                side.Controls.Add(b);
                y += height + 4;
            }
            var foot = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = Theme.Side };
            foot.Controls.Add(new Label { UseMnemonic = false, Text = "OUTPUT FOLDER", ForeColor = Theme.Muted, Font = Theme.Section, AutoSize = true, Location = new Point(6, 0), BackColor = Theme.Side });
            _sideFolder = new Label { UseMnemonic = false, ForeColor = Theme.Text, Font = Theme.UI(8.5f, FontStyle.Bold), AutoSize = false, Location = new Point(6, 20), Size = new Size(204, 36), BackColor = Theme.Side, AutoEllipsis = true };
            foot.Controls.Add(_sideFolder);
            var change = new LabButton("Change…", LabButton.Kind.Primary) { Location = new Point(6, 60), Size = new Size(118, 32), BackColor = Theme.Side };
            change.Click += (s, e) => ChooseFolder();
            var open = new LabButton("Open") { Location = new Point(130, 60), Size = new Size(80, 32), BackColor = Theme.Side };
            open.Click += (s, e) => Safe(() => _backend.ShowInFolder(Folder));
            foot.Controls.Add(change); foot.Controls.Add(open);
            foot.Controls.Add(new Label { UseMnemonic = false, Text = "by Drew Medina", ForeColor = Theme.Faint, Font = Theme.Small, AutoSize = true, Location = new Point(6, 118), BackColor = Theme.Side });
            side.Controls.Add(foot);
            return side;
        }

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Back };
            _title = new Label { UseMnemonic = false, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Location = new Point(24, 14) };
            _subtitle = new Label { UseMnemonic = false, Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(26, 44) };
            header.Controls.Add(_title); header.Controls.Add(_subtitle);
            header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Line });
            return header;
        }

        private Control BuildJobBar()
        {
            _jobBar = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = Color.FromArgb(17, 21, 27), Visible = false, Padding = new Padding(22, 10, 22, 10) };
            _jobBar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Line });
            _jobTitle = new Label { UseMnemonic = false, Font = Theme.Bold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(22, 12) };
            _jobStatus = new Label { UseMnemonic = false, Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Location = new Point(22, 12), Height = 20, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _jobProgress = new ProgressStrip { Location = new Point(22, 40), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _jobCancel = new LabButton("Cancel") { Width = 96, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _jobShow = new LabButton("Show in folder", LabButton.Kind.Primary) { Width = 130, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            _jobClose = new LabButton("Close") { Width = 80, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            _jobCancel.Click += (s, e) => { _jobCancelSource?.Cancel(); _jobStatus.Text = "Cancelling…"; };
            _jobShow.Click += (s, e) => { if (_jobOutput != null) Safe(() => _backend.ShowInFolder(_jobOutput)); };
            _jobClose.Click += (s, e) => _jobBar.Visible = false;
            _jobBar.Controls.AddRange(new Control[] { _jobTitle, _jobStatus, _jobProgress, _jobCancel, _jobShow, _jobClose });
            _jobBar.Resize += (s, e) => LayoutJobBar();
            return _jobBar;
        }

        private void LayoutJobBar()
        {
            int right = _jobBar.ClientSize.Width - 22;
            foreach (LabButton b in new[] { _jobClose, _jobCancel, _jobShow })
            {
                if (!b.Visible) continue;
                b.Location = new Point(right - b.Width, 16);
                right -= b.Width + 8;
            }
            int left = _jobTitle.Right + 12;
            _jobStatus.SetBounds(left, 12, Math.Max(50, right - left - 10), 20);
            _jobProgress.SetBounds(22, 42, Math.Max(50, right - 30), 6);
        }

        private static void AddPage(Panel host, string id, Panel page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            host.Controls.Add(page);
        }

        private void ShowPage(string id)
        {
            if (!_pages.ContainsKey(id)) id = "stereo";
            foreach (KeyValuePair<string, Panel> p in _pages) p.Value.Visible = p.Key == id;
            foreach (KeyValuePair<string, LabButton> n in _nav) n.Value.Selected = n.Key == id;
            switch (id)
            {
                case "measure": SetHeader("Stage 1 · Measure discs  (first time, or a new disc stack)", "disc-probe times your drive's reads to measure the exact spiral of your discs, then installs it as a preset, so pictures land straight instead of streaked."); break;
                case "stereo": SetHeader("Magic Eye-style picture", "A Magic Eye style random-dot picture. Burn it like any image with Disc Image Studio, then look through the disc to see the shape float."); break;
                case "test": SetHeader("Rainbow test disc", "Experimental. Finds which data patterns turn into clean colours on your burner and discs."); break;
                case "tilt": SetHeader("Tilt-image disc", "Experimental. Up to four pictures, each drawn with a different data pattern so each one flashes in colour at its own tilt."); break;
                case "pair": SetHeader("Hologram mode", "Experimental. A 3D picture at 12 o'clock built from 2–7 views, each drawn with its own pattern, so each eye (and each head position) catches a different view."); break;
                case "about": SetHeader("Info, instructions & credits", "Start here: setting up, step by step, then who made this and the open-source projects it is built on."); break;
                default: SetHeader("How to burn & view", "Step by step, for both kinds of disc."); break;
            }
            _settings["view"] = id;
            SaveSettings();
            if (id == "test" && !_testLoaded) RenderTest();
        }

        private void SetHeader(string title, string subtitle) { _title.Text = title; _subtitle.Text = subtitle; }

        // ───────────── page scaffolding ─────────────

        /// <summary>A page: scrolling controls column on the left, preview stage on the right.</summary>
        private Panel NewPage(string id, out FlowLayoutPanel column, out Panel stage)
        {
            var page = new Panel { BackColor = Theme.Back };
            stage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(20, 14, 22, 14) };
            var scroller = new Panel { Dock = DockStyle.Left, Width = ColumnWidth + 44, AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(18, 14, 0, 18) };
            column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Back, Location = new Point(18, 14) };
            scroller.Controls.Add(column);
            page.Controls.Add(stage);
            page.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Line });
            page.Controls.Add(scroller);
            _pages[id] = page;
            return page;
        }

        private static T Add<T>(FlowLayoutPanel column, T control, int top = 0, int bottom = 10) where T : Control
        {
            control.Width = ColumnWidth;
            control.Margin = new Padding(0, top, 0, bottom);
            column.Controls.Add(control);
            if (control is Slider slider && slider.HintText != null)
            {
                slider.Margin = new Padding(0, top, 0, 0);
                slider.HintLabel = new Label { UseMnemonic = false, Text = slider.HintText, Font = Theme.Small, ForeColor = Theme.Faint, AutoSize = true, MaximumSize = new Size(ColumnWidth, 0), Margin = new Padding(0, 0, 0, bottom) };
                column.Controls.Add(slider.HintLabel);
            }
            return control;
        }

        private static Label Section(FlowLayoutPanel column, string text, bool first = false) =>
            Add(column, new Label { UseMnemonic = false, Text = text.ToUpperInvariant(), Font = Theme.Section, ForeColor = Theme.Muted, AutoSize = false, Height = 18 }, first ? 0 : 10, 6);

        private static Label Hint(FlowLayoutPanel column, string text, Color? color = null)
        {
            var label = new Label { UseMnemonic = false, Text = text, Font = Theme.Small, ForeColor = color ?? Theme.Faint, AutoSize = true, MaximumSize = new Size(ColumnWidth, 0) };
            return Add(column, label, 0, 8);
        }

        private static ComboBox Combo() => new WheelSafeComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Body,
        };

        private static TextBox Box(string text) => new TextBox
        {
            Text = text, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Body,
        };

        private static PictureBox Picture() => new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(18, 22, 28) };

        private static TextBox KeyBox() => new TextBox
        {
            Dock = DockStyle.Bottom, Height = 190, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            BackColor = Theme.Panel, ForeColor = Color.FromArgb(198, 207, 220), BorderStyle = BorderStyle.None, Font = Theme.Mono,
        };

        private static Panel StageBar(Panel stage, string title, out Label titleLabel, Label extra = null)
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Back };
            titleLabel = new Label { UseMnemonic = false, Text = title, Font = Theme.Bold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, 10) };
            bar.Controls.Add(titleLabel);
            if (extra != null) bar.Controls.Add(extra);
            return bar;
        }

        private Panel FolderRow()
        {
            // Width first: anchored children are placed relative to the row's final width.
            var row = new Panel { Width = ColumnWidth, Height = 32, BackColor = Theme.Back };
            var box = Box("");
            box.SetBounds(0, 3, ColumnWidth - 92, 26);
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            box.Leave += (s, e) => { if (box.Text.Trim().Trim('"') != Folder) SetFolder(box.Text, picked: true); };
            var browse = new LabButton("Choose…") { Width = 84, Height = 30, Location = new Point(ColumnWidth - 84, 1), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            browse.Click += (s, e) => ChooseFolder();
            row.Controls.Add(box); row.Controls.Add(browse);
            _folderBoxes.Add(box);
            return row;
        }

        /// <summary>The folder picker (Windows' standard "Select folder" dialog).</summary>
        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose where HoloStudio3D saves its files", ShowNewFolderButton = true })
            {
#if NET
                dialog.UseDescriptionForTitle = true;
                dialog.InitialDirectory = Directory.Exists(Folder) ? Folder : Path.GetPathRoot(Folder) ?? "";
#endif
                if (Directory.Exists(Folder)) dialog.SelectedPath = Folder;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                    SetFolder(dialog.SelectedPath, picked: true);
            }
        }

        /// <summary>Sets the output folder. Only a folder the person chose is remembered; otherwise the default is used.</summary>
        private void SetFolder(string folder, bool picked)
        {
            folder = (folder ?? "").Trim().Trim('"');
            if (folder.Length == 0) { folder = _backend.DefaultOutputFolder; picked = false; }
            _folder = folder;
            foreach (TextBox b in _folderBoxes) if (b.Text != folder) b.Text = folder;
            if (_sideFolder != null) _sideFolder.Text = folder;
            if (picked) { _settings["out"] = folder; _settings["outPicked"] = "1"; }
            else { _settings.Remove("out"); _settings.Remove("outPicked"); }
            SaveSettings();
        }

        private string _folder;
        private string Folder => string.IsNullOrEmpty(_folder) ? _backend.DefaultOutputFolder : _folder;

        private ComboBox PresetCombo()
        {
            ComboBox combo = Combo();
            foreach (PresetInfo p in _backend.Presets) combo.Items.Add(p);
            // Until a preset is chosen, prefer your own measured/calibrated one over the generic standard.
            string fallback = _backend.Presets.FirstOrDefault(p => p.Id.StartsWith("user:", StringComparison.Ordinal))?.Id ?? "dvd-5-120mm";
            string id = Setting("preset", fallback);
            combo.SelectedItem = _backend.Presets.FirstOrDefault(p => p.Id == id) ?? _backend.Presets.FirstOrDefault(p => p.Id == "dvd-5-120mm") ?? _backend.Presets.FirstOrDefault();
            combo.SelectedIndexChanged += (s, e) =>
            {
                if (!(combo.SelectedItem is PresetInfo p) || _settings.TryGetValue("preset", out string old) && old == p.Id) return;
                _settings["preset"] = p.Id;
                SaveSettings();
                foreach (ComboBox other in _presetBoxes) if (other != combo) other.SelectedItem = p;
                _testLoaded = false;
                if (_pages["test"].Visible) RenderTest();
                QueueTilt();
                QueuePair();
            };
            _presetBoxes.Add(combo);
            return combo;
        }

        private string Preset => (_presetBoxes[0].SelectedItem as PresetInfo)?.Id ?? "dvd-5-120mm";

        // ───────────── hidden-3D page ─────────────

        private Panel BuildStereoPage()
        {
            Panel page = NewPage("stereo", out FlowLayoutPanel col, out Panel stage);

            Section(col, "3D shape", true);
            _source = Add(col, new Segmented(("demo", "Built-in"), ("depth", "Depth picture"), ("mesh", "3D model")));
            _source.Changed += (s, e) => { _stFile = null; SyncSource(); QueueStereo(); };
            _demo = Add(col, Combo());
            foreach (string d in _backend.Demos) _demo.Items.Add(char.ToUpperInvariant(d[0]) + d.Substring(1));
            _demo.SelectedIndex = 0;
            _demo.SelectedIndexChanged += (s, e) => QueueStereo();
            _stDrop = Add(col, new DropZone());
            _stDrop.FileChosen += path =>
            {
                _stFile = path;
                _stDrop.SetText(Path.GetFileName(path), "Click or drop to change");
                _stName.Text = Path.GetFileNameWithoutExtension(path) + "-3d";
                QueueStereo();
            };
            _invert = Add(col, new CheckBox { Text = "White is far (invert depth)", ForeColor = Theme.Text, Height = 26 });
            _invert.CheckedChanged += (s, e) => QueueStereo();
            _meshPanel = Add(col, new Panel { Height = 3 * 60, BackColor = Theme.Back });
            _yaw = new Slider("Turn left / right", -180, 180, 5, 0, v => v.ToString("0") + "°") { Dock = DockStyle.Top };
            _pitch = new Slider("Tilt up / down", -90, 90, 5, 0, v => v.ToString("0") + "°") { Dock = DockStyle.Top };
            _diameter = new Slider("Size on disc", 30, 115, 1, 70, v => v.ToString("0") + " mm") { Dock = DockStyle.Top };
            _meshPanel.Controls.Add(_diameter); _meshPanel.Controls.Add(_pitch); _meshPanel.Controls.Add(_yaw);
            foreach (Slider sl in new[] { _yaw, _pitch, _diameter }) sl.Changed += (s, e) => QueueStereo();

            Section(col, "Disc");
            _media = Add(col, new Segmented(("dvd", "DVD"), ("cd", "CD")));
            _media.Changed += (s, e) => _dot.Value = _media.Value == "cd" ? 0.25 : 0.15;

            Section(col, "Look");
            _separation = Add(col, new Slider("Pattern width", 8, 22, 0.5, 14, v => v.ToString("0.0") + " mm", "Smaller is easier to lock onto; larger gives more depth."), 0, 4);
            _depthFactor = Add(col, new Slider("Pop-out depth", 0.10, 0.50, 0.01, 0.33, v => Math.Round(v * 100) + "%"), 0, 4);
            _dot = Add(col, new Slider("Dot size", 0.06, 0.50, 0.01, 0.15, v => v.ToString("0.00") + " mm", "Bigger dots survive softer burns (CD-R likes 0.25+)."), 0, 4);
            foreach (Slider sl in new[] { _separation, _depthFactor, _dot }) sl.Changed += (s, e) => QueueStereo();
            var row = Add(col, new Panel { Height = 34, BackColor = Theme.Back });
            _guides = new CheckBox { Text = "Guide dots", Checked = true, ForeColor = Theme.Text, Location = new Point(0, 6), AutoSize = true };
            _guides.CheckedChanged += (s, e) => QueueStereo();
            var reseed = new LabButton("New dots") { Width = 100, Height = 30, Location = new Point(ColumnWidth - 100, 2) };
            reseed.Click += (s, e) => { _seed = new Random().Next(1, 1_000_000); QueueStereo(); QueueSave(); };
            row.Controls.Add(_guides); row.Controls.Add(reseed);

            Section(col, "Save");
            Add(col, FolderRow(), 0, 6);
            _stName = Add(col, Box("stereogram"), 0, 8);
            _stSave = Add(col, new LabButton("Save PNG for Disc Image Studio", LabButton.Kind.Primary) { Height = 38 }, 0, 6);
            _stSave.Click += async (s, e) => await SaveStereo();
            Hint(col, "Then in Disc Image Studio: new project → Import images → this PNG at 100% scale.");

            _stStatus = new Label { UseMnemonic = false, Text = "", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(250, 13) };
            _show = new Segmented(("stereogram", "Burned image"), ("depth", "Hidden shape")) { Width = 280, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _show.Changed += (s, e) => PaintStereo();
            _stPicture = Picture();
            stage.Controls.Add(_stPicture);
            Panel bar = StageBar(stage, "Burned image (120 mm disc)", out _stTitle, _stStatus);
            bar.Controls.Add(_show);
            bar.Resize += (s, e) => _show.Location = new Point(bar.Width - _show.Width, 0);
            stage.Controls.Add(bar);
            SyncSource();
            return page;
        }

        private void SyncSource()
        {
            string v = _source.Value;
            _demo.Visible = v == "demo";
            _stDrop.Visible = v != "demo";
            _invert.Visible = v == "depth";
            _meshPanel.Visible = v == "mesh";
            _stDrop.Filter = v == "mesh" ? "3D models (*.obj;*.stl)|*.obj;*.stl" : "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff";
            _stDrop.SetText(v == "mesh" ? "Drop a 3D model here, or click to choose" : "Drop a depth picture here, or click to choose",
                v == "mesh" ? ".obj or .stl  (export GLB / PLY to OBJ first)" : "PNG or JPG  ·  white = near, black = far");
        }

        private StereoRequest StereoRequest() => new StereoRequest
        {
            Source = _source.Value, Demo = _backend.Demos[Math.Max(0, _demo.SelectedIndex)], File = _stFile, Invert = _invert.Checked,
            Yaw = _yaw.Value, Pitch = _pitch.Value, Diameter = _diameter.Value,
            Separation = _separation.Value, DepthFactor = _depthFactor.Value, Dot = _dot.Value, Seed = _seed, Guides = _guides.Checked,
        };

        private void QueueStereo() { _stTimer.Stop(); _stTimer.Start(); }

        private async void RenderStereo()
        {
            if (_source.Value != "demo" && _stFile == null)
            {
                _stStatus.Text = _source.Value == "mesh" ? "choose a 3D model" : "choose a depth picture";
                return;
            }
            int sequence = ++_stSequence;
            StereoRequest request = StereoRequest();
            _stStatus.Text = "updating…";
            try
            {
                LabImage stereo = null, depth = null;
                await Task.Run(() => _backend.RenderStereoPreview(request, 1000, out stereo, out depth));
                if (sequence != _stSequence) return;
                Replace(ref _stStereo, ToBitmap(stereo));
                Replace(ref _stDepth, ToBitmap(depth));
                PaintStereo();
                _stStatus.Text = "live preview";
            }
            catch (Exception ex) when (sequence == _stSequence)
            {
                _stStatus.Text = "⚠ " + ex.Message;
            }
        }

        private void PaintStereo()
        {
            bool depth = _show.Value == "depth";
            _stPicture.Image = depth ? _stDepth : _stStereo;
            _stTitle.Text = depth ? "Hidden shape (white = near)" : "Burned image (120 mm disc)";
        }

        private async Task SaveStereo()
        {
            _stSave.Enabled = false;
            _stSave.Text = "Saving…";
            try
            {
                StereoRequest request = StereoRequest();
                string folder = Folder, name = _stName.Text;
                string path = await Task.Run(() => _backend.SaveStereogram(request, folder, name));
                if (MessageBox.Show(this, "Saved:\n" + path + "\n\nShow it in its folder?", "HoloStudio3D", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Safe(() => _backend.ShowInFolder(path));
            }
            catch (Exception ex) { Error(ex); }
            finally { _stSave.Enabled = true; _stSave.Text = "Save PNG for Disc Image Studio"; }
        }

        // ───────────── test disc page ─────────────

        private Panel BuildTestPage()
        {
            Panel page = NewPage("test", out FlowLayoutPanel col, out Panel stage);
            Section(col, "Your DVD", true);
            Add(col, PresetCombo(), 0, 4);
            Hint(col, "Only moves where the rings land; the colours don't depend on it.");
            Hint(col, _backend.PresetNote, Theme.Muted);
            Section(col, "Save to");
            Add(col, FolderRow(), 0, 6);
            _testName = Add(col, Box("diffraction-test"), 0, 8);
            Section(col, "What's on it");
            Hint(col, "12 rings, each a different repeating data pattern that should throw coloured light at its own angle. " +
                      "Around each ring, every other 45° segment is a plain \"no grating\" reference to compare against.", Theme.Muted);
            _testStart = Add(col, new LabButton("Make test disc image  (4.7 GB)", LabButton.Kind.Spectrum) { Height = 42 }, 8, 8);
            _testStart.Click += (s, e) => StartJob("test");
            Hint(col, "Takes a few minutes. Burn the .iso with ImgBurn, or right-click it → Burn disc image.");

            _testPicture = Picture();
            _testKey = KeyBox();
            stage.Controls.Add(_testPicture);
            stage.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = Theme.Back });
            stage.Controls.Add(_testKey);
            stage.Controls.Add(StageBar(stage, "Ring layout   —   false colours, just to show which ring is where", out _));
            return page;
        }

        private async void RenderTest()
        {
            _testLoaded = true;
            string preset = Preset;
            try
            {
                string key = null;
                LabImage map = await Task.Run(() => _backend.TestMap(preset, out key));
                _testPicture.Image?.Dispose();
                _testPicture.Image = ToBitmap(map);
                _testKey.Text = Crlf(key);
            }
            catch (Exception ex) { _testKey.Text = "⚠ " + ex.Message; }
        }

        // ───────────── tilt page ─────────────

        private Panel BuildTiltPage()
        {
            Panel page = NewPage("tilt", out FlowLayoutPanel col, out Panel stage);
            Section(col, "Pictures  ·  bright parts get drawn", true);
            for (int i = 0; i < 4; i++) Add(col, LayerRow(i), 0, 8);
            Section(col, "Grays");
            _dither = Add(col, new Segmented(("dither", "Dither (keeps grays)"), ("cut", "Hard cut-off")));
            _dither.Changed += (s, e) => { _threshold.Visible = _dither.Value == "cut"; QueueTilt(); };
            _threshold = Add(col, new Slider("Cut-off", 0.05, 0.95, 0.05, 0.5, v => Math.Round(v * 100) + "%") { Visible = false }, 0, 4);
            _threshold.Changed += (s, e) => QueueTilt();
            Section(col, "Your DVD");
            Add(col, PresetCombo(), 0, 4);
            Hint(col, "Use your measured preset (\"Yours:\") so the picture lands where it should, not streaked.", Theme.Muted);
            Section(col, "Save to");
            Add(col, FolderRow(), 0, 6);
            _tiltName = Add(col, Box("tilt-image"), 0, 8);
            _tiltStart = Add(col, new LabButton("Make tilt-image disc  (4.7 GB)", LabButton.Kind.Spectrum) { Height = 42, Enabled = false }, 8, 8);
            _tiltStart.Click += (s, e) => StartJob("tilt");
            Hint(col, "Burn the test disc first if you can. It shows which patterns are bright on your discs.");

            _tiltPicture = Picture();
            _tiltKey = KeyBox();
            stage.Controls.Add(_tiltPicture);
            stage.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = Theme.Back });
            stage.Controls.Add(_tiltKey);
            stage.Controls.Add(StageBar(stage, "Layout   —   each colour is one picture's pattern", out _));
            _tiltKey.Text = "Add a picture to see the layout.";
            return page;
        }

        private Panel LayerRow(int i)
        {
            var row = new Panel { Height = 74, BackColor = Theme.Panel, Padding = new Padding(8) };
            var thumb = new PictureBox
            {
                Location = new Point(8, 8), Size = new Size(58, 58), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Back,
                Cursor = Cursors.Hand, AllowDrop = true, BorderStyle = BorderStyle.FixedSingle,
            };
            thumb.Paint += (s, e) =>
            {
                if (thumb.Image == null) TextRenderer.DrawText(e.Graphics, "+", Theme.UI(18f), thumb.ClientRectangle, Theme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            thumb.Click += (s, e) =>
            {
                using (var dialog = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) SetLayer(i, dialog.FileName);
            };
            thumb.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            thumb.DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) SetLayer(i, f[0]); };
            var swatch = new Panel { Location = new Point(78, 14), Size = new Size(10, 10), BackColor = Theme.LayerColors[i] };
            var name = new Label
            {
                UseMnemonic = false,
                Text = "Picture " + (i + 1) + (i == 0 ? "  (smallest tilt)" : i == 3 ? "  (steepest tilt)" : ""), ForeColor = Theme.Text, BackColor = Theme.Panel,
                AutoSize = false, AutoEllipsis = true, Location = new Point(94, 9), Size = new Size(ColumnWidth - 150, 20),
            };
            ComboBox grating = Combo();
            grating.SetBounds(78, 36, ColumnWidth - 134, 26);
            foreach (GratingInfo g in _backend.Gratings) grating.Items.Add(g);
            grating.SelectedItem = _backend.Gratings.FirstOrDefault(g => g.Code == _backend.TiltDefaults[i]);
            grating.SelectedIndexChanged += (s, e) => { if (_layers[i] != null) { _layers[i].Grating = ((GratingInfo)grating.SelectedItem).Code; QueueTilt(); } };
            var remove = new LabButton("✕") { Size = new Size(34, 30), Location = new Point(ColumnWidth - 46, 34), Visible = false, BackColor = Theme.Panel };
            remove.Click += (s, e) => ClearLayer(i);
            row.Controls.AddRange(new Control[] { thumb, swatch, name, grating, remove });
            _thumbs[i] = thumb; _layerNames[i] = name; _layerGratings[i] = grating; _layerRemove[i] = remove;
            return row;
        }

        private void SetLayer(int i, string path)
        {
            try
            {
                Bitmap thumb;
                using (FileStream stream = File.OpenRead(path))
                using (Image image = Image.FromStream(stream)) thumb = new Bitmap(image, ThumbSize(image.Size, 116));
                Replace(i, thumb);
                _layers[i] = new TiltLayerRequest { File = path, Name = Path.GetFileName(path), Grating = ((GratingInfo)_layerGratings[i].SelectedItem)?.Code ?? _backend.TiltDefaults[i] };
                _layerNames[i].Text = Path.GetFileName(path);
                _layerRemove[i].Visible = true;
                _tiltStart.Enabled = JobIdle;
                QueueTilt();
            }
            catch (Exception ex) { Error(new ArgumentException("Couldn't open that picture: " + ex.Message)); }
            QueueSave();
        }

        private void ClearLayer(int i)
        {
            QueueSave();
            _layers[i] = null;
            Replace(i, null);
            _layerNames[i].Text = "Picture " + (i + 1) + (i == 0 ? "  (smallest tilt)" : i == 3 ? "  (steepest tilt)" : "");
            _layerRemove[i].Visible = false;
            _tiltStart.Enabled = _layers.Any(l => l != null) && JobIdle;
            QueueTilt();
        }

        private void Replace(int i, Bitmap bitmap)
        {
            Image old = _thumbs[i].Image;
            _thumbs[i].Image = bitmap;
            old?.Dispose();
        }

        private static Size ThumbSize(Size source, int max)
        {
            double scale = Math.Min((double)max / source.Width, (double)max / source.Height);
            return new Size(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        }

        private TiltRequest TiltRequest()
        {
            var request = new TiltRequest { Dither = _dither.Value == "dither", Threshold = _threshold.Value, Preset = Preset };
            foreach (TiltLayerRequest l in _layers) if (l != null) request.Layers.Add(new TiltLayerRequest { File = l.File, Name = l.Name, Grating = l.Grating });
            return request;
        }

        private void QueueTilt() { if (_tiltTimer != null) { _tiltTimer.Stop(); _tiltTimer.Start(); } }

        private async void RenderTilt()
        {
            if (!_layers.Any(l => l != null))
            {
                _tiltPicture.Image?.Dispose();
                _tiltPicture.Image = null;
                _tiltKey.Text = "Add a picture to see the layout.";
                return;
            }
            int sequence = ++_tiltSequence;
            TiltRequest request = TiltRequest();
            try
            {
                string key = null;
                LabImage map = await Task.Run(() => _backend.TiltMap(request, out key));
                if (sequence != _tiltSequence) return;
                _tiltPicture.Image?.Dispose();
                _tiltPicture.Image = ToBitmap(map);
                _tiltKey.Text = Crlf(key);
            }
            catch (Exception ex) when (sequence == _tiltSequence) { _tiltKey.Text = "⚠ " + ex.Message; }
        }

        // ───────────── stage 1: measure page ─────────────

        private Panel BuildMeasurePage()
        {
            Panel page = NewPage("measure", out FlowLayoutPanel col, out Panel stage);
            string problem = _backend.ProbeProblem;
            if (problem != null) Hint(col, "⚠ " + problem, Theme.Warn);

            Section(col, "Your drive", true);
            var driveRow = Add(col, new Panel { Width = ColumnWidth, Height = 32, BackColor = Theme.Back }, 0, 6);
            _mDrive = Combo();
            _mDrive.SetBounds(0, 3, ColumnWidth - 92, 26);
            var refresh = new LabButton("Refresh") { Width = 84, Height = 30, Location = new Point(ColumnWidth - 84, 1) };
            refresh.Click += (s, e) => FillDrives();
            driveRow.Controls.Add(_mDrive); driveRow.Controls.Add(refresh);
            _mPractice = Add(col, new CheckBox { Text = "Practice run (simulated drive, no disc)", ForeColor = Theme.Text, Height = 26 }, 0, 4);
            Hint(col, "Use the drive you'll burn with. Measuring only reads the disc; nothing is written.");

            Section(col, "1 · Measuring disc");
            Hint(col, "One disc per spindle of the same discs. Burn it, keep it, and never put a picture on it.", Theme.Muted);
            StepButton(col, "Make measuring disc image (.iso)", () => new List<string> { "random", "--output", Path.Combine(Folder, "measuring-disc.iso") },
                "Burn measuring-disc.iso with ImgBurn (Write image file to disc), lowest speed, then put it back in the drive.");

            Section(col, "2 · Check the drive  (1–3 min)");
            StepButton(col, "Check drive", () => WithDrive("timing"), "You want \"CAV\" and low jitter. If it suggests another speed, use the button on the right.");

            Section(col, "3 · Measure the disc  (about 5 min)");
            StepButton(col, "Measure geometry", () => WithDrive("geo"), "With the measuring disc in the drive. The verdict and next steps appear on the right.");

            Section(col, "4 · Install as a preset");
            Add(col, new Label { UseMnemonic = false, Text = "Blank disc sector count", ForeColor = Theme.Text, AutoSize = false, Height = 20 }, 0, 2);
            var sectorRow = Add(col, new Panel { Width = ColumnWidth, Height = 32, BackColor = Theme.Back }, 0, 4);
            _mSectors = Box("2295104");
            _mSectors.SetBounds(0, 3, ColumnWidth - 152, 26);
            var read = new LabButton("Read blank disc") { Width = 144, Height = 30, Location = new Point(ColumnWidth - 144, 1) };
            read.Click += (s, e) => RunProbe(() => WithDrive("info"), "Read blank disc");
            _mButtons.Add(read);
            sectorRow.Controls.Add(_mSectors); sectorRow.Controls.Add(read);
            Hint(col, "Put a BLANK disc from the same spindle in first. Disc Image Studio takes the same number.");
            Add(col, new Label { UseMnemonic = false, Text = "Preset name", ForeColor = Theme.Text, AutoSize = false, Height = 20 }, 0, 2);
            _mName = Add(col, Box("My Verbatim DVD+R (measured)"), 0, 6);
            _mReplace = Add(col, new CheckBox { Text = "Replace my earlier preset with the same id", Checked = true, ForeColor = Theme.Text, Height = 26 }, 0, 4);
            _mInstall = Add(col, new LabButton("Install preset into Disc Image Studio", LabButton.Kind.Primary) { Height = 38, Enabled = false }, 4, 6);
            _mInstall.Click += (s, e) => InstallPreset();
            Hint(col, "Then restart Disc Image Studio and pick it from the preset list. HoloStudio3D picks it up straight away.");

            Section(col, "Any disc-probe command");
            _mCommand = Add(col, Box(""), 0, 4);
            _mCommand.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunCustom(); } };
            var runCustom = Add(col, new LabButton("Run command") { Height = 34 }, 0, 4);
            runCustom.Click += (s, e) => RunCustom();
            _mButtons.Add(runCustom);
            Hint(col, "Type what comes after \"python -m discprobe\", e.g.  geo --drive E: --speed 3324 --reps 32 --ranges 71840-1863264   " +
                      "(\"geo -h\" lists every option). Commands that wait for typed answers can't be answered here.");

            // stage: verdict, suggested next steps, and the full log
            var top = new Panel { Dock = DockStyle.Top, Height = 132, BackColor = Theme.Back };
            _mVerdict = new Label { UseMnemonic = false, Font = Theme.Bold, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, Location = new Point(0, 4), Height = 22, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = "Run the steps on the left in order. disc-probe's verdicts and suggestions show up here." };
            _mStatus = new Label { UseMnemonic = false, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Location = new Point(0, 28), Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var suggestLabel = new Label { UseMnemonic = false, Text = "SUGGESTED NEXT STEPS", Font = Theme.Section, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(0, 54) };
            _mSuggestions = new FlowLayoutPanel { Location = new Point(0, 74), Height = 52, AutoScroll = true, WrapContents = true, BackColor = Theme.Back, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _mCancel = new LabButton("Stop") { Width = 80, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            _mCancel.Click += (s, e) => _mCancelSource?.Cancel();
            top.Controls.AddRange(new Control[] { _mVerdict, _mStatus, suggestLabel, _mSuggestions, _mCancel });
            top.Resize += (s, e) =>
            {
                _mVerdict.Width = top.Width - 90; _mStatus.Width = top.Width - 90; _mSuggestions.Width = top.Width;
                _mCancel.Location = new Point(top.Width - 80, 0);
            };
            _mLog = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                BackColor = Theme.Panel, ForeColor = Color.FromArgb(198, 207, 220), BorderStyle = BorderStyle.None, Font = Theme.Mono,
            };
            stage.Controls.Add(_mLog);
            stage.Controls.Add(top);
            FillDrives();
            if (problem != null) foreach (LabButton b in _mButtons) b.Enabled = false;
            return page;
        }

        private void RunCustom()
        {
            string text = _mCommand.Text.Trim().TrimStart('▶').Trim();
            // Accept the command however it was copied: from disc-probe's advice, its README or this log.
            foreach (string prefix in new[] { "python -m discprobe", "python3 -m discprobe", "disc-probe ", "discprobe " })
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { text = text.Substring(prefix.Length).Trim(); break; }
            if (text.StartsWith("python ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("python3 ", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(text.IndexOf(' ') + 1).Trim();
            List<string> args = SplitCommand(text);
            if (args.Count == 0) { Error(new ArgumentException("Type a disc-probe command first, e.g. geo -h")); return; }
            if (args[0].Replace('\\', '/').EndsWith("geo_polyfit.py", StringComparison.OrdinalIgnoreCase)) args = new[] { "tool", "geo_polyfit.py" }.Concat(args.Skip(1)).ToList();
            RunProbeAsync(args, "disc-probe " + args[0]);
        }

        /// <summary>
        /// When geo stops at a rung, offers the same measurement with --ranges that leave out the stretch
        /// where it failed (disc-probe's own advice), built from the ranges it reported.
        /// </summary>
        internal static List<string> AvoidTroubleSpot(List<string> geoArgs, string[] lines)
        {
            var rangesLine = lines.FirstOrDefault(l => l.Contains("readable ranges ["));
            var stopLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("stopped: after the rung at LBA", StringComparison.Ordinal));
            if (rangesLine == null || stopLine == null) return null;
            var nums = System.Text.RegularExpressions.Regex.Matches(rangesLine.Substring(rangesLine.IndexOf("readable ranges [", StringComparison.Ordinal)), @"\((\d+),\s*(\d+)\)");
            var failed = System.Text.RegularExpressions.Regex.Match(stopLine, @"LBA\s+(\d+)");
            if (nums.Count == 0 || !failed.Success) return null;
            long bad = long.Parse(failed.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            const long margin = 20000, minimum = 150000;
            var keep = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in nums)
            {
                long a = long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                long b = long.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (bad >= a && bad <= b)
                {
                    // Keep whichever side of the failure is bigger, with a margin around it.
                    long before = bad - margin - a, after = b - (bad + margin);
                    if (before >= after && before >= minimum) keep.Add(a + "-" + (bad - margin));
                    else if (after > before && after >= minimum) keep.Add((bad + margin) + "-" + b);
                }
                else keep.Add(a + "-" + b);
            }
            // The failure sat just past a gap: also drop the small stretch after the gap.
            if (keep.Count > 1)
            {
                long Length(string r) { var p = r.Split('-'); return long.Parse(p[1]) - long.Parse(p[0]); }
                long total = keep.Sum(Length);
                keep = keep.Where(r => Length(r) >= total * 0.15).ToList();
            }
            if (keep.Count == 0) return null;
            var args = new List<string>();
            for (int i = 0; i < geoArgs.Count; i++)
            {
                if (geoArgs[i] == "--ranges") { i++; continue; }
                args.Add(geoArgs[i]);
            }
            if (!args.Contains("--speed")) { args.Add("--speed"); args.Add("3324"); }
            if (!args.Contains("--reps")) { args.Add("--reps"); args.Add("32"); }
            args.Add("--ranges");
            args.Add(string.Join(",", keep));
            return args;
        }

        private void StepButton(FlowLayoutPanel col, string text, Func<List<string>> args, string hint)
        {
            LabButton b = Add(col, new LabButton(text) { Height = 36 }, 0, 4);
            b.Click += (s, e) => RunProbe(args, text);
            _mButtons.Add(b);
            Hint(col, hint);
        }

        private void FillDrives()
        {
            _mDrive.Items.Clear();
            foreach (string d in _backend.OpticalDrives()) _mDrive.Items.Add(d);
            if (_mDrive.Items.Count == 0) _mDrive.Items.Add("(no optical drive found)");
            _mDrive.SelectedIndex = 0;
        }

        private List<string> WithDrive(string command)
        {
            var args = new List<string> { command };
            if (_mPractice.Checked) { args.Add("--sim"); args.Add("cav"); }
            else
            {
                string drive = _mDrive.SelectedItem as string;
                if (drive == null || drive.StartsWith("(", StringComparison.Ordinal))
                    throw new ArgumentException("No optical drive found. Connect one and click Refresh, or tick the practice run.");
                args.Add("--drive"); args.Add(drive);
            }
            return args;
        }

        private void RunProbe(Func<List<string>> makeArgs, string title)
        {
            List<string> args;
            try { args = makeArgs(); }
            catch (ArgumentException ex) { Error(ex); return; }
            RunProbeAsync(args, title);
        }

        /// <summary>
        /// Runs a disc-probe command. If it uses --correction with a file that doesn't exist yet (geo's
        /// advice is to write it first), writes the correction from the measurement, then runs the command.
        /// </summary>
        private void RunProbeAsync(List<string> args, string title, Action<int> done = null)
        {
            if (_mTask != null && !_mTask.IsCompleted) return;
            List<string> fix = CorrectionStep(args, _backend.ProbeWorkFolder);
            if (fix == null) { RunProbeCore(args, title, done); return; }
            string file = args[args.IndexOf("--correction") + 1];
            RunProbeCore(fix, "Writing the correction file first", exit =>
            {
                if (exit == 0 && File.Exists(ResolveProbePath(file, _backend.ProbeWorkFolder)))
                {
                    AppendLog("Correction file written. Now running the command you asked for.");
                    RunProbeCore(args, title, done);
                }
                else
                {
                    _mVerdict.Text = "⚠ Couldn't write " + file + " from your measurement. You can still install the preset without it: run the same command without --correction " + file + ".";
                    _mVerdict.ForeColor = Theme.Bad;
                    done?.Invoke(exit == 0 ? 1 : exit);
                }
            });
        }

        private static string ResolveProbePath(string path, string workFolder) =>
            Path.IsPathRooted(path) ? path : Path.Combine(workFolder ?? "", path);

        /// <summary>
        /// The geo_polyfit command that writes a missing --correction file, or null if none is needed.
        /// Uses the --geo result the command names, or for "latest" the newest geo result folder.
        /// </summary>
        internal static List<string> CorrectionStep(List<string> args, string workFolder)
        {
            if (args.Count == 0 || args[0] == "tool") return null;
            int at = args.IndexOf("--correction");
            if (at < 0 || at + 1 >= args.Count) return null;
            string file = args[at + 1];
            if (File.Exists(ResolveProbePath(file, workFolder))) return null;

            int g = args.IndexOf("--geo");
            string geo = g >= 0 && g + 1 < args.Count ? args[g + 1] : "latest";
            if (string.Equals(geo, "latest", StringComparison.OrdinalIgnoreCase))
            {
                string results = Path.Combine(workFolder ?? "", "results");
                if (!Directory.Exists(results)) return null;
                // Folder names end in the measurement's date and time (geo-E-20261108-211613).
                DirectoryInfo newest = new DirectoryInfo(results).GetDirectories("geo-*")
                    .Where(d => File.Exists(Path.Combine(d.FullName, "summary.json")))
                    .OrderByDescending(d => d.Name.Length >= 15 ? d.Name.Substring(d.Name.Length - 15) : d.Name, StringComparer.Ordinal)
                    .ThenByDescending(d => d.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (newest == null) return null;
                geo = Path.Combine("results", newest.Name);
            }
            else if (geo.EndsWith("summary.json", StringComparison.OrdinalIgnoreCase))
                geo = Path.GetDirectoryName(geo);
            return new List<string> { "tool", "geo_polyfit.py", geo, "--write-correction", file, "--order", "quadratic" };
        }

        private async void RunProbeCore(List<string> args, string title, Action<int> done = null)
        {
            if (_mTask != null && !_mTask.IsCompleted) return;
            _mCancelSource = new CancellationTokenSource();
            CancellationToken token = _mCancelSource.Token;
            SetProbeBusy(true);
            _mSuggestions.Controls.Clear();
            _mVerdict.Text = title + "…";
            _mVerdict.ForeColor = Theme.Text;
            _mStatus.Text = "";
            AppendLog("");
            AppendLog("▶ disc-probe " + string.Join(" ", args.Select(Quote)));
            var lines = new List<string>();
            int exit = -1;
            bool skipCommand = false;
            // Drops disc-probe's blank-disc advice ("Nothing is recorded on this disc ... burn a random-data
            // disc") and its command: here a blank disc is read on purpose, for its sector count.
            Func<string, bool> keep = l =>
            {
                if (l.TrimStart().StartsWith("Nothing is recorded on this disc", StringComparison.Ordinal)) { skipCommand = true; return false; }
                if (skipCommand && l.Contains("discprobe random")) { skipCommand = false; return false; }
                skipCommand = false;
                return true;
            };
            try
            {
                Task<int> work = Task.Run(() => _backend.RunProbe(args,
                    l => { lock (lines) { if (!keep(l)) return; lines.Add(l); } BeginInvokeSafe(() => OnProbeLine(l)); },
                    st => BeginInvokeSafe(() => _mStatus.Text = st),
                    token));
                _mTask = work;
                exit = await work;
            }
            catch (OperationCanceledException) { _mVerdict.Text = "Stopped."; _mVerdict.ForeColor = Theme.Muted; }
            catch (Exception ex) { _mVerdict.Text = "⚠ " + ex.Message; _mVerdict.ForeColor = Theme.Bad; }
            finally { SetProbeBusy(false); }
            string[] all;
            lock (lines) all = lines.ToArray();
            if (exit >= 0) FinishProbe(args, exit, all);
            done?.Invoke(exit);
        }

        private void SetProbeBusy(bool busy)
        {
            foreach (LabButton b in _mButtons) b.Enabled = !busy && _backend.ProbeProblem == null;
            foreach (Control c in _mSuggestions.Controls) c.Enabled = !busy;
            _mInstall.Enabled = !busy && _mLastExport != null;
            _mCancel.Visible = busy;
            if (!busy) _mStatus.Text = "";
        }

        private void OnProbeLine(string line)
        {
            AppendLog(line);
            string t = line.Trim();
            if (t.StartsWith("Verdict:", StringComparison.Ordinal))
            {
                _mVerdict.Text = t;
                _mVerdict.ForeColor = t.IndexOf("good", StringComparison.OrdinalIgnoreCase) >= 0 ? Theme.Ok
                    : t.IndexOf("noisy", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("not ", StringComparison.OrdinalIgnoreCase) >= 0 ? Theme.Warn : Theme.Text;
            }
        }

        private void FinishProbe(List<string> args, int exit, string[] lines)
        {
            if (!_mVerdict.Text.StartsWith("Verdict", StringComparison.Ordinal))
            {
                _mVerdict.Text = exit == 0 ? "Done." : "disc-probe stopped with an error. The log below says why.";
                _mVerdict.ForeColor = exit == 0 ? Theme.Ok : Theme.Bad;
            }
            // The blank disc's sector count, from `info`.
            foreach (string l in lines)
            {
                const string marker = "Sector count for disexport --sectors:";
                int at = l.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) continue;
                string digits = new string(l.Substring(at + marker.Length).TrimStart().TakeWhile(char.IsDigit).ToArray());
                if (digits.Length > 0) { _mSectors.Text = digits; _mVerdict.Text = "Blank disc holds " + digits + " sectors."; _mVerdict.ForeColor = Theme.Ok; }
            }
            // disc-probe ends each step with the commands to run next; offer the safe ones as buttons.
            foreach (string l in lines)
            {
                List<string> next = ParseSuggestion(l);
                if (next == null) continue;
                if (next[0] == "disexport") { _mLastExport = next; continue; }
                if (_mSuggestions.Controls.OfType<Button>().Any(b => (string)b.Tag == string.Join("\u0001", next))) continue;
                var b = new LabButton("Run: " + string.Join(" ", next.Select(Quote))) { Height = 30, Tag = string.Join("\u0001", next), Margin = new Padding(0, 0, 8, 6) };
                b.Width = Math.Min(560, TextRenderer.MeasureText(b.Text, b.Font).Width + 30);
                List<string> captured = next;
                b.Click += (s, e) => RunProbe(() => new List<string>(captured), "Suggested step");
                _mSuggestions.Controls.Add(b);
            }
            if (args.Count > 0 && args[0] == "geo")
            {
                List<string> avoid = AvoidTroubleSpot(args, lines);
                if (avoid != null)
                {
                    string ranges = avoid[avoid.IndexOf("--ranges") + 1];
                    var b = new LabButton("Measure without the trouble spot  (--ranges " + ranges + ")", LabButton.Kind.Primary) { Height = 30, Margin = new Padding(0, 0, 8, 6) };
                    b.Width = Math.Min(620, TextRenderer.MeasureText(b.Text, b.Font).Width + 34);
                    b.Click += (s, e) => RunProbe(() => new List<string>(avoid), "Measuring without the trouble spot");
                    _mSuggestions.Controls.Add(b);
                    _mSuggestions.Controls.SetChildIndex(b, 0);
                    _mCommand.Text = string.Join(" ", avoid.Select(Quote));
                }
            }
            if (_mLastExport != null)
            {
                bool simulated = _mLastExport.Any(a => a.IndexOf("SIM", StringComparison.Ordinal) >= 0);
                _mInstall.Enabled = true;
                _mStatus.Text = simulated ? "That was a practice run: disc-probe won't install a preset from a simulated drive."
                                          : "Measured. Check the blank-disc sector count, then Install preset.";
            }
        }

        /// <summary>A disc-probe suggestion line ("python -m discprobe geo ..." or "python tools/x.py ..."),
        /// as arguments for our launcher; null for anything else or for commands we don't run from here.</summary>
        internal static List<string> ParseSuggestion(string line)
        {
            int at = line.IndexOf("python ", StringComparison.Ordinal);
            if (at < 0) return null;
            string before = line.Substring(0, at).Trim();
            if (before.Length > 0 && !before.EndsWith(":", StringComparison.Ordinal)) return null;
            List<string> words = SplitCommand(line.Substring(at));
            if (words.Count < 3) return null;
            string[] allowed = { "info", "timing", "geo", "disexport", "random", "results", "guide" };
            if (words[1] == "-m" && words[2] == "discprobe" && words.Count > 3 && allowed.Contains(words[3]))
                return words.Skip(3).ToList();
            if (words[1].StartsWith("tools/", StringComparison.Ordinal) && words[1] == "tools/geo_polyfit.py")
                return new[] { "tool", "geo_polyfit.py" }.Concat(words.Skip(2)).ToList();
            return null;
        }

        internal static List<string> SplitCommand(string text)
        {
            var words = new List<string>();
            var current = new System.Text.StringBuilder();
            char quote = '\0';
            bool any = false;
            foreach (char c in text)
            {
                if (quote != '\0') { if (c == quote) quote = '\0'; else current.Append(c); continue; }
                if (c == '"' || c == '\'') { quote = c; any = true; continue; }
                if (char.IsWhiteSpace(c)) { if (any || current.Length > 0) { words.Add(current.ToString()); current.Clear(); any = false; } continue; }
                current.Append(c);
            }
            if (any || current.Length > 0) words.Add(current.ToString());
            return words;
        }

        private static string Quote(string a) => a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a;

        private void InstallPreset()
        {
            if (_mLastExport == null) return;
            string sectors = _mSectors.Text.Trim();
            if (!uint.TryParse(sectors, out uint n) || n < 100000)
            {
                Error(new ArgumentException("Enter the blank disc's sector count (e.g. 2295104), or click Read blank disc."));
                return;
            }
            var args = new List<string>();
            for (int i = 0; i < _mLastExport.Count; i++)
            {
                string a = _mLastExport[i];
                if (a == "--output" || a == "--sectors" || a == "--name") { i++; continue; } // replaced below
                if (a == "--install" || a == "--replace") continue;
                args.Add(a);
            }
            args.AddRange(new[] { "--sectors", sectors, "--install" });
            if (_mName.Text.Trim().Length > 0) { args.Add("--name"); args.Add(_mName.Text.Trim()); }
            if (_mReplace.Checked) args.Add("--replace");
            RunProbeAsync(args, "Installing preset", exit =>
            {
                if (exit != 0) return;
                _backend.ReloadPresets();
                RefreshPresetCombos();
                _mVerdict.Text = "Preset installed. Restart Disc Image Studio to see it; it's already selected here.";
                _mVerdict.ForeColor = Theme.Ok;
            });
        }

        private void RefreshPresetCombos()
        {
            PresetInfo newest = _backend.Presets.FirstOrDefault(p => p.Id.StartsWith("user:", StringComparison.Ordinal));
            foreach (ComboBox combo in _presetBoxes)
            {
                combo.Items.Clear();
                foreach (PresetInfo p in _backend.Presets) combo.Items.Add(p);
            }
            PresetInfo pick = newest ?? _backend.Presets.FirstOrDefault();
            _settings.Remove("preset");
            foreach (ComboBox combo in _presetBoxes) combo.SelectedItem = pick;
        }

        private void AppendLog(string line)
        {
            if (_mLog.TextLength > 400_000) _mLog.Text = _mLog.Text.Substring(_mLog.TextLength - 200_000);
            _mLog.AppendText(line + "\r\n");
        }

        // ───────────── stereo pair page ─────────────

        private bool JobIdle => _jobTask == null || _jobTask.IsCompleted;

        private Panel BuildPairPage()
        {
            Panel page = NewPage("pair", out FlowLayoutPanel col, out Panel stage);
            Section(col, "Your 3D picture", true);
            _pairMode = Add(col, new Segmented(3, ("depth", "Picture + depth"), ("model", "3D model"), ("frames", "Several pictures"),
                ("sbs", "Side-by-side"), ("two", "Two pictures")));
            _pairMode.Changed += (s, e) => { SyncPairMode(); UpdatePairSummary(); QueuePair(); };
            const string pictures = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff";

            // A 3D model, rendered from each view's angle.
            _pairModel = Add(col, new DropZone { Filter = "3D models|*.obj;*.stl" });
            _pairModel.SetText("Drop a 3D model", ".obj or .stl  ·  rendered from each view's angle");
            _pairModel.FileChosen += path => { _pairModelFile = path; _pairModel.SetText(Path.GetFileName(path), "Click or drop to change"); NamePair(path); QueuePair(); };
            _pairYaw = Add(col, new Slider("Turn left / right", -180, 180, 5, 0, v => v.ToString("0") + "°"), 0, 2);
            _pairPitch = Add(col, new Slider("Tilt up / down", -90, 90, 5, 0, v => v.ToString("0") + "°"), 0, 2);
            _pairZoom = Add(col, new Slider("Size in the picture", 0.5, 2, 0.05, 1, v => Math.Round(v * 100) + " %"), 0, 2);
            _pairTurn = Add(col, new Slider("3D strength", 1, 20, 0.5, 6, v => v.ToString("0.#") + "°", "How far the model turns between your two eyes."), 0, 4);
            foreach (Slider sl in new[] { _pairYaw, _pairPitch, _pairZoom, _pairTurn }) sl.Changed += (s, e) => QueuePair();

            // Several pictures: lenticular-style frames, left-most view first.
            _pairFrames = Add(col, new DropZone { Filter = pictures, Multiple = true }, 0, 4);
            _pairFrames.SetText("Drop 2 to 7 pictures at once", "left-most view first  ·  sorted by file name");
            _pairFrames.FilesChosen += paths => SetFrames(paths);
            _pairFramesHint = Hint(col, "Like lenticular frames: each picture is one view, ordered left to right by name (frame1, frame2, …). Up to 7.");
            _pairSbs = Add(col, new DropZone { Filter = pictures });
            _pairSbs.SetText("Drop a side-by-side 3D picture", "e.g. from Convert3D  ·  left view on the left");
            _pairSbs.FileChosen += path => { _pairSbsFile = path; _pairSbs.SetText(Path.GetFileName(path), "Click or drop to change"); NamePair(path); QueuePair(); };
            _pairSqueezed = Add(col, new CheckBox { Text = "Half-width (squeezed) side-by-side", ForeColor = Theme.Text, Height = 24 }, 0, 2);
            _pairCross = Add(col, new CheckBox { Text = "Cross-eyed layout (right view on the left)", ForeColor = Theme.Text, Height = 24 });
            _pairLeft = Add(col, new DropZone { Filter = pictures, Height = 64 }, 0, 6);
            _pairLeft.SetText("Left-eye picture", "drop or click");
            _pairLeft.FileChosen += path => { _pairLeftFile = path; _pairLeft.SetText("Left: " + Path.GetFileName(path), "Click or drop to change"); NamePair(path); QueuePair(); };
            _pairRight = Add(col, new DropZone { Filter = pictures, Height = 64 });
            _pairRight.SetText("Right-eye picture", "drop or click");
            _pairRight.FileChosen += path => { _pairRightFile = path; _pairRight.SetText("Right: " + Path.GetFileName(path), "Click or drop to change"); QueuePair(); };
            foreach (CheckBox c in new[] { _pairSqueezed, _pairCross }) c.CheckedChanged += (s, e) => QueuePair();

            // Picture + depth map, the same pair you'd use for a lenticular.
            _pairColor = Add(col, new DropZone { Filter = pictures, Height = 64 }, 0, 6);
            _pairColor.SetText("Picture (RGB)", "drop or click");
            _pairColor.FileChosen += path => { _pairColorFile = path; _pairColor.SetText("Picture: " + Path.GetFileName(path), "Click or drop to change"); NamePair(path); QueuePair(); };
            _pairDepth = Add(col, new DropZone { Filter = pictures, Height = 64 }, 0, 6);
            _pairDepth.SetText("Depth map", "drop or click  ·  white = near");
            _pairDepth.FileChosen += path => { _pairDepthFile = path; _pairDepth.SetText("Depth: " + Path.GetFileName(path), "Click or drop to change"); QueuePair(); };
            _pairInvert = Add(col, new CheckBox { Text = "Depth map uses black = near", ForeColor = Theme.Text, Height = 24 }, 0, 4);
            _pairInvert.CheckedChanged += (s, e) => QueuePair();
            _pairStrength = Add(col, new Slider("3D strength", 0.5, 8, 0.25, 3, v => v.ToString("0.##") + " %", "Biggest left/right shift, as % of the picture width."), 0, 4);
            _pairFocus = Add(col, new Slider("Pop-out point", 0, 1, 0.05, 0.5, v => v <= 0.001 ? "all pops out" : v >= 0.999 ? "all sinks in" : Math.Round(v * 100) + "% depth",
                "This depth sits on the disc surface; nearer parts float in front."), 0, 4);
            foreach (Slider sl in new[] { _pairStrength, _pairFocus }) sl.Changed += (s, e) => QueuePair();

            _pairViews = Add(col, new Slider("Views", 2, MaxHologramViews, 1, 2, v => v <= 2 ? "stereo pair" : "look-around"), 0, 2);
            _pairViews.ShowValueInName(v => "Views:  " + v.ToString("0"));
            _pairViews.Changed += (s, e) => { SyncPairMode(); UpdatePairSummary(); QueuePair(); };
            _pairViewsHint = Hint(col, "2 = a stereo pair (brightest). More views let the picture turn as you move your head, like a lenticular, but each view gets fewer dots, so it's dimmer.");

            Section(col, "Viewing");
            _pairDistance = Add(col, new Slider("Viewing distance", 25, 80, 1, 40, v => v.ToString("0") + " cm", "Spaces the patterns to match your eyes at this distance."), 0, 4);
            _pairDistance.Changed += (s, e) => { UpdatePairSummary(); QueuePair(); };
            _pairChoiceLabel = Add(col, new Label { UseMnemonic = false, Text = "Pattern pair", ForeColor = Theme.Text, AutoSize = false, Height = 20 }, 0, 2);
            _pairChoice = Add(col, Combo(), 0, 6);
            _pairChoice.Items.Add("Automatic (from viewing distance)");
            for (int i = 0; i < _backend.StereoTestPairs.Count; i++)
            {
                string[] p = _backend.StereoTestPairs[i].Split(',');
                _pairChoice.Items.Add("Test tile " + (i + 1) + ":  " + p[0] + " left  +  " + p[1] + " right  (" + Separation(p[0], p[1]).ToString("0.0") + "° apart)");
            }
            _pairChoice.SelectedIndex = 0;
            _pairChoice.SelectedIndexChanged += (s, e) => { UpdatePairSummary(); QueuePair(); };
            _pairSummary = Hint(col, "", Theme.Muted);
            _pairSwap = Add(col, new CheckBox { Text = "Swap eyes (if the 3D looks inside-out)", ForeColor = Theme.Text, Height = 26 });
            _pairSwap.CheckedChanged += (s, e) => { UpdatePairSummary(); QueuePair(); };

            Section(col, "Picture size");
            _pairSize = Add(col, new Segmented(("band", "Fit in top band"), ("large", "Large (edges clip)")), 0, 6);
            _pairSize.Changed += (s, e) => { if (_pairSize.Value == "large" && _pairWidth.Value < 80) _pairWidth.Value = 100; QueuePair(); };
            _pairWidth = Add(col, new Slider("Width", 20, 116, 1, 50, v => v.ToString("0") + " mm",
                "Fit: whole picture stays in the band at the top (up to ~45 mm). Large: up to the full disc; the edge and centre hole cut off what's past them."), 0, 4);
            _pairWidth.Changed += (s, e) => QueuePair();

            Section(col, "Position on disc");
            _pairMoveX = Add(col, new Slider("Move left / right", -40, 40, 0.5, 0, v => Math.Abs(v) < 0.01 ? "centre" : Math.Abs(v).ToString("0.#") + " mm " + (v < 0 ? "left" : "right")), 0, 2);
            _pairMoveY = Add(col, new Slider("Move up / down", -30, 30, 0.5, 0, v => Math.Abs(v) < 0.01 ? "centre" : Math.Abs(v).ToString("0.#") + " mm " + (v < 0 ? "down" : "up")), 0, 2);
            foreach (Slider sl in new[] { _pairMoveX, _pairMoveY }) sl.Changed += (s, e) => QueuePair();
            var centre = Add(col, new LabButton("Centre it") { Height = 30 }, 0, 4);
            centre.Click += (s, e) => { _pairMoveX.Value = 0; _pairMoveY.Value = 0; };
            Hint(col, "Up moves it toward the rim. The 3D is strongest straight up at 12 o'clock and fades toward the sides, where the tracks curve. Anything past the rim or over the hole is cut off.");

            Section(col, "Copies around the disc");
            _pairCopies = Add(col, new Segmented(("1", "1"), ("2", "2"), ("3", "3"), ("4", "4"), ("6", "6"), ("8", "8")));
            _pairCopies.Changed += (s, e) => { _pairUpright.Visible = _pairCopies.Value == "2"; QueuePair(); };
            _pairUpright = Add(col, new CheckBox { Text = "Keep the bottom copy upright", ForeColor = Theme.Text, Height = 26 }, 0, 2);
            _pairUpright.CheckedChanged += (s, e) => QueuePair();
            _pairUpright.Visible = false;
            Hint(col, "Fills the disc: each copy is turned to face out from the centre, so whichever one you turn to the top shows the 3D. Each copy gets its own slice, about 82 / 71 / 58 / 41 / 31 mm wide for 2 / 3 / 4 / 6 / 8 copies; a wider picture is cut at its slice edges. With 2, Keep the bottom copy upright (experimental) makes both show at the same time.");
            Section(col, "Dither detail");
            _pairDetail = Add(col, new Segmented(("0.05", "Fine"), ("0.1", "Normal"), ("0.2", "Coarse")), 0, 4);
            _pairDetail.Changed += (s, e) => QueuePair();
            Hint(col, "Fine = 0.05 mm cells: the smoothest picture. Try Normal if a burn looks weak or washed out.");
            _pairDither = Add(col, new Segmented(("dither", "Dither (keeps grays)"), ("cut", "Hard cut-off")), 0, 8);
            _pairDither.Changed += (s, e) => QueuePair();
            _pairTest = Add(col, new CheckBox { Text = "Add L/R test tiles at 6 o'clock", Checked = true, ForeColor = Theme.Text, Height = 26 });
            _pairTest.CheckedChanged += (s, e) => QueuePair();

            Section(col, "Off areas");
            _pairOff = Add(col, new Segmented(("mix", "Random mix"), ("fixed", "Fixed, aimed away")), 0, 4);
            _pairOff.Changed += (s, e) => QueuePair();
            Hint(col, "What fills the dark parts of the picture. Random mix is the original. Fixed (experimental, suggested by disc-probe's author) uses one pattern that sends its light away from your eyes, for possibly better contrast. The test-patch disc below compares both.");

            Section(col, "Your DVD");
            Add(col, PresetCombo(), 0, 4);
            Hint(col, "Use your measured preset (\"Yours:\") so the picture lands where it should, not streaked.", Theme.Muted);
            Section(col, "Save to");
            Add(col, FolderRow(), 0, 6);
            _pairName = Add(col, Box("hologram"), 0, 8);
            _pairStart = Add(col, new LabButton("Make hologram disc  (4.7 GB)", LabButton.Kind.Spectrum) { Height = 42, Enabled = false }, 8, 8);
            _pairStart.Click += (s, e) => StartJob("pair");
            Hint(col, "All the views it builds are saved next to the .iso as -views.png (left-most first) so you can check them. View with one small light off to the SIDE. Close one eye, then the other: each should see its own view.");
            _pairPatches = Add(col, new LabButton("Make test-patch disc  (no picture needed)") { Height = 36 }, 4, 4);
            _pairPatches.Click += (s, e) => StartJob("patches");
            Hint(col, "Eight matched patches (all off / left only / right only / both on, with each kind of off area) for a photo test of whether each eye really gets its own view. Uses the viewing distance, pattern pair and DVD above; the key says how to photograph it.");

            _pairPicture = Picture();
            _pairKey = KeyBox();
            stage.Controls.Add(_pairPicture);
            stage.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = Theme.Back });
            stage.Controls.Add(_pairKey);
            stage.Controls.Add(StageBar(stage, "Layout   —   each view's pattern, interleaved", out _));
            _pairKey.Text = "Add a picture and its depth map, a 3D model, several pictures, or a side-by-side / left and right picture to see the layout.";
            SyncPairMode();
            UpdatePairSummary();
            return page;
        }

        private double Separation(string a, string b)
        {
            double A(string code) => _backend.Gratings.FirstOrDefault(g => g.Code == code)?.Angle ?? 0;
            return Math.Abs(A(a) - A(b));
        }

        private void SyncPairMode()
        {
            string mode = _pairMode.Value;
            bool sbs = mode == "sbs", depth = mode == "depth", model = mode == "model", frames = mode == "frames";
            _pairSbs.Visible = _pairSqueezed.Visible = _pairCross.Visible = sbs;
            _pairLeft.Visible = _pairRight.Visible = mode == "two";
            _pairColor.Visible = _pairDepth.Visible = _pairInvert.Visible = _pairStrength.Visible = _pairFocus.Visible = depth;
            _pairModel.Visible = _pairYaw.Visible = _pairPitch.Visible = _pairZoom.Visible = _pairTurn.Visible = model;
            _pairFrames.Visible = _pairFramesHint.Visible = frames;
            _pairViews.Visible = _pairViewsHint.Visible = depth || model;
            // The pattern-pair choice (from the test tiles) only applies to 2 views.
            _pairChoiceLabel.Visible = _pairChoice.Visible = PairViewCount() == 2;
            if (_pairStart != null) _pairStart.Enabled = PairReady() && JobIdle;
        }

        private void NamePair(string path) => _pairName.Text = Path.GetFileNameWithoutExtension(path) + "-hologram";

        /// <summary>Hologram mode can show up to this many views (one per clean pattern).</summary>
        private const int MaxHologramViews = 7;

        private int PairViewCount()
        {
            switch (_pairMode.Value)
            {
                case "depth": case "model": return (int)Math.Round(_pairViews.Value);
                case "frames": return _pairFrameFiles?.Length ?? 0;
                default: return 2;
            }
        }

        private void SetFrames(string[] paths)
        {
            string[] files = paths.Where(File.Exists).OrderBy(NaturalKey, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length < 2 || files.Length > MaxHologramViews)
            {
                MessageBox.Show(this, "Choose 2 to " + MaxHologramViews + " pictures at once (you chose " + files.Length + "). Each one is a view, left-most first.",
                    "Hologram mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _pairFrameFiles = files;
            _pairFrames.SetText(files.Length + " pictures = " + files.Length + " views:  " + Path.GetFileName(files[0]) + "  …  " + Path.GetFileName(files[files.Length - 1]), "left-most first  ·  click or drop to change");
            NamePair(files[0]);
            SyncPairMode();
            UpdatePairSummary();
            QueuePair();
            QueueSave();
        }

        /// <summary>Sort key that puts frame2 before frame10.</summary>
        private static string NaturalKey(string path) =>
            System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(path) ?? "", @"\d+", m => m.Value.PadLeft(10, '0'));

        private bool PairReady()
        {
            switch (_pairMode.Value)
            {
                case "sbs": return _pairSbsFile != null;
                case "depth": return _pairColorFile != null && _pairDepthFile != null;
                case "model": return _pairModelFile != null;
                case "frames": return _pairFrameFiles != null && _pairFrameFiles.Length >= 2;
                default: return _pairLeftFile != null && _pairRightFile != null;
            }
        }

        private StereoPairRequest PairRequest()
        {
            var r = new StereoPairRequest
            {
                Mode = _pairMode.Value, SbsFile = _pairSbsFile, LeftFile = _pairLeftFile, RightFile = _pairRightFile,
                ColorFile = _pairColorFile, DepthFile = _pairDepthFile, InvertDepth = _pairInvert.Checked,
                DepthStrength = _pairStrength.Value, DepthFocus = _pairFocus.Value,
                Squeezed = _pairSqueezed.Checked, CrossEyed = _pairCross.Checked, DistanceCm = _pairDistance.Value,
                SwapEyes = _pairSwap.Checked, WidthMm = _pairWidth.Value, Dither = _pairDither.Value == "dither",
                Large = _pairSize.Value == "large",
                OffsetXMm = _pairMoveX.Value, OffsetYMm = _pairMoveY.Value,
                Copies = int.Parse(_pairCopies.Value, CultureInfo.InvariantCulture), UprightBottom = _pairUpright.Checked,
                OffMode = _pairOff.Value,
                CellMm = double.Parse(_pairDetail.Value, System.Globalization.CultureInfo.InvariantCulture),
                TestStrips = _pairTest.Checked, Preset = Preset,
                ViewCount = (int)Math.Round(_pairViews.Value), FrameFiles = _pairFrameFiles,
                ModelFile = _pairModelFile, ModelYaw = _pairYaw.Value, ModelPitch = _pairPitch.Value, ModelTurn = _pairTurn.Value, ModelZoom = _pairZoom.Value,
            };
            if (_pairChoice.SelectedIndex > 0 && PairViewCount() == 2)
            {
                string[] p = _backend.StereoTestPairs[_pairChoice.SelectedIndex - 1].Split(',');
                r.PairLeft = p[0]; r.PairRight = p[1];
            }
            return r;
        }

        private void UpdatePairSummary()
        {
            if (_pairSummary == null || _pairChoice == null) return;
            try { _pairSummary.Text = _backend.DescribePair(PairRequest()); }
            catch (Exception ex) { _pairSummary.Text = ex.Message; }
        }

        private void QueuePair()
        {
            if (_pairTimer == null || _pairStart == null) return;
            _pairStart.Enabled = PairReady() && JobIdle;
            _pairTimer.Stop(); _pairTimer.Start();
        }

        private async void RenderPair()
        {
            if (!PairReady())
            {
                _pairPicture.Image?.Dispose();
                _pairPicture.Image = null;
                _pairKey.Text = "Add a picture and its depth map, a 3D model, several pictures, or a side-by-side / left and right picture to see the layout.";
                return;
            }
            int sequence = ++_pairSequence;
            StereoPairRequest request = PairRequest();
            try
            {
                string key = null;
                LabImage map = await Task.Run(() => _backend.StereoPairMap(request, out key));
                if (sequence != _pairSequence) return;
                _pairPicture.Image?.Dispose();
                _pairPicture.Image = ToBitmap(map);
                _pairKey.Text = Crlf(key);
            }
            catch (Exception ex) when (sequence == _pairSequence) { _pairKey.Text = "⚠ " + ex.Message; }
        }

        // ───────────── about page ─────────────

        internal const string AppVersion = "v1.03";
        internal const string WebsiteUrl = "https://drewmedina.com";
        internal const string DisRepoUrl = "https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio";
        internal const string ProbeRepoUrl = "https://github.com/Ashley-QwQ/disc-probe";

        private Panel BuildAboutPage()
        {
            var page = new Panel { BackColor = Theme.Back, AutoScroll = true };
            _pages["about"] = page;
            var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(28, 22), BackColor = Theme.Back };
            page.Controls.Add(col);
            Color body = Color.FromArgb(198, 207, 220);
            void H(string text, int top = 22) => col.Controls.Add(new Label { UseMnemonic = false, Text = text, Font = Theme.UI(11f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, top, 0, 6) });
            void P(string text, Color? color = null) => col.Controls.Add(new Label { UseMnemonic = false, Text = text, ForeColor = color ?? body, AutoSize = true, MaximumSize = new Size(820, 0), Margin = new Padding(0, 0, 0, 6) });
            void Link(string caption, string url)
            {
                var l = new LinkLabel
                {
                    UseMnemonic = false, Text = caption, AutoSize = true, Font = Theme.Bold, BackColor = Theme.Back,
                    LinkColor = Theme.Accent, ActiveLinkColor = Color.White, VisitedLinkColor = Theme.Accent,
                    LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, 0, 8), Cursor = Cursors.Hand,
                };
                l.LinkClicked += (s, e) => OpenLink(url);
                col.Controls.Add(l);
            }

            var top = new Panel { BackColor = Theme.Back, Size = new Size(820, 64), Margin = new Padding(0, 0, 0, 4) };
            top.Controls.Add(new BrandMark { Location = new Point(0, 4), Size = new Size(52, 52) });
            top.Controls.Add(new Label { UseMnemonic = false, Text = "HoloStudio3D  " + AppVersion, Font = Theme.UI(15f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(66, 2), BackColor = Theme.Back });
            top.Controls.Add(new Label { UseMnemonic = false, Text = "with Disc Image Studio · English Edition", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(68, 36), BackColor = Theme.Back });
            col.Controls.Add(top);

            int stepNo = 0;
            void Step(string title, string text)
            {
                stepNo++;
                var row = new Panel { BackColor = Theme.Back, Width = 820, Margin = new Padding(0, 0, 0, 10) };
                var badge = new Label { UseMnemonic = false, Text = stepNo.ToString(), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Bold, ForeColor = Theme.Back, BackColor = Theme.Accent, Size = new Size(26, 26), Location = new Point(0, 0) };
                var head = new Label { UseMnemonic = false, Text = title, Font = Theme.Bold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(38, 3), BackColor = Theme.Back };
                Size textSize = TextRenderer.MeasureText(text, Theme.Body, new Size(780, 0), TextFormatFlags.WordBreak);
                var desc = new Label { UseMnemonic = false, Text = text, Font = Theme.Body, ForeColor = body, AutoSize = false, Location = new Point(38, 26), Size = new Size(782, textSize.Height + 4), BackColor = Theme.Back };
                row.Controls.Add(badge); row.Controls.Add(head); row.Controls.Add(desc);
                row.Height = desc.Bottom + 2;
                col.Controls.Add(row);
            }

            H("Instructions, step by step", 14);
            P("Setting up (once)", Theme.Muted);
            Step("Unzip the setup folder", "Unzip the whole package somewhere simple, such as C:\\DiscImageStudio-English. If you were given a ready-built folder with HoloStudio3D.exe in it, skip to step 3.");
            Step("Build the apps", "Install the .NET 10 SDK from Microsoft if you don't have it, then double-click Build-English.bat. It takes a few minutes and needs the internet once, to fetch a private copy of Python for Stage 1. When it finishes, the HoloStudio3D-App folder opens with everything in it.");
            Step("Open HoloStudio3D", "Run HoloStudio3D-App\\HoloStudio3D.exe. Under OUTPUT FOLDER in the sidebar, click Change… to choose where discs are saved (default Documents\\HoloStudio3D, with a folder for each design). Disc Image Studio itself is DiscImageStudio.exe in the same folder.");
            P("Stage 1: once for each new stack of discs", Theme.Muted);
            Step("Burn a measuring disc", "Open Stage 1, pick the drive you'll burn with, and click Make measuring disc image. Burn measuring-disc.iso with ImgBurn (Write image file to disc) at the slowest speed, then put it back in the drive. Keep that disc; never put a picture on it.");
            Step("Check the drive", "Click Check drive. You want \"CAV\" and low jitter. If it suggests a different speed, click the suggested command on the right.");
            Step("Measure the disc", "Click Measure geometry (about 5 minutes) and follow the verdict on the right. If it keeps stopping at the same place, click Measure without the trouble spot.");
            Step("Install your preset", "Put a BLANK disc from the same stack in the drive, click Read blank disc, name the preset, then click Install preset into Disc Image Studio. It then appears as \"Yours: …\" under Your DVD on every page.");
            P("Making a disc", Theme.Muted);
            Step("Pick a disc type", "A Magic Eye-style picture works on CD or DVD: save the PNG, then in Disc Image Studio import it into a project using your preset and export an ISO. Hologram mode, Rainbow test disc and Tilt-image disc (DVD, experimental) build the finished .iso directly. On a new stack, burn the Rainbow test disc first to see which patterns glow.");
            Step("Burn the .iso", "Use ImgBurn → Write image file to disc, or right-click the .iso → Burn disc image, at your drive's slowest speed. Burn it as a disc image; don't copy the .iso onto a disc as a file. The image has no file system, so Explorer shows the burned disc as empty, which is expected.");
            Step("View it", "Use one small bright light (a phone flashlight) in a dim room, and tilt the disc slowly. The exact viewing steps for each disc type are on the How to burn & view page.");

            H("Created by Drew Medina");
            P("HoloStudio3D (hologram, hidden-3D, rainbow and tilt-image discs) and the English edition of Disc Image Studio.");
            Link("drewmedina.com", WebsiteUrl);

            H("Disc Image Studio");
            P("The disc-image engine, the presets and the main Disc Image Studio app are by JiaFeiMiao-K-Cat and the Disc Image Studio contributors. "
              + "This English edition translates the app and adds HoloStudio3D; it is a modified version, not the official release.");
            Link("github.com/JiaFeiMiao-K-Cat/DiscImageStudio", DisRepoUrl);

            H("disc-probe  (Stage 1)");
            P("Stage 1's disc measuring is disc-probe by Ashley-QwQ, bundled unmodified and run through a small launcher, with a private copy of Python, NumPy, SciPy and Pillow.");
            Link("github.com/Ashley-QwQ/disc-probe", ProbeRepoUrl);

            H("Other parts");
            P("CDImage (arduinocelentano), StbImageSharp / StbImageWriteSharp, the .NET runtime, CommunityToolkit.Mvvm, MahApps IconPacks, Lucide icons and HarmonyOS Sans fonts. "
              + "Their copyrights and full license texts are in THIRD-PARTY-NOTICES.txt.");

            H("License");
            P("Disc Image Studio, disc-probe and HoloStudio3D are free software under the GNU General Public License, version 3 or later (GPL-3.0-or-later). "
              + "You may share and change them under its terms. If you pass this program on, include the license and notices and make the source code available. "
              + "It comes with NO WARRANTY: burning experimental patterns is at your own risk.");

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, BackColor = Theme.Back, Margin = new Padding(0, 8, 0, 20), WrapContents = false };
            foreach ((string caption, string file) in new[] { ("License (GPL-3)", "LICENSE"), ("Third-party notices", "THIRD-PARTY-NOTICES.txt"), ("HoloStudio3D guide", "HOLOSTUDIO3D.md") })
            {
                var b = new LabButton(caption) { Size = new Size(170, 34), BackColor = Theme.Back, Margin = new Padding(0, 0, 8, 0) };
                b.Click += (s, e) => OpenBundledFile(file);
                buttons.Controls.Add(b);
            }
            col.Controls.Add(buttons);

            H("Settings", 4);
            P("HoloStudio3D remembers your last-used settings, pictures, page and window size, and reopens the way you left it.");
            var reset = new LabButton("Reset all settings to defaults") { Size = new Size(240, 34), BackColor = Theme.Back, Margin = new Padding(0, 4, 0, 24) };
            reset.Click += (s, e) => ResetSettings();
            col.Controls.Add(reset);
            return page;
        }

        private void OpenLink(string url)
        {
            // Opens in whatever browser Windows has set as the default.
            Safe(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        }

        private void OpenBundledFile(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path))
            {
                MessageBox.Show(this, name + " was not found next to HoloStudio3D.exe.\n\nIt is in the setup folder and in HoloStudio3D-App after a build.", "HoloStudio3D", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Safe(() => Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = false }));
        }

        // ───────────── help page ─────────────

        private Panel BuildHelpPage()
        {
            var page = new Panel { BackColor = Theme.Back, AutoScroll = true };
            _pages["help"] = page;
            var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(28, 22), BackColor = Theme.Back };
            page.Controls.Add(col);
            void H(string text) => col.Controls.Add(new Label { UseMnemonic = false, Text = text, Font = Theme.UI(11f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 18, 0, 6) });
            void P(string text, Color? color = null) => col.Controls.Add(new Label { UseMnemonic = false, Text = text, ForeColor = color ?? Color.FromArgb(198, 207, 220), AutoSize = true, MaximumSize = new Size(860, 0), Margin = new Padding(0, 0, 0, 6) });
            H("Magic Eye-style picture  (works on CD or DVD)");
            P("1.  Pick a shape, a depth picture (white = near) or a 3D model, then click Save PNG.");
            P("2.  In Disc Image Studio: new project with your disc preset → Import images → choose the PNG. Keep scale 100% and position 0, 0. Leave brightness in the middle.");
            P("3.  Export an ISO and burn it with ImgBurn (or use the app's Burn page).");
            P("4.  Viewing: hold the disc 30–40 cm away and look through it, as if at something far behind it. The two guide dots near the top turn into three, and the shape floats. On the disc itself it looks like plain gray noise, which is expected.");
            H("Rainbow test disc & tilt-image discs  (DVD, experimental)");
            P("1.  Make the .iso here. It's a complete raw DVD image with no file system on purpose, so it won't open in Explorer.");
            P("2.  Burn it as a disc image to a blank DVD±R: ImgBurn → Write image file to disc, or right-click the .iso → Burn disc image. Don't copy it onto a data disc as a file.");
            P("3.  Viewing: one small bright light (phone flashlight) in a dark room. Turn the disc so the area you're checking is at 12 or 6 o'clock, start at the mirror reflection, and tilt slowly left and right. Working patterns light up in a clean colour band at roughly the angle in the key.");
            P("4.  Ignore the big rainbow that runs from the centre outward. That's the normal DVD track rainbow.");
            P("The bytes written are verified exactly. What isn't known yet is how strongly your burner and dye turn them into colour; that's what the test disc is for. Note which ring numbers glow and use those patterns for tilt images.", Theme.Warn);
            H("Hologram mode  (DVD, experimental)");
            P("1.  Choose what to show: a picture plus its depth map (the same pair you'd use for a lenticular), a 3D model (.obj or .stl), several pictures (lenticular frames, left-most first), a side-by-side 3D picture (Convert3D works), or a left and a right picture. Simple, bold subjects work best.");
            P("2.  Pick how many views (2–7) for a picture + depth or a model. 2 is a classic stereo pair and the brightest. More views let the picture turn as you move your head, but each one is dimmer. The Lab builds every view itself and saves them next to the .iso as -views.png.");
            P("3.  Each view is drawn with its own pattern, spaced to match how far apart your eyes are at your viewing distance, and interleaved in a fine grid at 12 o'clock.");
            P("4.  Burn the .iso as a disc image. Hold the disc upright with the picture at the top, one small light off to the side. Close one eye, then the other: each eye should see its own view. Then open both, and with more views, move your head slowly left and right.");
            P("5.  Test tiles at 6 o'clock (turn the disc to read them): each holds an L for the left eye and an R for the right. With 2 views, the tile where each eye sees only its own letter is your best pattern pair; pick it under Pattern pair and burn again. If the depth looks inside-out, tick Swap eyes.");
            H("Why the 3D-looking \"rays\" happen");
            P("The tracks are thousands of tiny concentric circles. Each one reflects a glint toward your eye, and as you move, the glints slide along their circles. Your eyes read that as a beam of light reaching into the disc: small circles near the centre look shallow, big ones near the edge look deep.");
            return page;
        }

        // ───────────── long jobs ─────────────

        private void StartJob(string kind)
        {
            if (_jobTask != null && !_jobTask.IsCompleted) return;
            string folder = Folder;
            string name = kind == "test" ? _testName.Text : kind == "pair" ? _pairName.Text : kind == "patches" ? "hologram-test-patches" : _tiltName.Text;
            string preset = Preset;
            TiltRequest tilt = kind == "tilt" ? TiltRequest() : null;
            if (kind == "tilt" && tilt.Layers.Count == 0) return;
            StereoPairRequest pair = kind == "pair" || kind == "patches" ? PairRequest() : null;
            if (kind == "pair" && !PairReady()) return;
            if (kind == "patches") pair.TestPatches = true;

            _jobCancelSource = new CancellationTokenSource();
            CancellationToken token = _jobCancelSource.Token;
            _jobOutput = null;
            _jobTitle.Text = kind == "test" ? "Rainbow test disc" : kind == "pair" ? "Hologram disc" : kind == "patches" ? "Hologram test-patch disc" : "Tilt-image disc";
            _jobStatus.Text = "Starting…";
            _jobStatus.ForeColor = Theme.Muted;
            _jobProgress.Solid = null;
            _jobProgress.Value = 0;
            _jobCancel.Visible = true; _jobShow.Visible = false; _jobClose.Visible = false;
            _jobBar.Visible = true;
            LayoutJobBar();
            _testStart.Enabled = false; _tiltStart.Enabled = false; _pairStart.Enabled = false; _pairPatches.Enabled = false;

            Action<double, string> progress = (fraction, text) => BeginInvokeSafe(() =>
            {
                _jobProgress.Value = fraction;
                _jobStatus.Text = text.Trim();
            });
            Action<string> log = line => { };
            _jobTask = Task.Run(() => kind == "test" ? _backend.MakeTestDisc(preset, folder, name, progress, log, token)
                    : kind == "pair" || kind == "patches" ? _backend.MakeStereoPairDisc(pair, folder, name, progress, log, token)
                    : _backend.MakeTiltDisc(tilt, folder, name, progress, log, token))
                .ContinueWith(t => BeginInvokeSafe(() => FinishJob(t)), TaskScheduler.Default);
        }

        private void FinishJob(Task<string> task)
        {
            _jobCancel.Visible = false;
            _jobClose.Visible = true;
            if (task.Status == TaskStatus.RanToCompletion)
            {
                _jobOutput = task.Result;
                _jobTitle.Text = Path.GetFileName(task.Result);
                _jobStatus.Text = "Finished and verified. Burn the .iso as a disc image.";
                _jobStatus.ForeColor = Theme.Ok;
                _jobProgress.Solid = Theme.Ok;
                _jobProgress.Value = 1;
                _jobShow.Visible = true;
            }
            else
            {
                Exception ex = task.Exception?.GetBaseException();
                bool cancelled = task.IsCanceled || ex is OperationCanceledException;
                _jobStatus.Text = cancelled ? "Cancelled. The unfinished image was deleted." : "⚠ " + (ex?.Message ?? "Failed.");
                _jobStatus.ForeColor = cancelled ? Theme.Muted : Theme.Bad;
                _jobProgress.Solid = cancelled ? Theme.Line2 : Theme.Bad;
            }
            LayoutJobBar();
            _testStart.Enabled = true;
            _tiltStart.Enabled = _layers.Any(l => l != null);
            _pairStart.Enabled = PairReady();
            _pairPatches.Enabled = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_mTask != null && !_mTask.IsCompleted)
            {
                if (MessageBox.Show(this, "disc-probe is still running. Stop it and close?", "HoloStudio3D",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _mCancelSource.Cancel();
                try { _mTask.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
            }
            if (_jobTask != null && !_jobTask.IsCompleted)
            {
                if (MessageBox.Show(this, "A disc image is still being made. Stop it and close?", "HoloStudio3D",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _jobCancelSource.Cancel();
                try { _jobTask.Wait(TimeSpan.FromSeconds(15)); } catch (AggregateException) { }
            }
            if (!_resetting) RememberNow();
            base.OnFormClosing(e);
        }

        // ───────────── helpers ─────────────

        private void BeginInvokeSafe(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { /* window closing (includes ObjectDisposedException) */ }
        }

        private void Safe(Action action)
        {
            try { action(); } catch (Exception ex) { Error(ex); }
        }

        private void Error(Exception ex) =>
            MessageBox.Show(this, ex.Message, "HoloStudio3D", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private static string Crlf(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        private static void Replace(ref Bitmap field, Bitmap value)
        {
            Bitmap old = field;
            field = value;
            old?.Dispose();
        }

        internal static Bitmap ToBitmap(LabImage image)
        {
            var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < image.Height; y++)
                    Marshal.Copy(image.Bgra, y * image.Width * 4, IntPtr.Add(data.Scan0, y * data.Stride), image.Width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        // ───────────── remembering last-used settings ─────────────
        // Every slider, switch, tick box, list and name box is saved to settings.txt (as "ui.<field>")
        // a moment after it changes and again on close, along with the pictures that were loaded and
        // the window position. Logs, command boxes and the read-only key boxes are left out.

        private static readonly HashSet<string> NotRemembered = new HashSet<string> { "_mLog", "_mCommand", "_testKey", "_tiltKey", "_pairKey" };

        private IEnumerable<KeyValuePair<string, Control>> RememberedControls()
        {
            foreach (FieldInfo f in typeof(LabWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).OrderBy(f => f.MetadataToken))
            {
                if (NotRemembered.Contains(f.Name)) continue;
                object value = f.GetValue(this);
                if (value is Control c && Remembers(c)) yield return new KeyValuePair<string, Control>("ui." + f.Name, c);
                else if (value is Control[] array)
                    for (int i = 0; i < array.Length; i++)
                        if (array[i] != null && Remembers(array[i])) yield return new KeyValuePair<string, Control>("ui." + f.Name + "." + i, array[i]);
            }
        }

        private static bool Remembers(Control c) => c is Segmented || c is Slider || c is CheckBox || c is ComboBox || c is TextBox;

        private static string ReadControl(Control c)
        {
            switch (c)
            {
                case Segmented s: return s.Value;
                case Slider s: return s.Value.ToString("R", CultureInfo.InvariantCulture);
                case CheckBox b: return b.Checked ? "1" : "0";
                case ComboBox b: return b.SelectedIndex < 0 ? null : b.GetItemText(b.SelectedItem);
                case TextBox b: return b.Text.Replace("\r", " ").Replace("\n", " ");
                default: return null;
            }
        }

        private static void WriteControl(Control c, string value)
        {
            switch (c)
            {
                case Segmented s: if (s.Value != value) s.Choose(value); break;
                case Slider s:
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) s.Value = d;
                    break;
                case CheckBox b: b.Checked = value == "1"; break;
                case ComboBox b:
                    for (int i = 0; i < b.Items.Count; i++)
                        if (b.GetItemText(b.Items[i]) == value) { b.SelectedIndex = i; break; }
                    break;
                case TextBox b: b.Text = value; break;
            }
        }

        /// <summary>Puts every control back the way it was last time.</summary>
        private void RestoreControls()
        {
            _restoring = true;
            try
            {
                List<KeyValuePair<string, Control>> all = RememberedControls().ToList();
                // Switches first (they show/hide parts of a page), then tick boxes, lists and sliders,
                // then the pictures, and the name boxes last because loading a picture renames the output.
                foreach (Func<Control, bool> stage in new Func<Control, bool>[] { c => c is Segmented, c => c is CheckBox || c is ComboBox, c => c is Slider })
                    foreach (KeyValuePair<string, Control> p in all.Where(p => stage(p.Value)))
                    {
                        if (_settings.TryGetValue(p.Key, out string v)) Quietly(() => WriteControl(p.Value, v));
                        // A list whose labels changed (e.g. angles that depend on viewing distance): fall back to its position.
                        if (p.Value is ComboBox box && v != null && ReadControl(box) != v
                            && int.TryParse(Setting(p.Key + "#", ""), out int index) && index >= 0 && index < box.Items.Count)
                            Quietly(() => box.SelectedIndex = index);
                    }

                if (int.TryParse(Setting("ui._seed", ""), out int seed) && seed > 0) _seed = seed;
                if (_source.Value != "demo") RestoreFile("file.stereo", _stDrop);
                RestoreFile("file.pairSbs", _pairSbs);
                RestoreFile("file.pairLeft", _pairLeft);
                RestoreFile("file.pairRight", _pairRight);
                RestoreFile("file.pairPicture", _pairColor);
                RestoreFile("file.pairDepth", _pairDepth);
                RestoreFile("file.pairModel", _pairModel);
                string[] frames = Setting("file.pairFrames", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray();
                if (frames.Length >= 2) Quietly(() => _pairFrames.ChooseMany(frames));
                for (int i = 0; i < _layers.Length; i++)
                {
                    string path = Setting("file.tilt" + i, "");
                    if (path.Length > 0 && File.Exists(path)) Quietly(() => SetLayer(i, path));
                }

                foreach (KeyValuePair<string, Control> p in all.Where(p => p.Value is TextBox))
                    if (_settings.TryGetValue(p.Key, out string v) && v.Length > 0) Quietly(() => WriteControl(p.Value, v));
            }
            finally { _restoring = false; }
        }

        private void RestoreFile(string key, DropZone zone)
        {
            string path = Setting(key, "");
            if (path.Length > 0 && File.Exists(path)) Quietly(() => zone.Choose(path));
        }

        /// <summary>A setting that no longer fits (a moved file, a changed list) is skipped, never fatal.</summary>
        private static void Quietly(Action action)
        {
            try { action(); }
            catch (Exception) { }
        }

        private void HookRemember()
        {
            foreach (KeyValuePair<string, Control> p in RememberedControls())
            {
                switch (p.Value)
                {
                    case Segmented s: s.Changed += (o, e) => QueueSave(); break;
                    case Slider s: s.Changed += (o, e) => QueueSave(); break;
                    case CheckBox b: b.CheckedChanged += (o, e) => QueueSave(); break;
                    case ComboBox b: b.SelectedIndexChanged += (o, e) => QueueSave(); break;
                    case TextBox b: b.TextChanged += (o, e) => QueueSave(); break;
                }
            }
            foreach (DropZone zone in new[] { _stDrop, _pairSbs, _pairLeft, _pairRight, _pairColor, _pairDepth, _pairModel })
                zone.FileChosen += path => QueueSave();
            ResizeEnd += (s, e) => QueueSave();
        }

        private void QueueSave()
        {
            if (_restoring || _resetting) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Writes the current state of every control to settings.txt.</summary>
        private void RememberNow()
        {
            if (_restoring || _resetting) return;
            foreach (KeyValuePair<string, Control> p in RememberedControls())
            {
                string v = ReadControl(p.Value);
                if (v == null) _settings.Remove(p.Key); else _settings[p.Key] = v;
                if (p.Value is ComboBox box) _settings[p.Key + "#"] = box.SelectedIndex.ToString(CultureInfo.InvariantCulture);
            }
            _settings["ui._seed"] = _seed.ToString(CultureInfo.InvariantCulture);
            void Keep(string key, string path) { if (string.IsNullOrEmpty(path)) _settings.Remove(key); else _settings[key] = path; }
            Keep("file.stereo", _source.Value == "demo" ? null : _stFile);
            Keep("file.pairSbs", _pairSbsFile);
            Keep("file.pairLeft", _pairLeftFile);
            Keep("file.pairRight", _pairRightFile);
            Keep("file.pairPicture", _pairColorFile);
            Keep("file.pairDepth", _pairDepthFile);
            Keep("file.pairModel", _pairModelFile);
            Keep("file.pairFrames", _pairFrameFiles == null ? null : string.Join("|", _pairFrameFiles));
            for (int i = 0; i < _layers.Length; i++) Keep("file.tilt" + i, _layers[i]?.File);

            Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (WindowState != FormWindowState.Minimized || r.Width > 0)
                _settings["win"] = string.Join(",", r.X, r.Y, r.Width, r.Height, WindowState == FormWindowState.Maximized ? 1 : 0);
            SaveSettings();
        }

        private void RestoreWindow()
        {
            string[] parts = Setting("win", "").Split(',');
            if (parts.Length != 5 || !parts.All(x => int.TryParse(x, out _))) return;
            int[] n = parts.Select(int.Parse).ToArray();
            var r = new Rectangle(n[0], n[1], Math.Max(MinimumSize.Width, n[2]), Math.Max(MinimumSize.Height, n[3]));
            // Only if it is still on a screen (a monitor may have been unplugged since).
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(1, r.Width - 80), 40)))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
            if (n[4] == 1) WindowState = FormWindowState.Maximized;
        }

        /// <summary>Forgets every remembered setting and reopens the window with the defaults.</summary>
        private void ResetSettings()
        {
            if (MessageBox.Show(this, "Put every setting back to its default and reopen HoloStudio3D?\n\nYour output folder and disc preset choice are kept. Your measured presets and your files are not touched.",
                    "HoloStudio3D", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _resetting = true;
            _saveTimer.Stop();
            foreach (string key in _settings.Keys.Where(k => k.StartsWith("ui.", StringComparison.Ordinal) || k.StartsWith("file.", StringComparison.Ordinal) || k == "win").ToList())
                _settings.Remove(key);
            SaveSettings();
            try { Application.Restart(); }
            catch (Exception) { Close(); }
        }

        private string Setting(string key, string fallback) => _settings.TryGetValue(key, out string v) && v.Length > 0 ? v : fallback;

        private string SettingsFile => Path.Combine(_backend.SettingsFolder, "settings.txt");

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) _settings[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(_backend.SettingsFolder);
                File.WriteAllLines(SettingsFile, _settings.Select(p => p.Key + "=" + p.Value).ToArray());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The rainbow-disc logo in the sidebar.</summary>
    internal sealed class BrandMark : Control
    {
        public BrandMark() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Theme.Side; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Side);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            for (int i = 0; i < 360; i += 3)
                using (var b = new SolidBrush(Hue(i / 360.0))) g.FillPie(b, r, i - 90, 4);
            int inset = Width * 32 / 100;
            using (var b = new SolidBrush(Theme.Side)) g.FillEllipse(b, inset, inset, Width - 2 * inset, Height - 2 * inset);
            using (var p = new Pen(Color.FromArgb(200, 210, 225), 2f)) g.DrawEllipse(p, inset, inset, Width - 2 * inset, Height - 2 * inset);
        }

        private static Color Hue(double h)
        {
            double r = Math.Abs(h * 6 - 3) - 1, gr = 2 - Math.Abs(h * 6 - 2), b = 2 - Math.Abs(h * 6 - 4);
            int C(double v) => (int)(255 * (0.45 + 0.55 * Math.Max(0, Math.Min(1, v))));
            return Color.FromArgb(C(r), C(gr), C(b));
        }
    }
}
