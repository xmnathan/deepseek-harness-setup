# DeepSeek Harness Windows 安装与自启动管理器

这是一个 Windows 图形化安装管理器，用于下载安装 DeepSeek Harness，并配置成当前用户登录后自动启动。

普通用户只需要双击：

```text
DeepSeekHarnessSetup.exe
```

这是一个真正的 Windows 图形界面程序，启动时不会出现控制台窗口。

## 功能

1. 支持用户选择安装目录。
2. 检查 Node.js、npm、npx。
3. 缺少 Node.js 时安装 Node.js LTS。
4. 预拉取 `@deepseek-ai/dsh@latest`。
5. 将配置、日志、下载缓存、npm cache 写入用户指定安装目录。
6. 优先创建计划任务 `DeepSeekHarness`，当前用户登录后自动启动。
7. 如果系统策略拒绝创建计划任务，自动退到当前用户 `HKCU Run` 自启动项。
8. 启动后打开 `http://127.0.0.1:3080`。

DeepSeek Harness 官方快速启动方式：

```powershell
npx @deepseek-ai/dsh web
```

本工具围绕这条官方启动命令做 Windows 安装、自启动和依赖处理。

## 文件

- `DeepSeekHarnessSetup.exe`：最终用户入口，双击运行。
- `Start-DeepSeekHarness.ps1`：计划任务调用的后台启动脚本。
- `DeepSeekHarnessSetup.cs`：图形界面程序源码。
- `Build.ps1`：维护者构建脚本，用于重新生成 exe。
- `manager-settings.json`：运行后自动生成，记录上次选择的安装目录。

## 使用

1. 打开 `deepseek-harness-setup`。
2. 双击 `DeepSeekHarnessSetup.exe`。
3. 选择安装目录，例如 `D:\DeepSeekHarness`。
4. 点击 `Install and Start`。
5. 如果安装 Node.js 时弹出 UAC，请允许。
6. 完成后访问 `http://127.0.0.1:3080`。

## 安装目录说明

用户选择的安装目录会保存：

- `config.json`：DeepSeek Harness 启动配置。
- `logs\latest.log`：最新运行日志。
- `logs\deepseek-harness-*.log`：历史运行日志。
- `downloads\`：Node.js MSI 下载缓存。
- `npm-cache\`：npm/npx 下载包缓存。

Node.js 本身仍按 Windows 标准方式安装到系统目录，例如：

```text
C:\Program Files\nodejs
```

这是因为 Node.js MSI 和 winget 都是系统级运行时安装器。用户指定目录用于 DeepSeek Harness 数据、缓存和日志，不用于强行搬迁 Node.js 运行时。

## 依赖和兼容性

- 支持 Windows 10/11 和常见 Windows Server 桌面环境。
- 图形程序基于 .NET Framework 4.x，Windows 10/11 通常已内置。
- 需要 Windows PowerShell 5.1，用于计划任务后台启动脚本。
- 需要能访问 `nodejs.org` 和 npm registry。
- 如果系统有 winget，会优先尝试 `winget install OpenJS.NodeJS.LTS`。
- 如果 winget 不可用或失败，会从 `nodejs.org` 下载最新 LTS MSI 安装。
- npm/npx 随 Node.js 安装。
- 不需要 Git、pnpm、Python 或 Visual Studio。

## 自启动方式

管理器会优先创建计划任务。

计划任务名称：

```text
DeepSeekHarness
```

触发器：

```text
当前用户登录时启动
```

动作格式：

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "<setup目录>\Start-DeepSeekHarness.ps1" -InstallDir "<用户选择的安装目录>"
```

计划任务启动时不会显示控制台窗口。

如果创建计划任务时遇到 `拒绝访问`，通常是当前 Windows 用户、公司策略或系统任务计划程序权限限制导致。管理器会自动退到当前用户注册表自启动项：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DeepSeekHarness
```

这个兜底方式不需要管理员权限，也不会显示控制台窗口。

## 提权操作

界面提供 `Restart as Admin` 按钮。

适用场景：

- 当前 Windows 用户本身是管理员账号，但程序不是以管理员权限启动。
- 创建计划任务被 UAC 或本机权限限制拦截。

不建议的场景：

- 当前 Windows 用户是标准用户，并且需要输入另一个管理员账号提权。这种情况下计划任务可能会创建到那个管理员账号名下，不一定能在当前用户登录时启动。

因此管理器不会强制自动提权。计划任务创建失败时，会先退到当前用户 `HKCU Run` 自启动项，保证普通用户也能完成自启动配置。

如果移动了 `deepseek-harness-setup` 目录，或修改了安装目录，请重新打开 `DeepSeekHarnessSetup.exe` 并点击 `Create Autostart`，让自启动入口指向新的脚本路径和安装目录。

## 维护者重新构建

普通用户不需要执行此步骤。

如需重新生成 `DeepSeekHarnessSetup.exe`：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

构建脚本使用 Windows 自带或 .NET Framework 附带的 `csc.exe`，输出类型为 `winexe`，因此生成的程序没有控制台窗口。

## 常见问题

- Web 页面打不开：点击 `Refresh Status`，确认 `Autostart` 不是 `not created`；再点击 `Open Logs` 查看 `latest.log`。
- npm 下载失败：检查代理、防火墙或公司网络是否允许访问 npm registry。
- Node.js 安装后仍提示未找到：关闭程序后重新打开，或重启 Windows 让 PATH 刷新。
- 不想自启动：点击 `Remove Task`，会同时尝试删除计划任务和 HKCU Run 兜底项。

