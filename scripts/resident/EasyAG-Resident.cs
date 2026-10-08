using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace EasyAGResident {
    public class Program {
        #region Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessWindowStation(IntPtr hWinSta);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        #endregion

        private static Application wpfApp;
        private static Window capsuleWin;
        private static NotifyIcon trayIcon;
        private static ContextMenuStrip trayMenu;

        private static IntPtr eaHwnd = IntPtr.Zero;
        private static IntPtr agHwnd = IntPtr.Zero;
        private static int eaPid = 0;
        private static int agPid = 0;
        private static int backendPort = 8080;
        private static string currentType = "danger";
        private static DispatcherTimer autoHideTimer;

        // Capsule UI Elements
        private static Border cardBorder;
        private static DropShadowEffect cardGlow;
        private static Ellipse stateDot;
        private static TextBlock stateCatText;
        private static TextBlock stateMainText;
        private static TextBlock stateSubText;
        private static Button actionBtn;
        private static TextBlock actionBtnText;
        private static bool lightTheme;

        private static SolidColorBrush ThemeBrush(string light, string dark) {
            return new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? light : dark));
        }

        private static Button FlatButton(object content) {
            var button = new Button {
                Content = content,
                Foreground = ThemeBrush("#202020", "#F5F5F5"),
                Background = ThemeBrush("#F0F0F0", "#333333"),
                BorderBrush = ThemeBrush("#DFDFDF", "#414141"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 5, 14, 5),
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            string hover = lightTheme ? "#E8E8E8" : "#3D3D3D";
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                "<Border x:Name='Surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='5' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='" + hover + "'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='0.8'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>");
            return button;
        }

        public static void LogEvent(string json) {
            try {
                Console.WriteLine(json);
                Console.Out.Flush();
            } catch { }
        }

        public static void ForceForeground(IntPtr hWnd) {
            if (hWnd == IntPtr.Zero) return;
            try {
                ShowWindowAsync(hWnd, 9); // SW_RESTORE
                IntPtr fgWnd = GetForegroundWindow();
                uint dummy = 0;
                uint fgThread = GetWindowThreadProcessId(fgWnd, out dummy);
                uint curThread = GetCurrentThreadId();
                if (fgThread != 0 && fgThread != curThread) {
                    AttachThreadInput(curThread, fgThread, true);
                    SetForegroundWindow(hWnd);
                    AttachThreadInput(curThread, fgThread, false);
                } else {
                    SetForegroundWindow(hWnd);
                }
            } catch { }
        }

        public static bool IsAntigravityForeground() {
            try {
                IntPtr fgWnd = GetForegroundWindow();
                if (fgWnd == IntPtr.Zero) return false;
                if (agHwnd != IntPtr.Zero && fgWnd == agHwnd) return true;

                uint pId = 0;
                GetWindowThreadProcessId(fgWnd, out pId);
                if (agPid != 0 && pId == agPid) return true;

                var proc = System.Diagnostics.Process.GetProcessById((int)pId);
                if (proc.ProcessName.Equals("Antigravity", StringComparison.OrdinalIgnoreCase)) return true;
            } catch { }
            return false;
        }

        public static void RefreshWindowHandles() {
            try {
                IntPtr bestAgHwnd = IntPtr.Zero;
                int bestAgArea = 0;

                EnumWindows((hWnd, lParam) => {
                    if (!IsWindowVisible(hWnd)) return true;

                    StringBuilder title = new StringBuilder(512);
                    GetWindowText(hWnd, title, 512);
                    StringBuilder cls = new StringBuilder(256);
                    GetClassName(hWnd, cls, 256);
                    string t = title.ToString();
                    string c = cls.ToString();

                    uint pId = 0;
                    GetWindowThreadProcessId(hWnd, out pId);

                    // Find EasyAntigravity
                    if (c == "Tauri Window" || (t == "EasyAntigravity" && (eaPid == 0 || pId == eaPid))) {
                        eaHwnd = hWnd;
                    }

                    // Find Antigravity:
                    // 1. Must be Chrome_WidgetWin_1 (main viewport), never 0 or utility
                    // 2. Not EasyAntigravity process or window
                    // 3. Must be visible, non-empty title, and substantial size
                    if (c == "Chrome_WidgetWin_1" && pId != eaPid && t != "EasyAntigravity" && t != "Hidden Window" && !string.IsNullOrEmpty(t)) {
                        bool isAg = false;
                        if (agPid != 0 && pId == agPid) isAg = true;
                        else {
                            try {
                                var proc = System.Diagnostics.Process.GetProcessById((int)pId);
                                if (proc.ProcessName.Equals("Antigravity", StringComparison.OrdinalIgnoreCase)) isAg = true;
                            } catch { }
                        }
                        if (isAg) {
                            RECT r;
                            GetWindowRect(hWnd, out r);
                            int w = r.Right - r.Left;
                            int h = r.Bottom - r.Top;
                            int area = w * h;
                            if (w > 400 && h > 300 && area > bestAgArea) {
                                bestAgArea = area;
                                bestAgHwnd = hWnd;
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                if (bestAgHwnd != IntPtr.Zero) {
                    agHwnd = bestAgHwnd;
                }
            } catch { }
        }

        [STAThread]
        public static void Main(string[] args) {
            try {
                Console.InputEncoding = Encoding.UTF8;
                Console.OutputEncoding = Encoding.UTF8;
            } catch { }

            // Attach to interactive desktop station if possible
            try {
                IntPtr hWinsta = OpenWindowStation("winsta0", false, 0x037F);
                if (hWinsta != IntPtr.Zero) SetProcessWindowStation(hWinsta);
                IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
                if (hDesk != IntPtr.Zero) SetThreadDesktop(hDesk);
            } catch { }

            // Background thread to read commands from stdin immediately
            Thread stdinThread = new Thread(ReadCommandsLoop);
            stdinThread.IsBackground = true;
            stdinThread.Start();

            // Emit ready handshake signal immediately so caller never times out
            LogEvent("{\"event\":\"ready\"}");

            try {
                wpfApp = new Application();
                wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                InitTrayIcon();
                InitCapsuleWindow();

                wpfApp.Run();
            } catch (Exception) {
                // If GUI fails in headless or restricted CI environments, keep stdin alive
                while (true) {
                    Thread.Sleep(1000);
                }
            }
        }

        private static void InitTrayIcon() {
            try {
                trayMenu = new ContextMenuStrip();
                trayMenu.ShowImageMargin = false;
                trayMenu.ShowCheckMargin = false;
                trayMenu.BackColor = System.Drawing.Color.FromArgb(0x18, 0x19, 0x22);
                trayMenu.ForeColor = System.Drawing.Color.FromArgb(0xF1, 0xF5, 0xF9);
                trayMenu.Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Regular);
                trayMenu.Renderer = new ModernMenuRenderer();
                trayMenu.Padding = new Padding(2);

                // 风格对齐 Clash Party：短标签、动作一组、退出单独一组并带 Ctrl+Q
                var itemOpen = trayMenu.Items.Add("显示窗口");
                itemOpen.Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold);
                itemOpen.Padding = new Padding(14, 6, 14, 6);
                itemOpen.Click += (s, e) => {
                    ShowEasyAG();
                    LogEvent("{\"event\":\"tray_open\"}");
                };

                var itemWeb = trayMenu.Items.Add("浏览器控制台");
                itemWeb.Padding = new Padding(14, 6, 14, 6);
                itemWeb.Click += (s, e) => {
                    try {
                        System.Diagnostics.Process.Start(string.Format("http://127.0.0.1:{0}", backendPort));
                    } catch { }
                    LogEvent("{\"event\":\"tray_web\"}");
                };

                var sep = new ToolStripSeparator();
                sep.Margin = new Padding(4, 2, 4, 2);
                trayMenu.Items.Add(sep);

                var itemExit = new ToolStripMenuItem("退出应用");
                trayMenu.Items.Add(itemExit);
                itemExit.Padding = new Padding(14, 6, 14, 6);
                itemExit.ShortcutKeyDisplayString = "Ctrl+Q";
                itemExit.Click += (s, e) => {
                    LogEvent("{\"event\":\"tray_exit\"}");
                    ShutdownResident();
                };

                trayIcon = new NotifyIcon();
                trayIcon.Text = "EasyAntigravity (后台运行中)";
                trayIcon.ContextMenuStrip = trayMenu;

                // Load Icon
                try {
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    string iconPath = System.IO.Path.Combine(exeDir, "icon.ico");
                    if (!File.Exists(iconPath)) iconPath = System.IO.Path.Combine(exeDir, "assets", "icon.ico");
                    if (!File.Exists(iconPath)) iconPath = System.IO.Path.Combine(exeDir, "..", "assets", "icon.ico");
                    if (!File.Exists(iconPath)) iconPath = System.IO.Path.Combine(exeDir, "..", "src-tauri", "icons", "icon.ico");
                    if (!File.Exists(iconPath)) iconPath = System.IO.Path.Combine(exeDir, "..", "icon.ico");
                    if (File.Exists(iconPath)) {
                        trayIcon.Icon = new Icon(iconPath);
                    } else {
                        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    }
                } catch {
                    try {
                        trayIcon.Icon = SystemIcons.Application;
                    } catch { }
                }

                trayIcon.Visible = true;
                trayIcon.DoubleClick += (s, e) => {
                    ShowEasyAG();
                    LogEvent("{\"event\":\"tray_open\"}");
                };
            } catch { }
        }

        private static void InitCapsuleWindow() {
            try {
                try {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize")) {
                        lightTheme = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 0)) != 0;
                    }
                } catch { lightTheme = false; }
                capsuleWin = new Window {
                    Title = "EasyAG_HUD_Capsule",
                    Width = 400,
                    Height = 218,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    UseLayoutRounding = true,
                    SnapsToDevicePixels = true
                };

                try {
                    Rect workArea = SystemParameters.WorkArea;
                    if (workArea.Width > 0 && workArea.Height > 0) {
                        capsuleWin.Left = workArea.Right - capsuleWin.Width - 18;
                        capsuleWin.Top = workArea.Bottom - capsuleWin.Height - 16;
                    } else {
                        capsuleWin.Left = 800;
                        capsuleWin.Top = 600;
                    }
                } catch {
                    capsuleWin.Left = 800;
                    capsuleWin.Top = 600;
                }
                capsuleWin.MouseEnter += (s, e) => {
                    if (autoHideTimer != null) autoHideTimer.Stop();
                };
                capsuleWin.MouseLeave += (s, e) => RestartAutoHide();

            Grid rootGrid = new Grid();
            rootGrid.ClipToBounds = false;

            cardGlow = new DropShadowEffect {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.18
            };

            cardBorder = new Border {
                CornerRadius = new CornerRadius(8),
                Background = ThemeBrush("#FAFAFA", "#262626"),
                BorderBrush = ThemeBrush("#DFDFDF", "#414141"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8),
                Effect = cardGlow
            };

            Grid contentGrid = new Grid { Margin = new Thickness(18, 14, 18, 14) };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Header
            DockPanel header = new DockPanel { LastChildFill = true };
            StackPanel titleStack = new StackPanel { Orientation = Orientation.Horizontal };
            stateDot = new Ellipse {
                Width = 5,
                Height = 5,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            stateCatText = new TextBlock {
                FontWeight = FontWeights.Normal,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(stateDot);
            titleStack.Children.Add(stateCatText);
            Button closeBtn = FlatButton("×");
            closeBtn.FontSize = 18;
            closeBtn.Width = 26;
            closeBtn.Height = 26;
            closeBtn.Padding = new Thickness(0);
            closeBtn.Margin = new Thickness(0, -3, -6, -3);
            closeBtn.Background = Brushes.Transparent;
            closeBtn.BorderThickness = new Thickness(0);
            closeBtn.ToolTip = "关闭提示";
            System.Windows.Automation.AutomationProperties.SetName(closeBtn, "关闭提示");
            closeBtn.Click += (s, e) => {
                HideCapsule();
                LogEvent("{\"event\":\"capsule_close\"}");
            };
            DockPanel.SetDock(closeBtn, Dock.Right);
            header.Children.Add(closeBtn);
            var appName = new TextBlock {
                Text = "EasyAntigravity",
                FontSize = 12,
                Foreground = ThemeBrush("#616161", "#B5B5B5"),
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(appName);
            Grid.SetRow(header, 0);
            contentGrid.Children.Add(header);

            // Row 1: Body
            StackPanel body = new StackPanel { Margin = new Thickness(0, 12, 0, 10) };
            stateMainText = new TextBlock {
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = ThemeBrush("#202020", "#F5F5F5"),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 40
            };
            stateSubText = new TextBlock {
                FontSize = 12,
                Foreground = ThemeBrush("#616161", "#B5B5B5"),
                Margin = new Thickness(0, 7, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36
            };
            body.Children.Add(stateMainText);
            body.Children.Add(stateSubText);
            Grid.SetRow(body, 1);
            contentGrid.Children.Add(body);

            // Row 2: Action Button
            actionBtnText = new TextBlock {
                FontWeight = FontWeights.Normal,
                FontSize = 12,
                Foreground = ThemeBrush("#202020", "#F5F5F5")
            };
            actionBtn = FlatButton(actionBtnText);
            actionBtn.MinHeight = 30;
            actionBtn.Click += (s, e) => {
                HandleCapsuleAction();
            };
            DockPanel footer = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(actionBtn, Dock.Right);
            footer.Children.Add(actionBtn);
            titleStack.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(titleStack);
            Grid.SetRow(footer, 2);
            contentGrid.Children.Add(footer);

            cardBorder.Child = contentGrid;
            rootGrid.Children.Add(cardBorder);

            capsuleWin.Content = rootGrid;
            } catch { }
        }

        private static void ApplyStateVisuals(string type, string titleText, string subText) {
            currentType = type;
            if (type == "danger" || type == "risk_high") {
                // 🚨 命中危险命令
                var rose = ThemeBrush("#B42318", "#F0A09C");
                stateDot.Fill = rose;
                stateCatText.Text = "高风险命令";
                stateCatText.Foreground = rose;

                stateMainText.Text = string.IsNullOrEmpty(titleText) ? "命中危险指令" : titleText;
                stateSubText.Text = string.IsNullOrEmpty(subText) ? "已阻断自动放行，需人工核查确认。" : subText;

                actionBtnText.Text = "前往审查";

            } else if (type == "interaction" || type == "risk_medium" || type == "risk_low") {
                // 等待方案决策
                var tone = type == "risk_medium" ? ThemeBrush("#895B08", "#E8C281") : ThemeBrush("#0F6CBD", "#95C6F5");
                stateDot.Fill = tone;
                stateCatText.Text = type == "risk_medium" ? "中风险命令" : type == "risk_low" ? "低风险提醒" : "等待你的选择";
                stateCatText.Foreground = tone;

                stateMainText.Text = string.IsNullOrEmpty(titleText) ? "方案问答：等待您选择决策方案" : titleText;
                stateSubText.Text = string.IsNullOrEmpty(subText) ? "Agent 暂缓后续操作，等待您的指引。" : subText;

                actionBtnText.Text = type == "interaction" ? "前往选择" : "前往审查";

            } else {
                // 本轮任务完成
                var emerald = ThemeBrush("#217346", "#95CFAA");
                stateDot.Fill = emerald;
                stateCatText.Text = "任务已完成";
                stateCatText.Foreground = emerald;

                stateMainText.Text = string.IsNullOrEmpty(titleText) ? "生成完毕，所有步骤已就绪" : titleText;
                stateSubText.Text = string.IsNullOrEmpty(subText) ? "代码已就绪，随时可检视或开启下一轮。" : subText;

                actionBtnText.Text = "查看结果";
            }
            System.Windows.Automation.AutomationProperties.SetName(actionBtn, actionBtnText.Text);
        }

        private static void ShowCapsule(string type, string titleText, string subText) {
            RefreshWindowHandles();
            if (IsAntigravityForeground()) {
                // 当 Antigravity 在前台时无需弹窗打扰用户
                return;
            }
            wpfApp.Dispatcher.Invoke(() => {
                ApplyStateVisuals(type, titleText, subText);
                PositionCapsule();
                capsuleWin.Show();
                RestartAutoHide();
                try {
                    System.Media.SystemSounds.Asterisk.Play();
                } catch { }
            });
        }

        private static void HideCapsule() {
            wpfApp.Dispatcher.Invoke(() => {
                if (autoHideTimer != null) autoHideTimer.Stop();
                capsuleWin.Hide();
            });
        }

        private static void PositionCapsule() {
            try {
                RefreshWindowHandles();
                Screen screen = agHwnd != IntPtr.Zero ? Screen.FromHandle(agHwnd) : Screen.PrimaryScreen;
                var work = screen.WorkingArea;
                double scaleX = 1.0;
                double scaleY = 1.0;
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) {
                    scaleX = graphics.DpiX > 0 ? graphics.DpiX / 96.0 : 1.0;
                    scaleY = graphics.DpiY > 0 ? graphics.DpiY / 96.0 : 1.0;
                }
                capsuleWin.Left = work.Right / scaleX - capsuleWin.Width - 18;
                capsuleWin.Top = work.Bottom / scaleY - capsuleWin.Height - 18;
            } catch {
                Rect workArea = SystemParameters.WorkArea;
                capsuleWin.Left = workArea.Right - capsuleWin.Width - 18;
                capsuleWin.Top = workArea.Bottom - capsuleWin.Height - 18;
            }
        }

        private static void RestartAutoHide() {
            if (capsuleWin == null || !capsuleWin.IsVisible) return;
            if (autoHideTimer == null) {
                autoHideTimer = new DispatcherTimer();
                autoHideTimer.Tick += (s, e) => HideCapsule();
            }
            autoHideTimer.Stop();
            int seconds = currentType == "ready" ? 6 : currentType == "risk_high" || currentType == "danger" ? 15 : currentType == "risk_medium" ? 12 : 9;
            autoHideTimer.Interval = TimeSpan.FromSeconds(seconds);
            autoHideTimer.Start();
        }

        private static void HandleCapsuleAction() {
            HideCapsule();
            // Focus Antigravity window
            RefreshWindowHandles();
            if (agHwnd != IntPtr.Zero) {
                ForceForeground(agHwnd);
            }
            LogEvent(string.Format("{{\"event\":\"capsule_action\",\"type\":\"{0}\"}}", currentType));
        }

        public static void HideEasyAG() {
            RefreshWindowHandles();
            if (eaHwnd != IntPtr.Zero) {
                ShowWindowAsync(eaHwnd, 0); // SW_HIDE
            }
        }

        public static void ShowEasyAG() {
            RefreshWindowHandles();
            if (eaHwnd != IntPtr.Zero) {
                ShowWindowAsync(eaHwnd, 9); // SW_RESTORE
                ForceForeground(eaHwnd);
            }
            // Also notify Node to trigger popup in Tauri if applicable
            LogEvent("{\"event\":\"request_popup\"}");
        }

        public static void ShutdownResident() {
            try {
                if (trayIcon != null) {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
            } catch { }
            try {
                wpfApp.Dispatcher.Invoke(() => {
                    wpfApp.Shutdown();
                });
            } catch { }
            Environment.Exit(0);
        }

        private static void ReadCommandsLoop() {
            try {
                if (Console.IsInputRedirected) {
                    using (StreamReader reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8)) {
                        string line;
                        while ((line = reader.ReadLine()) != null) {
                            line = line.Trim();
                            if (string.IsNullOrEmpty(line)) continue;

                            if (line.StartsWith("{") && line.EndsWith("}")) {
                                ProcessJsonCommand(line);
                            }
                        }
                    }
                    ShutdownResident();
                } else {
                    while (true) {
                        Thread.Sleep(5000);
                    }
                }
            } catch { }
        }

        private static string UnescapeJson(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++) {
                if (s[i] == '\\' && i + 1 < s.Length) {
                    char next = s[++i];
                    switch (next) {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 < s.Length) {
                                string hex = s.Substring(i + 1, 4);
                                int code;
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code)) {
                                    sb.Append((char)code);
                                    i += 4;
                                } else {
                                    sb.Append("\\u");
                                }
                            } else {
                                sb.Append("\\u");
                            }
                            break;
                        default: sb.Append(next); break;
                    }
                } else {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        private static string ExtractJsonVal(string json, string key) {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return "";
            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\"')) idx++;
            int end = idx;
            bool inQuotes = (idx > 0 && json[idx - 1] == '\"');
            if (inQuotes) {
                while (end < json.Length) {
                    if (json[end] == '"') {
                        int backslashCount = 0;
                        int b = end - 1;
                        while (b >= idx && json[b] == '\\') {
                            backslashCount++;
                            b--;
                        }
                        if (backslashCount % 2 == 0) {
                            break;
                        }
                    }
                    end++;
                }
            } else {
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ' ') end++;
            }
            if (end > json.Length) end = json.Length;
            string raw = json.Substring(idx, end - idx);
            return inQuotes ? UnescapeJson(raw) : raw;
        }

        private static void ProcessJsonCommand(string json) {
            string cmd = ExtractJsonVal(json, "cmd");
            if (cmd == "init") {
                string portStr = ExtractJsonVal(json, "port");
                int.TryParse(portStr, out backendPort);
                string eaPidStr = ExtractJsonVal(json, "ea_pid");
                int.TryParse(eaPidStr, out eaPid);
                RefreshWindowHandles();
            } else if (cmd == "set_ag_pid") {
                string agPidStr = ExtractJsonVal(json, "ag_pid");
                int.TryParse(agPidStr, out agPid);
                RefreshWindowHandles();
            } else if (cmd == "hide_easyag") {
                HideEasyAG();
            } else if (cmd == "show_easyag") {
                ShowEasyAG();
            } else if (cmd == "show_capsule") {
                string type = ExtractJsonVal(json, "type");
                string title = ExtractJsonVal(json, "title");
                string detail = ExtractJsonVal(json, "detail");
                string solution = ExtractJsonVal(json, "solution");
                if (!string.IsNullOrEmpty(solution)) detail += "\n建议：" + solution;
                ShowCapsule(type, title, detail);
            } else if (cmd == "hide_capsule") {
                HideCapsule();
            } else if (cmd == "exit") {
                ShutdownResident();
            }
        }
    }

    public class ModernMenuColorTable : ProfessionalColorTable {
        public override System.Drawing.Color MenuBorder {
            get { return System.Drawing.Color.FromArgb(0x2E, 0x32, 0x45); }
        }
        public override System.Drawing.Color MenuItemBorder {
            get { return System.Drawing.Color.Transparent; }
        }
        public override System.Drawing.Color MenuItemSelected {
            get { return System.Drawing.Color.FromArgb(0x2A, 0x2D, 0x3D); }
        }
        public override System.Drawing.Color ToolStripDropDownBackground {
            get { return System.Drawing.Color.FromArgb(0x18, 0x19, 0x22); }
        }
        public override System.Drawing.Color ImageMarginGradientBegin {
            get { return System.Drawing.Color.FromArgb(0x18, 0x19, 0x22); }
        }
        public override System.Drawing.Color ImageMarginGradientMiddle {
            get { return System.Drawing.Color.FromArgb(0x18, 0x19, 0x22); }
        }
        public override System.Drawing.Color ImageMarginGradientEnd {
            get { return System.Drawing.Color.FromArgb(0x18, 0x19, 0x22); }
        }
        public override System.Drawing.Color SeparatorDark {
            get { return System.Drawing.Color.FromArgb(0x2E, 0x32, 0x45); }
        }
        public override System.Drawing.Color SeparatorLight {
            get { return System.Drawing.Color.Transparent; }
        }
    }

    public class ModernMenuRenderer : ToolStripProfessionalRenderer {
        public ModernMenuRenderer() : base(new ModernMenuColorTable()) { }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x2E, 0x32, 0x45), 1)) {
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            if (e.Item.Selected) {
                var rc = new System.Drawing.Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x2A, 0x2D, 0x3D))) {
                    e.Graphics.FillRectangle(brush, rc);
                }
            } else {
                base.OnRenderMenuItemBackground(e);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            e.TextColor = e.Item.Selected ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(0xF1, 0xF5, 0xF9);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            var rc = new System.Drawing.Rectangle(10, e.Item.Height / 2, e.Item.Width - 20, 1);
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x2E, 0x32, 0x45))) {
                e.Graphics.FillRectangle(brush, rc);
            }
        }
    }
}
