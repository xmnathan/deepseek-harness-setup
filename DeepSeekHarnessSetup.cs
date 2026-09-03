using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
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

            if (!IsAdministrator())
            {
                if (!RelaunchAsAdministrator())
                {
                    MessageBox.Show(
                        "DeepSeek Harness Setup needs administrator privileges to install dependencies and register autostart.\r\n\r\nPlease allow the UAC prompt and run it again.",
                        "Administrator privileges required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            Application.Run(new SetupForm());
        }

        private static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static bool RelaunchAsAdministrator()
        {
            try
            {
                var info = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                Process.Start(info);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private const string TaskName = "DeepSeekHarness";
        private const string RunValueName = "DeepSeekHarness";
        private const string LauncherResourceName = "Start-DeepSeekHarness.ps1";
        private const string PackageName = "@deepseek-ai/dsh@latest";
        private const string DefaultUrl = "http://127.0.0.1:3080";
        private const int RequiredNodeMajor = 18;

        private readonly string scriptRoot;
        private readonly string settingsPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly List<Button> actionButtons = new List<Button>();
        private volatile bool backgroundWorkRunning;
        private volatile bool lastAutostartUsedRunKey;
        private string backgroundWorkName = "";

        private TextBox installDirBox;
        private TextBox logBox;
        private Label nodeStatusLabel;
        private Label npmStatusLabel;
        private Label npxStatusLabel;
        private Label taskStatusLabel;
        private Label installInfoLabel;
        private Label cacheInfoLabel;
        private Label progressLabel;
        private ProgressBar progressBar;
        private CheckBox preferWingetCheck;
        private Button browseButton;

        public SetupForm()
        {
            scriptRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekHarnessSetup",
                "manager-settings.json");
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "DeepSeek Harness Setup";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(900, 720);
            MinimumSize = new Size(840, 640);

            var title = new Label { Text = "DeepSeek Harness Setup", Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true, Location = new Point(18, 16) };
            Controls.Add(title);

            var subtitle = new Label { Text = "Install or update DeepSeek Harness, then start it in the current desktop user session.", AutoSize = true, Location = new Point(20, 52) };
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

            NewButton(buttonsPanel, "Check, Update and Start", 180, delegate { RunBackground("Check, update and start", CheckUpdateAndStart); });
            NewButton(buttonsPanel, "Refresh Status", 110, delegate { RunBackground("Refresh status", UpdateStatus); });
            NewButton(buttonsPanel, "Create Autostart", 130, delegate { RunBackground("Create autostart", delegate { EnsureLauncher(); RegisterTask(); }); });
            NewButton(buttonsPanel, "Start Task", 100, delegate { RunBackground("Start scheduled task", StartTask); });
            NewButton(buttonsPanel, "Stop Task", 100, delegate { RunBackground("Stop scheduled task", StopTask); });
            NewButton(buttonsPanel, "Remove Task", 100, delegate { RunBackground("Remove scheduled task", RemoveTask); });
            NewButton(buttonsPanel, "Open Web UI", 110, delegate { Process.Start(DefaultUrl); });
            NewButton(buttonsPanel, "Open Logs", 120, delegate { EnsureInstallDirectories(); Process.Start(GetLogRoot()); });

            progressLabel = new Label { Text = "Ready", AutoSize = true, Location = new Point(24, 440) };
            Controls.Add(progressLabel);

            progressBar = new ProgressBar { Location = new Point(20, 462), Size = new Size(840, 18), Style = ProgressBarStyle.Continuous, Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(progressBar);

            logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), Location = new Point(20, 492), Size = new Size(840, 150), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(logBox);

            Shown += delegate
            {
                AddLog("Manager started.");
                AddLog("Launcher script will be extracted to: " + GetLauncherPath());
                AddLog("Process is elevated.");
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
        private string GetHomeRoot() { return Path.Combine(GetInstallDir(), "home"); }
        private string GetLauncherPath() { return Path.Combine(GetInstallDir(), "Start-DeepSeekHarness.ps1"); }

        private string GetRunMode()
        {
            return IsDeepSeekHarnessSourceCheckout(GetInstallDir()) ? "source" : "package";
        }

        private bool IsDeepSeekHarnessSourceCheckout(string directory)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;
                if (!File.Exists(Path.Combine(directory, "pnpm-workspace.yaml"))) return false;
                if (!File.Exists(Path.Combine(directory, "apps\\cli\\src\\bin.ts"))) return false;

                var packageJsonPath = Path.Combine(directory, "package.json");
                if (!File.Exists(packageJsonPath)) return false;

                var data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(packageJsonPath, Encoding.UTF8));
                if (data == null || !data.ContainsKey("name") || !Object.Equals(data["name"], "@deepseek-ai/dsh-root")) return false;
                if (!data.ContainsKey("scripts")) return false;

                var scripts = data["scripts"] as Dictionary<string, object>;
                return scripts != null && scripts.ContainsKey("dsh");
            }
            catch
            {
                return false;
            }
        }

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
            installDirBox.Enabled = !busy;
            browseButton.Enabled = !busy;
            UseWaitCursor = false;
            Cursor = Cursors.Default;

            if (progressBar != null)
            {
                progressBar.Visible = busy;
                progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                progressBar.MarqueeAnimationSpeed = busy ? 35 : 0;
            }

            if (!busy)
            {
                SetProgressText("Ready");
            }
        }

        private void RunBackground(string name, Action action)
        {
            SetBusy(true);
            AddLog(name + "...");
            SetProgressText(name + "...");
            backgroundWorkName = name;
            backgroundWorkRunning = true;
            StartProgressHeartbeat();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    action();
                    AddLog(name + " completed.");
                    SetProgressText(name + " completed.");
                }
                catch (Exception ex)
                {
                    AddLog("Failed: " + ex.Message);
                    SetProgressText(name + " failed.");
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
                    var message = backgroundWorkName + " is still running...";
                    SetProgressText(message);
                }
            });
        }

        private void SetProgressText(string text)
        {
            if (progressLabel != null && progressLabel.InvokeRequired)
            {
                progressLabel.BeginInvoke(new Action<string>(SetProgressText), text);
                return;
            }

            if (progressLabel != null)
            {
                progressLabel.Text = text;
            }
        }

        private void EnsureInstallDirectories()
        {
            Directory.CreateDirectory(GetInstallDir());
            Directory.CreateDirectory(GetLogRoot());
            Directory.CreateDirectory(GetNpmCacheRoot());
            Directory.CreateDirectory(GetRuntimeRoot());
            Directory.CreateDirectory(GetHomeRoot());
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            var settings = new Dictionary<string, object> { { "InstallDir", GetInstallDir() }, { "UpdatedAt", DateTime.Now.ToString("s") } };
            File.WriteAllText(settingsPath, json.Serialize(settings), Encoding.UTF8);
        }

        private void WriteLauncherConfig()
        {
            EnsureInstallDirectories();
            var runMode = GetRunMode();
            var config = new Dictionary<string, object>
            {
                { "PackageName", PackageName },
                { "Arguments", new[] { "web" } },
                { "Url", DefaultUrl },
                { "InstallDir", GetInstallDir() },
                { "NpmCache", GetNpmCacheRoot() },
                { "HomeDir", GetHomeRoot() },
                { "RunMode", runMode },
                { "SourceDir", runMode == "source" ? GetInstallDir() : "" },
                { "RuntimeDir", GetRuntimeRoot() },
                { "LocalBin", Path.Combine(GetRuntimeRoot(), "node_modules\\.bin\\dsh.cmd") },
                { "UpdatedAt", DateTime.Now.ToString("s") }
            };
            File.WriteAllText(GetConfigPath(), json.Serialize(config), Encoding.UTF8);
            AddLog("Config written: " + GetConfigPath());
            AddLog("Run mode: " + runMode);
        }

        private void EnsureLauncher()
        {
            ExtractLauncherScript();
            WriteLauncherConfig();
        }

        private void ExtractLauncherScript()
        {
            EnsureInstallDirectories();
            var target = GetLauncherPath();
            byte[] bytes;

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LauncherResourceName))
            {
                if (stream != null)
                {
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        bytes = memory.ToArray();
                    }
                }
                else
                {
                    var fallback = Path.Combine(scriptRoot, "Start-DeepSeekHarness.ps1");
                    if (!File.Exists(fallback)) throw new FileNotFoundException("Embedded launcher script is missing.", fallback);
                    bytes = File.ReadAllBytes(fallback);
                }
            }

            if (!File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(bytes))
            {
                File.WriteAllBytes(target, bytes);
                AddLog("Launcher script extracted: " + target);
            }
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
            installInfoLabel.Text = "Current install directory: " + GetInstallDir() + " (" + GetRunMode() + " mode)";
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

        private void CheckUpdateAndStart()
        {
            EnsureLauncher();
            EnsureNode();
            AddLog("Stopping current DeepSeek Harness before sync.");
            StopTask();
            SyncDeepSeekHarness();
            EnsureLauncher();
            RegisterTask();
            StartTask();
            if (WaitForWebUi(DefaultUrl, TimeSpan.FromSeconds(120)))
            {
                AddLog("DeepSeek Harness is ready.");
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

        private void SyncDeepSeekHarness()
        {
            var runMode = GetRunMode();
            if (runMode == "source")
            {
                SyncSourceCheckout();
            }
            else
            {
                EnsureDeepSeekHarnessPackage();
            }
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

        private void SyncSourceCheckout()
        {
            EnsureInstallDirectories();
            var sourceDir = GetInstallDir();
            if (!IsDeepSeekHarnessSourceCheckout(sourceDir))
            {
                throw new InvalidOperationException("Selected directory is not a DeepSeek Harness source checkout: " + sourceDir);
            }

            AddLog("DeepSeek Harness source checkout detected.");
            var sourceUpdated = TryUpdateSourceCheckout(sourceDir);
            EnsureSourceDependencies(sourceDir, sourceUpdated);
        }

        private bool TryUpdateSourceCheckout(string sourceDir)
        {
            if (!Directory.Exists(Path.Combine(sourceDir, ".git")))
            {
                AddLog("Source directory is not a Git checkout. Skipping source update.");
                return false;
            }

            var git = GetGitPath();
            if (git == null)
            {
                AddLog("git.exe was not found. Skipping source update and using the local source tree.");
                return false;
            }

            var env = BuildToolEnvironment();
            var status = RunProcess(git, "status --porcelain --untracked-files=no", env, false, sourceDir);
            if (status.ExitCode != 0)
            {
                AddLog("Unable to inspect Git status. Skipping source update.");
                return false;
            }

            if (!String.IsNullOrWhiteSpace(status.Output))
            {
                AddLog("Source checkout has local changes. Skipping git pull to avoid touching user work.");
                return false;
            }

            AddLog("Updating source checkout with git pull --ff-only.");
            var pull = RunProcess(git, "pull --ff-only", env, true, sourceDir);
            if (pull.ExitCode != 0)
            {
                AddLog("git pull returned code " + pull.ExitCode + ". Continuing with the local source tree.");
                return false;
            }

            return pull.Output.IndexOf("Already up to date", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private void EnsureSourceDependencies(string sourceDir, bool sourceUpdated)
        {
            var env = BuildToolEnvironment();
            var corepack = GetCorepackPath();
            if (corepack != null)
            {
                AddLog("Installing source workspace dependencies with corepack pnpm.");
                var install = RunProcess(corepack, "pnpm install --no-frozen-lockfile", env, true, sourceDir);
                if (install.ExitCode != 0) throw new InvalidOperationException("pnpm install failed with code " + install.ExitCode);
                AddLog("Source workspace dependencies are ready.");
                EnsureSourceBuild(sourceDir, sourceUpdated, corepack, "pnpm run clean", "pnpm run build", env);
                return;
            }

            var pnpm = GetPnpmPath();
            if (pnpm != null)
            {
                AddLog("Installing source workspace dependencies with pnpm.");
                var install = RunProcess(pnpm, "install --no-frozen-lockfile", env, true, sourceDir);
                if (install.ExitCode != 0) throw new InvalidOperationException("pnpm install failed with code " + install.ExitCode);
                AddLog("Source workspace dependencies are ready.");
                EnsureSourceBuild(sourceDir, sourceUpdated, pnpm, "run clean", "run build", env);
                return;
            }

            throw new FileNotFoundException("corepack.cmd or pnpm.cmd was not found. Install Node.js 22+ with Corepack, or install pnpm.");
        }

        private void EnsureSourceBuild(string sourceDir, bool sourceUpdated, string command, string cleanArguments, string buildArguments, IDictionary<string, string> env)
        {
            if (!sourceUpdated && !IsSourceBuildMissing(sourceDir))
            {
                AddLog("Source build artifacts are present. Skipping pnpm run build.");
                return;
            }

            AddLog(sourceUpdated ? "Source changed. Building source workspace." : "Source build artifacts are missing. Building source workspace.");
            AddLog("Cleaning old source build artifacts before build.");
            var clean = RunProcess(command, cleanArguments, env, true, sourceDir);
            if (clean.ExitCode != 0) throw new InvalidOperationException("source clean failed with code " + clean.ExitCode);
            var build = RunProcess(command, buildArguments, env, true, sourceDir);
            if (build.ExitCode != 0) throw new InvalidOperationException("source build failed with code " + build.ExitCode);
            AddLog("Source workspace build completed.");
        }

        private bool IsSourceBuildMissing(string sourceDir)
        {
            var required = new[]
            {
                "packages\\api\\session-controller\\lib\\client.js",
                "packages\\api\\workspace-controller\\lib\\client.js",
                "packages\\client\\ui-chat\\lib\\client.js",
                "packages\\client\\ui-renderer\\lib\\client.js",
                "packages\\llm\\llm\\lib\\typert.host.js",
                "packages\\subagent\\subagent\\lib\\typert.host.js"
            };

            return required.Any(path => !File.Exists(Path.Combine(sourceDir, path)));
        }

        private void RegisterTask()
        {
            EnsureLauncher();

            try
            {
                RegisterScheduledTaskWithCom();
                lastAutostartUsedRunKey = false;
                RemoveRunKeyAutostart(false);
                AddLog("Scheduled task created: " + TaskName);
                AddLog("Task install directory: " + GetInstallDir());
            }
            catch (Exception ex)
            {
                AddLog("Scheduled task creation failed: " + ex.Message);
                AddLog("Falling back to current-user Run autostart. This does not require administrator rights.");
                RegisterRunKeyAutostart();
                lastAutostartUsedRunKey = true;
            }
        }

        private void StartTask()
        {
            if (lastAutostartUsedRunKey)
            {
                AddLog("Starting through the hidden launcher because this run used HKCU Run fallback.");
                StartHiddenLauncher();
                return;
            }

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
            StopHarnessProcessesFromPidFiles();
            AddLog("Scheduled task stop requested: " + TaskName);
        }

        private void StopHarnessProcessesFromPidFiles()
        {
            var logRoot = GetLogRoot();
            if (!Directory.Exists(logRoot))
            {
                StopProcessListeningOnPort(3080);
                return;
            }

            foreach (var pidFile in Directory.GetFiles(logRoot, "dsh-web*.pid"))
            {
                try
                {
                    var text = File.ReadAllText(pidFile).Trim();
                    int pid;
                    if (!Int32.TryParse(text, out pid)) continue;

                    using (var process = Process.GetProcessById(pid))
                    {
                        if (process.HasExited) continue;
                        AddLog("Stopping dsh process from pid file: " + pid);
                        process.Kill();
                        process.WaitForExit(10000);
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (Exception ex)
                {
                    AddLog("Failed to stop process from " + Path.GetFileName(pidFile) + ": " + ex.Message);
                }
            }

            StopProcessListeningOnPort(3080);
        }

        private void StopProcessListeningOnPort(int port)
        {
            try
            {
                var result = RunProcess("netstat.exe", "-ano -p tcp", null, false);
                if (result.ExitCode != 0) return;

                foreach (var rawLine in result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = rawLine.Trim();
                    if (!line.Contains(":" + port + " ") || line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int pid;
                    if (parts.Length < 5 || !Int32.TryParse(parts[parts.Length - 1], out pid)) continue;

                    using (var process = Process.GetProcessById(pid))
                    {
                        if (process.HasExited) continue;
                        AddLog("Stopping process listening on port " + port + ": " + pid);
                        process.Kill();
                        process.WaitForExit(10000);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("Failed to stop process listening on port " + port + ": " + ex.Message);
            }
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
            action.WorkingDirectory = GetInstallDir();

            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3, null);
        }

        private string GetPowerShellPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\WindowsPowerShell\\v1.0\\powershell.exe");
        }

        private string GetLauncherArguments()
        {
            return "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + QuoteArg(GetLauncherPath()) + " -InstallDir " + QuoteArg(GetInstallDir());
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

        private string GetCorepackPath()
        {
            return FindCommand("corepack.cmd", NodeFallbacks("corepack.cmd"));
        }

        private string GetPnpmPath()
        {
            return FindCommand("pnpm.cmd");
        }

        private string GetGitPath()
        {
            return FindCommand("git.exe", GitFallbacks());
        }

        private IEnumerable<string> NodeFallbacks(string file)
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs\\" + file);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!String.IsNullOrEmpty(pf86)) yield return Path.Combine(pf86, "nodejs\\" + file);
        }

        private IEnumerable<string> GitFallbacks()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git\\cmd\\git.exe");
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!String.IsNullOrEmpty(pf86)) yield return Path.Combine(pf86, "Git\\cmd\\git.exe");
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

        private Dictionary<string, string> BuildToolEnvironment()
        {
            return new Dictionary<string, string>
            {
                { "npm_config_cache", GetNpmCacheRoot() },
                { "Path", BuildSearchPath() },
                { "npm_config_scripts_prepend_node_path", "true" },
                { "DSH_HOME", GetHomeRoot() },
                { "NO_COLOR", "1" },
                { "FORCE_COLOR", "0" },
                { "COREPACK_ENABLE_DOWNLOAD_PROMPT", "0" }
            };
        }

        private ProcessResult RunProcess(string fileName, string arguments, IDictionary<string, string> env, bool logOutput)
        {
            return RunProcess(fileName, arguments, env, logOutput, null);
        }

        private ProcessResult RunProcess(string fileName, string arguments, IDictionary<string, string> env, bool logOutput, string workingDirectory)
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
            if (!String.IsNullOrWhiteSpace(workingDirectory)) info.WorkingDirectory = workingDirectory;
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

        private static string QuoteArg(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
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
