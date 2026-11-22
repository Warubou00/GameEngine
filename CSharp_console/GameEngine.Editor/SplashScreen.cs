using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GameEngine.Editor;

// ==========================================================================
// v2.2 : スプラッシュ画面 (エディターの起動中に出す)
//   以前はエディターの窓をすぐ出していたので、読み込み (アセット / 描画の準備 / ゲームのコードのビルド / シーン) の間、
//   何も描かれない白い窓が出ていて、何をしているのか分からなかった
//   今 : 起動するとすぐこの画面を出し、今の作業 (段階) と最後のログの行を表示する。エディターの窓は隠したまま作り、
//        最初のフレームを描き終えてから出して (Engine.WindowShown)、この画面を閉じる
//   別のスレッド (STA) で自分のメッセージループを回す (メインのスレッドがビルドなどで止まっていても、動き続けて固まって見えない)
//   出さない : --editor-test / --quit-after / --resave-scenes / --no-splash
// ==========================================================================
internal sealed class SplashScreen : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private SplashForm? _form;

    private SplashScreen(string project)
    {
        _thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            _form = new SplashForm(project);
            _form.Shown += (_, _) => _ready.Set();
            Application.Run(_form);
            _ready.Set();   // 開けなかった時も待たせない
        })
        { IsBackground = true, Name = "Splash screen" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(3000);
    }

    /// <summary>スプラッシュ画面を出す (出せなければ null。エディターはそのまま起動する)</summary>
    public static SplashScreen? Show(string project)
    {
        try { return new SplashScreen(project); }
        catch (Exception ex) { Console.WriteLine($"[Splash] {ex.Message}"); return null; }
    }

    /// <summary>今の段階 (大きい文字)</summary>
    public void SetStatus(string text, float progress)
    {
        if (_form == null) return;
        _form.Status   = text;
        _form.Progress = Math.Clamp(progress, 0.0f, 1.0f);
    }

    /// <summary>最後のログの行 (小さい文字。どのスレッドからでもよい)</summary>
    public void SetDetail(string line)
    {
        if (_form == null || string.IsNullOrWhiteSpace(line)) return;
        _form.Detail = line.Length > 160 ? line[..160] + "…" : line;
    }

    public void Close()
    {
        var form = _form;
        _form = null;
        if (form == null) return;
        try { if (form.IsHandleCreated) form.BeginInvoke(form.Close); } catch (InvalidOperationException) { }
        _thread.Join(2000);
    }

    public void Dispose()
    {
        Close();
        _ready.Dispose();
    }

    // 画面そのもの (枠なし。自分で描く。30 ms ごとに描き直して、進み具合の帯を動かす)
    private sealed class SplashForm : Form
    {
        private readonly string _project;
        private readonly string _version = $"Editor {EditorVersion.Display}";
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
        private readonly Icon? _icon = EngineInfo.ExeIcon();
        private readonly Icon? _bigIcon;
        private readonly DateTime _start = DateTime.Now;
        private float _shownProgress;

        public volatile string Status = "Starting...";
        public volatile string Detail = "";
        public float Progress;

        private static readonly Color Background = Color.FromArgb(30, 31, 36);
        private static readonly Color Accent     = Color.FromArgb(232, 140, 64);   // 糸の色 (アイコンの色に合わせた橙)
        private static readonly Color TextMain   = Color.FromArgb(236, 236, 240);
        private static readonly Color TextSub    = Color.FromArgb(150, 152, 162);

        public SplashForm(string project)
        {
            _project = project;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.CenterScreen;
            ClientSize      = new Size(620, 340);
            BackColor       = Background;
            ShowInTaskbar   = true;   // 読み込み中もタスクバーに出す (他の窓の後ろに隠れても分かる)
            Text            = $"{EngineInfo.Name} Editor";
            if (_icon != null) { Icon = _icon; _bigIcon = new Icon(_icon, 64, 64); }
            DoubleBuffered  = true;
            _timer.Tick += (_, _) => Invalidate();
            _timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _bigIcon?.Dispose(); _icon?.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var w = ClientSize.Width;
            var h = ClientSize.Height;

            // 左の帯と枠
            using (var accent = new SolidBrush(Accent)) g.FillRectangle(accent, 0, 0, 6, h);
            using (var border = new Pen(Color.FromArgb(60, 62, 70))) g.DrawRectangle(border, 0, 0, w - 1, h - 1);

            // アイコン + 名前 + 版
            if (_bigIcon != null) g.DrawIcon(_bigIcon, new Rectangle(40, 44, 64, 64));
            using var title    = new Font("Segoe UI Semibold", 26.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var subtitle = new Font("Segoe UI", 15.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var status   = new Font("Segoe UI", 16.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var small    = new Font("Consolas", 12.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var mainBrush = new SolidBrush(TextMain);
            using var subBrush  = new SolidBrush(TextSub);
            g.DrawString(EngineInfo.Name, title, mainBrush, 120, 46);
            g.DrawString(_version, subtitle, subBrush, 122, 84);

            // プロジェクト
            var format = new StringFormat { Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString($"Project : {_project}", subtitle, subBrush, new RectangleF(40, 150, w - 80, 22), format);

            // 今の段階 + 経過時間
            var elapsed = (DateTime.Now - _start).TotalSeconds;
            g.DrawString(Status, status, mainBrush, new RectangleF(40, 210, w - 160, 24), format);
            var time = $"{elapsed:F1} s";
            var timeSize = g.MeasureString(time, subtitle);
            g.DrawString(time, subtitle, subBrush, w - 40 - timeSize.Width, 212);

            // 進み具合 (段階の値へなめらかに近づける) + 動く光 (止まって見えないように)
            _shownProgress += (Progress - _shownProgress) * 0.15f;
            var bar = new RectangleF(40, 246, w - 80, 4);
            using (var track = new SolidBrush(Color.FromArgb(55, 57, 64))) g.FillRectangle(track, bar);
            using (var fill = new SolidBrush(Accent)) g.FillRectangle(fill, bar.X, bar.Y, bar.Width * _shownProgress, bar.Height);
            var sweep = (float)((elapsed * 0.6) % 1.0);
            var glowX = bar.X + bar.Width * sweep;
            using (var glow = new LinearGradientBrush(new RectangleF(glowX - 60, bar.Y, 120, bar.Height), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Horizontal))
            {
                var blend = new ColorBlend
                {
                    Colors    = [Color.FromArgb(0, 255, 255, 255), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)],
                    Positions = [0.0f, 0.5f, 1.0f],
                };
                glow.InterpolationColors = blend;
                g.FillRectangle(glow, Math.Max(bar.X, glowX - 60), bar.Y, Math.Min(120, bar.Right - Math.Max(bar.X, glowX - 60)), bar.Height);
            }

            // 最後のログの行
            g.DrawString(Detail, small, subBrush, new RectangleF(40, 262, w - 80, 18), format);
            g.DrawString("The editor window opens when loading is done.", small, subBrush, 40, h - 30);
        }
    }
}
