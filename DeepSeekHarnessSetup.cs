using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepSeekHarnessSetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    internal sealed class SetupForm : Form
    {
        private const string TaskName = "DeepSeekHarness";
        private const string RunValueName = "DeepSeekHarness";
        private const string PackageName = "@deepseek-ai/dsh@latest";
        private const string DefaultUrl = "http://127.0.0.1:3080";
        private const int RequiredNodeMajor = 18;

        private readonly string scriptRoot;
        private readonly string launcherPath;
        private readonly string settingsPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly List<Button> actionButtons = new List<Button>();
        private volatile bool backgroundWorkRunning;
        private string backgroundWorkName = "";
        private DateTime backgroundWorkStartedAt;

        private TextBox installDirBox;
        private TextBox logBox;
        private Label nodeStatusLabel;
        private Label npmStatusLabel;
        private Label npxStatusLabel;
        private Label taskStatusLabel;
        private Label installInfoLabel;
        private Label cacheInfoLabel;
        private CheckBox preferWingetCheck;
        private Button browseButton;
        private Button restartAdminButton;

        public SetupForm()
        {
            scriptRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            launcherPath = Path.Combine(scriptRoot, "Start-DeepSeekHarness.ps1");
            settingsPath = Path.Combine(scriptRoot, "manager-settings.json");
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "DeepSeek Harness Setup";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(900, 680);
            MinimumSize = new Size(840, 600);

            var title = new Label { Text = "DeepSeek Harness Setup", Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true, Location = new Point(18, 16) };
            Controls.Add(title);

            var subtitle = new Label { Text = "Install dependencies, fetch @deepseek-ai/dsh, and create a hidden logon scheduled task.", AutoSize = true, Location = new Point(20, 52) };
            Controls.Add(subtitle);

            var installGroup = new GroupBox { Text = "Install directory", Location = new Point(20, 84), Size = new Size(840, 86) };
            Controls.Add(installGroup);

            installDirBox = new TextBox { Text = GetSavedInstallDir(), Location = new Point(16, 26), Size = new Size(680, 23) };
            installGroup.Controls.Add(installDirBox);

            browseButton = new Button { Text = "Browse...", Location = new Point(708, 24), Size = new Size(92, 28) };
            browseButton.Click += delegate { BrowseInstallDirectory(); };
            installGroup.Controls.Add(browseButton);

            installInfoLabel = new Label { Text = "Current install directory:", AutoSize = true, Location = new Point(16, 58) };
            installGroup.Controls.Add(installInfoLabel);

            var statusGroup = new GroupBox { Text = "Status", Location = new Point(20, 184), Size = new Size(410, 152) };
            Controls.Add(statusGroup);
            nodeStatusLabel = AddLabel(statusGroup, "Node.js: checking", 28);
            npmStatusLabel = AddLabel(statusGroup, "npm: checking", 56);
            npxStatusLabel = AddLabel(statusGroup, "npx: checking", 84);
            taskStatusLabel = AddLabel(statusGroup, "Scheduled task: checking", 112);

            var optionsGroup = new GroupBox { Text = "Options", Location = new Point(450, 184), Size = new Size(410, 152) };
            Controls.Add(optionsGroup);

            preferWingetCheck = new CheckBox { Text = "Prefer winget when installing Node.js", Checked = true, AutoSize = true, Location = new Point(16, 28) };
            optionsGroup.Controls.Add(preferWingetCheck);
            optionsGroup.Controls.Add(new Label { Text = "Web UI: " + DefaultUrl, AutoSize = true, Location = new Point(16, 60) });
            cacheInfoLabel = new Label { Text = "npm cache:", AutoSize = true, Location = new Point(16, 88) };
            optionsGroup.Controls.Add(cacheInfoLabel);
            optionsGroup.Controls.Add(new Label { Text = "Logs are stored under the selected install directory.", AutoSize = true, Location = new Point(16, 116) });

            var buttonsPanel = new FlowLayoutPanel { Location = new Point(20, 348), Size = new Size(840, 86), FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            Controls.Add(buttonsPanel);

            NewButton(buttonsPanel, "Install and Start", 150, delegate { RunBackground("Install and start", InstallAndStart); });
            NewButton(buttonsPanel, "Refresh Status", 110, delegate { RunBackground("Refresh status", UpdateStatus); });
            NewButton(buttonsPanel, "Create Autostart", 130, delegate { RunBackground("Create autostart", delegate { EnsureLauncher(); RegisterTask(); }); });
            NewButton(buttonsPanel, "Start Task", 100, delegate { RunBackground("Start scheduled task", StartTask); });
            NewButton(buttonsPanel, "Stop Task", 100, delegate { RunBackground("Stop scheduled task", StopTask); });
            NewButton(buttonsPanel, "Remove Task", 100, delegate { RunBackground("Remove scheduled task", RemoveTask); });
            NewButton(buttonsPanel, "Open Web UI", 110, delegate { Process.Start(DefaultUrl); });
            NewButton(buttonsPanel, "Open Logs", 120, delegate { EnsureInstallDirectories(); Process.Start(GetLogRoot()); });
            restartAdminButton = NewButton(buttonsPanel, "Restart as Admin", 130, delegate { RestartAsAdmin(); });

            logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), Location = new Point(20, 446), Size = new Size(840, 170), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(logBox);

            Shown += delegate
            {
                AddLog("Manager started.");
                AddLog("Launcher script: " + launcherPath);
                AddLog(IsAdministrator() ? "Process is elevated." : "Process is not elevated. Use Restart as Admin only if the current user is an administrator.");
                restartAdminButton.Enabled = !IsAdministrator();
                UpdateStatus();
            };
        }

        private static Label AddLabel(Control parent, string text, int top)
        {
            var label = new Label { Text = text, AutoSize = true, Location = new Point(16, top) };
            parent.Controls.Add(label);
            return label;
        }

        private Button NewButton(Control parent, string text, int width, EventHandler handler)
        {
            var button = new Button { Text = text, Width = width, Height = 32, Margin = new Padding(4) };
            button.Click += handler;
            parent.Controls.Add(button);
            actionButtons.Add(button);
            return button;
        }

        private string GetDefaultInstallDir()
        {
            return @"D:\deepseek-harness";
        }

        private string GetSavedInstallDir()
        {
            try
            {
                if (!File.Exists(settingsPath)) return GetDefaultInstallDir();
                var data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(settingsPath, Encoding.UTF8));
                if (data != null && data.ContainsKey("InstallDir") && data["InstallDir"] != null) return data["InstallDir"].ToString();
            }
            catch { }
            return GetDefaultInstallDir();
        }

        private string GetInstallDir()
        {
            var value = installDirBox == null ? "" : installDirBox.Text.Trim();
            if (String.IsNullOrWhiteSpace(value)) value = GetDefaultInstallDir();
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
        }

        private string GetConfigPath() { return Path.Combine(GetInstallDir(), "config.json"); }
        private string GetLogRoot() { return Path.Combine(GetInstallDir(), "logs"); }
        private string GetNpmCacheRoot() { return Path.Combine(GetInstallDir(), "npm-cache"); }
        private string GetRuntimeRoot() { return Path.Combine(GetInstallDir(), "runtime"); }

        private void BrowseInstallDirectory()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the DeepSeek Harness install directory";
                dialog.SelectedPath = GetInstallDir();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    installDirBox.Text = dialog.SelectedPath;
                    UpdateStatus();
                }
            }
        }

        private void AddLog(string message)
        {
            if (logBox != null && logBox.InvokeRequired)
            {
                logBox.BeginInvoke(new Action<string>(AddLog), message);
                return;
            }
            if (logBox == null) return;
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void SetBusy(bool busy)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetBusy), busy);
                return;
            }
            foreach (var button in actionButtons) button.Enabled = !busy;
            if (restartAdminButton != null && !busy) restartAdminButton.Enabled = !IsAdministrator();
            installDirBox.Enabled = !busy;
            browseButton.Enabled = !busy;
            UseWaitCursor = busy;
        }

        private void RunBackground(string name, Action action)
        {
            SetBusy(true);
            AddLog(name + "...");
            backgroundWorkName = name;
            backgroundWorkStartedAt = DateTime.UtcNow;
            backgroundWorkRunning = true;
            StartProgressHeartbeat();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    action();
                    AddLog(name + " completed.");
                }
                catch (Exception ex)
                {
                    AddLog("Failed: " + ex.Message);
                }
                finally
                {
                    backgroundWorkRunning = false;
                    try { UpdateStatus(); } catch (Exception ex) { AddLog("Status refresh failed: " + ex.Message); }
                    SetBusy(false);
                }
            });
        }

        private void StartProgressHeartbeat()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (backgroundWorkRunning)
                {
                    Thread.Sleep(15000);
                    if (!backgroundWorkRunning) break;
                    var elapsed = DateTime.UtcNow - backgroundWorkStartedAt;
                    AddLog(backgroundWorkName + " still running... elapsed " + FormatDuration(elapsed));
                }
            });
        }

        private void EnsureInstallDirectories()
        {
            Directory.CreateDirectory(GetInstallDir());
            Directory.CreateDirectory(GetLogRoot());
            Directory.CreateDirectory(GetNpmCacheRoot());
            Directory.CreateDirectory(GetRuntimeRoot());
            var settings = new Dictionary<string, object> { { "InstallDir", GetInstallDir() }, { "UpdatedAt", DateTime.Now.ToString("s") } };
            File.WriteAllText(settingsPath, json.Serialize(settings), Encoding.UTF8);
        }

        private void WriteLauncherConfig()
        {
            EnsureInstallDirectories();
            var config = new Dictionary<string, object>
            {
                { "PackageName", PackageName },
                { "Arguments", new[] { "web" } },
                { "Url", DefaultUrl },
                { "InstallDir", GetInstallDir() },
                { "NpmCache", GetNpmCacheRoot() },
                { "RuntimeDir", GetRuntimeRoot() },
                { "LocalBin", Path.Combine(GetRuntimeRoot(), "node_modules\\.bin\\dsh.cmd") },
                { "UpdatedAt", DateTime.Now.ToString("s") }
            };
            File.WriteAllText(GetConfigPath(), json.Serialize(config), Encoding.UTF8);
            AddLog("Config written: " + GetConfigPath());
        }

        private void EnsureLauncher()
        {
            if (!File.Exists(launcherPath)) throw new FileNotFoundException("Launcher script is missing.", launcherPath);
            WriteLauncherConfig();
        }

        private void UpdateStatus()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateStatus));
                return;
            }
            EnsureInstallDirectories();
            var node = GetNodeInfo();
            nodeStatusLabel.Text = node == null ? "Node.js: not installed" : "Node.js: " + node.Text;
            npmStatusLabel.Text = "npm: " + (GetNpmPath() == null ? "not found" : "found");
            npxStatusLabel.Text = "npx: " + (GetNpxPath() == null ? "not found" : "found");
            taskStatusLabel.Text = "Autostart: " + GetTaskState();
            installInfoLabel.Text = "Current install directory: " + GetInstallDir();
            cacheInfoLabel.Text = "npm cache: " + GetNpmCacheRoot();
        }

        private string GetTaskState()
        {
            var result = RunProcess("schtasks.exe", "/Query /TN " + QuoteArg(TaskName) + " /FO LIST", null, false);
            if (result.ExitCode != 0)
            {
                return IsRunKeyRegistered() ? "HKCU Run fallback" : "not created";
            }
            foreach (var rawLine in result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase)) return line.Substring("Status:".Length).Trim();
            }
            return "created";
        }

        private void InstallAndStart()
        {
            EnsureLauncher();
            EnsureNode();
            EnsureDeepSeekHarnessPackage();
            RegisterTask();
            StartTask();
            if (WaitForWebUi(DefaultUrl, TimeSpan.FromSeconds(120)))
            {
                Process.Start(DefaultUrl);
            }
            else
            {
                AddLog("Web UI did not become ready within 120 seconds.");
                AddLog("Check logs under: " + GetLogRoot());
                AddLog("Expected URL: " + DefaultUrl);
            }
        }

        private void EnsureNode()
        {
            var node = GetNodeInfo();
            if (node != null && node.Version.Major >= RequiredNodeMajor)
            {
                AddLog("Node.js is installed: " + node.Text + " (" + node.Path + ")");
                return;
            }
            AddLog(node == null ? "Node.js was not detected." : "Node.js is too old: " + node.Text + ". v" + RequiredNodeMajor + " or newer is required.");
            InstallNodeLts();
            node = GetNodeInfo();
            if (node == null || node.Version.Major < RequiredNodeMajor) throw new InvalidOperationException("Node.js is still unavailable after install. Restart this app or Windows and try again.");
            AddLog("Node.js is ready: " + node.Text);
        }

        private void InstallNodeLts()
        {
            var winget = FindCommand("winget.exe");
            if (preferWingetCheck.Checked && winget != null)
            {
                try
                {
                    AddLog("Trying winget install OpenJS.NodeJS.LTS...");
                    var wingetResult = RunProcess(winget, "install --exact --id OpenJS.NodeJS.LTS --silent --accept-package-agreements --accept-source-agreements", null, true);
                    if (wingetResult.ExitCode == 0 && GetNodePath() != null) return;
                    AddLog("winget install returned code " + wingetResult.ExitCode + ", falling back to MSI.");
                }
                catch (Exception ex)
                {
                    AddLog("winget install did not complete, falling back to MSI: " + ex.Message);
                }
            }

            var url = GetLatestNodeLtsMsiUrl();
            var downloadRoot = Path.Combine(GetInstallDir(), "downloads");
            Directory.CreateDirectory(downloadRoot);
            var msi = Path.Combine(downloadRoot, Path.GetFileName(new Uri(url).LocalPath));
            AddLog("Downloading Node.js LTS: " + url);
            using (var client = new WebClient()) client.DownloadFile(url, msi);

            AddLog("Installing Node.js LTS. Allow the UAC prompt if it appears.");
            var args = "/i " + QuoteArg(msi) + " /qn /norestart";
            int exitCode;
            if (IsAdministrator())
            {
                exitCode = RunProcess("msiexec.exe", args, null, true).ExitCode;
            }
            else
            {
                var info = new ProcessStartInfo("msiexec.exe", args) { UseShellExecute = true, Verb = "runas" };
                using (var process = Process.Start(info))
                {
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
            }
            if (exitCode != 0 && exitCode != 3010) throw new InvalidOperationException("Node.js MSI install failed with code " + exitCode);
        }

        private string GetLatestNodeLtsMsiUrl()
        {
            AddLog("Querying official Node.js LTS version...");
            string text;
            using (var client = new WebClient()) text = client.DownloadString("https://nodejs.org/dist/index.json");
            var versions = json.Deserialize<List<Dictionary<string, object>>>(text);
            var latest = versions
                .Where(item => item.ContainsKey("lts") && item["lts"] != null && !Object.Equals(item["lts"], false))
                .Select(item => item["version"].ToString())
                .OrderByDescending(ParseNodeVersion)
                .FirstOrDefault();
            if (String.IsNullOrEmpty(latest)) throw new InvalidOperationException("Unable to get Node.js LTS metadata from nodejs.org.");
            var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            return "https://nodejs.org/dist/" + latest + "/node-" + latest + "-" + arch + ".msi";
        }

        private static Version ParseNodeVersion(string text)
        {
            return new Version(text.TrimStart('v'));
        }

        private void EnsureDeepSeekHarnessPackage()
        {
            EnsureInstallDirectories();
            var npm = GetNpmPath();
            if (npm == null) throw new FileNotFoundException("npm.cmd was not found. Confirm Node.js is installed completely.");
            var env = new Dictionary<string, string>
            {
                { "npm_config_cache", GetNpmCacheRoot() },
                { "Path", BuildSearchPath() },
                { "npm_config_scripts_prepend_node_path", "true" }
            };
            AddLog("npm cache: " + GetNpmCacheRoot());
            AddLog("runtime: " + GetRuntimeRoot());
            AddLog("Installing DeepSeek Harness locally. First install can take several minutes on a new machine.");
            var installArgs = "--prefix " + QuoteArg(GetRuntimeRoot()) + " install --no-audit --no-fund --loglevel=notice --progress=true " + PackageName;
            var install = RunProcess(npm, installArgs, env, true);
            if (install.ExitCode != 0) throw new InvalidOperationException("npm install failed with code " + install.ExitCode);
            AddLog("DeepSeek Harness package install completed.");
            var view = RunProcess(npm, "view @deepseek-ai/dsh version", env, true);
            if (view.ExitCode != 0) throw new InvalidOperationException("npm view failed with code " + view.ExitCode);
        }

        private void RegisterTask()
        {
            EnsureLauncher();

            try
            {
                RegisterScheduledTaskWithCom();
                RemoveRunKeyAutostart(false);
                AddLog("Scheduled task created: " + TaskName);
                AddLog("Task install directory: " + GetInstallDir());
            }
            catch (Exception ex)
            {
                AddLog("Scheduled task creation failed: " + ex.Message);
                if (!IsAdministrator())
                {
                    AddLog("If this Windows account is an administrator, click Restart as Admin and run Create Autostart again.");
                }
                AddLog("Falling back to current-user Run autostart. This does not require administrator rights.");
                RegisterRunKeyAutostart();
            }
        }

        private void StartTask()
        {
            var result = RunProcess("schtasks.exe", "/Run /TN " + QuoteArg(TaskName), null, true);
            if (result.ExitCode == 0)
            {
                AddLog("Scheduled task started: " + TaskName);
                return;
            }

            if (!IsRunKeyRegistered())
            {
                throw new InvalidOperationException("schtasks run failed with code " + result.ExitCode);
            }

            AddLog("Scheduled task was not found. Starting through the hidden launcher command instead.");
            StartHiddenLauncher();
        }

        private void StopTask()
        {
            var result = RunProcess("schtasks.exe", "/End /TN " + QuoteArg(TaskName), null, true);
            if (result.ExitCode != 0) AddLog("schtasks end returned code " + result.ExitCode + ".");
            AddLog("Scheduled task stop requested: " + TaskName);
        }

        private void RemoveTask()
        {
            var result = RunProcess("schtasks.exe", "/Delete /TN " + QuoteArg(TaskName) + " /F", null, true);
            if (result.ExitCode != 0) AddLog("schtasks delete returned code " + result.ExitCode + ".");
            RemoveRunKeyAutostart(true);
            AddLog("Scheduled task remove requested: " + TaskName);
        }

        private void RegisterScheduledTaskWithCom()
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) throw new InvalidOperationException("Task Scheduler service COM API is unavailable.");

            dynamic service = Activator.CreateInstance(schedulerType);
            service.Connect();

            dynamic folder = service.GetFolder("\\");
            dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Start DeepSeek Harness web UI at user logon.";

            var currentUser = WindowsIdentity.GetCurrent().Name;
            definition.Principal.UserId = currentUser;
            definition.Principal.LogonType = 3;
            definition.Principal.RunLevel = 0;

            definition.Settings.Enabled = true;
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT0S";
            definition.Settings.MultipleInstances = 2;

            dynamic trigger = definition.Triggers.Create(9);
            trigger.Enabled = true;
            trigger.UserId = currentUser;

            dynamic action = definition.Actions.Create(0);
            action.Path = GetPowerShellPath();
            action.Arguments = GetLauncherArguments();
            action.WorkingDirectory = scriptRoot;

            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3, null);
        }

        private string GetPowerShellPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\WindowsPowerShell\\v1.0\\powershell.exe");
        }

        private string GetLauncherArguments()
        {
            return "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + QuoteArg(launcherPath) + " -InstallDir " + QuoteArg(GetInstallDir());
        }

        private string GetLauncherCommand()
        {
            return QuoteArg(GetPowerShellPath()) + " " + GetLauncherArguments();
        }

        private void RegisterRunKeyAutostart()
        {
            using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                if (key == null) throw new InvalidOperationException("Unable to open HKCU Run registry key.");
                key.SetValue(RunValueName, GetLauncherCommand(), RegistryValueKind.String);
            }
            AddLog("HKCU Run autostart created: " + RunValueName);
        }

        private void RemoveRunKeyAutostart(bool log)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key != null && key.GetValue(RunValueName) != null)
                    {
                        key.DeleteValue(RunValueName, false);
                        if (log) AddLog("HKCU Run autostart removed: " + RunValueName);
                    }
                }
            }
            catch (Exception ex)
            {
                if (log) AddLog("HKCU Run autostart remove failed: " + ex.Message);
            }
        }

        private bool IsRunKeyRegistered()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", false))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private void StartHiddenLauncher()
        {
            var info = new ProcessStartInfo(GetPowerShellPath(), GetLauncherArguments())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(info);
            AddLog("Hidden launcher started.");
        }

        private bool WaitForWebUi(string url, TimeSpan timeout)
        {
            AddLog("Waiting for Web UI: " + url);
            var deadline = DateTime.UtcNow.Add(timeout);
            Exception lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.Timeout = 3000;
                    request.ReadWriteTimeout = 3000;
                    request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 500)
                        {
                            AddLog("Web UI is ready: HTTP " + (int)response.StatusCode);
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                Thread.Sleep(2000);
            }

            if (lastError != null)
            {
                AddLog("Last Web UI check error: " + lastError.Message);
            }

            return false;
        }

        private NodeInfo GetNodeInfo()
        {
            var nodePath = GetNodePath();
            if (nodePath == null) return null;
            var result = RunProcess(nodePath, "--version", null, false);
            var text = result.Output.Trim();
            if (result.ExitCode != 0 || String.IsNullOrEmpty(text)) return null;
            return new NodeInfo { Path = nodePath, Text = text, Version = ParseNodeVersion(text) };
        }

        private string GetNodePath()
        {
            return FindCommand("node.exe", NodeFallbacks("node.exe"));
        }

        private string GetNpmPath()
        {
            return FindCommand("npm.cmd", NodeFallbacks("npm.cmd"));
        }

        private string GetNpxPath()
        {
            return FindCommand("npx.cmd", NodeFallbacks("npx.cmd"));
        }

        private IEnumerable<string> NodeFallbacks(string file)
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs\\" + file);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!String.IsNullOrEmpty(pf86)) yield return Path.Combine(pf86, "nodejs\\" + file);
        }

        private string FindCommand(string fileName)
        {
            return FindCommand(fileName, Enumerable.Empty<string>());
        }

        private string FindCommand(string fileName, IEnumerable<string> fallbackPaths)
        {
            foreach (var path in fallbackPaths)
            {
                if (!String.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }

            foreach (var rawDir in BuildSearchPath().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(rawDir.Trim()), fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        private string BuildSearchPath()
        {
            var parts = new List<string>
            {
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs")
            };
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!String.IsNullOrEmpty(pf86)) parts.Add(Path.Combine(pf86, "nodejs"));
            return String.Join(";", parts.Where(p => !String.IsNullOrEmpty(p)).ToArray());
        }

        private ProcessResult RunProcess(string fileName, string arguments, IDictionary<string, string> env, bool logOutput)
        {
            var info = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = GetProcessOutputEncoding(fileName),
                StandardErrorEncoding = GetProcessOutputEncoding(fileName)
            };
            if (env != null)
            {
                foreach (var item in env) info.EnvironmentVariables[item.Key] = item.Value;
            }

            var output = new StringBuilder();
            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) { output.AppendLine(e.Data); if (logOutput) AddLog(e.Data); } };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) { output.AppendLine(e.Data); if (logOutput) AddLog(e.Data); } };
                AddLog("Run: " + fileName + " " + arguments);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                return new ProcessResult { ExitCode = process.ExitCode, Output = output.ToString() };
            }
        }

        private static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static Encoding GetProcessOutputEncoding(string fileName)
        {
            var name = Path.GetFileName(fileName ?? "");
            if (name.Equals("winget.exe", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("node.exe", StringComparison.OrdinalIgnoreCase))
            {
                return new UTF8Encoding(false);
            }

            return Encoding.Default;
        }

        private void RestartAsAdmin()
        {
            try
            {
                EnsureInstallDirectories();
                var info = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = scriptRoot
                };
                Process.Start(info);
                Close();
            }
            catch (Exception ex)
            {
                AddLog("Restart as admin failed: " + ex.Message);
            }
        }

        private static string QuoteArg(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return ((int)duration.TotalHours) + "h " + duration.Minutes + "m " + duration.Seconds + "s";
            }
            if (duration.TotalMinutes >= 1)
            {
                return duration.Minutes + "m " + duration.Seconds + "s";
            }
            return duration.Seconds + "s";
        }

        private sealed class NodeInfo
        {
            public string Path;
            public string Text;
            public Version Version;
        }

        private sealed class ProcessResult
        {
            public int ExitCode;
            public string Output;
        }
    }
}
