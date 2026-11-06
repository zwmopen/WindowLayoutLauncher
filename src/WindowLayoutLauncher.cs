using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices.ComTypes;

namespace WindowLayoutLauncher
{
    static class Program
    {
        public const string Version = "3.2.0";
        public const string AppName = "窗口管理器";
        public const string RepoUrl = "https://github.com/zwmopen/WindowLayoutLauncher";
        public const string VersionCheckUrl = "https://raw.githubusercontent.com/zwmopen/WindowLayoutLauncher/main/version.json";
        public static readonly int ShowMainWindowMessage = Native.RegisterWindowMessage("WindowLayoutLauncher.ShowMainWindow.v3");
        private static Mutex appMutex;

        [STAThread]
        private static void WriteCliLog(string logDir, string line)
        {
            try
            {
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "cli.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        static void Main(string[] args)
        {
            try
            {
                TrySetDpiAwareness();
                TaskbarIntegration.Initialize();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (args.Length == 0)
                {
                    bool createdNew;
                    appMutex = new Mutex(true, "Local\\WindowLayoutLauncher.v3", out createdNew);
                    if (!createdNew)
                    {
                        ActivateExistingInstance();
                        return;
                    }
                }

                var manager = new LayoutManager(AppDomain.CurrentDomain.BaseDirectory);
                var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (args.Length >= 1 && string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(logDir);
                    var probeFile = Path.Combine(logDir, "browser_probe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                    File.WriteAllText(probeFile, BrowserProbe.BuildReport(), Encoding.UTF8);
                    return;
                }
                if (args.Length >= 2 && string.Equals(args[0], "--save", StringComparison.OrdinalIgnoreCase))
                {
                    var saved = manager.SaveCurrent(args[1]);
                    var browsers = 0;
                    if (saved != null)
                    {
                        foreach (var item in saved.Items)
                        {
                            if (item.Kind == "browser" || item.Kind == "web") browsers++;
                        }
                    }
                    WriteCliLog(logDir, "save \"" + args[1] + "\" items=" + (saved != null ? saved.Items.Count : 0) + " browsers=" + browsers + "  " + BrowserProbe.LastTiming);
                    return;
                }
                if (args.Length >= 2 && string.Equals(args[0], "--restore", StringComparison.OrdinalIgnoreCase))
                {
                    var restoreResult = manager.Restore(args[1]);
                    var details = restoreResult.FailDetails == null ? "" : (" | " + string.Join(" ; ", restoreResult.FailDetails.ToArray()));
                    WriteCliLog(logDir, "restore \"" + args[1] + "\" ok=" + restoreResult.Ok + " failed=" + restoreResult.Failed + details);
                    var quiet = args.Length >= 3 && string.Equals(args[2], "--quiet", StringComparison.OrdinalIgnoreCase);
                    if (restoreResult.Failed > 0 && !quiet)
                    {
                        Application.Run(NotificationToastForm.ForRestoreResult(restoreResult));
                    }
                    return;
                }

                Application.Run(new MainForm(manager));
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex);
                if (args.Length == 0)
                {
                    MessageBox.Show(AppName + "遇到错误，已写入 crash.log。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static void ActivateExistingInstance()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                IntPtr found = IntPtr.Zero;
                Native.EnumWindows((hWnd, lParam) =>
                {
                    int length = Native.GetWindowTextLength(hWnd);
                    if (length <= 0) return true;
                    var title = new StringBuilder(length + 1);
                    Native.GetWindowText(hWnd, title, title.Capacity);
                    if (!string.Equals(title.ToString(), AppName, StringComparison.Ordinal)) return true;
                    found = hWnd;
                    return false;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero)
                {
                    Native.PostMessage(found, ShowMainWindowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Thread.Sleep(100);
            }
        }

        private static void WriteCrashLog(Exception ex)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var lines = new List<string>();
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                lines.Add(ex == null ? "null exception" : ex.GetType().FullName);
                lines.Add(ex == null ? "" : ex.Message);
                lines.Add(ex == null ? "" : ex.StackTrace);
                if (ex != null && ex.InnerException != null)
                {
                    lines.Add("Inner:");
                    lines.Add(ex.InnerException.GetType().FullName);
                    lines.Add(ex.InnerException.Message);
                    lines.Add(ex.InnerException.StackTrace);
                }
                File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void TrySetDpiAwareness()
        {
            try
            {
                Native.SetProcessDpiAwareness(1);
                return;
            }
            catch
            {
            }

            try
            {
                Native.SetProcessDPIAware();
            }
            catch
            {
            }
        }
    }

    [DataContract]
    public class LayoutConfig
    {
        [DataMember(Name = "format_version", EmitDefaultValue = false)]
        public int FormatVersion { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "task_mode", EmitDefaultValue = false)]
        public string TaskMode { get; set; }

        [DataMember(Name = "saved_at")]
        public string SavedAt { get; set; }

        [DataMember(Name = "items")]
        public List<LayoutItem> Items { get; set; }

        [DataMember(Name = "screens", EmitDefaultValue = false)]
        public List<SavedScreen> Screens { get; set; }
    }

    [DataContract]
    public class SavedScreen
    {
        [DataMember(Name = "device")]
        public string Device { get; set; }

        [DataMember(Name = "index")]
        public int Index { get; set; }

        [DataMember(Name = "x")]
        public int X { get; set; }

        [DataMember(Name = "y")]
        public int Y { get; set; }

        [DataMember(Name = "w")]
        public int W { get; set; }

        [DataMember(Name = "h")]
        public int H { get; set; }

        [DataMember(Name = "primary")]
        public bool Primary { get; set; }
    }

    [DataContract]
    public class LayoutItem
    {
        [DataMember(Name = "kind")]
        public string Kind { get; set; }

        [DataMember(Name = "path", EmitDefaultValue = false)]
        public string Path { get; set; }

        [DataMember(Name = "browser", EmitDefaultValue = false)]
        public string Browser { get; set; }

        [DataMember(Name = "exe_path", EmitDefaultValue = false)]
        public string ExePath { get; set; }

        [DataMember(Name = "process_name", EmitDefaultValue = false)]
        public string ProcessName { get; set; }

        [DataMember(Name = "title", EmitDefaultValue = false)]
        public string Title { get; set; }

        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        [DataMember(Name = "urls", EmitDefaultValue = false)]
        public List<string> Urls { get; set; }

        [DataMember(Name = "profile", EmitDefaultValue = false)]
        public string Profile { get; set; }

        [DataMember(Name = "profile_name", EmitDefaultValue = false)]
        public string ProfileName { get; set; }

        [DataMember(Name = "user_data_dir", EmitDefaultValue = false)]
        public string UserDataDir { get; set; }

        [DataMember(Name = "launch_args", EmitDefaultValue = false)]
        public string LaunchArgs { get; set; }

        [DataMember(Name = "title_keyword", EmitDefaultValue = false)]
        public string TitleKeyword { get; set; }

        /// <summary>该窗口保存的全部网页，兼容只有 url 字段的旧布局。</summary>
        public List<string> UrlList
        {
            get
            {
                var list = new List<string>();
                if (Urls != null)
                {
                    foreach (var u in Urls)
                    {
                        if (!string.IsNullOrWhiteSpace(u) && !list.Contains(u)) list.Add(u.Trim());
                    }
                }
                if (list.Count == 0 && !string.IsNullOrWhiteSpace(Url)) list.Add(Url.Trim());
                return list;
            }
        }

        [DataMember(Name = "x")]
        public int X { get; set; }

        [DataMember(Name = "y")]
        public int Y { get; set; }

        [DataMember(Name = "w")]
        public int W { get; set; }

        [DataMember(Name = "h")]
        public int H { get; set; }

        [DataMember(Name = "screen_device", EmitDefaultValue = false)]
        public string ScreenDevice { get; set; }

        [DataMember(Name = "screen_index", EmitDefaultValue = false)]
        public int ScreenIndex { get; set; }

        [DataMember(Name = "relative_x", EmitDefaultValue = false)]
        public double RelativeX { get; set; }

        [DataMember(Name = "relative_y", EmitDefaultValue = false)]
        public double RelativeY { get; set; }

        [DataMember(Name = "relative_w", EmitDefaultValue = false)]
        public double RelativeW { get; set; }

        [DataMember(Name = "relative_h", EmitDefaultValue = false)]
        public double RelativeH { get; set; }
    }

    public class LayoutSummary
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string SavedAt { get; set; }
        public int Count { get; set; }
        public string TaskMode { get; set; }
        public List<LayoutItem> Items { get; set; }
        public List<SavedScreen> Screens { get; set; }
        public string Display
        {
            get { return Name + "    " + Count + " 个窗口"; }
        }
    }

    public class HealthIssue
    {
        public string Level { get; set; }
        public string Message { get; set; }
    }

    public class HealthReport
    {
        public List<HealthIssue> Issues { get; set; }
        public bool HasBlockingIssues
        {
            get { return Issues != null && Issues.Any(x => string.Equals(x.Level, "error", StringComparison.OrdinalIgnoreCase)); }
        }
    }

    public class WindowInfo
    {
        public IntPtr Hwnd { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string Title { get; set; }
        public string ExePath { get; set; }
        public bool IsMinimized { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }

    public class ExplorerInfo
    {
        public IntPtr Hwnd { get; set; }
        public string Path { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }

    public class LayoutManager
    {
        private readonly string baseDir;
        private readonly string layoutDir;

        public string LayoutDir { get { return layoutDir; } }
        public LayoutManager(string baseDir)
        {
            this.baseDir = baseDir;
            layoutDir = System.IO.Path.Combine(baseDir, "layouts");
            Directory.CreateDirectory(layoutDir);
        }

        public List<LayoutSummary> GetLayouts()
        {
            Directory.CreateDirectory(layoutDir);
            var result = new List<LayoutSummary>();
            foreach (var file in Directory.GetFiles(layoutDir, "*.json").OrderBy(x => x))
            {
                try
                {
                    var layout = ReadLayout(file);
                    result.Add(new LayoutSummary
                    {
                        Name = string.IsNullOrWhiteSpace(layout.Name) ? System.IO.Path.GetFileNameWithoutExtension(file) : layout.Name,
                        Path = file,
                        SavedAt = layout.SavedAt,
                        Count = layout.Items == null ? 0 : layout.Items.Count,
                        TaskMode = string.IsNullOrWhiteSpace(layout.TaskMode) ? InferTaskMode(layout.Name) : layout.TaskMode,
                        Items = layout.Items ?? new List<LayoutItem>(),
                        Screens = layout.Screens ?? new List<SavedScreen>()
                    });
                }
                catch
                {
                }
            }
            return result;
        }

        public LayoutConfig SaveCurrent(string name, string taskMode = null)
        {
            var safeName = SafeName(name);
            var topWindows = WindowTools.GetTopLevelWindows();
            var items = new List<LayoutItem>();
            var screens = WindowTools.GetSavedScreens();
            var captured = new HashSet<IntPtr>();
            var currentExe = FullPathTrim(Application.ExecutablePath);

            foreach (var explorer in WindowTools.GetExplorerWindows(topWindows, false).OrderBy(w => w.X).ThenBy(w => w.Y))
            {
                var item = new LayoutItem
                {
                    Kind = "explorer",
                    Path = explorer.Path,
                    X = explorer.X,
                    Y = explorer.Y,
                    W = explorer.W,
                    H = explorer.H
                };
                WindowTools.AttachScreenPosition(item, screens);
                items.Add(item);
                captured.Add(explorer.Hwnd);
            }

            var swTotal = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            var visibleWindows = topWindows.Where(w => !w.IsMinimized).ToList();
            var browserWindows = BrowserProbe.FilterBrowserWindows(visibleWindows, 6000)
                .OrderBy(w => w.X).ThenBy(w => w.Y)
                .ToList();
            var tProbe = sw.ElapsedMilliseconds; sw.Restart();

            var browserUrls = BrowserProbe.ReadActiveUrls(browserWindows.Select(w => w.Hwnd).ToList(), 2500);
            var tUrls = sw.ElapsedMilliseconds; sw.Restart();

            var browserTabUrls = BrowserProbe.ReadAllTabUrlsForWindows(browserWindows.Select(w => w.Hwnd).ToList(), 10, 5000);
            var tTabs = sw.ElapsedMilliseconds; sw.Restart();

            foreach (var browser in browserWindows)
            {
                var probe = BrowserProbe.Probe(browser.ProcessId, browser.ExePath, browser.Title, browser.Hwnd);
                if (probe == null)
                {
                    probe = new BrowserLaunchInfo { Family = "other", Profile = "", ProfileName = "" };
                }

                string onlyUrl;
                if (!browserUrls.TryGetValue(browser.Hwnd, out onlyUrl)) onlyUrl = null;

                var urls = new List<string>();

                // 多标签窗口尽量把整窗口的标签页都记下来；读不到就只记当前这一页
                List<string> tabUrls = null;
                if (browserTabUrls.TryGetValue(browser.Hwnd, out tabUrls) && tabUrls != null && tabUrls.Count > 0)
                {
                    urls.AddRange(tabUrls);
                }
                if (urls.Count == 0 && !string.IsNullOrWhiteSpace(onlyUrl)) urls.Add(onlyUrl.Trim());

                var item = new LayoutItem
                {
                    Kind = "browser",
                    Browser = string.IsNullOrWhiteSpace(probe.Family) ? "other" : probe.Family,
                    ProcessName = browser.ProcessName,
                    Profile = probe.Profile,
                    ProfileName = probe.ProfileName,
                    UserDataDir = probe.UserDataDir,
                    LaunchArgs = probe.Family == "bit" ? BrowserProbe.ArgsOnly(probe.CommandLine, probe.ExePath) : null,
                    ExePath = string.IsNullOrWhiteSpace(probe.ExePath) ? WindowTools.GetProcessPath(browser.ProcessId) : probe.ExePath,
                    Url = urls.Count > 0 ? urls[0] : null,
                    Urls = urls,
                    Title = browser.Title,
                    TitleKeyword = TitleKeywordFromTitle(BrowserProbe.KeywordFromTitle(browser.Title)),
                    X = browser.X,
                    Y = browser.Y,
                    W = browser.W,
                    H = browser.H
                };
                WindowTools.AttachScreenPosition(item, screens);
                items.Add(item);
                captured.Add(browser.Hwnd);
            }

            foreach (var app in topWindows
                .Where(w => !w.IsMinimized && !captured.Contains(w.Hwnd))
                .OrderBy(w => w.X).ThenBy(w => w.Y))
            {
                var exePath = WindowTools.GetProcessPath(app.ProcessId);
                if (IsIgnoredAppWindow(app, exePath, currentExe)) continue;

                var item = new LayoutItem
                {
                    Kind = "app",
                    ProcessName = app.ProcessName,
                    ExePath = exePath,
                    Title = app.Title,
                    TitleKeyword = TitleKeywordFromTitle(app.Title),
                    X = app.X,
                    Y = app.Y,
                    W = app.W,
                    H = app.H
                };
                WindowTools.AttachScreenPosition(item, screens);
                items.Add(item);
            }

            var layout = new LayoutConfig
            {
                FormatVersion = 3,
                Name = safeName,
                TaskMode = string.IsNullOrWhiteSpace(taskMode) ? InferTaskMode(safeName) : taskMode,
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Items = items,
                Screens = screens
            };

            var tItems = sw.ElapsedMilliseconds;
            BrowserProbe.LastTiming = "total=" + swTotal.ElapsedMilliseconds + "ms probe=" + tProbe + " urls=" + tUrls + " tabs=" + tTabs + " items=" + tItems;

            WriteLayout(GetLayoutPath(safeName), layout);
            return layout;
        }

        public RestoreResult Restore(string nameOrPath)
        {
            var path = File.Exists(nameOrPath) ? nameOrPath : GetLayoutPath(SafeName(nameOrPath));
            if (!File.Exists(path))
            {
                return new RestoreResult { Ok = 0, Failed = 1, Message = "没有找到这个布局", FailDetails = new List<string> { "布局文件不存在: " + path } };
            }

            var layout = ReadLayout(path);
            var topWindows = WindowTools.GetTopLevelWindows();
            var explorerWindows = WindowTools.GetExplorerWindows(topWindows, true);
            var used = new HashSet<IntPtr>();
            int ok = 0;
            int failed = 0;
            var failDetails = new List<string>();
            var logLines = new List<string>();
            logLines.Add("===== 恢复布局: " + (layout.Name ?? Path.GetFileNameWithoutExtension(path)) + " =====");
            logLines.Add("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            logLines.Add("");

            // 先把当前所有浏览器窗口已经打开的网页收进来：已经开着的网址不再重复打开
            var openUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if ((layout.Items ?? new List<LayoutItem>()).Any(i => EqualsIgnoreCase(i.Kind, "browser")))
            {
                // 最小化的浏览器窗口也算：它们里面的网址同样已经开着；非浏览器窗口直接跳过，省得白读
                var handles = topWindows
                    .Where(w => BrowserProbe.IsBrowserProcessName(w.ProcessName))
                    .Select(w => w.Hwnd)
                    .ToList();
                try
                {
                    foreach (var pair in BrowserProbe.ReadActiveUrls(handles, 2500))
                    {
                        var key = UrlKey(pair.Value);
                        if (key.Length > 0) openUrls.Add(key);
                    }
                    foreach (var pair in BrowserProbe.ReadAllTabUrlsForWindows(handles, 10, 8000))
                    {
                        foreach (var url in pair.Value)
                        {
                            var key = UrlKey(url);
                            if (key.Length > 0) openUrls.Add(key);
                        }
                    }
                }
                catch
                {
                }
                var keyList = openUrls.OrderBy(k => k).Take(8).ToList();
                logLines.Add("已打开的网页: " + openUrls.Count + " 个（恢复时不会重复打开）" +
                             (keyList.Count > 0 ? "：" + string.Join("、", keyList) : ""));
                logLines.Add("");
            }

            int idx = 0;
            foreach (var item in layout.Items ?? new List<LayoutItem>())
            {
                idx++;
                string itemDesc = DescribeItem(item);
                logLines.Add("[" + idx + "] " + itemDesc);

                IntPtr hwnd = IntPtr.Zero;
                string failReason = null;

                if (EqualsIgnoreCase(item.Kind, "explorer"))
                {
                    hwnd = FindOrOpenExplorer(item.Path, explorerWindows, out failReason);
                }
                else if (EqualsIgnoreCase(item.Kind, "browser"))
                {
                    hwnd = FindOrOpenBrowser(item, topWindows, used, openUrls, out failReason);
                }
                else if (EqualsIgnoreCase(item.Kind, "app"))
                {
                    hwnd = FindOrOpenApp(item, topWindows, used, out failReason);
                }
                else
                {
                    failReason = "未知类型: " + item.Kind;
                }

                if (hwnd == IntPtr.Zero)
                {
                    // 托盘常驻程序已在运行、没有可摆的窗口：只提示，不算失败
                    if (failReason != null && failReason.StartsWith("INFO:", StringComparison.Ordinal))
                    {
                        var note = failReason.Substring(5);
                        logLines.Add("    ○ " + note);
                        continue;
                    }

                    failed++;
                    var detail = itemDesc + " → " + (failReason ?? "未找到窗口");
                    failDetails.Add(detail);
                    logLines.Add("    ✗ 失败: " + (failReason ?? "未找到窗口"));
                    continue;
                }

                var target = WindowTools.GetTargetBounds(item, layout.Screens);
                if (WindowTools.MoveWindow(hwnd, target.X, target.Y, target.Width, target.Height))
                {
                    ok++;
                    used.Add(hwnd);
                    logLines.Add("    ✓ 已移动到 (" + target.X + "," + target.Y + ") " + target.Width + "x" + target.Height + "（窗口: " + GetWindowTitleSafe(hwnd) + "）");
                }
                else
                {
                    failed++;
                    var detail = itemDesc + " → 移动窗口失败";
                    failDetails.Add(detail);
                    logLines.Add("    ✗ 移动窗口失败");
                }
            }

            logLines.Add("");
            logLines.Add("结果: 成功 " + ok + "，失败 " + failed);
            if (failDetails.Count > 0)
            {
                logLines.Add("失败详情:");
                foreach (var d in failDetails)
                {
                    logLines.Add("  - " + d);
                }
            }

            try
            {
                var logDir = Path.Combine(baseDir, "logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "restore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
                File.WriteAllLines(logPath, logLines.ToArray(), Encoding.UTF8);
            }
            catch { }

            return new RestoreResult
            {
                Ok = ok,
                Failed = failed,
                Message = failed > 0 ? "已恢复 " + ok + "，失败 " + failed : "已恢复窗口布局 " + ok,
                FailDetails = failDetails
            };
        }

        private static string GetWindowTitleSafe(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return "";
                var builder = new StringBuilder(256);
                if (Native.GetWindowText(hwnd, builder, builder.Capacity) <= 0) return "";
                return builder.ToString();
            }
            catch { return ""; }
        }

        private static string DescribeItem(LayoutItem item)
        {
            if (EqualsIgnoreCase(item.Kind, "explorer"))
            {
                return "资源管理器: " + item.Path;
            }
            if (EqualsIgnoreCase(item.Kind, "browser"))
            {
                return "浏览器: " + (item.Browser ?? "") + " - " + (item.Url ?? "");
            }
            if (EqualsIgnoreCase(item.Kind, "app"))
            {
                return "应用: " + (item.ProcessName ?? "") + " - " + (item.Title ?? "");
            }
            return item.Kind + ": " + (item.Title ?? "");
        }

        public void DeleteLayout(LayoutSummary summary)
        {
            if (summary == null || string.IsNullOrWhiteSpace(summary.Path)) return;
            if (File.Exists(summary.Path)) File.Delete(summary.Path);
        }

        public LayoutConfig GetLayout(LayoutSummary summary)
        {
            if (summary == null || string.IsNullOrWhiteSpace(summary.Path) || !File.Exists(summary.Path)) return null;
            return ReadLayout(summary.Path);
        }

        public HealthReport CheckHealth(LayoutSummary summary)
        {
            var report = new HealthReport { Issues = new List<HealthIssue>() };
            var layout = GetLayout(summary);
            if (layout == null)
            {
                report.Issues.Add(new HealthIssue { Level = "error", Message = "布局文件不存在或无法读取。" });
                return report;
            }

            var openWindows = WindowTools.GetTopLevelWindows();
            foreach (var item in layout.Items ?? new List<LayoutItem>())
            {
                if (EqualsIgnoreCase(item.Kind, "explorer") && !string.IsNullOrWhiteSpace(item.Path) && !Directory.Exists(item.Path))
                {
                    report.Issues.Add(new HealthIssue { Level = "error", Message = "文件夹不存在：" + item.Path });
                }
                else if (EqualsIgnoreCase(item.Kind, "app") && !string.IsNullOrWhiteSpace(item.ExePath) && !File.Exists(item.ExePath))
                {
                    var isOpen = openWindows.Any(w => EqualsIgnoreCase(w.ProcessName, item.ProcessName));
                    if (!isOpen)
                    {
                        report.Issues.Add(new HealthIssue { Level = "warning", Message = "软件路径失效：" + (item.Title ?? item.ProcessName ?? item.ExePath) });
                    }
                }
                else if (EqualsIgnoreCase(item.Kind, "browser"))
                {
                    if (string.IsNullOrWhiteSpace(item.Profile))
                    {
                        // 旧布局没有配置信息，按原来方式打开即可，不弹确认框打扰
                        report.Issues.Add(new HealthIssue
                        {
                            Level = "info",
                            Message = "旧布局未记录浏览器配置，会按当前默认配置打开：" + (item.Title ?? item.ProfileName ?? "")
                        });
                    }
                    else if (EqualsIgnoreCase(item.Browser, "bit") && BitBrowserApi.ListEnvironments().Count == 0)
                    {
                        report.Issues.Add(new HealthIssue
                        {
                            Level = "warning",
                            Message = "比特浏览器客户端没有运行，环境「" + (item.ProfileName ?? item.Profile) + "」可能打不开。"
                        });
                    }
                }
            }

            var savedCount = layout.Screens == null ? 0 : layout.Screens.Count;
            var currentCount = Screen.AllScreens.Length;
            if (savedCount > 0 && savedCount != currentCount)
            {
                report.Issues.Add(new HealthIssue { Level = "warning", Message = "保存时有 " + savedCount + " 台显示器，当前有 " + currentCount + " 台；窗口会自动适配。" });
            }
            return report;
        }

        public LayoutConfig RenameLayout(LayoutSummary summary, string newName)
        {
            if (summary == null || string.IsNullOrWhiteSpace(summary.Path) || !File.Exists(summary.Path))
            {
                throw new InvalidOperationException("先选择一个布局。");
            }

            var safeName = SafeName(newName);
            var newPath = GetLayoutPath(safeName);
            var oldPath = summary.Path;
            if (!EqualsIgnoreCase(oldPath, newPath) && File.Exists(newPath))
            {
                throw new InvalidOperationException("已经有同名布局。");
            }

            var layout = ReadLayout(oldPath);
            layout.Name = safeName;
            WriteLayout(newPath, layout);
            if (!EqualsIgnoreCase(oldPath, newPath) && File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
            return layout;
        }

        public void SaveLayout(string name, LayoutConfig layout)
        {
            if (layout == null) throw new InvalidOperationException("布局内容为空。");
            var safeName = SafeName(string.IsNullOrWhiteSpace(name) ? layout.Name : name);
            layout.Name = safeName;
            WriteLayout(GetLayoutPath(safeName), layout);
        }

        public string ExportLayoutJson(LayoutSummary summary)
        {
            if (summary == null || !File.Exists(summary.Path))
            {
                throw new InvalidOperationException("先选择一个布局。");
            }

            var exportRoot = System.IO.Path.Combine(baseDir, "exports");
            Directory.CreateDirectory(exportRoot);
            var safeName = SafeName(summary.Name);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var jsonPath = System.IO.Path.Combine(exportRoot, safeName + "-" + stamp + ".json");
            File.Copy(summary.Path, jsonPath, false);
            return jsonPath;
        }

        public LayoutConfig ImportLayoutJson(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new InvalidOperationException("没有找到要导入的布局文件。");
            }

            var imported = ReadLayout(sourcePath);
            var baseName = string.IsNullOrWhiteSpace(imported.Name)
                ? System.IO.Path.GetFileNameWithoutExtension(sourcePath)
                : imported.Name;
            var safeName = SafeName(baseName);
            var targetPath = GetUniqueLayoutPath(safeName, out safeName);
            imported.Name = safeName;
            if (string.IsNullOrWhiteSpace(imported.SavedAt))
            {
                imported.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            WriteLayout(targetPath, imported);
            return imported;
        }

        public string GetLayoutPath(string name)
        {
            return System.IO.Path.Combine(layoutDir, SafeName(name) + ".json");
        }

        private string GetUniqueLayoutPath(string name, out string finalName)
        {
            var baseName = SafeName(name);
            finalName = baseName;
            var path = GetLayoutPath(finalName);
            if (!File.Exists(path)) return path;

            for (int i = 2; i < 1000; i++)
            {
                finalName = baseName + "-" + i.ToString("00");
                path = GetLayoutPath(finalName);
                if (!File.Exists(path)) return path;
            }

            finalName = baseName + "-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return GetLayoutPath(finalName);
        }

        private IntPtr FindOrOpenExplorer(string path, List<ExplorerInfo> existing, out string failReason)
        {
            failReason = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                failReason = "路径为空";
                return IntPtr.Zero;
            }
            var target = FullPathTrim(path);
            var found = existing.FirstOrDefault(w => EqualsIgnoreCase(FullPathTrim(w.Path), target));
            if (found != null) return found.Hwnd;
            if (!Directory.Exists(path))
            {
                failReason = "文件夹不存在: " + path;
                return IntPtr.Zero;
            }

            try
            {
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                failReason = "启动资源管理器失败: " + ex.Message;
                return IntPtr.Zero;
            }

            var created = WaitFor(() =>
            {
                return WindowTools.GetExplorerWindows(WindowTools.GetTopLevelWindows(), true)
                    .FirstOrDefault(w => EqualsIgnoreCase(FullPathTrim(w.Path), target));
            }, 4500);
            if (created == null)
            {
                failReason = "等待资源管理器窗口超时";
                return IntPtr.Zero;
            }
            return created.Hwnd;
        }

        private IntPtr FindOrOpenBrowser(LayoutItem item, List<WindowInfo> existing, HashSet<IntPtr> used, HashSet<string> openUrls, out string failReason)
        {
            failReason = null;
            var family = string.IsNullOrWhiteSpace(item.Browser) ? "other" : item.Browser;
            var processName = ResolveBrowserProcessName(family, item.ProcessName);
            if (string.IsNullOrWhiteSpace(item.ProcessName) && !string.IsNullOrWhiteSpace(item.ExePath))
            {
                processName = System.IO.Path.GetFileNameWithoutExtension(item.ExePath);
            }
            var allUrls = item.UrlList;

            // 已经开着的不再打开，只补真正缺的那几个
            var urls = new List<string>();
            foreach (var u in allUrls)
            {
                var key = UrlKey(u);
                if (key.Length > 0 && openUrls != null && openUrls.Contains(key)) continue;
                urls.Add(u);
            }
            var profile = (item.Profile ?? "").Trim();
            var keyword = string.IsNullOrWhiteSpace(item.TitleKeyword) ? (item.Title ?? "") : item.TitleKeyword;
            var label = BrowserProbe.FamilyLabel(family) + (string.IsNullOrWhiteSpace(item.ProfileName) ? (string.IsNullOrWhiteSpace(profile) ? "" : " / " + profile) : " / " + item.ProfileName);

            // 1) 先复用“同一个用户配置”已经打开的窗口，避免开重
            var candidates = existing
                .Where(w => !used.Contains(w.Hwnd) && IsSameBrowserFamily(w, family, processName))
                .OrderBy(w => w.IsMinimized ? 1 : 0)
                .ThenBy(w => w.X).ThenBy(w => w.Y)
                .ToList();

            WindowInfo found = null;
            var itemDir = (item.UserDataDir ?? "").Trim().TrimEnd('\\', '/');
            var expectCustomDir = !string.IsNullOrWhiteSpace(itemDir) && !BrowserProbe.IsStandardUserDataDir(itemDir, family);
            var profileName = (item.ProfileName ?? "").Trim();

            // 先按“配置显示名”复用（Chrome 运行时最可靠的判据）
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                foreach (var candidate in candidates)
                {
                    var info = BrowserProbe.Probe(candidate.ProcessId, candidate.ExePath, candidate.Title, candidate.Hwnd);
                    if (info == null) continue;
                    if (!string.IsNullOrWhiteSpace(info.ProfileName) && BrowserProbe.NamesCompatible(info.ProfileName, profileName))
                    {
                        if (expectCustomDir)
                        {
                            var candidateDir = (info.UserDataDir ?? "").Trim().TrimEnd('\\', '/');
                            if (!string.Equals(candidateDir, itemDir, StringComparison.OrdinalIgnoreCase)) continue;
                        }
                        found = candidate;
                        break;
                    }
                }
            }

            // 再按配置目录匹配（浏览器冷启动时命令行带 --profile-directory 的场景）
            if (found == null && !string.IsNullOrWhiteSpace(profile))
            {
                foreach (var candidate in candidates)
                {
                    var info = BrowserProbe.Probe(candidate.ProcessId, candidate.ExePath, candidate.Title, candidate.Hwnd);
                    if (info == null) continue;
                    if (!string.Equals((info.Profile ?? "").Trim(), profile, StringComparison.OrdinalIgnoreCase)) continue;
                    if (expectCustomDir)
                    {
                        var candidateDir = (info.UserDataDir ?? "").Trim().TrimEnd('\\', '/');
                        if (!string.Equals(candidateDir, itemDir, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    found = candidate;
                    break;
                }
            }

            // 标题/域名兜底只给“没有写明配置”的条目用：写明了配置却去抢别的窗口，
            // 会把别的配置的窗口挪走，比新开一个正确配置的窗口糟糕得多
            if (found == null && candidates.Count > 0 &&
                string.IsNullOrWhiteSpace(profile) && string.IsNullOrWhiteSpace(profileName))
            {
                var domain = GetDomain(urls.Count > 0 ? urls[0] : item.Url);
                found = candidates.FirstOrDefault(w =>
                {
                    // 已经识别出别的配置的窗口不能抢，否则多开时会张冠李戴
                    var info = BrowserProbe.Probe(w.ProcessId, w.ExePath, w.Title, w.Hwnd);
                    if (info != null && (!string.IsNullOrWhiteSpace(info.Profile) || !string.IsNullOrWhiteSpace(info.ProfileName))) return false;
                    return (!string.IsNullOrWhiteSpace(keyword) && ContainsIgnoreCase(w.Title, keyword)) ||
                           (!string.IsNullOrWhiteSpace(domain) && ContainsIgnoreCase(w.Title, domain));
                });
            }

            if (found != null)
            {
                // 复用窗口时，这个窗口里“所有标签页”的网址都算已经打开，不只是当前显示的那一页。
                // 否则保存的网址正好开在后台标签里时，会被当成没开过而重复开一遍。
                var windowKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var openedKey = UrlKey(BrowserProbe.ReadActiveUrl(found.Hwnd));
                if (openedKey.Length > 0) windowKeys.Add(openedKey);
                try
                {
                    foreach (var tabUrl in BrowserProbe.ReadAllTabUrls(found.Hwnd, 20, 6000))
                    {
                        var tabKey = UrlKey(tabUrl);
                        if (tabKey.Length > 0) windowKeys.Add(tabKey);
                    }
                }
                catch { }
                var pending = urls
                    .Where(u => !windowKeys.Contains(UrlKey(u)))
                    .ToList();
                if (pending.Count > 0)
                {
                    OpenBrowserTabs(family, item, found.Hwnd, pending);
                    if (openUrls != null)
                    {
                        foreach (var u in pending) openUrls.Add(UrlKey(u));
                    }
                }
                return found.Hwnd;
            }

            // 网址全都已经开着、又没有对应配置的窗口：不用再开新窗口
            if (allUrls.Count > 0 && urls.Count == 0)
            {
                failReason = "INFO:这 " + allUrls.Count + " 个网页已经在其它窗口打开，跳过";
                return IntPtr.Zero;
            }

            // 2) 打开新窗口
            var before = new HashSet<IntPtr>(existing
                .Where(w => IsSameBrowserFamily(w, family, processName))
                .Select(w => w.Hwnd));

            if (!LaunchBrowserWindow(family, item, urls, out failReason))
            {
                return IntPtr.Zero;
            }

            var created = WaitFor(() =>
            {
                var latest = WindowTools.GetTopLevelWindows();
                var fresh = latest
                    .Where(w => !before.Contains(w.Hwnd) && !used.Contains(w.Hwnd) && IsSameBrowserFamily(w, family, processName))
                    .OrderBy(w => w.X).ThenBy(w => w.Y)
                    .FirstOrDefault();
                if (fresh != null) return fresh;
                // 新窗口没等到时的兜底：排除本来就在的窗口，且带配置的条目
                // 只认“配置对得上”的窗口，避免把别的配置的窗口挪走
                return latest
                    .Where(w => !before.Contains(w.Hwnd) && !used.Contains(w.Hwnd) && IsSameBrowserFamily(w, family, processName))
                    .Where(w =>
                    {
                        if (string.IsNullOrWhiteSpace(profile) && string.IsNullOrWhiteSpace(profileName)) return true;
                        var info = BrowserProbe.Probe(w.ProcessId, w.ExePath, w.Title, w.Hwnd);
                        if (info == null) return false;
                        if (!string.IsNullOrWhiteSpace(profile) &&
                            string.Equals((info.Profile ?? "").Trim(), profile, StringComparison.OrdinalIgnoreCase)) return true;
                        if (!string.IsNullOrWhiteSpace(profileName) && !string.IsNullOrWhiteSpace(info.ProfileName) &&
                            BrowserProbe.NamesCompatible(info.ProfileName, profileName)) return true;
                        return false;
                    })
                    .Where(w => string.IsNullOrWhiteSpace(keyword) || ContainsIgnoreCase(w.Title, keyword))
                    .OrderBy(w => w.X).ThenBy(w => w.Y)
                    .FirstOrDefault();
            }, 10000);

            if (created == null)
            {
                failReason = "等待浏览器窗口超时（" + label + "）";
                return IntPtr.Zero;
            }

            // 比特浏览器环境打开后只带一个空白/上次页面：第一个网址用当前页，其余开新标签，不能互相顶掉
            if (string.Equals(family, "bit", StringComparison.OrdinalIgnoreCase) && urls.Count > 0)
            {
                Thread.Sleep(600);
                var firstBit = true;
                foreach (var url in urls)
                {
                    Native.NavigateInWindow(created.Hwnd, url, !firstBit);
                    firstBit = false;
                }
            }
            return created.Hwnd;
        }

        private static string ResolveBrowserProcessName(string family, string fallback)
        {
            if (EqualsIgnoreCase(family, "edge")) return "msedge";
            if (EqualsIgnoreCase(family, "chrome")) return "chrome";
            return string.IsNullOrWhiteSpace(fallback) ? (family ?? "") : fallback;
        }

        private static bool IsSameBrowserFamily(WindowInfo window, string family, string processName)
        {
            if (window == null) return false;
            if (EqualsIgnoreCase(family, "bit"))
            {
                var info = BrowserProbe.Probe(window.ProcessId, window.ExePath, window.Title, window.Hwnd);
                return info != null && EqualsIgnoreCase(info.Family, "bit");
            }
            return EqualsIgnoreCase(window.ProcessName, processName);
        }

        private static string ResolveBrowserExe(string family, string processName)
        {
            if (EqualsIgnoreCase(family, "edge")) return "msedge.exe";
            if (EqualsIgnoreCase(family, "chrome")) return "chrome.exe";
            if (!string.IsNullOrWhiteSpace(processName) && !processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return processName + ".exe";
            }
            return processName;
        }

        /// <summary>组装“用哪个用户配置打开”的命令行参数：多开浏览器靠 user-data-dir 区分实例。</summary>
        private static string BrowserProfileArgs(LayoutItem item, string family)
        {
            var builder = new StringBuilder();
            var userDataDir = item.UserDataDir;
            if (!string.IsNullOrWhiteSpace(userDataDir) && !BrowserProbe.IsStandardUserDataDir(userDataDir, family))
            {
                builder.Append("--user-data-dir=").Append(Quote(userDataDir)).Append(' ');
            }
            var profile = (item.Profile ?? "").Trim();
            // 旧布局或界面树识别只留下配置名时，按名反查配置目录，否则会打开到默认配置上
            if (string.IsNullOrWhiteSpace(profile) && !string.IsNullOrWhiteSpace(item.ProfileName))
            {
                profile = ChromeProfiles.FindDirectory(family, item.ProfileName, "").Trim();
            }
            if (!string.IsNullOrWhiteSpace(profile))
            {
                builder.Append("--profile-directory=").Append(Quote(profile)).Append(' ');
            }
            return builder.ToString();
        }

        private bool LaunchBrowserWindow(string family, LayoutItem item, List<string> urls, out string failReason)
        {
            failReason = null;
            var profile = (item.Profile ?? "").Trim();
            var exe = item.ExePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                exe = ResolveBrowserExe(family, item.ProcessName);
            }

            if (EqualsIgnoreCase(family, "bit"))
            {
                string apiError = null;
                if (!string.IsNullOrWhiteSpace(profile) && BitBrowserApi.OpenEnvironment(profile, out apiError))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(item.LaunchArgs) && !string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                {
                    try
                    {
                        Process.Start(exe, item.LaunchArgs);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        failReason = "比特浏览器环境启动失败: " + ex.Message;
                        return false;
                    }
                }

                failReason = "比特浏览器没有运行，无法打开环境「" +
                             (string.IsNullOrWhiteSpace(item.ProfileName) ? (profile.Length > 0 ? profile : "未知") : item.ProfileName) +
                             "」" + (string.IsNullOrWhiteSpace(apiError) ? "" : "（" + apiError + "）");
                return false;
            }

            if (string.IsNullOrWhiteSpace(exe))
            {
                failReason = "找不到浏览器程序";
                return false;
            }

            var firstUrl = urls.Count > 0 ? urls[0] : null;
            var profileArgs = BrowserProfileArgs(item, family);
            var args = new StringBuilder();
            args.Append(profileArgs);
            args.Append("--new-window");
            if (!string.IsNullOrWhiteSpace(firstUrl))
            {
                args.Append(' ').Append(Quote(firstUrl));
            }

            try
            {
                Process.Start(exe, args.ToString());
            }
            catch (Exception ex)
            {
                failReason = "启动浏览器失败: " + ex.Message;
                return false;
            }

            if (urls.Count > 1)
            {
                Thread.Sleep(800);
                for (int i = 1; i < urls.Count; i++)
                {
                    try
                    {
                        var tabArgs = profileArgs + Quote(urls[i]);
                        Process.Start(exe, tabArgs);
                    }
                    catch
                    {
                    }
                    Thread.Sleep(250);
                }
            }
            return true;
        }

        private static void OpenBrowserTabs(string family, LayoutItem item, IntPtr hwnd, List<string> urls)
        {
            if (EqualsIgnoreCase(family, "bit"))
            {
                // 第一个网址用当前页打开，其余开新标签；逐个在当前页输入会把前面的顶掉
                var firstBit = true;
                foreach (var url in urls)
                {
                    Native.NavigateInWindow(hwnd, url, !firstBit);
                    firstBit = false;
                }
                return;
            }

            var exe = item.ExePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                exe = ResolveBrowserExe(family, item.ProcessName);
            }
            if (string.IsNullOrWhiteSpace(exe)) return;

            var profileArgs = BrowserProfileArgs(item, family);
            foreach (var url in urls)
            {
                try
                {
                    Process.Start(exe, profileArgs + Quote(url));
                }
                catch
                {
                }
                Thread.Sleep(300);
            }
        }

        private IntPtr FindOrOpenApp(LayoutItem item, List<WindowInfo> existing, HashSet<IntPtr> used, out string failReason)
        {
            failReason = null;
            var processName = item.ProcessName;
            var exePath = FullPathTrim(item.ExePath);
            var keyword = string.IsNullOrWhiteSpace(item.TitleKeyword) ? item.Title : item.TitleKeyword;

            var found = FindBestAppWindow(existing, processName, exePath, keyword, used, null);
            if (found != null) return found.Hwnd;

            if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(exePath))
            {
                failReason = "进程名和路径都为空，无法定位";
                return IntPtr.Zero;
            }

            bool hasExe = !string.IsNullOrWhiteSpace(item.ExePath) && File.Exists(item.ExePath);
            bool isUwp = !string.IsNullOrWhiteSpace(item.ExePath) &&
                         ContainsIgnoreCase(item.ExePath, "\\WindowsApps\\");

            var before = new HashSet<IntPtr>(existing
                .Where(w => IsSameAppWindow(w, processName, exePath))
                .Select(w => w.Hwnd));

            // 程序已经在跑（桌面整理这类托盘常驻工具最常见）：不再启动重复实例，也不再干等窗口。
            // 有主窗口就直接拿来摆位；没有窗口就给一条提示，不算失败。
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var running = Process.GetProcessesByName(processName);
                if (running.Length > 0)
                {
                    foreach (var proc in running)
                    {
                        var handle = GetMainWindowHandleSafe(proc);
                        if (handle == IntPtr.Zero || used.Contains(handle) || before.Contains(handle)) continue;
                        return handle;
                    }
                    failReason = "INFO:程序已在运行，但没有可摆放的窗口（托盘常驻工具）";
                    return IntPtr.Zero;
                }
            }

            if (!hasExe && !isUwp && string.IsNullOrWhiteSpace(processName))
            {
                failReason = "程序路径不存在且无进程名: " + (item.ExePath ?? "(空)");
                return IntPtr.Zero;
            }

            bool launched = false;
            string launchError = null;

            if (hasExe)
            {
                try
                {
                    Process.Start(item.ExePath);
                    launched = true;
                }
                catch (Exception ex)
                {
                    launchError = ex.Message;
                }
            }

            if (!launched && isUwp)
            {
                try
                {
                    var psi = new ProcessStartInfo();
                    psi.FileName = "explorer.exe";
                    psi.Arguments = "shell:appsFolder\\" + GetUwpAppUserModelId(item.ExePath, processName);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    launched = true;
                }
                catch (Exception ex)
                {
                    launchError = (launchError ?? "") + " UWP启动也失败: " + ex.Message;
                }
            }

            if (!launched && !string.IsNullOrWhiteSpace(processName))
            {
                var existingProc = Process.GetProcessesByName(processName)
                    .FirstOrDefault(p => !before.Contains(GetMainWindowHandleSafe(p)));
                if (existingProc != null && existingProc.MainWindowHandle != IntPtr.Zero)
                {
                    return existingProc.MainWindowHandle;
                }

                failReason = "无法启动程序，路径不存在或无权限: " + (item.ExePath ?? "(空)") +
                             (launchError != null ? " (" + launchError + ")" : "");
                return IntPtr.Zero;
            }

            if (!launched)
            {
                failReason = "无法启动: " + (launchError ?? "未知原因");
                return IntPtr.Zero;
            }

            var created = WaitFor(() =>
            {
                var latest = WindowTools.GetTopLevelWindows();
                var fresh = FindBestAppWindow(latest, processName, exePath, keyword, used, before);
                if (fresh != null) return fresh;
                return FindBestAppWindow(latest, processName, exePath, "", used, before);
            }, 8000);

            if (created == null)
            {
                failReason = "等待程序窗口超时 (进程=" + (processName ?? "?") + ")";
                return IntPtr.Zero;
            }
            return created.Hwnd;
        }

        private static IntPtr GetMainWindowHandleSafe(Process p)
        {
            try { return p.MainWindowHandle; }
            catch { return IntPtr.Zero; }
        }

        private static string GetUwpAppUserModelId(string exePath, string processName)
        {
            var path = exePath ?? "";
            var idx = path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return processName ?? "";

            var after = path.Substring(idx + "\\WindowsApps\\".Length);
            var parts = after.Split('\\');
            if (parts.Length >= 2)
            {
                var pkgName = parts[0];
                var underscoreIdx = pkgName.IndexOf('_');
                if (underscoreIdx > 0)
                {
                    var pubPart = pkgName.Substring(underscoreIdx + 1);
                    var nextUnderscore = pubPart.IndexOf('_');
                    if (nextUnderscore > 0)
                    {
                        var publisher = pubPart.Substring(nextUnderscore + 1);
                        return pkgName.Substring(0, underscoreIdx) + "_" + publisher + "!" +
                               (processName ?? "App");
                    }
                }
            }
            return processName ?? "";
        }

        private static WindowInfo FindBestAppWindow(List<WindowInfo> windows, string processName, string exePath, string keyword, HashSet<IntPtr> used, HashSet<IntPtr> exclude)
        {
            var candidates = windows
                .Where(w => !used.Contains(w.Hwnd))
                .Where(w => exclude == null || !exclude.Contains(w.Hwnd))
                .Where(w => IsSameAppWindow(w, processName, exePath))
                .ToList();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var titled = candidates
                    .Where(w => ContainsIgnoreCase(w.Title, keyword))
                    .OrderBy(w => w.IsMinimized ? 1 : 0)
                    .ThenBy(w => w.X).ThenBy(w => w.Y)
                    .FirstOrDefault();
                if (titled != null) return titled;
            }

            return candidates
                .OrderBy(w => w.IsMinimized ? 1 : 0)
                .ThenBy(w => w.X).ThenBy(w => w.Y)
                .FirstOrDefault();
        }

        private static bool IsSameAppWindow(WindowInfo window, string processName, string exePath)
        {
            if (!string.IsNullOrWhiteSpace(processName) && EqualsIgnoreCase(window.ProcessName, processName))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(exePath))
            {
                var windowExe = FullPathTrim(string.IsNullOrWhiteSpace(window.ExePath) ? WindowTools.GetProcessPath(window.ProcessId) : window.ExePath);
                return EqualsIgnoreCase(windowExe, exePath);
            }

            return false;
        }

        private static T WaitFor<T>(Func<T> getter, int timeoutMs) where T : class
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var value = getter();
                if (value != null) return value;
                Thread.Sleep(100);
            }
            return null;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string GetDomain(string url)
        {
            try
            {
                Uri uri;
                return Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri.Host.Replace("www.", "") : "";
            }
            catch { return ""; }
        }

        /// <summary>把网址化成可比较的键：忽略协议、www、末尾斜杠和大小写。</summary>
        private static string UrlKey(string url)
        {
            var value = BrowserProbe.NormalizeUrl(url);
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();

            var scheme = value.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0) value = value.Substring(scheme + 3);
            if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) value = value.Substring(4);
            value = value.TrimEnd('/');
            return value.ToLowerInvariant();
        }

        private static string FullPathTrim(string path)
        {
            try { return System.IO.Path.GetFullPath(path).TrimEnd('\\'); }
            catch { return (path ?? "").TrimEnd('\\'); }
        }

        private static bool IsIgnoredAppWindow(WindowInfo window, string exePath, string currentExe)
        {
            if (window == null) return true;
            if (string.IsNullOrWhiteSpace(window.Title)) return true;
            if (window.W < 120 || window.H < 90) return true;
            if (EqualsIgnoreCase(window.Title, "Program Manager")) return true;

            var processName = window.ProcessName ?? "";
            var ignoredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "explorer",
                "dwm",
                "sihost",
                "ShellExperienceHost",
                "StartMenuExperienceHost",
                "SearchHost",
                "TextInputHost",
                "ApplicationFrameHost",
                "SystemSettings",
                "RuntimeBroker",
                "LockApp",
                "SpeedBall",
                "msedgewebview2"
            };

            if (ignoredProcesses.Contains(processName)) return true;
            if (!string.IsNullOrWhiteSpace(exePath) && EqualsIgnoreCase(FullPathTrim(exePath), currentExe)) return true;

            // 浏览器进程里剩下的窗口都是气泡/对话框（“翻译此页？”之类），不是真正的应用窗口
            if (BrowserProbe.IsBrowserProcessName(processName)) return true;
            return false;
        }

        private static string TitleKeywordFromTitle(string title)
        {
            var value = (title ?? "").Trim();
            if (value.Length > 60)
            {
                value = value.Substring(0, 60);
            }
            return value;
        }

        public static string SafeName(string name)
        {
            var value = string.IsNullOrWhiteSpace(name) ? "未命名布局" : name.Trim();
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            value = value.Trim(' ', '.');
            return string.IsNullOrWhiteSpace(value) ? "未命名布局" : value;
        }

        public static string InferTaskMode(string name)
        {
            var value = name ?? "";
            if (ContainsIgnoreCase(value, "素材")) return "素材处理";
            if (ContainsIgnoreCase(value, "小红书") || ContainsIgnoreCase(value, "写作") || ContainsIgnoreCase(value, "文案")) return "写小红书";
            if (ContainsIgnoreCase(value, "剪辑") || ContainsIgnoreCase(value, "视频")) return "剪辑";
            if (ContainsIgnoreCase(value, "团建") || ContainsIgnoreCase(value, "方案")) return "做团建方案";
            if (ContainsIgnoreCase(value, "客服")) return "客服工作";
            if (ContainsIgnoreCase(value, "运营")) return "运营";
            return "工作";
        }

        private static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return (text ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static LayoutConfig ReadLayout(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (!string.IsNullOrEmpty(text) && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                var serializer = new DataContractJsonSerializer(typeof(LayoutConfig));
                return (LayoutConfig)serializer.ReadObject(stream);
            }
        }

        private static void WriteLayout(string path, LayoutConfig layout)
        {
            using (var stream = File.Create(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(LayoutConfig));
                serializer.WriteObject(stream, layout);
            }
        }
    }

    public class RestoreResult
    {
        public int Ok { get; set; }
        public int Failed { get; set; }
        public string Message { get; set; }
        public List<string> FailDetails { get; set; }
    }

    public static class WindowTools
    {
        public static List<WindowInfo> GetTopLevelWindows()
        {
            var result = new List<WindowInfo>();
            Native.EnumWindows((hWnd, lParam) =>
            {
                if (!Native.IsWindowVisible(hWnd)) return true;
                bool isMinimized = Native.IsIconic(hWnd);
                int length = Native.GetWindowTextLength(hWnd);
                if (length <= 0) return true;

                var builder = new StringBuilder(length + 1);
                Native.GetWindowText(hWnd, builder, builder.Capacity);
                var title = builder.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                Native.RECT rect;
                if (!Native.GetWindowRect(hWnd, out rect)) return true;
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (!isMinimized && (width < 100 || height < 80)) return true;

                uint pid;
                Native.GetWindowThreadProcessId(hWnd, out pid);
                Process process = null;
                try { process = Process.GetProcessById((int)pid); }
                catch { return true; }

                result.Add(new WindowInfo
                {
                    Hwnd = hWnd,
                    ProcessId = (int)pid,
                    ProcessName = process.ProcessName,
                    Title = title,
                    ExePath = "",
                    IsMinimized = isMinimized,
                    X = rect.Left,
                    Y = rect.Top,
                    W = width,
                    H = height
                });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static string GetProcessPath(int processId)
        {
            try
            {
                return Process.GetProcessById(processId).MainModule.FileName;
            }
            catch
            {
                return "";
            }
        }

        public static List<ExplorerInfo> GetExplorerWindows(List<WindowInfo> topWindows, bool includeMinimized)
        {
            var result = new List<ExplorerInfo>();
            var visible = new HashSet<IntPtr>(topWindows
                .Where(w => string.Equals(w.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(w.Title, "Program Manager", StringComparison.OrdinalIgnoreCase) &&
                            (includeMinimized || !w.IsMinimized))
                .Select(w => w.Hwnd));

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(shellType);
                foreach (object rawWindow in shell.Windows())
                {
                    try
                    {
                        dynamic window = rawWindow;
                        string fullName = Convert.ToString(window.FullName);
                        if (!string.Equals(System.IO.Path.GetFileName(fullName), "explorer.exe", StringComparison.OrdinalIgnoreCase))
                            continue;

                        IntPtr hwnd = new IntPtr(Convert.ToInt64(window.HWND));
                        if (!visible.Contains(hwnd)) continue;

                        string path = Convert.ToString(window.Document.Folder.Self.Path);
                        if (string.IsNullOrWhiteSpace(path)) continue;

                        Native.RECT rect;
                        if (!Native.GetWindowRect(hwnd, out rect)) continue;
                        result.Add(new ExplorerInfo
                        {
                            Hwnd = hwnd,
                            Path = path,
                            X = rect.Left,
                            Y = rect.Top,
                            W = rect.Right - rect.Left,
                            H = rect.Bottom - rect.Top
                        });
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        public static List<SavedScreen> GetSavedScreens()
        {
            var result = new List<SavedScreen>();
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var area = screens[i].WorkingArea;
                result.Add(new SavedScreen
                {
                    Device = screens[i].DeviceName,
                    Index = i,
                    X = area.X,
                    Y = area.Y,
                    W = area.Width,
                    H = area.Height,
                    Primary = screens[i].Primary
                });
            }
            return result;
        }

        public static void AttachScreenPosition(LayoutItem item, List<SavedScreen> screens)
        {
            if (item == null || screens == null || screens.Count == 0) return;
            var center = new Point(item.X + Math.Max(1, item.W) / 2, item.Y + Math.Max(1, item.H) / 2);
            var screen = screens.FirstOrDefault(s => new Rectangle(s.X, s.Y, s.W, s.H).Contains(center)) ?? screens.FirstOrDefault(s => s.Primary) ?? screens[0];
            item.ScreenDevice = screen.Device;
            item.ScreenIndex = screen.Index;
            item.RelativeX = screen.W <= 0 ? 0 : (item.X - screen.X) / (double)screen.W;
            item.RelativeY = screen.H <= 0 ? 0 : (item.Y - screen.Y) / (double)screen.H;
            item.RelativeW = screen.W <= 0 ? 0 : item.W / (double)screen.W;
            item.RelativeH = screen.H <= 0 ? 0 : item.H / (double)screen.H;
        }

        public static Rectangle GetTargetBounds(LayoutItem item, List<SavedScreen> savedScreens)
        {
            var fallback = new Rectangle(item.X, item.Y, Math.Max(240, item.W), Math.Max(160, item.H));
            var current = Screen.AllScreens;
            if (current == null || current.Length == 0) return fallback;

            Screen target = current.FirstOrDefault(s => string.Equals(s.DeviceName, item.ScreenDevice, StringComparison.OrdinalIgnoreCase));
            if (target == null && item.ScreenIndex >= 0 && item.ScreenIndex < current.Length) target = current[item.ScreenIndex];
            if (target == null) target = Screen.PrimaryScreen ?? current[0];
            var area = target.WorkingArea;

            bool hasRelative = item.RelativeW > 0.05 && item.RelativeH > 0.05;
            var rect = hasRelative
                ? new Rectangle(
                    area.X + (int)Math.Round(item.RelativeX * area.Width),
                    area.Y + (int)Math.Round(item.RelativeY * area.Height),
                    (int)Math.Round(item.RelativeW * area.Width),
                    (int)Math.Round(item.RelativeH * area.Height))
                : fallback;

            rect.Width = Math.Max(240, Math.Min(rect.Width, area.Width));
            rect.Height = Math.Max(160, Math.Min(rect.Height, area.Height));
            rect.X = Math.Max(area.Left, Math.Min(rect.X, area.Right - rect.Width));
            rect.Y = Math.Max(area.Top, Math.Min(rect.Y, area.Bottom - rect.Height));
            return rect;
        }

        public static bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height)
        {
            if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return false;
            bool wasMinimized = Native.IsIconic(hwnd);
            Native.ShowWindowAsync(hwnd, 9);
            if (wasMinimized) Thread.Sleep(60);
            return Native.MoveWindow(hwnd, x, y, width, height, true);
        }
    }

    public class BrowserLaunchInfo
    {
        public string Family { get; set; }
        public string Profile { get; set; }
        public string ProfileName { get; set; }
        public string UserDataDir { get; set; }
        public string ExePath { get; set; }
        public string CommandLine { get; set; }
    }

    /// <summary>识别浏览器窗口属于哪个用户配置，并读取当前标签页地址。</summary>
    public static class BrowserProbe
    {
        private static readonly object Sync = new object();
        // 同一个浏览器进程里可以同时存在多个不同配置的窗口（Chrome 就是这样），
        // 所以缓存必须按窗口句柄做键，按进程 ID 缓存会把第一个窗口的配置名套到所有窗口上。
        private static readonly Dictionary<IntPtr, BrowserLaunchInfo> Cache = new Dictionary<IntPtr, BrowserLaunchInfo>();
        private static DateTime cacheStamp = DateTime.MinValue;

        private static readonly HashSet<string> ChromiumProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "brave", "vivaldi", "chromium", "opera", "yandex",
            "360chrome", "360ChromeX", "360se", "360se6", "qqbrowser", "sogouexplorer",
            "ucbrowser", "liebao", "maxthon", "2345explorer", "centbrowser", "qiyu", "twinkstar"
        };

        private static readonly Dictionary<string, bool> ChromiumDirCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>是不是标准浏览器进程（用来区分“浏览器自己的气泡窗口”和“普通应用”）。</summary>
        public static bool IsBrowserProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            if (ChromiumProcessNames.Contains(processName)) return true;
            return EqualsIgnoreCase(processName, "firefox") || EqualsIgnoreCase(processName, "iexplore");
        }

        public static string FamilyLabel(string family)
        {
            if (EqualsIgnoreCase(family, "chrome")) return "Chrome";
            if (EqualsIgnoreCase(family, "edge")) return "Edge";
            if (EqualsIgnoreCase(family, "bit")) return "比特浏览器";
            if (EqualsIgnoreCase(family, "chromium")) return "其他 Chromium 浏览器";
            if (string.IsNullOrWhiteSpace(family)) return "浏览器";
            return family;
        }

        /// <summary>Chromium 内核浏览器都可以用这套方式识别配置，不限于 Chrome / Edge。</summary>
        private static bool LooksLikeChromium(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return false;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(exePath);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;

                lock (Sync)
                {
                    bool hit;
                    if (ChromiumDirCache.TryGetValue(dir, out hit)) return hit;
                }

                var result = File.Exists(System.IO.Path.Combine(dir, "chrome_100_percent.pak"))
                             || File.Exists(System.IO.Path.Combine(dir, "chrome.dll"))
                             || (File.Exists(System.IO.Path.Combine(dir, "icudtl.dat")) && File.Exists(System.IO.Path.Combine(dir, "chrome_elf.dll")));

                lock (Sync) { ChromiumDirCache[dir] = result; }
                return result;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>是否是浏览器自带的默认用户目录（不是多开用的独立目录）。</summary>
        public static bool IsStandardUserDataDir(string userDataDir, string family)
        {
            if (string.IsNullOrWhiteSpace(userDataDir)) return true;
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var chromeDefault = System.IO.Path.Combine(local, "Google", "Chrome", "User Data");
            var edgeDefault = System.IO.Path.Combine(local, "Microsoft", "Edge", "User Data");
            var value = userDataDir.TrimEnd('\\', '/');
            if (value.Equals(chromeDefault, StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals(edgeDefault, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return (text ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>窗口标题里的配置名常写成 “Hichem (二次申诉xxx)”，配置目录里存的却是短名，这里取括号内的主体。</summary>
        public static string ProfileCore(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return (value ?? "").Trim();
            var text = value.Trim();
            var match = Regex.Match(text, "\\((.+)\\)\\s*$");
            return match.Success ? match.Groups[1].Value.Trim() : text;
        }

        /// <summary>两个配置名是否指向同一个配置：允许 “人名 (配置名)” 与 “配置名” 互认。</summary>
        public static bool NamesCompatible(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (EqualsIgnoreCase(a, b)) return true;

            var coreA = ProfileCore(a);
            var coreB = ProfileCore(b);
            if (coreA.Length > 0 && EqualsIgnoreCase(coreA, b)) return true;
            if (coreB.Length > 0 && EqualsIgnoreCase(a, coreB)) return true;
            if (coreA.Length > 0 && EqualsIgnoreCase(coreA, coreB)) return true;

            var shorter = a.Length <= b.Length ? a : b;
            var longer = a.Length <= b.Length ? b : a;
            if (shorter.Length >= 6 && longer.IndexOf(shorter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static BrowserLaunchInfo Probe(int processId, string exeHint, string title, IntPtr hwnd)
        {
            lock (Sync)
            {
                if (DateTime.UtcNow - cacheStamp > TimeSpan.FromSeconds(3))
                {
                    Cache.Clear();
                    cacheStamp = DateTime.UtcNow;
                }
                BrowserLaunchInfo cached;
                if (hwnd != IntPtr.Zero && Cache.TryGetValue(hwnd, out cached)) return cached;
            }

            var info = ProbeCore(processId, exeHint, title, hwnd);
            lock (Sync)
            {
                if (hwnd != IntPtr.Zero) Cache[hwnd] = info;
            }
            return info;
        }

        // WMI 查询很慢，而同一个浏览器进程的几十个窗口命令行完全一样，按进程缓存即可
        private static readonly Dictionary<int, KeyValuePair<string, string>> CommandLineCache =
            new Dictionary<int, KeyValuePair<string, string>>();
        private static DateTime commandLineStamp = DateTime.MinValue;

        /// <summary>一次浅层遍历同时取到的窗口信息：配置名、地址栏网址。</summary>
        public class WindowInspect
        {
            public string Url = "";
            public bool Omnibox;
            public string ProfileName = "";
            public string RootName = "";
        }

        // 界面树遍历不便宜，同一个窗口短期内复用一次遍历的结果
        private static readonly Dictionary<IntPtr, WindowInspect> InspectCache = new Dictionary<IntPtr, WindowInspect>();
        private static DateTime inspectStamp = DateTime.MinValue;

        public static WindowInspect Inspect(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return new WindowInspect();

            lock (Sync)
            {
                if (DateTime.UtcNow - inspectStamp < TimeSpan.FromSeconds(30))
                {
                    WindowInspect hit;
                    if (InspectCache.TryGetValue(hwnd, out hit)) return hit;
                }
                else
                {
                    InspectCache.Clear();
                }
            }

            var result = new WindowInspect();
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (root != null)
                {
                    result.RootName = root.Current.Name ?? "";

                    var elements = new List<System.Windows.Automation.AutomationElement>();
                    CollectElements(root, elements, 0, 6, 150);

                    foreach (var element in elements)
                    {
                        string name;
                        try { name = element.Current.Name ?? ""; }
                        catch { continue; }
                        if (name.Length == 0 || !string.IsNullOrEmpty(result.ProfileName)) continue;
                        var match = Regex.Match(name, "^(.*?)\\s+-\\s+(Google Chrome|Microsoft Edge|Chromium|360[^\\s]*)\\s+-\\s+(.+)$");
                        if (match.Success) result.ProfileName = match.Groups[3].Value.Trim();
                    }

                    foreach (var element in elements)
                    {
                        string id;
                        try { id = element.Current.AutomationId ?? ""; }
                        catch { continue; }
                        if (!IsOmniboxId(id)) continue;
                        var value = TryGetValue(element);
                        if (string.IsNullOrWhiteSpace(value)) continue;
                        result.Omnibox = true;
                        result.Url = value.Trim();
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(result.Url))
                    {
                        foreach (var element in elements)
                        {
                            string name;
                            try { name = element.Current.Name ?? ""; }
                            catch { continue; }
                            if (!IsOmniboxName(name)) continue;
                            var value = TryGetValue(element);
                            if (string.IsNullOrWhiteSpace(value)) continue;
                            result.Omnibox = true;
                            result.Url = value.Trim();
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            lock (Sync)
            {
                inspectStamp = DateTime.UtcNow;
                InspectCache[hwnd] = result;
            }
            return result;
        }

        private static string GetCommandLineCached(int processId, out string exePath)
        {
            exePath = null;
            lock (Sync)
            {
                if (DateTime.UtcNow - commandLineStamp > TimeSpan.FromSeconds(60)) CommandLineCache.Clear();
                KeyValuePair<string, string> hit;
                if (CommandLineCache.TryGetValue(processId, out hit))
                {
                    exePath = hit.Value;
                    return hit.Key;
                }
            }

            string exe;
            var commandLine = ProcessQuery.GetCommandLine(processId, out exe);
            lock (Sync)
            {
                commandLineStamp = DateTime.UtcNow;
                CommandLineCache[processId] = new KeyValuePair<string, string>(commandLine, exe);
            }
            exePath = exe;
            return commandLine;
        }

        private static BrowserLaunchInfo ProbeCore(int processId, string exeHint, string title, IntPtr hwnd)
        {
            string exeFromWmi;
            var commandLine = GetCommandLineCached(processId, out exeFromWmi);
            var exe = !string.IsNullOrWhiteSpace(exeFromWmi) ? exeFromWmi : (exeHint ?? "");

            string processName = "";
            try { processName = Process.GetProcessById(processId).ProcessName; }
            catch { }

            var family = DetectFamily(processName, exe, commandLine);
            if (family == null) return null;

            // 界面树里的配置名最可靠：Chrome 运行时会忽略 --profile-directory，一个进程里还能同时跑多个配置
            // 一次遍历同时取配置名和地址栏，分开各查一遍会让保存慢一倍
            var inspect = (hwnd != IntPtr.Zero) ? Inspect(hwnd) : null;
            string rootName = inspect != null ? inspect.RootName : null;
            var treeProfile = inspect != null ? inspect.ProfileName : null;

            // Chromium 系必须能读到地址栏、或从界面树认出配置名，
            // 否则它多半不是浏览器主窗口（翻译气泡、下载气泡、对话框等）
            if (EqualsIgnoreCase(family, "chrome") || EqualsIgnoreCase(family, "edge") || EqualsIgnoreCase(family, "chromium"))
            {
                if (hwnd == IntPtr.Zero) return null;
                var url = inspect != null ? inspect.Url : null;
                if (string.IsNullOrWhiteSpace(treeProfile) && string.IsNullOrWhiteSpace(url)) return null;
            }

            var profileDir = ExtractArgument(commandLine, "--profile-directory");
            var userDataDir = ExtractArgument(commandLine, "--user-data-dir");

            var info = new BrowserLaunchInfo
            {
                Family = family,
                ExePath = exe,
                CommandLine = commandLine,
                UserDataDir = userDataDir
            };

            if (EqualsIgnoreCase(family, "bit"))
            {
                var envId = BitBrowserApi.ResolveEnvironmentId(title, userDataDir);
                if (!string.IsNullOrWhiteSpace(envId))
                {
                    info.Profile = envId;
                    info.ProfileName = BitBrowserApi.NameOf(envId, title);
                }
                else if (!string.IsNullOrWhiteSpace(userDataDir))
                {
                    info.Profile = System.IO.Path.GetFileName(userDataDir.TrimEnd('\\', '/'));
                    info.ProfileName = info.Profile;
                }
                else
                {
                    // 比特浏览器客户端自己的界面窗口也带 bitbrowser 路径，不能当成环境窗口
                    return null;
                }
            }
            else
            {
                // 界面树里的配置名最可靠（Chrome 运行时会忽略 --profile-directory）
                var standardDir = IsStandardUserDataDir(userDataDir, family);

                if (!string.IsNullOrWhiteSpace(treeProfile))
                {
                    info.ProfileName = treeProfile;
                    info.Profile = ChromeProfiles.FindDirectory(family, treeProfile, profileDir);
                }
                else if (!standardDir)
                {
                    var folder = System.IO.Path.GetFileName((userDataDir ?? "").TrimEnd('\\', '/'));
                    var suffix = string.IsNullOrWhiteSpace(profileDir) || EqualsIgnoreCase(profileDir, "Default") ? "" : " / " + profileDir;
                    var name = string.IsNullOrWhiteSpace(folder) || EqualsIgnoreCase(folder, "User Data")
                        ? (profileDir ?? "Default")
                        : folder;
                    info.ProfileName = name + suffix;
                    info.Profile = string.IsNullOrWhiteSpace(profileDir) ? "Default" : profileDir;
                }
                else
                {
                    info.Profile = string.IsNullOrWhiteSpace(profileDir) ? "Default" : profileDir;
                    info.ProfileName = ChromeProfiles.GetDisplayName(family, info.Profile);
                }
            }

            // 进程名兜底：偶尔拿不到 exe 路径时，用进程名还原浏览器类型
            if (string.IsNullOrWhiteSpace(info.ExePath))
            {
                info.ExePath = "";
            }
            return info;
        }

        private static string DetectFamily(string processName, string exe, string commandLine)
        {
            if (EqualsIgnoreCase(processName, "msedgewebview2")) return null;
            if (ContainsIgnoreCase(exe, "bitbrowser") || ContainsIgnoreCase(commandLine, "bitbrowser")) return "bit";
            if (EqualsIgnoreCase(processName, "msedge") || ContainsIgnoreCase(exe, "msedge.exe")) return "edge";
            if (EqualsIgnoreCase(processName, "chrome") || ContainsIgnoreCase(exe, "chrome.exe")) return "chrome";
            if (ChromiumProcessNames.Contains(processName ?? "")) return "chromium";
            if (LooksLikeChromium(exe)) return "chromium";
            return null;
        }

        public static bool IsBrowserWindow(WindowInfo window)
        {
            if (window == null || string.IsNullOrWhiteSpace(window.Title)) return false;
            return Probe(window.ProcessId, window.ExePath, window.Title, window.Hwnd) != null;
        }

        /// <summary>并行判断哪些窗口是浏览器：单个窗口的界面树查询要一秒上下，串行会让保存卡住。</summary>
        public static List<WindowInfo> FilterBrowserWindows(List<WindowInfo> windows, int totalTimeoutMs)
        {
            var results = new Dictionary<IntPtr, bool>();
            if (windows == null || windows.Count == 0) return new List<WindowInfo>();

            var gate = new object();
            var threads = new List<Thread>();
            foreach (var window in windows)
            {
                var captured = window;
                var thread = new Thread(() =>
                {
                    var ok = false;
                    try { ok = IsBrowserWindow(captured); }
                    catch { }
                    lock (gate) { results[captured.Hwnd] = ok; }
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                threads.Add(thread);
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(totalTimeoutMs);
            foreach (var thread in threads)
            {
                var left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left > 0) thread.Join(left);
            }

            lock (gate)
            {
                var picked = new List<WindowInfo>();
                foreach (var window in windows)
                {
                    bool ok;
                    if (results.TryGetValue(window.Hwnd, out ok) && ok) picked.Add(window);
                }
                return picked;
            }
        }

        /// <summary>从完整命令行里去掉可执行文件本身，只留参数（用于按原样重启）。</summary>
        public static string ArgsOnly(string commandLine, string exePath)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return null;
            var text = commandLine.Trim();
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = text.IndexOf('"', 1);
                if (end > 0) return text.Substring(end + 1).Trim();
                return null;
            }
            if (!string.IsNullOrWhiteSpace(exePath) && text.StartsWith(exePath, StringComparison.OrdinalIgnoreCase))
            {
                return text.Substring(exePath.Length).Trim();
            }
            var space = text.IndexOf(' ');
            if (space > 0)
            {
                var first = text.Substring(0, space);
                if (first.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return text.Substring(space + 1).Trim();
            }
            return null;
        }

        public static string ExtractArgument(string commandLine, string name)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return null;
            // 配置目录里可能带空格（如 Profile 32），未加引号时必须取到下一个 --参数 之前
            var pattern = Regex.Escape(name) + "=(?:\"([^\"]*)\"|(.*?))(?=\\s+--|\\s*$)";
            var match = Regex.Match(commandLine, pattern, RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

    public class UrlRead
    {
        public string Url { get; set; }
        public bool Omnibox { get; set; }
        public int EditCount { get; set; }
        public int Ms { get; set; }
        public string Error { get; set; }
    }

        /// <summary>读取窗口当前标签页地址；读不到返回 null（不阻塞主流程）。</summary>
        public static string ReadActiveUrl(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            string result = null;
            var thread = new Thread(() =>
            {
                try { result = ReadActiveUrlCore(hwnd).Url; }
                catch { }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(2500)) return null;
            return NormalizeUrl(result);
        }

        /// <summary>带诊断信息的读取，用于排查“为什么读不到网址”。</summary>
        public static UrlRead ReadActiveUrlWithDiagnostics(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return new UrlRead { Error = "empty-handle" };
            UrlRead result = null;
            var thread = new Thread(() =>
            {
                try { result = ReadActiveUrlCore(hwnd); }
                catch (Exception ex) { result = new UrlRead { Error = ex.GetType().Name + ": " + ex.Message }; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(3000))
            {
                return new UrlRead { Error = "timeout-3000ms" };
            }
            if (result == null) result = new UrlRead { Error = "unknown" };
            result.Url = NormalizeUrl(result.Url);
            return result;
        }

        /// <summary>并行读取多个窗口的地址，整体最多等 totalTimeoutMs，避免保存布局时被逐个卡住。</summary>
        public static Dictionary<IntPtr, string> ReadActiveUrls(List<IntPtr> handles, int totalTimeoutMs)
        {
            var results = new Dictionary<IntPtr, string>();
            if (handles == null || handles.Count == 0) return results;

            var gate = new object();
            var threads = new List<Thread>();
            foreach (var hwnd in handles)
            {
                var handle = hwnd;
                var thread = new Thread(() =>
                {
                    string value = null;
                    try { value = NormalizeUrl(ReadActiveUrlCore(handle).Url); }
                    catch { }
                    lock (gate) { results[handle] = value; }
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                threads.Add(thread);
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(totalTimeoutMs);
            foreach (var thread in threads)
            {
                var left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left > 0) thread.Join(left);
            }

            lock (gate) { return new Dictionary<IntPtr, string>(results); }
        }

        /// <summary>并行读出多个窗口的标签页网址，总耗时可控；单窗口读不到就返回空列表。</summary>
        public static Dictionary<IntPtr, List<string>> ReadAllTabUrlsForWindows(List<IntPtr> handles, int maxTabs, int totalTimeoutMs)
        {
            var results = new Dictionary<IntPtr, List<string>>();
            if (handles == null || handles.Count == 0) return results;

            var gate = new object();
            var threads = new List<Thread>();
            foreach (var hwnd in handles)
            {
                var handle = hwnd;
                var thread = new Thread(() =>
                {
                    List<string> value = null;
                    try { value = ReadAllTabUrls(handle, maxTabs, totalTimeoutMs); }
                    catch { }
                    lock (gate) { results[handle] = value ?? new List<string>(); }
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                threads.Add(thread);
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(totalTimeoutMs);
            foreach (var thread in threads)
            {
                var left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left > 0) thread.Join(left);
            }

            lock (gate) { return new Dictionary<IntPtr, List<string>>(results); }
        }

        /// <summary>
        /// 读出窗口里所有标签页的网址：逐个用界面树选中标签再读地址栏，读完切回原来的标签。
        /// 拿不到标签页（浏览器不暴露、或只有一个标签）时返回空列表，调用方退回“只记当前页”。
        /// </summary>
        public static List<string> ReadAllTabUrls(IntPtr hwnd, int maxTabs, int timeoutMs)
        {
            var result = new List<string>();
            if (hwnd == IntPtr.Zero) return result;
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (root == null) return result;

                // 先翻窗口顶部几层找标签条（快），翻不到再整棵树找一次，保证多标签不漏
                var candidates = new List<System.Windows.Automation.AutomationElement>();
                CollectElements(root, candidates, 0, 8, 200);

                var list = new List<System.Windows.Automation.AutomationElement>();
                foreach (var element in candidates)
                {
                    try
                    {
                        if (element.Current.ControlType == System.Windows.Automation.ControlType.TabItem) list.Add(element);
                    }
                    catch
                    {
                    }
                }

                if (list.Count < 2)
                {
                    var tabCondition = new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.ControlTypeProperty,
                        System.Windows.Automation.ControlType.TabItem);
                    var probe = root.FindFirst(System.Windows.Automation.TreeScope.Descendants, tabCondition);
                    if (probe != null)
                    {
                        list.Clear();
                        foreach (System.Windows.Automation.AutomationElement element in
                                 root.FindAll(System.Windows.Automation.TreeScope.Descendants, tabCondition))
                        {
                            list.Add(element);
                        }
                    }
                }

                if (list.Count < 2) return result;

                // 先记下当前正在看的这一页，读完再切回去，不改变用户视线所在
                var first = NormalizeUrl(ReadActiveUrl(hwnd));
                if (!string.IsNullOrWhiteSpace(first)) result.Add(first);

                System.Windows.Automation.AutomationElement original = null;
                foreach (var tab in list)
                {
                    object pat;
                    if (!tab.TryGetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern, out pat)) continue;
                    try
                    {
                        if (((System.Windows.Automation.SelectionItemPattern)pat).Current.IsSelected)
                        {
                            original = tab;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                var sw = Stopwatch.StartNew();
                var done = 0;
                foreach (var tab in list)
                {
                    if (done >= maxTabs || sw.ElapsedMilliseconds > timeoutMs) break;

                    object pat;
                    if (!tab.TryGetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern, out pat)) continue;

                    var already = false;
                    try { already = ((System.Windows.Automation.SelectionItemPattern)pat).Current.IsSelected; }
                    catch { }
                    if (already) { done++; continue; }

                    try { ((System.Windows.Automation.SelectionItemPattern)pat).Select(); }
                    catch { continue; }

                    Thread.Sleep(120);
                    var url = NormalizeUrl(ReadActiveUrl(hwnd));
                    if (!string.IsNullOrWhiteSpace(url) && !result.Contains(url)) result.Add(url);
                    done++;
                }

                if (original != null)
                {
                    object pat;
                    if (original.TryGetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern, out pat))
                    {
                        try { ((System.Windows.Automation.SelectionItemPattern)pat).Select(); }
                        catch { }
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        private static UrlRead ReadActiveUrlCore(IntPtr hwnd)
        {
            var read = new UrlRead();
            var sw = Stopwatch.StartNew();
            var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
            if (root == null)
            {
                read.Error = "no-automation-element";
                read.Ms = (int)sw.ElapsedMilliseconds;
                return read;
            }

            // 1) 浅层快路径：地址栏就在窗口顶部几层里，只翻这几层。
            // 整棵树扫描（Descendants）在 ChatGPT 这种复杂页面上要好几秒，保存时会明显卡住。
            var shallow = new List<System.Windows.Automation.AutomationElement>();
            CollectElements(root, shallow, 0, 6, 150);

            foreach (var element in shallow)
            {
                string id;
                try { id = element.Current.AutomationId ?? ""; }
                catch { continue; }
                if (!IsOmniboxId(id)) continue;
                read.Omnibox = true;
                var value = TryGetValue(element);
                if (string.IsNullOrWhiteSpace(value)) continue;
                read.Url = value.Trim();
                read.Ms = (int)sw.ElapsedMilliseconds;
                return read;
            }

            foreach (var element in shallow)
            {
                string name;
                try { name = element.Current.Name ?? ""; }
                catch { continue; }
                if (!IsOmniboxName(name)) continue;
                read.Omnibox = true;
                var value = TryGetValue(element);
                if (string.IsNullOrWhiteSpace(value)) continue;
                read.Url = value.Trim();
                read.Ms = (int)sw.ElapsedMilliseconds;
                return read;
            }

            // 2) 浅层没翻到（浏览器结构特殊）才退回整棵树定位
            var omniboxIds = new[] { "view_1012", "view_1013", "view_1014", "omnibox" };
            foreach (var id in omniboxIds)
            {
                var element = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.AutomationIdProperty, id));
                if (element == null) continue;
                read.Omnibox = true;
                var value = TryGetValue(element);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    read.Url = value.Trim();
                    read.Ms = (int)sw.ElapsedMilliseconds;
                    return read;
                }
            }

            // 2) 按地址栏的显示名找
            var omniboxNames = new[] { "地址和搜索栏", "Address and search bar" };
            foreach (var text in omniboxNames)
            {
                var element = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.NameProperty, text));
                if (element == null) continue;
                read.Omnibox = true;
                var value = TryGetValue(element);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    read.Url = value.Trim();
                    read.Ms = (int)sw.ElapsedMilliseconds;
                    return read;
                }
            }

            // 3) 页面根节点（RootWebArea）通常带完整网址
            var webArea = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.AutomationIdProperty, "RootWebArea"));
            if (webArea != null)
            {
                var value = TryGetValue(webArea);
                if (!string.IsNullOrWhiteSpace(value) &&
                    (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    read.Url = value.Trim();
                    read.Ms = (int)sw.ElapsedMilliseconds;
                    return read;
                }
            }

            // 4) 兜底：编辑框 / 整棵树里找第一个像网址的值
            var all = root.FindAll(System.Windows.Automation.TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            read.EditCount = all.Count;

            string documentValue = null;
            string anyHttpValue = null;

            foreach (System.Windows.Automation.AutomationElement element in all)
            {
                string id = null;
                string name = null;
                string value = null;
                try { id = element.Current.AutomationId; } catch { }
                try { name = element.Current.Name; } catch { }
                try
                {
                    object pattern;
                    if (element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out pattern))
                    {
                        value = ((System.Windows.Automation.ValuePattern)pattern).Current.Value;
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(value)) continue;

                var isOmnibox = IsOmniboxId(id) || IsOmniboxName(name);
                if (isOmnibox)
                {
                    read.Omnibox = true;
                    if (read.Url == null) read.Url = value.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(id) && id.IndexOf("RootWebArea", StringComparison.OrdinalIgnoreCase) >= 0
                         && string.IsNullOrWhiteSpace(documentValue)
                         && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    documentValue = value.Trim();
                }
                else if (string.IsNullOrWhiteSpace(anyHttpValue)
                         && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    anyHttpValue = value.Trim();
                }
            }

            var picked = !string.IsNullOrWhiteSpace(read.Url) ? read.Url
                       : !string.IsNullOrWhiteSpace(documentValue) ? documentValue
                       : anyHttpValue;

            if (string.IsNullOrWhiteSpace(picked))
            {
                read.Error = read.Omnibox ? "omnibox-empty" : "not-found";
                read.Ms = (int)sw.ElapsedMilliseconds;
                return read;
            }

            read.Url = picked;
            read.Ms = (int)sw.ElapsedMilliseconds;
            return read;
        }

        /// <summary>
        /// Chrome / Edge 把窗口所属配置写在界面元素名里："标题 - Google Chrome - 配置名"。
        /// 命令行里的 --profile-directory 只在浏览器冷启动时有效，进程跑起来后一律失效，所以以这里为准。
        /// </summary>
        /// <summary>上一次保存各阶段耗时（毫秒），用于排查保存卡在哪一步。</summary>
        public static string LastTiming = "";

        public static string DetectProfileName(IntPtr hwnd, out string rootName)
        {
            rootName = null;
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (root == null) return null;
                rootName = root.Current.Name;

                var candidates = new List<System.Windows.Automation.AutomationElement>();
                CollectElements(root, candidates, 0, 3, 60);

                foreach (var element in candidates)
                {
                    string name = null;
                    try { name = element.Current.Name; } catch { }
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var match = Regex.Match(name, "^(.*?)\\s+-\\s+(Google Chrome|Microsoft Edge|Chromium|360[^\\s]*)\\s+-\\s+(.+)$");
                    if (match.Success)
                    {
                        var profile = match.Groups[3].Value.Trim();
                        if (profile.Length > 0) return profile;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static void CollectElements(System.Windows.Automation.AutomationElement parent,
            List<System.Windows.Automation.AutomationElement> result, int depth, int maxDepth, int maxCount)
        {
            if (depth >= maxDepth || result.Count >= maxCount) return;
            System.Windows.Automation.AutomationElementCollection children;
            try
            {
                children = parent.FindAll(System.Windows.Automation.TreeScope.Children,
                    System.Windows.Automation.Condition.TrueCondition);
            }
            catch
            {
                return;
            }

            foreach (System.Windows.Automation.AutomationElement child in children)
            {
                if (child == null) continue;
                result.Add(child);
                if (result.Count >= maxCount) return;
                CollectElements(child, result, depth + 1, maxDepth, maxCount);
            }
        }

        /// <summary>窗口根节点名字：Chrome 会把它写成“页面标题 - Google Chrome - 配置名”。</summary>
        public static string RootName(IntPtr hwnd)
        {
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (root == null) return null;
                return root.Current.Name;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetValue(System.Windows.Automation.AutomationElement element)
        {
            try
            {
                object pattern;
                if (element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out pattern))
                {
                    return ((System.Windows.Automation.ValuePattern)pattern).Current.Value;
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool IsOmniboxId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            return EqualsIgnoreCase(id, "omnibox")
                || EqualsIgnoreCase(id, "view_1012")
                || EqualsIgnoreCase(id, "view_1013")
                || EqualsIgnoreCase(id, "view_1014");
        }

        private static bool IsOmniboxName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOf("地址和搜索栏", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Address and search bar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>导出窗口的自动化控件树，用于排查“地址栏到底暴露成什么”。</summary>
        public static string DumpTree(IntPtr hwnd, int max)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[");
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                var all = root.FindAll(System.Windows.Automation.TreeScope.Descendants,
                    System.Windows.Automation.Condition.TrueCondition);
                var count = Math.Min(all.Count, max);
                for (int i = 0; i < count; i++)
                {
                    var element = all[i] as System.Windows.Automation.AutomationElement;
                    if (element == null) continue;
                    var type = "";
                    var id = "";
                    var name = "";
                    var value = "";
                    try { type = element.Current.ControlType.ProgrammaticName; } catch { }
                    try { id = element.Current.AutomationId; } catch { }
                    try { name = element.Current.Name; } catch { }
                    try
                    {
                        object pattern;
                        if (element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out pattern))
                        {
                            value = ((System.Windows.Automation.ValuePattern)pattern).Current.Value;
                        }
                    }
                    catch { }
                    builder.Append("  {\"t\":\"").Append(JsonEscape(type))
                           .Append("\",\"id\":\"").Append(JsonEscape(Truncate(id, 40)))
                           .Append("\",\"n\":\"").Append(JsonEscape(Truncate(name, 60)))
                           .Append("\",\"v\":\"").Append(JsonEscape(Truncate(value, 160)))
                           .Append("\"}");
                    if (i < count - 1) builder.AppendLine(","); else builder.AppendLine();
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine("  {\"error\":\"" + JsonEscape(ex.Message) + "\"}");
            }
            builder.AppendLine("]");
            return builder.ToString();
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max);
        }

        public static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var value = url.Trim();
            if (value.Length == 0) return null;
            if (value.IndexOf(' ') >= 0 && value.IndexOf("://") < 0) return null; // 地址栏里是搜索词，不是网址
            if (value.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return null;
            if (value.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase)) return null;
            if (value.StartsWith("edge:", StringComparison.OrdinalIgnoreCase)) return null;
            if (value.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)) return null;
            if (value.StartsWith("edge-extension://", StringComparison.OrdinalIgnoreCase)) return null;
            if (value.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase)) return null;

            // 地址栏常常不带协议头（如 chatgpt.com/xxx），补全成 https
            if (value.IndexOf("://") < 0)
            {
                if (value.IndexOf('.') < 0 && value.IndexOf(':') < 0) return null;
                value = "https://" + value;
            }
            return value;
        }

        /// <summary>诊断用：把当前所有浏览器窗口的识别结果导出为 JSON。</summary>
        public static string BuildReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("[");
            bool first = true;
            foreach (var window in WindowTools.GetTopLevelWindows().Where(w => !w.IsMinimized))
            {
                var info = Probe(window.ProcessId, window.ExePath, window.Title, window.Hwnd);
                if (info == null) continue;
                if (first) dumpFirstWindowHwnd = window.Hwnd;
                if (!first) builder.AppendLine(",");
                first = false;
                var urlRead = ReadActiveUrlWithDiagnostics(window.Hwnd);
                string treeRoot;
                var treeProfile = DetectProfileName(window.Hwnd, out treeRoot);
                var tabUrls = ReadAllTabUrls(window.Hwnd, 12, 2500);
                var tabUrlsText = new StringBuilder();
                foreach (var tabUrl in tabUrls)
                {
                    if (tabUrlsText.Length > 0) tabUrlsText.Append(" , ");
                    tabUrlsText.Append(tabUrl);
                }
                builder.Append("  {\"title\":\"").Append(JsonEscape(window.Title))
                       .Append("\",\"process\":\"").Append(JsonEscape(window.ProcessName))
                       .Append("\",\"pid\":").Append(window.ProcessId)
                       .Append(",\"family\":\"").Append(JsonEscape(info.Family))
                       .Append("\",\"profile\":\"").Append(JsonEscape(info.Profile))
                       .Append("\",\"profile_name\":\"").Append(JsonEscape(info.ProfileName))
                       .Append("\",\"exe\":\"").Append(JsonEscape(info.ExePath))
                       .Append("\",\"url\":\"").Append(JsonEscape(urlRead.Url ?? ""))
                       .Append("\",\"omnibox\":").Append(urlRead.Omnibox ? "true" : "false")
                       .Append(",\"edits\":").Append(urlRead.EditCount)
                       .Append(",\"ms\":").Append(urlRead.Ms)
                       .Append(",\"root\":\"").Append(JsonEscape(RootName(window.Hwnd) ?? "")).Append("\"")
                       .Append(",\"treeprofile\":\"").Append(JsonEscape(treeProfile ?? "")).Append("\"")
                       .Append(",\"tabs\":").Append(tabUrls.Count)
                       .Append(",\"taburls\":\"").Append(JsonEscape(tabUrlsText.ToString())).Append("\"")
                       .Append(",\"why\":\"").Append(JsonEscape(urlRead.Error ?? ""))
                       .Append("\"}");
            }
            builder.AppendLine();
            builder.AppendLine("]");

            if (dumpFirstWindowHwnd != IntPtr.Zero)
            {
                builder.AppendLine("--- tree ---");
                builder.Append(DumpTree(dumpFirstWindowHwnd, 120));
            }
            return builder.ToString();
        }

        internal static IntPtr dumpFirstWindowHwnd = IntPtr.Zero;

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var builder = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }

        public static string KeywordFromTitle(string title)
        {
            var value = (title ?? "").Trim();
            var suffixes = new[]
            {
                " - Google Chrome", " - Microsoft Edge", " - Chromium", " - 比特浏览器", " - BitBrowser",
                " - 360极速浏览器", " - 360安全浏览器", " - QQ浏览器"
            };
            foreach (var suffix in suffixes)
            {
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(0, value.Length - suffix.Length).Trim();
                    break;
                }
            }
            if (value.Length > 60) value = value.Substring(0, 60);
            return value;
        }
    }

    /// <summary>读取 Chrome / Edge 的配置显示名。</summary>
    public static class ChromeProfiles
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string GetDisplayName(string family, string profileDirectory)
        {
            if (string.IsNullOrWhiteSpace(profileDirectory)) return "";
            var key = (family ?? "") + "|" + profileDirectory;
            lock (Sync)
            {
                string hit;
                if (Cache.TryGetValue(key, out hit)) return hit;
            }

            var name = ReadName(family, profileDirectory);
            lock (Sync) { Cache[key] = name; }
            return name;
        }

        private class ProfileEntry
        {
            public string Dir;
            public string Name;
            public string UserName;
        }

        // Local State 有几 MB，每个窗口都读一遍会让保存卡好几秒，这里整个进程只解析一次
        private static readonly object StateSync = new object();
        private static readonly Dictionary<string, List<ProfileEntry>> StateCache =
            new Dictionary<string, List<ProfileEntry>>(StringComparer.OrdinalIgnoreCase);
        private static DateTime stateStamp = DateTime.MinValue;

        private static List<ProfileEntry> LoadEntries(string family)
        {
            lock (StateSync)
            {
                if (DateTime.UtcNow - stateStamp < TimeSpan.FromMinutes(5))
                {
                    List<ProfileEntry> hit;
                    if (StateCache.TryGetValue(family, out hit)) return hit;
                }
                else
                {
                    StateCache.Clear();
                }

                var list = ParseEntries(family);
                stateStamp = DateTime.UtcNow;
                StateCache[family] = list;
                return list;
            }
        }

        private static List<ProfileEntry> ParseEntries(string family)
        {
            var list = new List<ProfileEntry>();
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var userData = EqualsIgnoreCase(family, "edge")
                    ? System.IO.Path.Combine(local, "Microsoft", "Edge", "User Data")
                    : System.IO.Path.Combine(local, "Google", "Chrome", "User Data");
                var statePath = System.IO.Path.Combine(userData, "Local State");
                if (!File.Exists(statePath)) return list;

                var json = File.ReadAllText(statePath, Encoding.UTF8);
                // info_cache 里形如 "Profile 27":{"name":"GPT1半价-曹",...}
                foreach (Match block in Regex.Matches(json, "\"(Profile \\d+|Default)\"\\s*:\\s*\\{[^{}]*\\}"))
                {
                    var body = block.Value;
                    var dir = Regex.Match(body, "\"(Profile \\d+|Default)\"\\s*:").Groups[1].Value;
                    var nameMatch = Regex.Match(body, "\"name\"\\s*:\\s*\"([^\"]*)\"");
                    var userMatch = Regex.Match(body, "\"user_name\"\\s*:\\s*\"([^\"]*)\"");
                    list.Add(new ProfileEntry
                    {
                        Dir = dir,
                        Name = DecodeJsonString(nameMatch.Success ? nameMatch.Groups[1].Value : ""),
                        UserName = DecodeJsonString(userMatch.Success ? userMatch.Groups[1].Value : "")
                    });
                }
            }
            catch
            {
            }
            return list;
        }

        /// <summary>按界面树里读到的配置显示名反查配置目录（Profile 27 之类），用于冷启动时按配置打开。</summary>
        public static string FindDirectory(string family, string displayName, string fallbackDirectory)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return fallbackDirectory ?? "";
            try
            {
                foreach (var entry in LoadEntries(family))
                {
                    if (BrowserProbe.NamesCompatible(entry.Name, displayName)) return entry.Dir;
                    if (!string.IsNullOrWhiteSpace(entry.UserName) && BrowserProbe.NamesCompatible(entry.UserName, displayName)) return entry.Dir;
                }
            }
            catch
            {
            }
            return fallbackDirectory ?? "";
        }

        private static string ReadName(string family, string profileDirectory)
        {
            try
            {
                foreach (var entry in LoadEntries(family))
                {
                    if (!EqualsIgnoreCase(entry.Dir, profileDirectory)) continue;
                    return string.IsNullOrWhiteSpace(entry.Name) ? profileDirectory : entry.Name;
                }
            }
            catch
            {
            }
            return profileDirectory;
        }

        private static string DecodeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0) return value;
            return Regex.Replace(value, "\\\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        }

        private static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>比特浏览器本地接口：按环境 ID 打开环境。</summary>
    public static class BitBrowserApi
    {
        private const string BaseUrl = "http://127.0.0.1:54345";
        private static readonly object Sync = new object();
        private static List<BitEnv> cache;
        private static DateTime cacheStamp = DateTime.MinValue;

        [DataContract]
        public class BitEnv
        {
            [DataMember(Name = "id")] public string Id { get; set; }
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "remark")] public string Remark { get; set; }
            [DataMember(Name = "seq")] public int Seq { get; set; }
        }

        private static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [DataContract]
        private class BitListResponse
        {
            [DataMember(Name = "code")] public int Code { get; set; }
            [DataMember(Name = "data")] public BitListData Data { get; set; }
        }

        [DataContract]
        private class BitListData
        {
            [DataMember(Name = "list")] public List<BitEnv> List { get; set; }
        }

        [DataContract]
        private class BitOpenResponse
        {
            [DataMember(Name = "code")] public int Code { get; set; }
            [DataMember(Name = "msg")] public string Msg { get; set; }
        }

        public static List<BitEnv> ListEnvironments()
        {
            lock (Sync)
            {
                if (cache != null && DateTime.UtcNow - cacheStamp < TimeSpan.FromSeconds(30)) return cache;
            }

            var list = new List<BitEnv>();
            try
            {
                var json = Post("/browser/list", "{\"page\":0,\"pageSize\":200}");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(BitListResponse));
                        var response = serializer.ReadObject(stream) as BitListResponse;
                        if (response != null && response.Data != null && response.Data.List != null)
                        {
                            list = response.Data.List;
                        }
                    }
                }
            }
            catch
            {
            }

            lock (Sync)
            {
                cache = list;
                cacheStamp = DateTime.UtcNow;
            }
            return list;
        }

        public static string ResolveEnvironmentId(string windowTitle, string userDataDir)
        {
            var list = ListEnvironments();
            if (list.Count == 0) return null;

            var title = windowTitle ?? "";
            string bestId = null;
            int bestLength = 0;
            foreach (var env in list)
            {
                if (env == null || string.IsNullOrWhiteSpace(env.Name)) continue;
                if (title.IndexOf(env.Name, StringComparison.OrdinalIgnoreCase) >= 0 && env.Name.Length > bestLength)
                {
                    bestId = env.Id;
                    bestLength = env.Name.Length;
                }
            }
            if (!string.IsNullOrWhiteSpace(bestId)) return bestId;

            if (!string.IsNullOrWhiteSpace(userDataDir))
            {
                foreach (var env in list)
                {
                    if (env == null || string.IsNullOrWhiteSpace(env.Id)) continue;
                    if (userDataDir.IndexOf(env.Id, StringComparison.OrdinalIgnoreCase) >= 0) return env.Id;
                }
            }
            return null;
        }

        public static string NameOf(string envId, string fallback)
        {
            var list = ListEnvironments();
            var env = list.FirstOrDefault(x => x != null && EqualsIgnoreCase(x.Id, envId));
            if (env != null && !string.IsNullOrWhiteSpace(env.Name)) return env.Name;
            return string.IsNullOrWhiteSpace(fallback) ? envId : fallback;
        }

        public static bool OpenEnvironment(string envId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(envId))
            {
                error = "缺少环境 ID";
                return false;
            }

            try
            {
                var json = Post("/browser/open", "{\"id\":\"" + envId.Replace("\"", "") + "\"}");
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "本地接口没有响应（比特浏览器未启动？）";
                    return false;
                }
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(BitOpenResponse));
                    var response = serializer.ReadObject(stream) as BitOpenResponse;
                    if (response != null && response.Code == 0) return true;
                    error = response == null ? "返回内容无法识别" : (string.IsNullOrWhiteSpace(response.Msg) ? "接口返回 code=" + response.Code : response.Msg);
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string Post(string path, string body)
        {
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(BaseUrl + path);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = 3000;
            request.ReadWriteTimeout = 3000;
            var bytes = Encoding.UTF8.GetBytes(body ?? "{}");
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = (System.Net.HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }

    public static class ProcessQuery
    {
        public static string GetCommandLine(int processId, out string executablePath)
        {
            executablePath = null;
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT CommandLine, ExecutablePath FROM Win32_Process WHERE ProcessId=" + processId))
                using (var results = searcher.Get())
                {
                    foreach (System.Management.ManagementObject item in results)
                    {
                        executablePath = Convert.ToString(item["ExecutablePath"]);
                        return Convert.ToString(item["CommandLine"]);
                    }
                }
            }
            catch
            {
            }
            return null;
        }
    }

    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        /// <summary>把窗口拉到前台；抢不到焦点就返回 false，避免后面的键盘输入打到别的程序里。</summary>
        public static bool ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                if (GetForegroundWindow() == hwnd) return true;
                SetForegroundWindow(hwnd);
                if (GetForegroundWindow() == hwnd) return true;

                uint pid;
                var targetThread = GetWindowThreadProcessId(hwnd, out pid);
                var foreground = GetForegroundWindow();
                var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out pid);
                if (targetThread != 0 && foregroundThread != 0 && targetThread != foregroundThread)
                {
                    AttachThreadInput(foregroundThread, targetThread, true);
                    SetForegroundWindow(hwnd);
                    SetFocus(hwnd);
                    AttachThreadInput(foregroundThread, targetThread, false);
                }
            }
            catch
            {
            }
            return GetForegroundWindow() == hwnd;
        }

        /// <summary>把窗口激活后，用地址栏打开一个网址（比特浏览器等无法用命令行加标签时使用）。</summary>
        public static void NavigateInWindow(IntPtr hwnd, string url)
        {
            NavigateInWindow(hwnd, url, false);
        }

        /// <summary>在窗口里打开网址。newTab=true 时先 Ctrl+T 开新标签，避免把当前页顶掉。</summary>
        public static void NavigateInWindow(IntPtr hwnd, string url, bool newTab)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(url)) return;
            try
            {
                ShowWindowAsync(hwnd, 9);
                Thread.Sleep(140);
                if (!ForceForeground(hwnd)) return;
                Thread.Sleep(80);
                if (newTab)
                {
                    SendKeys.SendWait("^t");
                    Thread.Sleep(280);
                }
                SendKeys.SendWait("^l");
                Thread.Sleep(130);

                bool pasted = false;
                for (int i = 0; i < 3 && !pasted; i++)
                {
                    try
                    {
                        Clipboard.SetText(url);
                        pasted = true;
                    }
                    catch
                    {
                        Thread.Sleep(100);
                    }
                }

                if (pasted) SendKeys.SendWait("^v");
                else SendKeys.SendWait(EscapeSendKeys(url));
                Thread.Sleep(100);
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(260);
            }
            catch
            {
            }
        }

        private static string EscapeSendKeys(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder(text.Length + 8);
            foreach (var c in text)
            {
                if ("+^%~(){}[]".IndexOf(c) >= 0) builder.Append('{').Append(c).Append('}');
                else builder.Append(c);
            }
            return builder.ToString();
        }

        [DllImport("user32.dll")]
        public static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll")]
        public static extern int SetProcessDpiAwareness(int awareness);
    }

    public static class TaskbarIntegration
    {
        private const string AppId = "Zwm.WindowLayoutLauncher";

        private static readonly PropertyKey AppIdKey = new PropertyKey(
            new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        private static readonly PropertyKey TitleKey = new PropertyKey(
            new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);

        public static void Initialize()
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID(AppId);
            }
            catch
            {
            }
        }

        public static void UpdateJumpList(IEnumerable<LayoutSummary> layouts)
        {
            try
            {
                EnsureShortcutIdentities();

                ICustomDestinationList destination = (ICustomDestinationList)new DestinationList();
                destination.SetAppID(AppId);
                uint maxSlots;
                object removed;
                Guid objectArrayId = typeof(IObjectArray).GUID;
                destination.BeginList(out maxSlots, ref objectArrayId, out removed);
                ReleaseComObject(removed);

                var layoutList = (layouts ?? Enumerable.Empty<LayoutSummary>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                    .Take(maxSlots == 0 ? 12 : (int)maxSlots)
                    .ToList();
                if (layoutList.Count > 0)
                {
                    IObjectCollection collection = (IObjectCollection)new EnumerableObjectCollection();
                    foreach (var layout in layoutList)
                    {
                        string label = string.IsNullOrWhiteSpace(layout.TaskMode)
                            ? layout.Name
                            : layout.TaskMode + "  |  " + layout.Name;
                        collection.AddObject(CreateLink(label, "--restore \"" + layout.Name.Replace("\"", "\\\"") + "\""));
                    }
                    destination.AppendCategory("已保存布局", (IObjectArray)collection);
                    ReleaseComObject(collection);
                }

                IObjectCollection tasks = (IObjectCollection)new EnumerableObjectCollection();
                tasks.AddObject(CreateLink("打开窗口管理器", ""));
                destination.AddUserTasks((IObjectArray)tasks);
                ReleaseComObject(tasks);

                destination.CommitList();
                ReleaseComObject(destination);
            }
            catch
            {
            }
        }

        private static object CreateLink(string title, string arguments)
        {
            IShellLinkW link = (IShellLinkW)new ShellLink();
            string exe = Application.ExecutablePath;
            link.SetPath(exe);
            link.SetArguments(arguments ?? "");
            link.SetWorkingDirectory(AppDomain.CurrentDomain.BaseDirectory);
            link.SetIconLocation(exe, 0);
            link.SetDescription(title);
            SetStringProperty((IPropertyStore)link, TitleKey, title);
            return link;
        }

        private static void EnsureShortcutIdentities()
        {
            var folders = new List<string>();
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"));

            foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                foreach (var shortcut in Directory.GetFiles(folder, "*.lnk"))
                {
                    UpdateShortcutIdentity(shortcut);
                }
            }
        }

        private static void UpdateShortcutIdentity(string shortcutPath)
        {
            IShellLinkW link = null;
            try
            {
                link = (IShellLinkW)new ShellLink();
                IPersistFile file = (IPersistFile)link;
                file.Load(shortcutPath, 2);
                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                if (!string.Equals(FullPath(target.ToString()), FullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                    return;

                SetStringProperty((IPropertyStore)link, AppIdKey, AppId);
                file.Save(shortcutPath, true);
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(link);
            }
        }

        private static string FullPath(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd('\\'); }
            catch { return path ?? ""; }
        }

        private static void SetStringProperty(IPropertyStore store, PropertyKey key, string value)
        {
            PropVariant variant = PropVariant.FromString(value ?? "");
            try
            {
                store.SetValue(ref key, ref variant);
                store.Commit();
            }
            finally
            {
                variant.Clear();
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try { Marshal.FinalReleaseComObject(value); }
                catch { }
            }
        }

        [DllImport("shell32.dll")]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;

            public PropertyKey(Guid formatId, uint propertyId)
            {
                FormatId = formatId;
                PropertyId = propertyId;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort VariantType;
            [FieldOffset(8)] public IntPtr PointerValue;

            public static PropVariant FromString(string value)
            {
                return new PropVariant
                {
                    VariantType = 31,
                    PointerValue = Marshal.StringToCoTaskMemUni(value)
                };
            }

            public void Clear()
            {
                PropVariantClear(ref this);
            }
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICustomDestinationList
        {
            void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
            void BeginList(out uint maxSlots, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object removedItems);
            void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);
            void AppendKnownCategory(uint category);
            void AddUserTasks(IObjectArray tasks);
            void CommitList();
            void GetRemovedDestinations(ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object removedItems);
            void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
            void AbortList();
        }

        [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectArray
        {
            void GetCount(out uint count);
            void GetAt(uint index, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object value);
        }

        [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectCollection
        {
            void GetCount(out uint count);
            void GetAt(uint index, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object value);
            void AddObject([MarshalAs(UnmanagedType.IUnknown)] object value);
            void AddFromArray(IObjectArray source);
            void RemoveObjectAt(uint index);
            void Clear();
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int pathLength, IntPtr findData, uint flags);
            void GetIDList(out IntPtr itemIdList);
            void SetIDList(IntPtr itemIdList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maxLength);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxLength);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxLength);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCommand);
            void SetShowCmd(int showCommand);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint propertyCount);
            void GetAt(uint propertyIndex, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }

        [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")]
        private class DestinationList { }

        [ComImport, Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")]
        private class EnumerableObjectCollection { }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }
    }

    public static class UiTheme
    {
        public static readonly Color WindowTop = Color.FromArgb(229, 237, 243);
        public static readonly Color WindowBottom = Color.FromArgb(209, 220, 229);
        public static readonly Color Panel = Color.FromArgb(226, 235, 241);
        public static readonly Color PanelLight = Color.FromArgb(238, 244, 248);
        public static readonly Color Ink = Color.FromArgb(28, 41, 56);
        public static readonly Color Muted = Color.FromArgb(96, 111, 128);
        public static readonly Color Blue = Color.FromArgb(48, 126, 255);
        public static readonly Color BlueDark = Color.FromArgb(25, 93, 222);
        public static readonly Color Cyan = Color.FromArgb(65, 213, 207);
        public static readonly Color Amber = Color.FromArgb(236, 170, 28);
        public static readonly Color Border = Color.FromArgb(195, 208, 220);
        public static readonly Color Selection = Color.FromArgb(215, 226, 237);

        public static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return path;
            }
            radius = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static int Dpi(int value)
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return (int)Math.Round(value * g.DpiX / 96F);
                }
            }
            catch
            {
                return value;
            }
        }

        public static Size DpiSize(int width, int height)
        {
            return new Size(Dpi(width), Dpi(height));
        }
    }

    public class GlassPanel : Panel
    {
        public int Radius { get; set; }

        public GlassPanel()
        {
            Radius = 22;
            FillColor = UiTheme.Panel;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public Color FillColor { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(9, 9, Width - 18, Height - 18);
            using (var path = UiTheme.RoundedRect(rect, Radius))
            using (var lightPath = UiTheme.RoundedRect(new Rectangle(rect.X - 3, rect.Y - 3, rect.Width, rect.Height), Radius))
            using (var darkPath = UiTheme.RoundedRect(new Rectangle(rect.X + 6, rect.Y + 7, rect.Width, rect.Height), Radius))
            using (var light = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
            using (var shadow = new SolidBrush(Color.FromArgb(35, 118, 135, 155)))
            using (var fill = new SolidBrush(FillColor))
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255)))
            using (var edge = new Pen(Color.FromArgb(80, 164, 181, 198)))
            {
                e.Graphics.FillPath(light, lightPath);
                e.Graphics.FillPath(shadow, darkPath);
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
                e.Graphics.DrawPath(edge, path);
            }
            base.OnPaint(e);
        }
    }

    public class GlassButton : Button
    {
        private bool hover;
        private bool down;

        public bool Primary { get; set; }
        public Color SurfaceColor { get; set; }

        public GlassButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SurfaceColor = UiTheme.Panel;
            BackColor = SurfaceColor;
            Cursor = Cursors.Hand;
            Height = 36;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            down = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.Clear(SurfaceColor);
            var rect = new Rectangle(4, 4, Width - 9, Height - 9);
            Color fill;
            Color text;
            Color border;
            if (Primary)
            {
                fill = down ? UiTheme.BlueDark : hover ? Color.FromArgb(66, 142, 255) : UiTheme.Blue;
                text = Color.White;
                border = Color.FromArgb(100, 255, 255, 255);
            }
            else
            {
                fill = down ? Color.FromArgb(214, 225, 234) : hover ? Color.FromArgb(232, 240, 246) : UiTheme.PanelLight;
                text = UiTheme.Ink;
                border = Color.FromArgb(130, 255, 255, 255);
            }

            using (var path = UiTheme.RoundedRect(rect, 16))
            using (var lightPath = UiTheme.RoundedRect(new Rectangle(rect.X - 2, rect.Y - 2, rect.Width, rect.Height), 16))
            using (var darkPath = UiTheme.RoundedRect(new Rectangle(rect.X + 3, rect.Y + 4, rect.Width, rect.Height), 16))
            using (var light = new SolidBrush(Color.FromArgb(Primary ? 50 : 115, 255, 255, 255)))
            using (var shadow = new SolidBrush(Color.FromArgb(Primary ? 30 : 42, 112, 130, 150)))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                if (!down)
                {
                    pevent.Graphics.FillPath(light, lightPath);
                    pevent.Graphics.FillPath(shadow, darkPath);
                }
                pevent.Graphics.FillPath(brush, path);
                pevent.Graphics.DrawPath(pen, path);
                TextRenderer.DrawText(pevent.Graphics, Text, Font, rect, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    public class MainForm : Form
    {
        private readonly LayoutManager manager;
        private Label titleLabel;
        private Label sectionLabel;
        private Label hintLabel;
        private ListBox listBox;
        private Label statusLabel;
        private GlassPanel card;
        private GlassButton openButton;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool allowExit;
        private string pendingTaskMode;
        private DateTime ignoreMinimizeUntilUtc;

        public MainForm(LayoutManager manager)
        {
            this.manager = manager;
            ignoreMinimizeUntilUtc = DateTime.UtcNow.AddMilliseconds(1200);
            Initialize();
            RefreshLayouts();
            InitializeTray();
        }

        private void Initialize()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = Program.AppName;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 650);
            MinimumSize = new Size(640, 500);
            BackColor = UiTheme.WindowBottom;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;
            KeyPreview = true;
            TrySetWindowIcon();
            Shown += (s, e) => CenterOnPrimaryScreen();

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Color.Transparent;
            root.Padding = new Padding(38, 28, 38, 26);
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            Controls.Add(root);

            titleLabel = new Label();
            titleLabel.Text = "窗口管理器";
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Font = new Font("Microsoft YaHei UI", 14.5F, FontStyle.Bold);
            titleLabel.ForeColor = UiTheme.Ink;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(titleLabel, 0, 0);

            sectionLabel = new Label();
            sectionLabel.Text = "选择一个工作布局";
            sectionLabel.Dock = DockStyle.Fill;
            sectionLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            sectionLabel.ForeColor = UiTheme.Ink;
            sectionLabel.BackColor = Color.Transparent;
            sectionLabel.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(sectionLabel, 0, 1);

            hintLabel = new Label();
            hintLabel.Text = "保存不同工作流的窗口位置和大小，下次一键恢复。";
            hintLabel.Dock = DockStyle.Fill;
            hintLabel.ForeColor = UiTheme.Muted;
            hintLabel.BackColor = Color.Transparent;
            hintLabel.TextAlign = ContentAlignment.TopLeft;
            root.Controls.Add(hintLabel, 0, 2);

            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Color.Transparent;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214F));
            body.Margin = new Padding(0, 8, 0, 10);
            root.Controls.Add(body, 0, 3);

            card = new GlassPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 20, 0);
            card.Padding = new Padding(22);
            card.FillColor = UiTheme.Panel;
            body.Controls.Add(card, 0, 0);

            listBox = new ListBox();
            listBox.Dock = DockStyle.Fill;
            listBox.DisplayMember = "Display";
            listBox.BackColor = UiTheme.Panel;
            listBox.ForeColor = UiTheme.Ink;
            listBox.BorderStyle = BorderStyle.None;
            listBox.DrawMode = DrawMode.OwnerDrawFixed;
            listBox.ItemHeight = 104;
            listBox.IntegralHeight = false;
            listBox.DrawItem += DrawLayoutItem;
            listBox.DoubleClick += (s, e) => RestoreSelected();
            listBox.SelectedIndexChanged += (s, e) => UpdateSelectedLayoutUi();
            listBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    RestoreSelected();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.F5)
                {
                    RefreshLayouts();
                    e.Handled = true;
                }
            };
            card.Controls.Add(listBox);

            var actionScroll = new Panel();
            actionScroll.Dock = DockStyle.Fill;
            actionScroll.BackColor = Color.Transparent;
            actionScroll.AutoScroll = true;
            actionScroll.AutoScrollMinSize = new Size(0, 412);
            actionScroll.Margin = new Padding(0);
            body.Controls.Add(actionScroll, 1, 0);

            var actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Top;
            actions.Height = 448;
            actions.BackColor = Color.Transparent;
            actions.AutoScroll = false;
            actions.ColumnCount = 1;
            actions.RowCount = 9;
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 144F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            actionScroll.Controls.Add(actions);

            openButton = AddButton(actions, "打开布局", 0, 0, true, (s, e) => RestoreSelected());
            openButton.Dock = DockStyle.Top;
            openButton.Margin = new Padding(8, 0, 8, 0);
            actions.Controls.Add(openButton, 0, 0);

            var manageGroup = AddActionGroup(
                new[] { "保存为新布局", "覆盖所选布局", "重命名布局", "删除布局" },
                new EventHandler[] { (s, e) => SaveNew(), (s, e) => OverwriteSelected(), (s, e) => RenameSelected(), (s, e) => DeleteSelected() });
            manageGroup.Dock = DockStyle.Fill;
            manageGroup.Margin = new Padding(0);
            actions.Controls.Add(manageGroup, 0, 2);

            var shareGroup = AddActionGroup(
                new[] { "导出 JSON", "导入 JSON", "网页地址" },
                new EventHandler[] { (s, e) => ExportSelected(), (s, e) => ImportLayout(), (s, e) => EditBrowserUrls() });
            shareGroup.Dock = DockStyle.Fill;
            shareGroup.Margin = new Padding(0);
            actions.Controls.Add(shareGroup, 0, 4);

            var fileGroup = AddActionGroup(
                new[] { "打开布局文件夹", "刷新布局" },
                new EventHandler[] { (s, e) => Process.Start("explorer.exe", manager.LayoutDir), (s, e) => RefreshLayouts() });
            fileGroup.Dock = DockStyle.Fill;
            fileGroup.Margin = new Padding(0);
            actions.Controls.Add(fileGroup, 0, 6);

            var aboutButton = AddButton(actions, "关于 / 检查更新", 0, 0, false, (s, e) => ShowAbout());
            aboutButton.Dock = DockStyle.Top;
            aboutButton.Margin = new Padding(8, 0, 8, 0);
            aboutButton.Height = 34;
            actions.Controls.Add(aboutButton, 0, 8);
            actionScroll.HorizontalScroll.Enabled = false;
            actionScroll.HorizontalScroll.Visible = false;
            actionScroll.HorizontalScroll.Maximum = 0;
            actionScroll.SizeChanged += (s, e) =>
            {
                actionScroll.HorizontalScroll.Maximum = 0;
                actionScroll.HorizontalScroll.Visible = false;
            };

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.ForeColor = UiTheme.Muted;
            statusLabel.BackColor = Color.Transparent;
            statusLabel.AutoEllipsis = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(statusLabel, 0, 4);

            Shown += OnFirstShown;
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.N)
                {
                    SaveNew();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.F5)
                {
                    RefreshLayouts();
                    e.Handled = true;
                }
            };
        }

        private void OnFirstShown(object sender, EventArgs e)
        {
            Shown -= OnFirstShown;
            if (listBox.Items.Count == 0)
            {
                using (var wizard = new SetupWizardForm())
                {
                    if (wizard.ShowDialog(this) == DialogResult.OK)
                    {
                        pendingTaskMode = wizard.TaskMode;
                        hintLabel.Text = "先打开需要的窗口并摆好位置，然后保存为“" + pendingTaskMode + "”布局。";
                        statusLabel.Text = "准备好了以后，点击“保存为新布局”。";
                    }
                }
            }
        }

        private void ShowAbout()
        {
            using (var about = new AboutForm())
            {
                about.ShowDialog(this);
            }
        }

        private void CenterOnPrimaryScreen()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            int x = area.Left + Math.Max(0, (area.Width - Width) / 2);
            int y = area.Top + Math.Max(0, (area.Height - Height) / 2);
            Location = new Point(x, y);
        }

        private void TrySetWindowIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "app.ico");
                if (File.Exists(iconPath))
                {
                    Icon = new Icon(iconPath);
                    return;
                }

                var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (associated != null)
                {
                    Icon = associated;
                }
            }
            catch
            {
            }
        }

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Font = new Font("Microsoft YaHei UI", 9F);
            trayIcon = new NotifyIcon();
            trayIcon.Text = Program.AppName;
            trayIcon.Icon = Icon ?? Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => ShowMainWindow();
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (trayMenu == null) return;
            trayMenu.Items.Clear();
            trayMenu.Items.Add("打开窗口管理器", null, (s, e) => ShowMainWindow());
            trayMenu.Items.Add(new ToolStripSeparator());
            var layouts = manager.GetLayouts();
            foreach (var layout in layouts)
            {
                var captured = layout;
                trayMenu.Items.Add("开始" + (string.IsNullOrWhiteSpace(captured.TaskMode) ? captured.Name : captured.TaskMode) + "  ·  " + captured.Name,
                    null, (s, e) => RestoreLayout(captured));
            }
            if (layouts.Count == 0)
            {
                var empty = trayMenu.Items.Add("还没有保存布局");
                empty.Enabled = false;
            }
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, (s, e) => { allowExit = true; Close(); });
            TaskbarIntegration.UpdateJumpList(layouts);
        }

        private void ShowMainWindow()
        {
            ignoreMinimizeUntilUtc = DateTime.UtcNow.AddMilliseconds(1200);
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Show();
            BringToFront();
            Activate();
        }

        private void HideToTray()
        {
            ShowInTaskbar = true;
            WindowState = FormWindowState.Minimized;
        }

        protected override void WndProc(ref Message m)
        {
            const int WmSysCommand = 0x0112;
            const int ScClose = 0xF060;
            const int ScMinimize = 0xF020;
            int systemCommand = m.Msg == WmSysCommand ? ((int)m.WParam & 0xFFF0) : 0;
            if (!allowExit && systemCommand == ScClose)
            {
                HideToTray();
                return;
            }
            if (systemCommand == ScMinimize && DateTime.UtcNow < ignoreMinimizeUntilUtc)
            {
                return;
            }
            if (m.Msg == Program.ShowMainWindowMessage)
            {
                BeginInvoke((Action)(() => ShowMainWindow()));
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason != CloseReason.WindowsShutDown &&
                e.CloseReason != CloseReason.TaskManagerClosing &&
                e.CloseReason != CloseReason.ApplicationExitCall)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            if (trayIcon != null) trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }
                if (trayMenu != null)
                {
                    trayMenu.Dispose();
                    trayMenu = null;
                }
            }
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var brush = new LinearGradientBrush(ClientRectangle, UiTheme.WindowTop, UiTheme.WindowBottom, 90F))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        }

        private GlassButton AddButton(Control parent, string text, int left, int top, bool primary, EventHandler handler)
        {
            var button = new GlassButton();
            button.Text = text;
            button.Left = left;
            button.Top = top;
            button.Width = 136;
            button.Height = 34;
            button.Primary = primary;
            if (parent is MainForm)
            {
                button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }
            var panel = parent as GlassPanel;
            var surface = panel == null ? parent.BackColor : panel.FillColor;
            if (surface == Color.Transparent || surface.A == 0)
            {
                surface = UiTheme.WindowBottom;
            }
            button.SurfaceColor = surface;
            button.BackColor = surface;
            button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Click += handler;
            if (!(parent is TableLayoutPanel))
            {
                parent.Controls.Add(button);
            }
            return button;
        }

        private Control AddActionGroup(string[] labels, EventHandler[] handlers)
        {
            var group = new TableLayoutPanel();
            group.BackColor = Color.Transparent;
            group.ColumnCount = 1;
            group.RowCount = labels.Length;
            group.Margin = new Padding(0);
            for (int i = 0; i < labels.Length; i++)
            {
                group.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            }

            for (int i = 0; i < labels.Length; i++)
            {
                var button = AddButton(group, labels[i], 0, 0, false, handlers[i]);
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(6, 2, 6, 2);
                group.Controls.Add(button, 0, i);
            }
            return group;
        }

        private void DrawLayoutItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var back = new SolidBrush(listBox.BackColor))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }

            var item = listBox.Items[e.Index] as LayoutSummary;
            var rect = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 6, e.Bounds.Width - 8, e.Bounds.Height - 12);
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var path = UiTheme.RoundedRect(rect, 14))
            using (var lightPath = UiTheme.RoundedRect(new Rectangle(rect.X - 2, rect.Y - 2, rect.Width, rect.Height), 14))
            using (var darkPath = UiTheme.RoundedRect(new Rectangle(rect.X + 3, rect.Y + 4, rect.Width, rect.Height), 14))
            using (var light = new SolidBrush(Color.FromArgb(selected ? 128 : 50, 255, 255, 255)))
            using (var shadow = new SolidBrush(Color.FromArgb(selected ? 36 : 0, 112, 130, 150)))
            using (var fill = new SolidBrush(selected ? UiTheme.Selection : UiTheme.Panel))
            using (var border = new Pen(selected ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(0, 255, 255, 255)))
            {
                if (selected)
                {
                    e.Graphics.FillPath(light, lightPath);
                    e.Graphics.FillPath(shadow, darkPath);
                }
                e.Graphics.FillPath(fill, path);
                if (selected) e.Graphics.DrawPath(border, path);
            }

            if (selected)
            {
                using (var accent = new SolidBrush(UiTheme.Blue))
                using (var accentPath = UiTheme.RoundedRect(new Rectangle(rect.Left + 9, rect.Top + 13, 4, rect.Height - 26), 2))
                {
                    e.Graphics.FillPath(accent, accentPath);
                }
            }

            var name = item == null ? listBox.Items[e.Index].ToString() : item.Name;
            var browserPages = item == null || item.Items == null
                ? 0
                : item.Items.Where(x => string.Equals(x.Kind, "browser", StringComparison.OrdinalIgnoreCase)).Sum(x => Math.Max(1, x.UrlList.Count));
            var meta = item == null
                ? ""
                : (item.Count + " 个窗口" + (browserPages > 0 ? "｜" + browserPages + " 个网页" : "") + "｜" + FormatSavedAt(item.SavedAt));
            var mode = item == null ? "工作" : (string.IsNullOrWhiteSpace(item.TaskMode) ? "工作" : item.TaskMode);
            int textLeft = rect.Left + (selected ? 22 : 14);
            int previewWidth = rect.Width >= 330 ? Math.Min(112, rect.Width / 3) : 0;
            int textWidth = rect.Width - 30 - (previewWidth > 0 ? previewWidth + 12 : 0);
            using (var nameFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold))
            using (var metaFont = new Font("Microsoft YaHei UI", 8.5F))
            {
                TextRenderer.DrawText(e.Graphics, name, nameFont, new Rectangle(textLeft, rect.Top + 13, textWidth, 24), UiTheme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, "开始" + mode, metaFont, new Rectangle(textLeft, rect.Top + 40, textWidth, 20), UiTheme.BlueDark, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, meta, metaFont, new Rectangle(textLeft, rect.Top + 62, textWidth, 20), UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            if (previewWidth > 0 && item != null)
            {
                DrawLayoutPreview(e.Graphics, item, new Rectangle(rect.Right - previewWidth - 12, rect.Top + 13, previewWidth, rect.Height - 26));
            }
        }

        private void DrawLayoutPreview(Graphics g, LayoutSummary summary, Rectangle bounds)
        {
            using (var screenPath = UiTheme.RoundedRect(bounds, 8))
            using (var screenBrush = new SolidBrush(Color.FromArgb(202, 215, 226)))
            using (var screenPen = new Pen(Color.FromArgb(150, 166, 183, 198)))
            {
                g.FillPath(screenBrush, screenPath);
                g.DrawPath(screenPen, screenPath);
            }
            var items = summary.Items ?? new List<LayoutItem>();
            if (items.Count == 0) return;
            int minX = items.Min(x => x.X);
            int minY = items.Min(x => x.Y);
            int maxX = items.Max(x => x.X + Math.Max(1, x.W));
            int maxY = items.Max(x => x.Y + Math.Max(1, x.H));
            double scaleX = (bounds.Width - 12) / (double)Math.Max(1, maxX - minX);
            double scaleY = (bounds.Height - 12) / (double)Math.Max(1, maxY - minY);
            double scale = Math.Min(scaleX, scaleY);
            int usedW = (int)Math.Round((maxX - minX) * scale);
            int usedH = (int)Math.Round((maxY - minY) * scale);
            int offsetX = bounds.X + (bounds.Width - usedW) / 2;
            int offsetY = bounds.Y + (bounds.Height - usedH) / 2;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var r = new Rectangle(
                    offsetX + (int)Math.Round((item.X - minX) * scale),
                    offsetY + (int)Math.Round((item.Y - minY) * scale),
                    Math.Max(4, (int)Math.Round(item.W * scale)),
                    Math.Max(4, (int)Math.Round(item.H * scale)));
                r.Intersect(new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8));
                var fill = string.Equals(item.Kind, "explorer", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(119, 180, 255)
                    : string.Equals(item.Kind, "browser", StringComparison.OrdinalIgnoreCase)
                        ? Color.FromArgb(72, 205, 190)
                        : Color.FromArgb(151, 164, 184);
                using (var brush = new SolidBrush(fill))
                using (var pen = new Pen(Color.FromArgb(180, 255, 255, 255)))
                {
                    g.FillRectangle(brush, r);
                    g.DrawRectangle(pen, r);
                }
            }
        }

        private static string FormatSavedAt(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, out parsed))
            {
                return parsed.ToString("yyyy-MM-dd HH:mm");
            }
            return value ?? "";
        }

        private void RefreshLayouts(string selectName = null)
        {
            listBox.Items.Clear();
            foreach (var layout in manager.GetLayouts())
            {
                listBox.Items.Add(layout);
            }

            if (listBox.Items.Count > 0)
            {
                var selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(selectName))
                {
                    for (int i = 0; i < listBox.Items.Count; i++)
                    {
                        var item = listBox.Items[i] as LayoutSummary;
                        if (item != null && string.Equals(item.Name, selectName, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }
                listBox.SelectedIndex = selectedIndex;
            }
            statusLabel.Text = listBox.Items.Count > 0 ? "已加载 " + listBox.Items.Count + " 个布局。" : "还没有布局。先摆好窗口，然后点“保存为新布局”。";
            UpdateSelectedLayoutUi();
            RebuildTrayMenu();
        }

        private void UpdateSelectedLayoutUi()
        {
            var selected = SelectedLayout();
            if (openButton == null) return;
            openButton.Text = selected == null ? "打开布局" : "开始" + (string.IsNullOrWhiteSpace(selected.TaskMode) ? "工作" : selected.TaskMode);
        }

        private LayoutSummary SelectedLayout()
        {
            return listBox.SelectedItem as LayoutSummary;
        }

        private void RestoreSelected()
        {
            var selected = SelectedLayout();
            if (selected == null)
            {
                statusLabel.Text = "先选一个布局。";
                return;
            }

            RestoreLayout(selected);
        }

        private void RestoreLayout(LayoutSummary selected)
        {
            if (selected == null) return;
            var health = manager.CheckHealth(selected);
            var visibleIssues = health.Issues
                .Where(x => !string.Equals(x.Level, "info", StringComparison.OrdinalIgnoreCase))
                .Select(x => "• " + x.Message)
                .Distinct()
                .ToList();
            if (visibleIssues.Count > 0)
            {
                ShowMainWindow();
                var answer = MessageBox.Show(this,
                    "打开前发现以下情况：\n\n" + string.Join("\n", visibleIssues.ToArray()) + "\n\n仍然继续吗？",
                    "布局健康检查",
                    MessageBoxButtons.YesNo,
                    health.HasBlockingIssues ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                if (answer != DialogResult.Yes)
                {
                    statusLabel.Text = "已取消，先处理布局健康检查中的问题。";
                    return;
                }
            }

            statusLabel.Text = "正在恢复：" + selected.Name;
            Refresh();
            var sw = Stopwatch.StartNew();
            var result = manager.Restore(selected.Name);
            sw.Stop();

            if (result.Failed > 0 && result.FailDetails != null && result.FailDetails.Count > 0)
            {
                statusLabel.Text = result.Message + "，用时 " + sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒。详情已写入日志。";
                NotificationToastForm.ShowRestoreResult(result);
            }
            else
            {
                statusLabel.Text = result.Message + "，用时 " + sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒。";
            }
        }

        private void EditBrowserUrls()
        {
            var selected = SelectedLayout();
            if (selected == null)
            {
                statusLabel.Text = "先选一个布局。";
                return;
            }

            var layout = manager.GetLayout(selected);
            var browserItems = (layout == null ? new List<LayoutItem>() : (layout.Items ?? new List<LayoutItem>()))
                .Where(x => string.Equals(x.Kind, "browser", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (browserItems.Count == 0)
            {
                statusLabel.Text = "这个布局里没有浏览器窗口。";
                return;
            }

            using (var form = new BrowserUrlsForm(browserItems))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    statusLabel.Text = "已取消。";
                    return;
                }
            }

            manager.SaveLayout(selected.Name, layout);
            RefreshLayouts(selected.Name);
            statusLabel.Text = "已更新网页地址：" + selected.Name + "（" + browserItems.Count + " 个浏览器窗口）。";
        }

        private void SaveNew()
        {            var name = PromptForm.Ask("保存为新布局", "给这个布局起个名字：", "新布局");
            if (string.IsNullOrWhiteSpace(name))
            {
                statusLabel.Text = "已取消。";
                return;
            }
            var taskMode = string.IsNullOrWhiteSpace(pendingTaskMode) ? LayoutManager.InferTaskMode(name) : pendingTaskMode;
            var layout = manager.SaveCurrent(name, taskMode);
            pendingTaskMode = null;
            RefreshLayouts(layout.Name);
            statusLabel.Text = "已保存：" + layout.Name + "（" + layout.Items.Count + " 个窗口）。";
        }

        private void OverwriteSelected()
        {
            var selected = SelectedLayout();
            if (selected == null)
            {
                statusLabel.Text = "先选一个要覆盖的布局。";
                return;
            }
            var layout = manager.SaveCurrent(selected.Name, selected.TaskMode);
            RefreshLayouts(layout.Name);
            statusLabel.Text = "已覆盖：" + layout.Name + "（" + layout.Items.Count + " 个窗口）。";
        }

        private void RenameSelected()
        {
            var selected = SelectedLayout();
            if (selected == null)
            {
                statusLabel.Text = "先选一个要重命名的布局。";
                return;
            }

            var name = PromptForm.Ask("重命名布局", "新的布局名称：", selected.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                statusLabel.Text = "已取消。";
                return;
            }

            try
            {
                var layout = manager.RenameLayout(selected, name);
                RefreshLayouts(layout.Name);
                statusLabel.Text = "已重命名：" + layout.Name;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "重命名失败：" + ex.Message;
            }
        }

        private void DeleteSelected()
        {
            var selected = SelectedLayout();
            if (selected == null)
            {
                statusLabel.Text = "先选一个要删除的布局。";
                return;
            }
            var ok = MessageBox.Show(this, "删除布局“" + selected.Name + "”？", "确认删除", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;
            manager.DeleteLayout(selected);
            RefreshLayouts();
            statusLabel.Text = "已删除：" + selected.Name;
        }

        private void ExportSelected()
        {
            var selected = SelectedLayout();
            if (selected == null)
            {
                statusLabel.Text = "先选一个要导出的布局。";
                return;
            }
            try
            {
                var json = manager.ExportLayoutJson(selected);
                statusLabel.Text = "已导出 JSON：" + json;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "导出失败：" + ex.Message;
            }
        }

        private void ImportLayout()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "导入布局 JSON";
                dialog.Filter = "布局 JSON (*.json)|*.json|所有文件 (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.CheckFileExists = true;
                dialog.InitialDirectory = Directory.Exists(manager.LayoutDir) ? manager.LayoutDir : AppDomain.CurrentDomain.BaseDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    statusLabel.Text = "已取消。";
                    return;
                }

                try
                {
                    var layout = manager.ImportLayoutJson(dialog.FileName);
                    RefreshLayouts(layout.Name);
                    statusLabel.Text = "已导入：" + layout.Name;
                }
                catch (Exception ex)
                {
                    statusLabel.Text = "导入失败：" + ex.Message;
                }
            }
        }

    }

    [DataContract]
    public class VersionInfo
    {
        [DataMember(Name = "version")]
        public string Version { get; set; }

        [DataMember(Name = "download_url")]
        public string DownloadUrl { get; set; }

        [DataMember(Name = "release_notes")]
        public string ReleaseNotes { get; set; }

        [DataMember(Name = "published_at")]
        public string PublishedAt { get; set; }
    }

    public static class UpdateChecker
    {
        public static VersionInfo CheckForUpdate(out string error)
        {
            error = null;
            try
            {
                EnableModernTls();
                var url = Program.VersionCheckUrl + "?t=" + DateTime.UtcNow.Ticks;
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.Method = "GET";
                request.UserAgent = "WindowLayoutLauncher/" + Program.Version;
                request.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                request.Headers.Add("Cache-Control", "no-cache");
                request.Headers.Add("Pragma", "no-cache");

                using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var json = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        error = "版本信息为空";
                        return null;
                    }

                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(VersionInfo));
                        var info = (VersionInfo)serializer.ReadObject(ms);
                        if (info == null || string.IsNullOrWhiteSpace(info.Version))
                        {
                            error = "版本信息格式错误";
                            return null;
                        }
                        return info;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static bool IsNewerVersion(string remoteVersion, string currentVersion)
        {
            try
            {
                var r = NormalizeVersion(remoteVersion);
                var c = NormalizeVersion(currentVersion);
                if (r == null || c == null) return false;
                return r > c;
            }
            catch
            {
                return false;
            }
        }

        private static Version NormalizeVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            var cleaned = new StringBuilder();
            foreach (var ch in v)
            {
                if (char.IsDigit(ch) || ch == '.')
                    cleaned.Append(ch);
            }
            var parts = cleaned.ToString().Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            var nums = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                nums.Add(i < parts.Length ? int.Parse(parts[i]) : 0);
            }
            return new Version(nums[0], nums[1], nums[2], nums[3]);
        }

        public static string DownloadUpdate(string url, string targetPath, Action<int> progressCallback)
        {
            try
            {
                EnableModernTls();
                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.UserAgent] = "WindowLayoutLauncher/" + Program.Version;
                    var done = false;
                    client.DownloadProgressChanged += (s, e) =>
                    {
                        if (progressCallback != null) progressCallback(e.ProgressPercentage);
                    };
                    client.DownloadFileCompleted += (s, e) =>
                    {
                        done = true;
                    };
                    client.DownloadFileAsync(new Uri(url), targetPath);
                    while (!done)
                    {
                        Application.DoEvents();
                        Thread.Sleep(50);
                    }
                }
                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    return null;
                }
                return "下载文件为空";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static void EnableModernTls()
        {
            const System.Net.SecurityProtocolType tls12 = (System.Net.SecurityProtocolType)3072;
            System.Net.ServicePointManager.SecurityProtocol = System.Net.ServicePointManager.SecurityProtocol | tls12;
        }

        public static bool ApplyUpdate(string newExePath)
        {
            try
            {
                var currentExe = Application.ExecutablePath;
                var dir = Path.GetDirectoryName(currentExe);
                var batPath = Path.Combine(dir, "update.bat");
                var oldExeBackup = Path.Combine(dir, "窗口管理器_old.exe");

                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("echo 正在更新窗口管理器...");
                sb.AppendLine("timeout /t 2 /nobreak >nul");
                sb.AppendLine("if exist \"" + oldExeBackup + "\" del \"" + oldExeBackup + "\"");
                sb.AppendLine("ren \"" + currentExe + "\" \"" + Path.GetFileName(oldExeBackup) + "\"");
                sb.AppendLine("move /Y \"" + newExePath + "\" \"" + currentExe + "\"");
                sb.AppendLine("echo 更新完成，正在启动新版本...");
                sb.AppendLine("start \"\" \"" + currentExe + "\"");
                sb.AppendLine("del \"%~f0\"");
                File.WriteAllText(batPath, sb.ToString(), Encoding.Default);

                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    WorkingDirectory = dir,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class NotificationToastForm : Form
    {
        private readonly System.Windows.Forms.Timer closeTimer;

        public static NotificationToastForm ForRestoreResult(RestoreResult result)
        {
            int ok = result == null ? 0 : result.Ok;
            int failed = result == null ? 0 : result.Failed;
            string message = "已恢复 " + ok + " 个窗口，" + failed + " 个未恢复\n详情已自动保存到日志";
            return new NotificationToastForm("布局恢复完成", message);
        }

        public static void ShowRestoreResult(RestoreResult result)
        {
            var toast = ForRestoreResult(result);
            toast.FormClosed += (s, e) => toast.Dispose();
            toast.Show();
        }

        private NotificationToastForm(string title, string message)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(420, 108);
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = UiTheme.PanelLight;
            Opacity = 0.97;
            Font = new Font("Microsoft YaHei UI", 9F);

            var accent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 5,
                BackColor = UiTheme.Amber
            };
            Controls.Add(accent);

            var titleLabel = new Label
            {
                Text = title,
                Left = 26,
                Top = 18,
                Width = 365,
                Height = 25,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UiTheme.Ink,
                BackColor = Color.Transparent
            };
            Controls.Add(titleLabel);

            var messageLabel = new Label
            {
                Text = message,
                Left = 26,
                Top = 48,
                Width = 365,
                Height = 45,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UiTheme.Muted,
                BackColor = Color.Transparent
            };
            Controls.Add(messageLabel);

            closeTimer = new System.Windows.Forms.Timer { Interval = 2100 };
            closeTimer.Tick += (s, e) =>
            {
                closeTimer.Stop();
                Close();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WsExToolWindow = 0x00000080;
                const int WsExNoActivate = 0x08000000;
                var parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(
                area.Left + Math.Max(0, (area.Width - Width) / 2),
                area.Top + Math.Max(0, (area.Height - Height) / 2));
            using (var path = UiTheme.RoundedRect(new Rectangle(0, 0, Width, Height), 12))
            {
                Region = new Region(path);
            }
            closeTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && closeTimer != null)
            {
                closeTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class AboutForm : Form
    {
        private Label titleLabel;
        private Label versionLabel;
        private Label descLabel;
        private GlassButton checkUpdateButton;
        private GlassButton closeButton;
        private Label updateStatusLabel;

        public AboutForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "关于 " + Program.AppName;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UiTheme.WindowTop;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(34, 26, 34, 24);
            root.ColumnCount = 1;
            root.RowCount = 10;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            Controls.Add(root);

            titleLabel = new Label
            {
                Text = Program.AppName,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = UiTheme.Ink,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(titleLabel, 0, 0);

            versionLabel = new Label
            {
                Text = "版本 v" + Program.Version + "  ·  窗口 · 浏览器配置 · 网页一键还原",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = UiTheme.Muted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(versionLabel, 0, 1);

            descLabel = new Label
            {
                Text = "把一组文件夹、网页和软件窗口保存成工作布局。下次开始工作时，直接恢复已经打开的窗口，并把它们放回对应屏幕和位置。",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = UiTheme.Ink,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(descLabel, 0, 2);

            root.Controls.Add(SectionLabel("设计思路"), 0, 3);
            root.Controls.Add(BodyLabel("少开窗口、少找入口、尽量复用已经打开的内容。布局只记录窗口位置和启动规则，不上传个人文件；主界面、任务栏和托盘共用同一套布局。"), 0, 4);
            root.Controls.Add(SectionLabel("怎么使用"), 0, 5);
            root.Controls.Add(BodyLabel("1. 打开工作需要的文件夹、网页和软件，摆好位置。\n2. 回到主界面，选择“保存为新布局”。\n3. 以后从主界面、任务栏右键或托盘菜单直接开始工作。"), 0, 6);

            updateStatusLabel = new Label
            {
                Text = "更新由你主动检查，程序启动时不会打扰你。",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UiTheme.Muted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(updateStatusLabel, 0, 8);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            root.Controls.Add(buttons, 0, 9);

            checkUpdateButton = new GlassButton
            {
                Text = "检查更新",
                Dock = DockStyle.Fill,
                Primary = false,
                SurfaceColor = UiTheme.WindowTop,
                BackColor = UiTheme.WindowTop
            };
            checkUpdateButton.Click += CheckUpdateButton_Click;
            buttons.Controls.Add(checkUpdateButton, 0, 0);

            closeButton = new GlassButton
            {
                Text = "关闭",
                Dock = DockStyle.Fill,
                Primary = true,
                SurfaceColor = UiTheme.WindowTop,
                BackColor = UiTheme.WindowTop,
                DialogResult = DialogResult.OK
            };
            buttons.Controls.Add(closeButton, 2, 0);

            AcceptButton = closeButton;
            CancelButton = closeButton;
        }

        private static Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UiTheme.Ink,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft
            };
        }

        private static Label BodyLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.3F),
                ForeColor = UiTheme.Muted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft
            };
        }

        private void CheckUpdateButton_Click(object sender, EventArgs e)
        {
            checkUpdateButton.Enabled = false;
            updateStatusLabel.Text = "正在检查更新...";
            updateStatusLabel.ForeColor = UiTheme.Muted;
            Refresh();

            string error;
            var info = UpdateChecker.CheckForUpdate(out error);

            if (info == null)
            {
                updateStatusLabel.Text = "检查失败：" + (error ?? "未知错误");
                updateStatusLabel.ForeColor = Color.FromArgb(220, 80, 80);
                checkUpdateButton.Enabled = true;
                return;
            }

            if (UpdateChecker.IsNewerVersion(info.Version, Program.Version))
            {
                updateStatusLabel.Text = "发现新版本 v" + info.Version;
                updateStatusLabel.ForeColor = UiTheme.Blue;
                using (var prompt = new UpdatePromptForm(info))
                {
                    if (prompt.ShowDialog(this) == DialogResult.OK)
                    {
                        DoUpdate(info);
                    }
                    else
                    {
                        updateStatusLabel.Text = "已跳过版本 v" + info.Version;
                        updateStatusLabel.ForeColor = UiTheme.Muted;
                    }
                }
            }
            else
            {
                updateStatusLabel.Text = "当前已是最新版本";
                updateStatusLabel.ForeColor = Color.FromArgb(60, 160, 100);
            }
            checkUpdateButton.Enabled = true;
        }

        private void DoUpdate(VersionInfo info)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WLL_Update");
            Directory.CreateDirectory(tempDir);
            var newExe = Path.Combine(tempDir, "窗口管理器_new.exe");

            var progressForm = new Form();
            progressForm.Text = "正在下载更新";
            progressForm.Size = UiTheme.DpiSize(360, 120);
            progressForm.StartPosition = FormStartPosition.CenterParent;
            progressForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            progressForm.MaximizeBox = false;
            progressForm.MinimizeBox = false;
            progressForm.BackColor = UiTheme.WindowTop;
            progressForm.Font = new Font("Microsoft YaHei UI", 9F);

            var progressLabel = new Label();
            progressLabel.Text = "正在下载新版本...";
            progressLabel.Left = 25;
            progressLabel.Top = 20;
            progressLabel.Width = 300;
            progressLabel.ForeColor = UiTheme.Ink;
            progressLabel.BackColor = Color.Transparent;
            progressForm.Controls.Add(progressLabel);

            var progressBar = new ProgressBar();
            progressBar.Left = 25;
            progressBar.Top = 50;
            progressBar.Width = 300;
            progressBar.Height = 20;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressForm.Controls.Add(progressBar);

            progressForm.Shown += (s, e) =>
            {
                var error = UpdateChecker.DownloadUpdate(info.DownloadUrl, newExe, p =>
                {
                    try
                    {
                        progressBar.Value = Math.Max(0, Math.Min(100, p));
                        progressLabel.Text = "正在下载... " + p + "%";
                    }
                    catch { }
                });

                progressForm.DialogResult = string.IsNullOrEmpty(error) ? DialogResult.OK : DialogResult.Abort;
                progressForm.Close();
            };

            var result = progressForm.ShowDialog(this);
            if (result != DialogResult.OK)
            {
                updateStatusLabel.Text = "下载失败";
                updateStatusLabel.ForeColor = Color.FromArgb(220, 80, 80);
                return;
            }

            if (UpdateChecker.ApplyUpdate(newExe))
            {
                Application.Exit();
            }
            else
            {
                MessageBox.Show(this, "更新失败，请稍后重试。", "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class UpdatePromptForm : Form
    {
        public UpdatePromptForm(VersionInfo info)
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "发现新版本";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UiTheme.WindowTop;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 24, 28, 22),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            Controls.Add(root);

            root.Controls.Add(new Label
            {
                Text = "发现新版本 v" + (info == null ? "" : info.Version),
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = UiTheme.Ink,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            root.Controls.Add(new Label
            {
                Text = "本次更新内容",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = UiTheme.Muted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 1);

            root.Controls.Add(new Label
            {
                Text = info == null || string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "体验优化与问题修复。" : info.ReleaseNotes,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.3F),
                ForeColor = UiTheme.Ink,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft
            }, 0, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            var update = new GlassButton
            {
                Text = "立刻更新",
                Width = 105,
                Height = 36,
                Primary = true,
                SurfaceColor = UiTheme.WindowTop,
                BackColor = UiTheme.WindowTop,
                DialogResult = DialogResult.OK
            };
            var skip = new GlassButton
            {
                Text = "跳过本次版本",
                Width = 130,
                Height = 36,
                Primary = false,
                SurfaceColor = UiTheme.WindowTop,
                BackColor = UiTheme.WindowTop,
                DialogResult = DialogResult.Cancel
            };
            buttons.Controls.Add(update);
            buttons.Controls.Add(skip);
            root.Controls.Add(buttons, 0, 3);

            AcceptButton = update;
            CancelButton = skip;
        }
    }

    public class SetupWizardForm : Form
    {
        private ComboBox modeBox;
        public string TaskMode { get { return Convert.ToString(modeBox.SelectedItem); } }

        public SetupWizardForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "第一次使用";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(500, 310);
            MinimumSize = new Size(460, 290);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UiTheme.WindowTop;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(34, 28, 34, 24);
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            Controls.Add(root);

            var title = new Label { Text = "你要创建哪种工作布局？", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold), ForeColor = UiTheme.Ink };
            root.Controls.Add(title, 0, 0);
            var guide = new Label { Text = "选择工作类型后，打开需要的文件夹、网页和软件，把窗口摆好，再回到这里保存。", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted };
            root.Controls.Add(guide, 0, 1);
            var label = new Label { Text = "工作类型", Dock = DockStyle.Fill, ForeColor = UiTheme.Ink, TextAlign = ContentAlignment.BottomLeft };
            root.Controls.Add(label, 0, 2);

            modeBox = new ComboBox();
            modeBox.Dock = DockStyle.Top;
            modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modeBox.Items.AddRange(new object[] { "素材处理", "GPT 双开", "剪辑", "写小红书", "运营", "客服工作", "做团建方案", "其他工作" });
            modeBox.SelectedIndex = 0;
            root.Controls.Add(modeBox, 0, 3);

            var tip = new Label { Text = "这一步不会替你打开固定软件，只负责确定这套布局以后显示成“开始什么工作”。", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.TopLeft };
            root.Controls.Add(tip, 0, 4);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var ok = new GlassButton { Text = "开始准备", Width = 110, Height = 36, Primary = true, SurfaceColor = UiTheme.WindowTop, BackColor = UiTheme.WindowTop, DialogResult = DialogResult.OK };
            var cancel = new GlassButton { Text = "稍后再说", Width = 100, Height = 36, SurfaceColor = UiTheme.WindowTop, BackColor = UiTheme.WindowTop, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 5);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    public class PromptForm : Form
    {
        private TextBox textBox;
        public string Value { get { return textBox.Text; } }

        public static string Ask(string title, string message, string defaultValue)
        {
            using (var form = new PromptForm(title, message, defaultValue))
            {
                return form.ShowDialog() == DialogResult.OK ? form.Value : null;
            }
        }

        private PromptForm(string title, string message, string defaultValue)
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            Size = UiTheme.DpiSize(420, 178);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UiTheme.WindowTop;

            var label = new Label();
            label.Text = message;
            label.Left = 24;
            label.Top = 22;
            label.Width = 350;
            label.Height = 24;
            label.ForeColor = UiTheme.Ink;
            label.BackColor = Color.Transparent;
            Controls.Add(label);

            textBox = new TextBox();
            textBox.Left = 24;
            textBox.Top = 54;
            textBox.Width = 354;
            textBox.Height = 26;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Text = defaultValue;
            Controls.Add(textBox);

            var ok = new GlassButton();
            ok.Text = "确定";
            ok.Left = 212;
            ok.Top = 100;
            ok.Width = 78;
            ok.Height = 34;
            ok.Primary = true;
            ok.SurfaceColor = UiTheme.WindowTop;
            ok.BackColor = UiTheme.WindowTop;
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            var cancel = new GlassButton();
            cancel.Text = "取消";
            cancel.Left = 300;
            cancel.Top = 100;
            cancel.Width = 78;
            cancel.Height = 34;
            cancel.SurfaceColor = UiTheme.WindowTop;
            cancel.BackColor = UiTheme.WindowTop;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => { textBox.SelectAll(); textBox.Focus(); };
        }
    }

    /// <summary>编辑布局里每个浏览器窗口保存的网址（一行一个）。</summary>
    public class BrowserUrlsForm : Form
    {
        private readonly List<LayoutItem> items;
        private readonly List<TextBox> boxes = new List<TextBox>();

        public BrowserUrlsForm(List<LayoutItem> browserItems)
        {
            items = browserItems ?? new List<LayoutItem>();

            AutoScaleMode = AutoScaleMode.None;
            Text = "网页地址";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(700, 610);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UiTheme.WindowTop;

            var header = new Label();
            header.Text = "每个浏览器配置可以保存多个网址：一行一个，恢复布局时按顺序在这个配置里打开。";
            header.SetBounds(24, 16, ClientSize.Width - 48, 24);
            header.ForeColor = UiTheme.Muted;
            header.BackColor = Color.Transparent;
            Controls.Add(header);

            var hint = new Label();
            hint.Text = "留空则只打开这个配置的窗口，不指定网页。";
            hint.SetBounds(24, 40, ClientSize.Width - 48, 22);
            hint.ForeColor = UiTheme.Muted;
            hint.BackColor = Color.Transparent;
            Controls.Add(hint);

            var scroll = new Panel();
            scroll.SetBounds(20, 70, ClientSize.Width - 40, ClientSize.Height - 148);
            scroll.AutoScroll = true;
            scroll.BackColor = UiTheme.PanelLight;
            Controls.Add(scroll);

            int top = 12;
            int boxWidth = scroll.Width - 46;
            foreach (var item in items)
            {
                var title = new Label();
                title.Text = BrowserProbe.FamilyLabel(item.Browser) + "  ·  " + Caption(item);
                title.SetBounds(14, top, boxWidth, 22);
                title.ForeColor = UiTheme.Ink;
                title.BackColor = Color.Transparent;
                title.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                scroll.Controls.Add(title);

                var box = new TextBox();
                box.Multiline = true;
                box.ScrollBars = ScrollBars.Vertical;
                box.BorderStyle = BorderStyle.FixedSingle;
                box.SetBounds(14, top + 26, boxWidth, 84);
                box.Text = string.Join("\r\n", item.UrlList.ToArray());
                scroll.Controls.Add(box);
                boxes.Add(box);

                top += 126;
            }

            var ok = new GlassButton();
            ok.Text = "保存";
            ok.SetBounds(ClientSize.Width - 208, ClientSize.Height - 58, 84, 34);
            ok.Primary = true;
            ok.SurfaceColor = UiTheme.WindowTop;
            ok.BackColor = UiTheme.WindowTop;
            ok.Click += (s, e) =>
            {
                Apply();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            var cancel = new GlassButton();
            cancel.Text = "取消";
            cancel.SetBounds(ClientSize.Width - 112, ClientSize.Height - 58, 84, 34);
            cancel.SurfaceColor = UiTheme.WindowTop;
            cancel.BackColor = UiTheme.WindowTop;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static string Caption(LayoutItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.ProfileName)) return item.ProfileName;
            if (!string.IsNullOrWhiteSpace(item.Profile)) return item.Profile;
            return string.IsNullOrWhiteSpace(item.Title) ? "未识别配置" : item.Title;
        }

        private void Apply()
        {
            for (int i = 0; i < items.Count && i < boxes.Count; i++)
            {
                var urls = boxes[i].Text
                    .Replace("\r\n", "\n")
                    .Split('\n')
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();
                items[i].Urls = urls;
                items[i].Url = urls.Count > 0 ? urls[0] : null;
            }
        }
    }
}

