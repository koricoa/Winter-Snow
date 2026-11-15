using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

sealed class SnowWindow : Form
{
    sealed class Flake
    {
        public float X, Y, Size, Speed, Phase;
    }

    readonly Random random = new Random();
    readonly Flake[] flakes;
    readonly Timer animation = new Timer { Interval = 33 };
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem pause;
    public event Action SnowChanged;
    public event Action OpenControls;
    public bool SnowEnabled { get { return animation.Enabled; } }
    readonly bool selfTest;
    int cleaner;
    sealed class Spray { public float X, Y, Drift; public int Age; }
    readonly System.Collections.Generic.List<Spray> spray = new System.Collections.Generic.List<Spray>();
    readonly System.Collections.Generic.List<Rectangle> covered = new System.Collections.Generic.List<Rectangle>();
    const int SnowCell = 8;
    readonly Rectangle plowLane;
    readonly int[] snowbank;
    int ticksUntilPlow = 300;
    int shellCheck;
    float truckX = -54;
    bool plowing;
    bool plowEnabled = true;
    public int SpeedPercent { get; set; }

    public void SelectCleaner(int index)
    {
        cleaner = index;
        truckX = -54;
        plowing = false;
        ticksUntilPlow = 1;
        spray.Clear();
        Invalidate();
    }

    public void SetPlowEnabled(bool enabled)
    {
        plowEnabled = enabled;
        if (!enabled) {
            plowing = false;
            truckX = -54;
            spray.Clear();
        } else {
            ticksUntilPlow = 1;
        }
        Invalidate();
    }

    delegate bool WindowVisitor(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor callback, IntPtr state);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);

    static bool IsDesktopClass(string name)
    {
        return name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" ||
            name == "Shell_SecondaryTrayWnd";
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            // WS_EX_LAYERED, WS_EX_TRANSPARENT and WS_EX_NOACTIVATE.
            parameters.ExStyle |= 0x80000 | 0x20 | 0x08000000;
            return parameters;
        }
    }

    SnowWindow(Icon icon, bool selfTest)
    {
        this.selfTest = selfTest;
        SpeedPercent = 100;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        Rectangle primary = Screen.PrimaryScreen.Bounds;
        int snowLine = work.Bottom < primary.Bottom ? work.Bottom : primary.Bottom - 4;
        plowLane = new Rectangle(primary.Left - Left, snowLine - Top, primary.Width, 1);
        snowbank = new int[(plowLane.Width + SnowCell - 1) / SnowCell];
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = BackColor;
        DoubleBuffered = true;
        flakes = Enumerable.Range(0, 100).Select(i => new Flake {
            X = (float)random.NextDouble() * Width,
            Y = (float)random.NextDouble() * Height,
            Size = 4 + (float)random.NextDouble() * 6,
            Speed = 0.7f + (float)random.NextDouble() * 2,
            Phase = (float)random.NextDouble() * 6.28f
        }).ToArray();

        menu.Items.Add("Открыть управление", null, (s, e) => { if (OpenControls != null) OpenControls(); });
        pause = (ToolStripMenuItem)menu.Items.Add("Включить снег", null, (s, e) => SetSnow(!SnowEnabled));
        menu.Items.Add("Выход", null, (s, e) => Close());
        tray = new NotifyIcon { Text = "Winter Snow", Icon = icon,
            ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (s, e) => { if (OpenControls != null) OpenControls(); };
        animation.Tick += (s, e) => {
            foreach (Flake flake in flakes) {
                flake.Y += flake.Speed * SpeedPercent / 100f;
                flake.Phase += 0.035f;
                float x = FlakeX(flake);
                bool landed = x >= plowLane.Left && x < plowLane.Right && flake.Y >= plowLane.Top;
                if (landed) {
                    int cell = (int)(x - plowLane.Left) / SnowCell;
                    snowbank[cell] = Math.Min(16, snowbank[cell] + 2);
                }
                if (landed || flake.Y > ClientSize.Height) {
                    flake.Y = -flake.Size;
                    flake.X = (float)random.NextDouble() * ClientSize.Width;
                }
            }
            AdvancePlow();
            if (++shellCheck == 3) {
                shellCheck = 0;
                if (!selfTest) UpdateDesktopVisibility();
            }
            Invalidate();
        };
    }

    static float FlakeX(Flake flake)
    {
        return flake.X + (float)Math.Sin(flake.Phase) * 10;
    }

    void UpdateDesktopVisibility()
    {
        covered.Clear();
        EnumWindows((candidate, state) => {
            if ((IsHandleCreated && candidate == Handle) || !IsWindowVisible(candidate) || IsIconic(candidate))
                return true;
            var name = new System.Text.StringBuilder(128);
            GetClassName(candidate, name, name.Capacity);
            if (IsDesktopClass(name.ToString())) return true;
            int cloaked;
            if (DwmGetWindowAttribute(candidate, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            NativeRect rect;
            if (GetWindowRect(candidate, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                covered.Add(Rectangle.FromLTRB(rect.Left - Left, rect.Top - Top, rect.Right - Left, rect.Bottom - Top));
            return true;
        }, IntPtr.Zero);
    }

    void AdvancePlow()
    {
        foreach (Spray particle in spray) particle.Age++;
        spray.RemoveAll(particle => particle.Age > 24);
        if (!plowEnabled) return;
        if (!plowing) {
            if (--ticksUntilPlow > 0) return;
            plowing = true;
        }
        int previousCell = (int)Math.Floor((truckX + 54) / SnowCell);
        truckX += cleaner == 0 ? 1.8f : 5f;
        int nextCell = (int)Math.Floor((truckX + 54) / SnowCell);
        int collected = 0;
        for (int cell = Math.Max(0, previousCell); cell <= nextCell && cell < snowbank.Length; cell++) {
            collected += snowbank[cell];
            snowbank[cell] = 0;
        }
        if (cleaner == 0 && collected > 0) {
            for (int particle = 0; particle < Math.Min(8, collected + 2); particle++)
                spray.Add(new Spray { X = plowLane.Left + truckX + 49,
                    Y = plowLane.Top - 38, Drift = 1.8f + (float)random.NextDouble() * 2.3f });
        }
        if (truckX >= plowLane.Width) {
            truckX = -54;
            ticksUntilPlow = 540;
            plowing = false;
        }
    }

    static void DrawFlake(Graphics graphics, float x, float y, float radius, float angle)
    {
        for (int arm = 0; arm < 6; arm++) {
            double direction = angle + arm * Math.PI / 3;
            float dx = (float)Math.Cos(direction), dy = (float)Math.Sin(direction);
            graphics.DrawLine(Pens.White, x, y, x + dx * radius, y + dy * radius);
            float branchX = x + dx * radius * 0.62f, branchY = y + dy * radius * 0.62f;
            for (int side = -1; side <= 1; side += 2) {
                double branch = direction + side * Math.PI / 3;
                graphics.DrawLine(Pens.White, branchX, branchY,
                    branchX + (float)Math.Cos(branch) * radius * 0.34f,
                    branchY + (float)Math.Sin(branch) * radius * 0.34f);
            }
        }
    }

    static void DrawPlow(Graphics graphics, float x, float y)
    {
        graphics.FillRectangle(Brushes.DarkOrange, x + 2, y - 20, 38, 12);
        graphics.FillRectangle(Brushes.Orange, x + 24, y - 30, 16, 13);
        graphics.FillRectangle(Brushes.LightSkyBlue, x + 27, y - 28, 11, 8);
        graphics.FillRectangle(Brushes.Gold, x + 27, y - 34, 6, 4);
        graphics.FillEllipse(Brushes.DarkSlateGray, x + 5, y - 12, 12, 12);
        graphics.FillEllipse(Brushes.DarkSlateGray, x + 25, y - 12, 12, 12);
        graphics.FillPolygon(Brushes.Gold, new[] {
            new PointF(x + 39, y - 12), new PointF(x + 44, y - 16),
            new PointF(x + 54, y), new PointF(x + 42, y)
        });
    }

    public void SetSnow(bool enabled)
    {
        if (enabled) {
            if (!selfTest) UpdateDesktopVisibility();
            Show();
            if (plowEnabled && !plowing) ticksUntilPlow = 1;
            animation.Start();
        }
        else { animation.Stop(); Hide(); }
        pause.Text = enabled ? "Выключить снег" : "Включить снег";
        if (SnowChanged != null) SnowChanged();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        foreach (Rectangle window in covered) e.Graphics.ExcludeClip(window);
        foreach (Flake flake in flakes)
            DrawFlake(e.Graphics, FlakeX(flake), flake.Y, flake.Size, flake.Phase * 0.12f);
        for (int cell = 0; cell < snowbank.Length; cell++) {
            int height = snowbank[cell];
            if (height == 0) continue;
            e.Graphics.FillEllipse(Brushes.White, plowLane.Left + cell * SnowCell - 2,
                plowLane.Top - height, SnowCell + 4, height + 4);
        }
        foreach (Spray particle in spray) {
            float x = particle.X + particle.Drift * particle.Age;
            float y = particle.Y - particle.Age * 3.6f + particle.Age * particle.Age * 0.19f;
            e.Graphics.FillEllipse(Brushes.AliceBlue, x, y, 3, 3);
        }
        if (plowing) {
            float x = plowLane.Left + truckX, y = plowLane.Top;
            if (cleaner == 1) DrawPlow(e.Graphics, x, y);
            else {
                DrawOperator(e.Graphics, x, y, truckX);
                DrawSnowBlower(e.Graphics, x, y);
            }
        }
    }

    static void DrawOperator(Graphics g, float x, float y, float distance)
    {
        float stride = (float)Math.Sin(distance * 0.10f);
        float bounce = Math.Abs(stride) * 0.75f;
        float farFoot = x - 11 - stride * 5;
        float nearFoot = x - 11 + stride * 5;
        float farGround = y - Math.Max(0, stride) * 3;
        float nearGround = y + Math.Min(0, stride) * 3;

        using (var farLeg = new Pen(Color.MidnightBlue, 5))
        using (var nearLeg = new Pen(Color.RoyalBlue, 5))
        using (var sleeve = new Pen(Color.DodgerBlue, 5))
        {
            g.DrawLine(farLeg, x - 11, y - 17 - bounce, farFoot, farGround - 3);
            g.FillRectangle(Brushes.DarkSlateGray, farFoot - 2, farGround - 4, 7, 4);
            g.DrawLine(nearLeg, x - 10, y - 17 - bounce, nearFoot, nearGround - 3);
            g.FillRectangle(Brushes.Black, nearFoot - 2, nearGround - 4, 8, 4);
            g.FillRectangle(Brushes.DodgerBlue, x - 17, y - 35 - bounce, 13, 19);
            g.DrawLine(sleeve, x - 8, y - 32 - bounce, x + 3, y - 29);
            g.DrawLine(sleeve, x + 3, y - 29, x + 10, y - 30);
        }
        float headY = y - 49 - bounce;
        g.FillEllipse(Brushes.Moccasin, x - 15, headY + 3, 14, 12);
        g.FillRectangle(Brushes.BurlyWood, x - 15, headY + 8, 2, 3);
        g.FillEllipse(Brushes.Moccasin, x - 3, headY + 8, 4, 3);
        g.FillEllipse(Brushes.Crimson, x - 17, headY - 4, 16, 9);
        g.FillRectangle(Brushes.Firebrick, x - 17, headY + 2, 17, 3);
        g.FillRectangle(Brushes.White, x - 11, headY + 6, 4, 3);
        g.FillRectangle(Brushes.White, x - 5, headY + 6, 3, 3);
        g.FillRectangle(Brushes.DarkSlateGray, x - 9, headY + 6, 2, 3);
        g.FillRectangle(Brushes.DarkSlateGray, x - 4, headY + 6, 2, 3);
        g.DrawLine(Pens.Sienna, x - 7, headY + 12, x - 4, headY + 12);
        g.FillRectangle(Brushes.OrangeRed, x - 16, y - 35 - bounce, 13, 4);
        g.FillRectangle(Brushes.Moccasin, x + 8, y - 32, 4, 4);
    }

    static void DrawSnowBlower(Graphics g, float x, float y)
    {
        g.DrawLine(Pens.DimGray, x + 29, y - 12, x + 13, y - 30);
        g.DrawLine(Pens.DarkOrange, x + 9, y - 30, x + 16, y - 30);
        g.FillEllipse(Brushes.Black, x + 24, y - 15, 15, 15);
        g.FillEllipse(Brushes.OrangeRed, x + 28, y - 11, 7, 7);
        g.FillRectangle(Brushes.DarkOrange, x + 25, y - 22, 20, 9);
        g.FillPolygon(Brushes.DarkSlateGray, new[] {
            new PointF(x + 36, y - 20), new PointF(x + 46, y - 39),
            new PointF(x + 52, y - 36), new PointF(x + 43, y - 16)
        });
        g.FillRectangle(Brushes.Black, x + 39, y - 15, 15, 13);
        g.DrawArc(Pens.Orange, x + 41, y - 13, 11, 10, 0, 280);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        animation.Dispose();
        tray.Dispose();
        menu.Dispose();
        base.OnFormClosed(e);
    }

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        using (var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
        using (var window = new SnowWindow(icon, args.Contains("--self-test")))
        using (var controls = new SnowControls(window, icon))
        {
            if (!args.Contains("--self-test")) { Application.Run(controls); return 0; }
            float initialY = window.flakes[0].Y;
            bool passed = false;
            using (var test = new Timer { Interval = 400 })
            {
                controls.Shown += (s, e) => {
                    controls.EnableSnow.PerformClick();
                    test.Start();
                };
                test.Tick += (s, e) => {
                    test.Stop();
                    bool moving = initialY != window.flakes[0].Y && window.Visible;
                    controls.DisableSnow.PerformClick();
                    bool paused = !window.Visible && !window.animation.Enabled;
                    controls.EnableSnow.PerformClick();
                    window.snowbank[0] = 10;
                    controls.PlowEnabled.Checked = false;
                    window.AdvancePlow();
                    bool plowStopped = !window.plowing && window.snowbank[0] == 10;
                    controls.PlowEnabled.Checked = true;
                    window.AdvancePlow();
                    bool plowCleared = window.plowing && window.snowbank[0] == 0 && window.spray.Count > 0;
                    controls.Cleaner.SelectedIndex = 1;
                    window.snowbank[0] = 10;
                    window.AdvancePlow();
                    bool truckCleared = window.cleaner == 1 && window.snowbank[0] == 0 && window.spray.Count == 0;
                    controls.Speed.Value = 175;
                    passed = moving && paused && window.Visible && window.animation.Enabled &&
                        plowStopped && plowCleared && truckCleared && window.SpeedPercent == 175 &&
                        SnowControls.NewYearCountdown(new DateTime(2026, 12, 31, 23, 59, 58)) ==
                            "До Нового года · 0 д. 00:00:02" &&
                        SnowControls.NewYearCountdown(new DateTime(2027, 1, 1)) ==
                            "До Нового года · 365 д. 00:00:00" &&
                        IsDesktopClass("Progman") && !IsDesktopClass("CabinetWClass");
                    controls.Close();
                };
                Application.Run(controls);
            }
            return passed ? 0 : 1;
        }
    }
}

sealed class SnowControls : Form
{
    public readonly Button EnableSnow;
    public readonly Button DisableSnow;
    public readonly CheckBox PlowEnabled;
    public readonly TrackBar Speed;
    public readonly ComboBox Cleaner;
    readonly Label status;
    readonly Timer countdownTimer = new Timer { Interval = 250 };
    bool closing;
    readonly Color ice = Color.FromArgb(159, 231, 255);

    public SnowControls(SnowWindow snow, Icon icon)
    {
        Text = "Winter Snow";
        Icon = icon;
        ClientSize = new Size(434, 461);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(12, 26, 46);
        ForeColor = Color.White;

        var heading = new Label { Text = "Winter Snow", AutoSize = true,
            Font = new Font("Segoe UI", 25, FontStyle.Bold), Location = new Point(26, 46) };
        var subtitle = new Label { Text = "Снег и снегоуборщик на рабочем столе", AutoSize = true,
            Font = new Font("Segoe UI", 10), ForeColor = ice, Location = new Point(29, 104) };
        var countdown = new Label { Text = NewYearCountdown(DateTime.Now), AutoSize = true,
            Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.PaleTurquoise,
            Location = new Point(29, 130) };
        countdownTimer.Tick += (s, e) => countdown.Text = NewYearCountdown(DateTime.Now);
        countdownTimer.Start();
        status = new Label { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Location = new Point(29, 162) };
        EnableSnow = CreateButton("Включить", 29, Color.FromArgb(188, 239, 228), Color.FromArgb(10, 53, 49));
        DisableSnow = CreateButton("Выключить", 222, Color.FromArgb(40, 64, 95), Color.White);
        EnableSnow.Click += (s, e) => snow.SetSnow(true);
        DisableSnow.Click += (s, e) => snow.SetSnow(false);
        Controls.AddRange(new Control[] { heading, subtitle, countdown, status, EnableSnow, DisableSnow });
        var speedLabel = new Label { Text = "Скорость снега · 100%", AutoSize = true,
            Location = new Point(29, 275), Font = new Font("Segoe UI", 11), ForeColor = ice };
        Speed = new TrackBar { Minimum = 25, Maximum = 250, Value = 100,
            TickFrequency = 25, SmallChange = 5, LargeChange = 25, Location = new Point(24, 305),
            Width = 383, AccessibleName = "Скорость падения снега в процентах" };
        Speed.ValueChanged += (s, e) => {
            snow.SpeedPercent = Speed.Value;
            speedLabel.Text = "Скорость снега · " + Speed.Value + "%";
        };
        PlowEnabled = new CheckBox { Text = "Снегоуборщик подбирает снег", Checked = true,
            AutoSize = true, Location = new Point(29, 365), Font = new Font("Segoe UI", 11) };
        PlowEnabled.CheckedChanged += (s, e) => snow.SetPlowEnabled(PlowEnabled.Checked);
        Cleaner = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(29, 401), Width = 375, Font = new Font("Segoe UI", 11),
            AccessibleName = "Выбор снегоуборщика" };
        Cleaner.Items.AddRange(new object[] { "Человек со снегоуборщиком", "Машина с отвалом" });
        Cleaner.SelectedIndex = 0;
        Cleaner.SelectedIndexChanged += (s, e) => snow.SelectCleaner(Cleaner.SelectedIndex);
        Controls.AddRange(new Control[] { speedLabel, Speed, PlowEnabled, Cleaner });

        Action refresh = () => {
            status.Text = snow.SnowEnabled ? "●  Снег включён на рабочем столе" : "○  Снег выключен";
            status.ForeColor = snow.SnowEnabled ? Color.PaleGreen : ice;
            EnableSnow.Enabled = !snow.SnowEnabled;
            DisableSnow.Enabled = snow.SnowEnabled;
        };
        refresh();
        snow.SnowChanged += refresh;
        snow.OpenControls += () => {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        };
        snow.FormClosed += (s, e) => { if (!closing) { closing = true; Close(); } };
        FormClosed += (s, e) => { if (!closing) { closing = true; snow.Close(); } };
        Paint += (s, e) => {
            e.Graphics.FillRectangle(Brushes.LightSkyBlue, 0, 0, ClientSize.Width, 4);
            e.Graphics.DrawIcon(icon, new Rectangle(ClientSize.Width - 86, 40, 56, 56));
            e.Graphics.FillEllipse(Brushes.White, 22, 28, 3, 3);
            e.Graphics.FillEllipse(Brushes.LightSkyBlue, 312, 38, 4, 4);
            e.Graphics.FillEllipse(Brushes.White, 400, 118, 3, 3);
        };
    }

    internal static string NewYearCountdown(DateTime now)
    {
        DateTime nextYear = new DateTime(now.Year + 1, 1, 1);
        TimeSpan remaining = TimeSpan.FromSeconds(Math.Ceiling((nextYear - now).TotalSeconds));
        return string.Format("До Нового года · {0} д. {1:00}:{2:00}:{3:00}",
            remaining.Days, remaining.Hours, remaining.Minutes, remaining.Seconds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) countdownTimer.Dispose();
        base.Dispose(disposing);
    }

    Button CreateButton(string text, int x, Color background, Color foreground)
    {
        var button = new Button { Text = text, Location = new Point(x, 208),
            Size = new Size(182, 49), FlatStyle = FlatStyle.Flat, BackColor = background,
            ForeColor = foreground, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}

