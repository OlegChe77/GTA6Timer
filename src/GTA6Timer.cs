// Таймер обратного отсчёта до выхода GTA VI (19.11.2026).
// Сборка: build.ps1 (компилятор csc из .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new TimerForm());
    }
}

class TimerForm : Form
{
    // Дата выхода: 19 ноября 2026, 00:00 по местному времени.
    static readonly DateTime Release = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Local);
    const int SlideSeconds = 10;
    const int FadeMs = 1200;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "GTA6Countdown";
    const string SettingsKey = @"Software\GTA6Countdown";
    const int Grip = 10;               // ширина зоны у края окна, за которую тянут размер
    const int NewsSeconds = 20;        // смена заголовка
    const int NewsFadeMs = 500;
    const int NewsRefreshMinutes = 5 * 60; // как часто перечитывать RSS (плюс при каждом запуске)
    const int NewsKeep = 150;
    const string MediaPage = "https://www.rockstargames.com/VI/media";
    const string ScreensZip = "https://media-rockstargames-com.akamaized.net/VI/downloads/screenshots/GTAVI_Screenshots.zip";

    // Игровые сайты с RSS. Из лент берутся только новости о GTA.
    static readonly string[][] FeedsRu =
    {
        new[] { "3DNews", "https://3dnews.ru/games/rss/" },
        new[] { "PlayGround.ru", "https://www.playground.ru/rss/news.xml" },
        new[] { "StopGame", "https://stopgame.ru/rss/rss_all.xml" },
        new[] { "Игромания", "https://www.igromania.ru/rss/news.rss" },
    };
    static readonly string[][] FeedsEn =
    {
        new[] { "IGN", "https://feeds.feedburner.com/ign/news" },
        new[] { "GameSpot", "https://www.gamespot.com/feeds/news/" },
        new[] { "Eurogamer", "https://www.eurogamer.net/feed/news" },
        new[] { "GamesRadar+", "https://www.gamesradar.com/rss/" },
        new[] { "VGC", "https://www.videogameschronicle.com/feed/" },
        new[] { "Dexerto", "https://www.dexerto.com/gaming/feed/" },
        new[] { "Rock Paper Shotgun", "https://www.rockpapershotgun.com/feed/news" },
    };
    static readonly Regex GtaRx = new Regex(@"\bGTA|Grand Theft Auto|\bГТА|Vice City|Вайс[- ]?Сити", RegexOptions.IgnoreCase);

    class NewsItem { public DateTime Date; public string Source, Title, Link; }
    List<NewsItem> news = new List<NewsItem>();
    int newsIndex;
    DateTime newsChanged = DateTime.MinValue, nextNews;
    RectangleF newsRect;
    bool newsLoading, newsLoadedOnce;
    readonly Timer newsTimer = new Timer { Interval = NewsRefreshMinutes * 60 * 1000 };
    string NewsCache { get { return Path.Combine(baseDir, english ? "news_cache_en.txt" : "news_cache.txt"); } }

    // Язык интерфейса и ленты новостей (переключается в меню, хранится в реестре).
    bool english;
    string L(string ru, string en) { return english ? en : ru; }

    bool inSizeMove, dragged;

    readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    readonly List<string> images = new List<string>();
    readonly Random rnd = new Random();
    int index = -1;
    volatile string mediaStatus;       // строка о загрузке картинок; null — ничего не показывать

    Bitmap current, previous, logo;
    DateTime fadeStart = DateTime.MinValue;
    bool loading;

    readonly Timer tick = new Timer { Interval = 1000 };
    readonly Timer fade = new Timer { Interval = 33 };
    DateTime nextSlide;

    bool fullscreen;
    Rectangle normalBounds;
    Point dragFrom;

    ToolStripMenuItem miTop, miAuto, miMini;
    MiniTimer mini;   // мини-таймер в углу экрана вместо свёрнутого окна
    bool InMini { get { return mini != null && mini.Visible; } }
    FontFamily fontBig, fontText;

    // ---------- шоу в момент выхода: фейерверк и фанфары ----------
    const float ShowSeconds = 5f;
    const float TextFadeFrom = 4f;      // с этой секунды шоу проявляется надпись «УЖЕ ВЫШЛА»
    const float RocketFlight = 0.6f;
    const int TestHoldSeconds = 5;      // сколько держать надпись после тестового шоу
    // Моменты взрывов (с начала шоу). По ним же в звук вставлены хлопки.
    static readonly float[] BurstTimes = { 0.65f, 1.0f, 1.3f, 1.6f, 1.95f, 2.25f, 2.5f, 2.8f, 3.05f, 3.3f, 3.5f, 3.65f, 3.75f };
    // Цвета Vice City: розовый, оранжевый, фиолетовый.
    static readonly Color[] FireColors =
    {
        Color.FromArgb(255, 45, 150), Color.FromArgb(255, 105, 200), Color.FromArgb(255, 140, 30),
        Color.FromArgb(255, 190, 80), Color.FromArgb(150, 60, 255), Color.FromArgb(200, 95, 255),
    };

    class Spark { public float X, Y, VX, VY, Age, Life, Size; public Color C; public bool Glitter, Flash; }
    class Rocket { public float X0, Y0, X1, Y1, T0; public int Kind; public bool Done; }
    readonly List<Spark> sparks = new List<Spark>();
    readonly List<Rocket> rockets = new List<Rocket>();
    readonly Timer showTimer = new Timer { Interval = 15 };
    readonly Stopwatch showClock = new Stopwatch();
    float showLast;
    bool releaseFired;
    DateTime testEnd = DateTime.MinValue;
    static byte[] fanfareWav;
    System.Media.SoundPlayer player;

    public TimerForm()
    {
        LoadLanguage();
        Text = L("GTA VI — обратный отсчёт", "GTA VI — countdown");
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 720);
        MinimumSize = new Size(480, 270);
        BackColor = Color.Black;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        KeyPreview = true;

        string ico = Path.Combine(baseDir, "app.ico");
        if (File.Exists(ico)) Icon = new Icon(ico);

        fontBig = PickFont("Segoe UI Black", "Arial Black", "Impact");
        fontText = PickFont("Segoe UI Semibold", "Segoe UI", "Arial");

        string logoPath = Path.Combine(baseDir, "logo.png");
        if (File.Exists(logoPath)) logo = new Bitmap(logoPath);

        LoadImageList();

        BuildMenu();

        LoadBounds();
        LoadNewsCache();

        tick.Tick += (s, e) =>
        {
            if (Visible && DateTime.Now >= nextSlide) NextImage(1);
            if (DateTime.Now >= nextNews) ShowNews(newsIndex + 1);
            // Отсчёт дошёл до нуля — шоу (и если программу открыли в первый час после выхода).
            if (!releaseFired && DateTime.Now >= Release)
            {
                releaseFired = true;
                if (DateTime.Now - Release < TimeSpan.FromHours(1)) StartShow(false);
            }
            Invalidate();
            UpdateMini();
        };
        showTimer.Tick += (s, e) => ShowTick();
        fade.Tick += (s, e) =>
        {
            bool slideDone = (DateTime.Now - fadeStart).TotalMilliseconds >= FadeMs;
            bool newsDone = (DateTime.Now - newsChanged).TotalMilliseconds >= NewsFadeMs;
            if (slideDone) DisposePrevious();
            if (slideDone && newsDone) fade.Stop();
            Invalidate();
        };
        newsTimer.Tick += (s, e) => RefreshNews();

        MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragFrom = e.Location; dragged = false; } };
        MouseMove += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && !fullscreen && e.Location != dragFrom)
            {
                Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y);
                dragged = true;
            }
            Cursor = e.Button == MouseButtons.None && CurrentNews() != null && newsRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        };
        MouseClick += (s, e) =>
        {
            var n = CurrentNews();
            if (e.Button == MouseButtons.Left && !dragged && n != null && newsRect.Contains(e.Location) && n.Link.StartsWith("http"))
                try { Process.Start(n.Link); } catch { }
        };
        MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left && !newsRect.Contains(e.Location)) ToggleFullscreen(); };
        MouseWheel += (s, e) => NextImage(e.Delta < 0 ? 1 : -1);
        KeyDown += OnKey;
        Resize += (s, e) =>
        {
            // Свернули в обход WM_SYSCOMMAND (Win+M и т.п.) — тоже в мини-режим.
            if (WindowState == FormWindowState.Minimized) { BeginInvoke(new Action(() => { WindowState = FormWindowState.Normal; EnterMini(); })); return; }
            if (!inSizeMove) ReloadCurrent();
        };
        FormClosing += (s, e) => SaveBounds();
        FormClosed += (s, e) => { if (mini != null) mini.Dispose(); };
        Shown += (s, e) =>
        {
            NextImage(1); ShowNews(0); tick.Start(); newsTimer.Start(); RefreshNews();
            if (images.Count == 0 || logo == null) DownloadMedia();
            if (Environment.GetCommandLineArgs().Contains("--test-show")) StartShow(true);
            if (Environment.GetCommandLineArgs().Contains("--mini")) EnterMini();
        };
    }

    // ---------- размер окна: тянуть за края, пропорции 16:9 ----------

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84, WM_SIZING = 0x214, WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232, WM_SYSCOMMAND = 0x112, SC_MINIMIZE = 0xF020;
        switch (m.Msg)
        {
            case WM_SYSCOMMAND:
                if ((m.WParam.ToInt64() & 0xFFF0) == SC_MINIMIZE) { EnterMini(); return; } // клик по кнопке в панели задач
                break;
            case WM_NCHITTEST:
                base.WndProc(ref m);
                if (fullscreen || (int)m.Result != 1) return;
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                bool l = p.X < Grip, r = p.X >= ClientSize.Width - Grip, t = p.Y < Grip, b = p.Y >= ClientSize.Height - Grip;
                int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 1;
                m.Result = (IntPtr)hit;
                return;
            case WM_SIZING:
                var rc = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                int edge = m.WParam.ToInt32();
                if (edge == 3 || edge == 6) // верх / низ — подгоняем ширину
                    rc.Right = rc.Left + (rc.Bottom - rc.Top) * 16 / 9;
                else
                {
                    int h = (rc.Right - rc.Left) * 9 / 16;
                    if (edge == 4 || edge == 5) rc.Top = rc.Bottom - h; else rc.Bottom = rc.Top + h;
                }
                Marshal.StructureToPtr(rc, m.LParam, false);
                m.Result = (IntPtr)1;
                return;
            case WM_ENTERSIZEMOVE: inSizeMove = true; break;
            case WM_EXITSIZEMOVE: inSizeMove = false; ReloadCurrent(); break;
        }
        base.WndProc(ref m);
    }

    void SetSize(int w)
    {
        LeaveMini();
        if (fullscreen) ToggleFullscreen();
        var c = new Point(Left + Width / 2, Top + Height / 2);
        Bounds = new Rectangle(c.X - w / 2, c.Y - w * 9 / 32, w, w * 9 / 16);
    }

    void LoadBounds()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                var v = k == null ? null : k.GetValue("Bounds") as string;
                if (v == null) return;
                var a = v.Split(',').Select(int.Parse).ToArray();
                var r = new Rectangle(a[0], a[1], a[2], a[2] * 9 / 16);
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r)) && r.Width >= MinimumSize.Width)
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = r;
                }
            }
        }
        catch { }
    }

    void SaveBounds()
    {
        var r = fullscreen ? normalBounds : Bounds;
        if (WindowState != FormWindowState.Normal) return;
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                k.SetValue("Bounds", r.X + "," + r.Y + "," + r.Width);
        }
        catch { }
    }

    // ---------- новости ----------

    NewsItem CurrentNews()
    {
        return news.Count == 0 ? null : news[newsIndex % news.Count];
    }

    void ShowNews(int i)
    {
        nextNews = DateTime.Now.AddSeconds(NewsSeconds);
        if (news.Count == 0) return;
        newsIndex = ((i % news.Count) + news.Count) % news.Count;
        newsChanged = DateTime.Now;
        fade.Start();
    }

    void RefreshNews()
    {
        if (newsLoading) return;
        newsLoading = true;
        bool lang = english;
        Task.Run(() => FetchNews(lang ? FeedsEn : FeedsRu)).ContinueWith(t =>
        {
            newsLoading = false;
            if (lang != english) { RefreshNews(); return; } // пока грузили, язык переключили
            newsLoadedOnce = true;
            if (t.Exception == null) MergeNews(t.Result, true);
            Invalidate();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    static List<NewsItem> FetchNews(string[][] feeds)
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        var result = new List<NewsItem>();
        Parallel.ForEach(feeds, feed =>
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(feed[1]);
                req.UserAgent = "Mozilla/5.0 GTA6Countdown";
                req.Timeout = 20000;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                var doc = new XmlDocument();
                using (var resp = req.GetResponse())
                using (var st = resp.GetResponseStream())
                    doc.Load(st);
                foreach (XmlNode item in doc.SelectNodes("//item"))
                {
                    string title = Clean(Node(item, "title"));
                    if (title.Length == 0 || !GtaRx.IsMatch(title)) continue;
                    DateTimeOffset d;
                    var date = DateTimeOffset.TryParse(Node(item, "pubDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                        ? d.LocalDateTime : DateTime.Now;
                    lock (result) result.Add(new NewsItem { Date = date, Source = feed[0], Title = title, Link = Node(item, "link").Trim() });
                }
            }
            catch { } // сайт недоступен — берём остальные
        });
        return result;
    }

    static string Node(XmlNode item, string name)
    {
        var n = item.SelectSingleNode(name);
        return n == null ? "" : n.InnerText;
    }

    static string Clean(string s)
    {
        s = WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", ""));
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    // Новые новости добавляются к уже накопленным (RSS отдаёт только последние записи).
    void MergeNews(List<NewsItem> fresh, bool save)
    {
        var cur = CurrentNews();
        var all = news.Concat(fresh)
            .GroupBy(n => n.Title.ToLowerInvariant())
            .Select(g => g.OrderBy(n => n.Date).First())
            .OrderByDescending(n => n.Date)
            .Take(NewsKeep).ToList();
        bool wasEmpty = news.Count == 0;
        news = all;
        newsIndex = cur == null ? 0 : Math.Max(0, news.FindIndex(n => n.Title == cur.Title));
        if (wasEmpty && news.Count > 0) ShowNews(0);
        if (save) SaveNewsCache();
    }

    void LoadNewsCache()
    {
        try
        {
            if (!File.Exists(NewsCache)) return;
            var list = new List<NewsItem>();
            foreach (var line in File.ReadAllLines(NewsCache))
            {
                var a = line.Split('\t');
                if (a.Length == 4)
                    list.Add(new NewsItem { Date = new DateTime(long.Parse(a[0])), Source = a[1], Title = a[2], Link = a[3] });
            }
            MergeNews(list, false);
        }
        catch { }
    }

    void SaveNewsCache()
    {
        try
        {
            File.WriteAllLines(NewsCache, news.Select(n => n.Date.Ticks + "\t" + n.Source + "\t" + n.Title.Replace('\t', ' ') + "\t" + n.Link));
        }
        catch { }
    }

    string Ago(DateTime d)
    {
        var span = DateTime.Now - d;
        if (span.TotalMinutes < 1) return L("только что", "just now");
        if (span.TotalMinutes < 60) return (int)span.TotalMinutes + L(" мин назад", " min ago");
        if (span.TotalHours < 24) return (int)span.TotalHours + L(" ч назад", " h ago");
        if (span.TotalDays < 2) return L("вчера", "yesterday");
        return english ? d.ToString("MMMM d", new CultureInfo("en-US")) : d.ToString("d MMMM", new CultureInfo("ru-RU"));
    }

    // По умолчанию русский; английский — если выбран в меню.
    void LoadLanguage()
    {
        string v = null;
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                if (k != null) v = k.GetValue("Language") as string;
        }
        catch { }
        english = v == "en";
    }

    void SetLanguage(bool en)
    {
        if (en == english) return;
        english = en;
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                k.SetValue("Language", en ? "en" : "ru");
        }
        catch { }
        Text = L("GTA VI — обратный отсчёт", "GTA VI — countdown");
        BuildMenu();
        UpdateMini();
        // У каждого языка своя лента и свой кэш новостей.
        news.Clear();
        newsIndex = 0;
        newsLoadedOnce = false;
        LoadNewsCache();
        RefreshNews();
        Invalidate();
    }

    static FontFamily PickFont(params string[] names)
    {
        using (var fonts = new InstalledFontCollection())
            foreach (var n in names)
                if (fonts.Families.Any(f => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    return new FontFamily(n);
        return FontFamily.GenericSansSerif;
    }

    void BuildMenu()
    {
        var old = ContextMenuStrip;
        var menu = new ContextMenuStrip();
        menu.Items.Add(L("Полный экран (F11)", "Fullscreen (F11)"), null, (s, e) => ToggleFullscreen());
        menu.Items.Add(L("Следующая картинка (→)", "Next image (→)"), null, (s, e) => NextImage(1));
        menu.Items.Add(L("Предыдущая картинка (←)", "Previous image (←)"), null, (s, e) => NextImage(-1));
        var miSize = new ToolStripMenuItem(L("Размер окна", "Window size"));
        foreach (var w in new[] { 640, 960, 1280, 1600, 1920 })
        {
            int width = w;
            miSize.DropDownItems.Add(w + " × " + w * 9 / 16, null, (s, e) => SetSize(width));
        }
        menu.Items.Add(miSize);
        menu.Items.Add(L("Следующая новость (N)", "Next headline (N)"), null, (s, e) => ShowNews(newsIndex + 1));
        menu.Items.Add(L("Обновить новости", "Refresh news"), null, (s, e) => RefreshNews());
        menu.Items.Add(L("Тест фейерверка", "Test fireworks"), null, (s, e) => StartShow(true));
        var miLang = new ToolStripMenuItem("Язык / Language");
        miLang.DropDownItems.Add(new ToolStripMenuItem("Русский", null, (s, e) => SetLanguage(false)) { Checked = !english });
        miLang.DropDownItems.Add(new ToolStripMenuItem("English", null, (s, e) => SetLanguage(true)) { Checked = english });
        menu.Items.Add(miLang);
        menu.Items.Add(new ToolStripSeparator());
        miTop = new ToolStripMenuItem(L("Поверх всех окон (T)", "Always on top (T)"), null, (s, e) => { TopMost = !TopMost; miTop.Checked = TopMost; });
        miTop.Checked = TopMost;
        menu.Items.Add(miTop);
        miAuto = new ToolStripMenuItem(L("Запускать вместе с Windows", "Start with Windows"), null, (s, e) => ToggleAutostart());
        miAuto.Checked = IsAutostart();
        menu.Items.Add(miAuto);
        menu.Items.Add(new ToolStripSeparator());
        miMini = new ToolStripMenuItem(L("Свернуть", "Minimize"), null, (s, e) => { if (InMini) LeaveMini(); else EnterMini(); });
        menu.Items.Add(miMini);
        menu.Items.Add(L("Выход (Esc)", "Exit (Esc)"), null, (s, e) => Close());
        // То же меню открывается и на мини-таймере — там пункт возвращает окно.
        menu.Opening += (s, e) => miMini.Text = InMini ? L("Развернуть окно", "Restore window") : L("Свернуть", "Minimize");
        ContextMenuStrip = menu;
        if (mini != null) mini.ContextMenuStrip = menu;
        if (old != null) old.Dispose();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F11: case Keys.F: case Keys.Enter: ToggleFullscreen(); break;
            case Keys.Escape: if (fullscreen) ToggleFullscreen(); else Close(); break;
            case Keys.Right: case Keys.Space: NextImage(1); break;
            case Keys.Left: NextImage(-1); break;
            case Keys.T: TopMost = !TopMost; miTop.Checked = TopMost; break;
            case Keys.N: case Keys.Down: ShowNews(newsIndex + 1); break;
            case Keys.Up: ShowNews(newsIndex - 1); break;
        }
    }

    void ToggleFullscreen()
    {
        LeaveMini();
        if (!fullscreen) { normalBounds = Bounds; Bounds = Screen.FromControl(this).Bounds; }
        else Bounds = normalBounds;
        fullscreen = !fullscreen;
    }

    bool IsAutostart()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(AppName) != null;
    }

    void ToggleAutostart()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (IsAutostart()) k.DeleteValue(AppName, false);
            else k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
        }
        miAuto.Checked = IsAutostart();
    }

    // ---------- мини-режим ----------

    void EnterMini()
    {
        if (InMini) return;
        if (mini == null)
        {
            mini = new MiniTimer { Text = Text, Icon = Icon, ContextMenuStrip = ContextMenuStrip };
            mini.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) LeaveMini(); };
        }
        mini.Area = Screen.FromControl(this).WorkingArea;
        Hide();
        mini.Show();
        UpdateMini();
    }

    void LeaveMini()
    {
        if (!InMini) return;
        mini.Hide();
        Show();
        Activate();
        ReloadCurrent();
    }

    void UpdateMini()
    {
        if (!InMini) return;
        TimeSpan left = Release - DateTime.Now;
        if (left < TimeSpan.Zero || showTimer.Enabled) left = TimeSpan.Zero;
        string text = ReleasedAlpha() > 0.5f ? L("GTA VI УЖЕ ВЫШЛА!", "GTA VI IS OUT!")
            : string.Format("{0:00}:{1:00}:{2:00}:{3:00}", (int)left.TotalDays, left.Hours, left.Minutes, left.Seconds);
        mini.Render(text, fontBig);
    }

    // ---------- картинки ----------

    void LoadImageList()
    {
        images.Clear();
        index = -1;
        string dir = Path.Combine(baseDir, "images");
        if (Directory.Exists(dir))
            images.AddRange(Directory.GetFiles(dir).Where(f => new[] { ".jpg", ".jpeg", ".png", ".bmp" }
                .Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => rnd.Next()));
    }

    // Картинки Rockstar не лежат в репозитории (их права): при первом запуске качаем
    // официальный архив скриншотов и логотип с rockstargames.com/VI/media.
    void DownloadMedia()
    {
        bool needImages = images.Count == 0, needLogo = logo == null;
        mediaStatus = L("Загрузка картинок с rockstargames.com…", "Downloading images from rockstargames.com…");
        Task.Run(() => FetchMedia(needImages, needLogo)).ContinueWith(t =>
        {
            mediaStatus = t.Exception == null ? null
                : L("Не удалось скачать картинки, попробую при следующем запуске", "Could not download images, will retry on next start");
            string logoPath = Path.Combine(baseDir, "logo.png");
            if (logo == null && File.Exists(logoPath)) logo = new Bitmap(logoPath);
            if (images.Count == 0) { LoadImageList(); NextImage(1); }
            Invalidate();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void FetchMedia(bool needImages, bool needLogo)
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        if (needLogo)
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 GTA6Countdown";
                    var m = Regex.Match(wc.DownloadString(MediaPage), @"/VI/_next/static/media/vi\.[^""'\s()]+\.png");
                    if (m.Success) wc.DownloadFile("https://www.rockstargames.com" + m.Value, Path.Combine(baseDir, "logo.png"));
                }
            }
            catch { } // без логотипа тоже работает
        if (!needImages) return;

        string zipPath = Path.Combine(Path.GetTempPath(), "GTAVI_Screenshots.zip");
        var req = (HttpWebRequest)WebRequest.Create(ScreensZip);
        req.UserAgent = "Mozilla/5.0 GTA6Countdown";
        req.Timeout = 30000;
        using (var resp = req.GetResponse())
        using (var src = resp.GetResponseStream())
        using (var dst = File.Create(zipPath))
        {
            long total = resp.ContentLength, done = 0;
            var buf = new byte[81920];
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                dst.Write(buf, 0, n);
                done += n;
                if (total > 0) mediaStatus = L("Загрузка картинок с rockstargames.com…", "Downloading images from rockstargames.com…") + " " + done * 100 / total + "%";
            }
        }

        // Распаковываем во временную папку и переименовываем целиком, чтобы оборванная загрузка не оставила полпапки.
        string dir = Path.Combine(baseDir, "images"), part = dir + ".part";
        if (Directory.Exists(part)) Directory.Delete(part, true);
        Directory.CreateDirectory(part);
        using (var zip = ZipFile.OpenRead(zipPath))
            foreach (var e in zip.Entries)
            {
                string ext = Path.GetExtension(e.Name).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                    e.ExtractToFile(Path.Combine(part, e.Name), true); // e.Name — только имя, папки архива отбрасываем
            }
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.Move(part, dir);
        File.Delete(zipPath);
    }

    void NextImage(int step)
    {
        nextSlide = DateTime.Now.AddSeconds(SlideSeconds);
        if (images.Count == 0 || loading) return;
        index = ((index + step) % images.Count + images.Count) % images.Count;
        LoadAsync(images[index], true);
    }

    void ReloadCurrent()
    {
        if (index >= 0 && WindowState != FormWindowState.Minimized && (current == null || current.Size != ClientSize))
            LoadAsync(images[index], false);
    }

    void LoadAsync(string path, bool withFade)
    {
        var size = ClientSize;
        if (size.Width < 10 || size.Height < 10) return;
        loading = true;
        Task.Run(() => MakeCover(path, size)).ContinueWith(t =>
        {
            loading = false;
            if (t.Exception != null || t.Result == null) return;
            if (withFade && current != null)
            {
                DisposePrevious();
                previous = current;
                fadeStart = DateTime.Now;
                fade.Start();
            }
            else if (current != null) current.Dispose();
            current = t.Result;
            Invalidate();
            if (!inSizeMove && current.Size != ClientSize) ReloadCurrent(); // окно успело поменять размер
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // Масштабирует картинку «с обрезкой» под размер окна (как background-size: cover).
    static Bitmap MakeCover(string path, Size size)
    {
        using (var src = Image.FromFile(path))
        {
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float scale = Math.Max((float)size.Width / src.Width, (float)size.Height / src.Height);
                float w = src.Width * scale, h = src.Height * scale;
                g.DrawImage(src, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
            }
            return bmp;
        }
    }

    void DisposePrevious()
    {
        if (previous != null) previous.Dispose();
        previous = null;
    }

    // ---------- отрисовка ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int W = ClientSize.Width, H = ClientSize.Height;
        g.Clear(Color.Black);

        DrawBackground(g);

        // Затемнение снизу и по краям, чтобы текст читался на любой картинке.
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, W, H + 1), Color.FromArgb(70, 0, 0, 0), Color.FromArgb(215, 8, 0, 20), 90f))
        {
            br.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(90, 0, 0, 0), Color.FromArgb(25, 0, 0, 0), Color.FromArgb(150, 10, 0, 25), Color.FromArgb(225, 5, 0, 15) },
                Positions = new[] { 0f, 0.3f, 0.6f, 1f }
            };
            g.FillRectangle(br, 0, 0, W, H);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        float unit = Math.Min(W / 16f, H / 9f); // 1/16 ширины кадра 16:9

        // Логотип
        if (logo != null)
        {
            float lh = unit * 2.2f, lw = lh * logo.Width / logo.Height;
            float lx = (W - lw) / 2, ly = H * 0.07f;
            DrawShadowImage(g, logo, new RectangleF(lx, ly, lw, lh));
        }

        TimeSpan left = Release - DateTime.Now;
        if (left < TimeSpan.Zero || showTimer.Enabled) left = TimeSpan.Zero; // во время шоу — нули
        float cy = H * 0.46f;

        // Надпись «УЖЕ ВЫШЛА» проявляется в конце шоу, отсчёт в это время гаснет.
        float rel = ReleasedAlpha();
        if (rel > 0)
        {
            int a = (int)(255 * rel);
            DrawCenteredText(g, L("GTA VI УЖЕ ВЫШЛА!", "GTA VI IS OUT!"), fontBig, unit * 1.3f, W / 2f, cy, true, a);
            DrawCenteredText(g, L("Добро пожаловать в Vice City", "Welcome to Vice City"), fontText, unit * 0.45f, W / 2f, cy + unit * 1.4f, false, a);
        }
        if (rel < 1)
        {
            float k = 1 - rel;
            DrawCenteredText(g, L("ДО ВЫХОДА GRAND THEFT AUTO VI", "GRAND THEFT AUTO VI RELEASES IN"), fontText, unit * 0.38f, W / 2f, cy - unit * 0.55f, false, (int)(230 * k));

            var values = new[] { (int)left.TotalDays, left.Hours, left.Minutes, left.Seconds };
            var labels = english
                ? new[] { EnPlural(values[0], "DAY"), EnPlural(values[1], "HOUR"), EnPlural(values[2], "MINUTE"), EnPlural(values[3], "SECOND") }
                : new[] { Plural(values[0], "ДЕНЬ", "ДНЯ", "ДНЕЙ"), Plural(values[1], "ЧАС", "ЧАСА", "ЧАСОВ"),
                          Plural(values[2], "МИНУТА", "МИНУТЫ", "МИНУТ"), Plural(values[3], "СЕКУНДА", "СЕКУНДЫ", "СЕКУНД") };

            float num = unit * 1.75f;
            float block = unit * 2.7f, gap = unit * 0.7f;
            float total = block * 4 + gap * 3;
            float x0 = (W - total) / 2;
            float ny = cy + unit * 0.75f;
            for (int i = 0; i < 4; i++)
            {
                float bx = x0 + i * (block + gap) + block / 2;
                DrawCenteredText(g, values[i].ToString("00"), fontBig, num, bx, ny, true, (int)(255 * k));
                DrawCenteredText(g, labels[i], fontText, unit * 0.27f, bx, ny + num * 0.78f, false, (int)(210 * k));
                if (i < 3) DrawCenteredText(g, ":", fontBig, num * 0.8f, bx + block / 2 + gap / 2, ny - num * 0.06f, true, (int)(160 * k));
            }
        }

        DrawCenteredText(g, L("19 НОЯБРЯ 2026", "NOVEMBER 19, 2026") + "  •  PS5  •  XBOX SERIES X|S", fontText, unit * 0.3f, W / 2f, H * 0.795f, false, 200);

        // Подпись картинки и подсказка
        if (index >= 0)
        {
            string name = Path.GetFileNameWithoutExtension(images[index]).Replace('_', ' ').Replace("Art ", "Artwork: ");
            using (var f = new Font(fontText, Math.Max(8f, unit * 0.18f), GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
            {
                g.DrawString(name, f, b, unit * 0.3f, unit * 0.22f);
                var hint = L("ПКМ — меню", "Right-click — menu");
                var sz = g.MeasureString(hint, f);
                g.DrawString(hint, f, b, W - sz.Width - unit * 0.3f, unit * 0.22f);
            }
        }

        string status = mediaStatus;
        if (status != null)
            DrawCenteredText(g, status, fontText, unit * 0.22f, W / 2f, H * 0.72f, false, 200);

        DrawNewsBar(g, W, H, unit);
        if (showTimer.Enabled) DrawFireworks(g, W, H, unit);
    }

    // ---------- шоу ----------

    // 0 — обычный отсчёт, 1 — надпись «УЖЕ ВЫШЛА» (после выхода или в тесте).
    float ReleasedAlpha()
    {
        if (DateTime.Now < Release && DateTime.Now >= testEnd) return 0;
        if (!releaseFired && DateTime.Now >= Release && DateTime.Now - Release < TimeSpan.FromHours(1)) return 0; // шоу вот-вот начнётся
        if (!showTimer.Enabled) return 1;
        return Math.Max(0, Math.Min(1, ((float)showClock.Elapsed.TotalSeconds - TextFadeFrom) / (ShowSeconds - TextFadeFrom)));
    }

    void StartShow(bool test)
    {
        if (showTimer.Enabled) return;
        LeaveMini(); // шоу — в большом окне, даже если таймер был свёрнут
        if (test) testEnd = DateTime.Now.AddSeconds(ShowSeconds + TestHoldSeconds);
        int W = ClientSize.Width, H = ClientSize.Height;
        float unit = Math.Min(W / 16f, H / 9f);
        sparks.Clear();
        rockets.Clear();
        for (int i = 0; i < BurstTimes.Length; i++)
        {
            float x = W * (0.15f + 0.7f * (float)rnd.NextDouble());
            rockets.Add(new Rocket
            {
                X0 = x, Y0 = H - unit * 0.95f,
                X1 = x + W * 0.08f * (float)(rnd.NextDouble() * 2 - 1), Y1 = H * (0.12f + 0.33f * (float)rnd.NextDouble()),
                T0 = BurstTimes[i] - RocketFlight, Kind = rnd.Next(3)
            });
        }
        showLast = 0;
        showClock.Restart();
        showTimer.Start();
        PlayFanfare();
        Invalidate();
    }

    void PlayFanfare()
    {
        try
        {
            if (fanfareWav == null) fanfareWav = MakeFanfare();
            if (player != null) player.Dispose();
            player = new System.Media.SoundPlayer(new MemoryStream(fanfareWav));
            player.Play();
        }
        catch { }
    }

    void ShowTick()
    {
        float t = (float)showClock.Elapsed.TotalSeconds;
        float dt = Math.Min(0.05f, t - showLast);
        showLast = t;
        if (t >= ShowSeconds)
        {
            showTimer.Stop();
            sparks.Clear();
            Invalidate();
            return;
        }
        float unit = Math.Min(ClientSize.Width / 16f, ClientSize.Height / 9f);

        foreach (var r in rockets)
        {
            if (r.Done || t < r.T0) continue;
            float s = (t - r.T0) / RocketFlight;
            if (s >= 1) { r.Done = true; Explode(r, t, unit); continue; }
            float e = 1 - (1 - s) * (1 - s); // ракета замедляется к верхней точке
            float x = r.X0 + (r.X1 - r.X0) * e, y = r.Y0 + (r.Y1 - r.Y0) * e;
            for (int i = 0; i < 2; i++) // искры хвоста
                sparks.Add(new Spark
                {
                    X = x, Y = y, VX = unit * 0.3f * (float)(rnd.NextDouble() - 0.5), VY = unit * 0.5f * (float)rnd.NextDouble(),
                    Life = 0.25f + 0.2f * (float)rnd.NextDouble(), Size = unit * 0.05f, C = Color.FromArgb(255, 200, 120)
                });
        }

        float drag = (float)Math.Exp(-1.8 * dt), grav = unit * 1.6f;
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var p = sparks[i];
            p.Age += dt;
            if (p.Age >= p.Life) { sparks.RemoveAt(i); continue; }
            p.VX *= drag;
            p.VY = p.VY * drag + grav * dt;
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
        }
        Invalidate();
    }

    // Kind: 0 — шар, 1 — кольцо двух цветов, 2 — ива с мерцающими искрами.
    void Explode(Rocket r, float t, float unit)
    {
        Color c1 = FireColors[rnd.Next(FireColors.Length)], c2 = FireColors[rnd.Next(FireColors.Length)];
        float maxLife = ShowSeconds - 0.05f - t; // к концу шоу все искры гаснут
        int n = r.Kind == 1 ? 72 : 100;
        float speed = unit * (r.Kind == 2 ? 3.6f : 4.6f) * (0.85f + 0.3f * (float)rnd.NextDouble());
        for (int i = 0; i < n; i++)
        {
            float a, v;
            if (r.Kind == 1) { a = (float)(2 * Math.PI * i / n); v = speed * (0.96f + 0.08f * (float)rnd.NextDouble()); }
            else
            {
                // проекция сферы на плоскость: больше искр по краю, как у настоящего шара
                a = (float)(2 * Math.PI * rnd.NextDouble());
                double cos = rnd.NextDouble() * 2 - 1;
                v = speed * (float)Math.Sqrt(1 - cos * cos) * (0.9f + 0.1f * (float)rnd.NextDouble());
            }
            float life = (r.Kind == 2 ? 1.2f : 0.9f) + 0.3f * (float)rnd.NextDouble();
            sparks.Add(new Spark
            {
                X = r.X1, Y = r.Y1, VX = v * (float)Math.Cos(a), VY = v * (float)Math.Sin(a),
                Life = Math.Min(life, maxLife), Size = unit * (r.Kind == 2 ? 0.08f : 0.1f),
                C = r.Kind == 1 ? (i % 2 == 0 ? c1 : c2) : (rnd.Next(3) == 0 ? c2 : c1), Glitter = r.Kind == 2
            });
        }
        sparks.Add(new Spark { X = r.X1, Y = r.Y1, Life = Math.Min(0.3f, maxLife), Size = unit * 1.6f, C = c1, Flash = true });
    }

    void DrawFireworks(Graphics g, int W, int H, float unit)
    {
        float t = (float)showClock.Elapsed.TotalSeconds;
        // Лёгкое затемнение, чтобы вспышки были ярче; к концу шоу уходит.
        float dim = Math.Min(1, t / 0.3f) * Math.Min(1, (ShowSeconds - t) / 0.7f);
        using (var br = new SolidBrush(Color.FromArgb((int)(110 * Math.Max(0, dim)), 5, 0, 15))) g.FillRectangle(br, 0, 0, W, H);

        using (var pen = new Pen(Color.White) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var brush = new SolidBrush(Color.White))
        {
            foreach (var p in sparks)
            {
                float k = 1 - p.Age / p.Life;
                if (p.Flash)
                {
                    float rad = p.Size * (0.5f + p.Age / p.Life);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(p.X - rad, p.Y - rad, rad * 2, rad * 2);
                        using (var pg = new PathGradientBrush(path))
                        {
                            pg.CenterColor = Color.FromArgb((int)(150 * k), Mix(p.C, Color.White, 0.5f));
                            pg.SurroundColors = new[] { Color.FromArgb(0, p.C) };
                            g.FillPath(pg, path);
                        }
                    }
                    continue;
                }
                float alpha = (float)Math.Pow(k, 1.3);
                if (p.Glitter && rnd.Next(3) == 0) alpha *= 0.25f; // мерцание
                // Только что вылетевшие искры — почти белые, потом набирают цвет.
                Color c = Mix(p.C, Color.White, Math.Max(0, 1 - p.Age / 0.15f) * 0.8f);
                pen.Color = Color.FromArgb((int)(170 * alpha), c);
                pen.Width = p.Size;
                g.DrawLine(pen, p.X - p.VX * 0.05f, p.Y - p.VY * 0.05f, p.X, p.Y);
                float gr = p.Size * 1.8f;
                brush.Color = Color.FromArgb((int)(45 * alpha), c);
                g.FillEllipse(brush, p.X - gr, p.Y - gr, gr * 2, gr * 2);
                brush.Color = Color.FromArgb((int)(255 * alpha), Mix(c, Color.White, 0.35f));
                g.FillEllipse(brush, p.X - p.Size / 2, p.Y - p.Size / 2, p.Size, p.Size);
            }
            foreach (var r in rockets)
            {
                if (r.Done || t < r.T0) continue;
                float s = (t - r.T0) / RocketFlight, e = 1 - (1 - s) * (1 - s);
                float x = r.X0 + (r.X1 - r.X0) * e, y = r.Y0 + (r.Y1 - r.Y0) * e, d = unit * 0.1f;
                brush.Color = Color.FromArgb(255, 255, 235, 200);
                g.FillEllipse(brush, x - d / 2, y - d / 2, d, d);
            }
        }
    }

    static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    // Фанфары синтезируются в WAV в памяти: «медные» ноты (пилообразный тон с вибрато) и хлопки взрывов.
    internal static byte[] MakeFanfare()
    {
        const int sr = 22050;
        int n = (int)(sr * ShowSeconds);
        var buf = new double[n];
        // начало, длительность (с), ноты MIDI (67 — G4, 72 — C5 …)
        double[][] score =
        {
            new double[] { 0.00, 0.13, 67 }, new double[] { 0.15, 0.13, 67 }, new double[] { 0.30, 0.13, 67 },
            new double[] { 0.45, 0.50, 72, 64 },
            new double[] { 1.00, 0.13, 67 }, new double[] { 1.15, 0.13, 72 }, new double[] { 1.30, 0.13, 76 },
            new double[] { 1.45, 0.70, 79, 72, 64 },
            new double[] { 2.20, 0.18, 76 }, new double[] { 2.40, 0.18, 79 },
            new double[] { 2.60, 2.35, 84, 79, 76, 72, 60 },
        };
        foreach (var note in score)
        {
            double start = note[0], dur = note[1];
            bool hold = dur > 1;
            double vol = 0.25 / Math.Sqrt(note.Length - 2);
            for (int v = 2; v < note.Length; v++)
            {
                double f0 = 440 * Math.Pow(2, (note[v] - 69) / 12), ph = 0;
                int s0 = (int)(start * sr), len = (int)(dur * sr);
                for (int i = 0; i < len && s0 + i < n; i++)
                {
                    double t = (double)i / sr;
                    ph += 2 * Math.PI * f0 * (1 + 0.005 * Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1, t / 0.25)) / sr;
                    double env = Math.Min(1, t / 0.025) * (0.8 + 0.2 * Math.Exp(-t * 8)) * Math.Min(1, (dur - t) / (hold ? 0.6 : 0.05));
                    if (hold) env *= Math.Exp(-t * 0.35);
                    double bright = 0.3 + 0.6 * Math.Min(1, t / 0.06), amp = 1, s = 0; // верхние гармоники нарастают в атаке
                    for (int k = 1; k <= 10 && k * f0 < sr * 0.45; k++, amp *= bright) s += Math.Sin(k * ph) * amp / k;
                    buf[s0 + i] += vol * env * s;
                }
            }
        }
        var rand = new Random(7);
        foreach (float bt in BurstTimes)
        {
            int s0 = (int)(bt * sr);
            double lp = 0;
            for (int i = 0; i < sr * 0.5 && s0 + i < n; i++)
            {
                double t = (double)i / sr;
                lp += (rand.NextDouble() * 2 - 1 - lp) * 0.3;
                buf[s0 + i] += 0.35 * lp * Math.Exp(-t / 0.07) + 0.25 * Math.Sin(2 * Math.PI * 55 * t) * Math.Exp(-t / 0.09);
            }
        }

        double peak = buf.Max(x => Math.Abs(x));
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("RIFF".ToCharArray()); w.Write(36 + n * 2); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(n * 2);
            foreach (double x in buf) w.Write((short)(Math.Tanh(x / peak * 1.3) / Math.Tanh(1.3) * 0.9 * 32767));
        }
        return ms.ToArray();
    }

    // Лента новостей внизу окна: значок, источник с датой и заголовок.
    void DrawNewsBar(Graphics g, int W, int H, float unit)
    {
        // В маленьком окне лента не мельчает дальше: заголовок не меньше ~14 px, иначе его не прочесть.
        unit = Math.Max(unit, 52f);
        float barH = unit * 0.95f, barY = H - barH;
        using (var br = new SolidBrush(Color.FromArgb(185, 12, 2, 24))) g.FillRectangle(br, 0, barY, W, barH);
        using (var br = new LinearGradientBrush(new RectangleF(0, barY, W, 3), Color.FromArgb(255, 190, 110), Color.FromArgb(200, 70, 230), 0f))
            g.FillRectangle(br, 0, barY, W, Math.Max(2f, unit * 0.03f));

        // Значок «НОВОСТИ GTA»
        float pad = unit * 0.3f;
        using (var bf = new Font(fontBig, unit * 0.2f, GraphicsUnit.Pixel))
        {
            var label = L("НОВОСТИ GTA", "GTA NEWS");
            var ls = g.MeasureString(label, bf);
            var badge = new RectangleF(pad, barY + (barH - ls.Height * 1.25f) / 2, ls.Width + unit * 0.2f, ls.Height * 1.25f);
            using (var path = RoundRect(badge, badge.Height * 0.25f))
            using (var br = new LinearGradientBrush(badge, Color.FromArgb(255, 160, 110), Color.FromArgb(225, 80, 200), 0f))
                g.FillPath(br, path);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(label, bf, Brushes.White, badge, sf);
            pad = badge.Right + unit * 0.3f;
        }

        var n = CurrentNews();
        using (var small = new Font(fontText, Math.Max(8f, unit * 0.17f), GraphicsUnit.Pixel))
        using (var big = new Font(fontText, Math.Max(9f, unit * 0.27f), GraphicsUnit.Pixel))
        using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
        {
            string counter = n == null ? "" : (newsIndex + 1) + " / " + news.Count;
            float cw = g.MeasureString(counter, small).Width;
            using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                g.DrawString(counter, small, b, W - cw - unit * 0.3f, barY + barH / 2 - small.Height / 2f);

            float right = W - cw - unit * 0.55f;
            newsRect = new RectangleF(pad, barY, Math.Max(0, right - pad), barH);
            if (n == null)
            {
                string msg = newsLoadedOnce ? L("Свежих новостей о GTA пока нет", "No fresh GTA news yet") : L("Загружаю новости…", "Loading news…");
                using (var b = new SolidBrush(Color.FromArgb(170, 255, 255, 255)))
                    g.DrawString(msg, big, b, newsRect, sf);
                return;
            }

            float t = (float)Math.Min(1, (DateTime.Now - newsChanged).TotalMilliseconds / NewsFadeMs);
            float dy = (1 - t) * unit * 0.25f;
            int a = (int)(255 * t);
            var meta = new RectangleF(pad, barY + barH * 0.08f + dy, newsRect.Width, barH * 0.36f);
            var head = new RectangleF(pad, barY + barH * 0.40f + dy, newsRect.Width, barH * 0.52f);
            using (var b = new SolidBrush(Color.FromArgb(a * 190 / 255, 255, 190, 220)))
                g.DrawString(n.Source + "  •  " + Ago(n.Date), small, b, meta, sf);
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                g.DrawString(n.Title, big, b, head, sf);
        }
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void DrawBackground(Graphics g)
    {
        if (current == null) return;
        var dst = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        double t = previous == null ? 1 : Math.Min(1, (DateTime.Now - fadeStart).TotalMilliseconds / FadeMs);
        if (previous != null) DrawFit(g, previous, dst);
        if (t >= 1) { DrawFit(g, current, dst); return; }
        using (var ia = new ImageAttributes())
        {
            ia.SetColorMatrix(new ColorMatrix { Matrix33 = (float)t });
            g.DrawImage(current, dst, 0, 0, current.Width, current.Height, GraphicsUnit.Pixel, ia);
        }
    }

    // Пока окно тянут за край, картинка растягивается; после отпускания перерисовывается в точный размер.
    static void DrawFit(Graphics g, Bitmap bmp, Rectangle dst)
    {
        if (bmp.Size == dst.Size) g.DrawImageUnscaled(bmp, 0, 0);
        else g.DrawImage(bmp, dst);
    }

    static void DrawShadowImage(Graphics g, Image img, RectangleF r)
    {
        using (var ia = new ImageAttributes())
        {
            ia.SetColorMatrix(new ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0.55f, 0 }, new float[] { 0, 0, 0, 0, 1 }
            }));
            float o = r.Height * 0.03f;
            g.DrawImage(img, new[] { new PointF(r.X + o, r.Y + o), new PointF(r.Right + o, r.Y + o), new PointF(r.X + o, r.Bottom + o) },
                new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, ia);
        }
        g.DrawImage(img, r);
    }

    // Текст по центру точки (cx, cy). Яркий — с градиентом «закат Vice City», обычный — белый.
    static void DrawCenteredText(Graphics g, string text, FontFamily family, float size, float cx, float cy, bool vivid, int alpha = 255)
    {
        using (var path = new GraphicsPath())
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            path.AddString(text, family, (int)FontStyle.Regular, size, new PointF(cx, cy), sf);
            var bounds = path.GetBounds();
            if (bounds.Width <= 0) return;

            using (var m = new Matrix())
            {
                float o = Math.Max(1.5f, size * 0.035f);
                m.Translate(o, o);
                path.Transform(m);
                using (var sb = new SolidBrush(Color.FromArgb(alpha * 150 / 255, 0, 0, 0))) g.FillPath(sb, path);
                m.Reset();
                m.Translate(-o, -o);
                path.Transform(m);
            }

            if (vivid) FillSunset(g, path, alpha);
            else
                using (var br = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255))) g.FillPath(br, path);
        }
    }

    // Заливка фирменным градиентом сверху вниз: золотой → розовый → фиолетовый.
    internal static void FillSunset(Graphics g, GraphicsPath path, int alpha)
    {
        var bounds = path.GetBounds();
        var rect = new RectangleF(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2);
        using (var br = new LinearGradientBrush(rect, Color.White, Color.White, 90f))
        {
            br.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(alpha, 255, 214, 120), Color.FromArgb(alpha, 255, 110, 150), Color.FromArgb(alpha, 205, 70, 230) },
                Positions = new[] { 0f, 0.5f, 1f }
            };
            g.FillPath(br, path);
        }
    }

    static string EnPlural(int n, string one)
    {
        return n == 1 ? one : one + "S";
    }

    static string Plural(int n, string one, string few, string many)
    {
        int a = n % 100, b = n % 10;
        if (a >= 11 && a <= 14) return many;
        if (b == 1) return one;
        if (b >= 2 && b <= 4) return few;
        return many;
    }
}

// Мини-таймер вместо свёрнутого окна: только цифры поверх всех окон, без фона.
// Layered window с попиксельной альфой (UpdateLayeredWindow), рисуется заново каждую секунду.
class MiniTimer : Form
{
    const float Em = 40f;  // высота шрифта цифр, px
    const int Pad = 12;    // запас под тень вокруг текста
    public Rectangle Area; // рабочая область экрана: таймер прижат к её правому нижнему углу

    public MiniTimer()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80; // WS_EX_LAYERED | WS_EX_TOOLWINDOW (нет в Alt+Tab)
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    public void Render(string text, FontFamily family)
    {
        using (var path = new GraphicsPath())
        using (var refPath = new GraphicsPath())
        {
            path.AddString(text, family, (int)FontStyle.Regular, Em, PointF.Empty, StringFormat.GenericTypographic);
            // Габариты — по строке из восьмёрок, чтобы окно не дёргалось при смене цифр.
            refPath.AddString(Regex.Replace(text, @"\d", "8"), family, (int)FontStyle.Regular, Em, PointF.Empty, StringFormat.GenericTypographic);
            var b = refPath.GetBounds();
            if (b.Width <= 0) return;
            int w = (int)Math.Ceiling(b.Width) + Pad * 2, h = (int)Math.Ceiling(b.Height) + Pad * 2;
            using (var m = new Matrix()) { m.Translate(Pad - b.X, Pad - b.Y); path.Transform(m); }

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // Почти прозрачный фон: окно ловит мышь и между цифрами (пиксели с альфой 0 клики пропускают).
                    g.Clear(Color.FromArgb(1, 0, 0, 0));
                    // Мягкий тёмный ореол — читается на светлом фоне.
                    for (int i = 4; i >= 1; i--)
                        using (var pen = new Pen(Color.FromArgb(45, 0, 0, 0), i * 2.5f) { LineJoin = LineJoin.Round })
                            g.DrawPath(pen, path);
                    using (var m = new Matrix())
                    {
                        m.Translate(2, 3);
                        path.Transform(m);
                        using (var sb = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillPath(sb, path);
                        m.Reset();
                        m.Translate(-2, -3);
                        path.Transform(m);
                    }
                    using (var pen = new Pen(Color.FromArgb(200, 20, 0, 30), 1.5f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                    TimerForm.FillSunset(g, path, 255);
                }
                Apply(bmp, new Point(Area.Right - w - 8, Area.Bottom - h - 4));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize, IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

    void Apply(Bitmap bmp, Point pos)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memDc, hBmp);
        try
        {
            var size = bmp.Size;
            var src = Point.Empty;
            var blend = new BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_ALPHA
            UpdateLayeredWindow(Handle, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, 2); // ULW_ALPHA
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
