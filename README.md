# DeepSeek Harness Windows Setup

Windows 图形化安装管理器，用于安装、更新、启动 DeepSeek Harness，并配置为当前登录用户自启动。

双击运行：

```text
DeepSeekHarnessSetup.exe
```

程序是 Windows GUI 应用，启动和后台拉起 DeepSeek Harness 时都不会显示控制台窗口。

## 核心特性

1. 启动时自动检查管理员权限；非管理员状态会立即触发 UAC 提权。
2. 支持用户指定安装目录，默认目录为 `D:\deepseek-harness`。
3. 自动安装 Node.js LTS，并处理 npm、npx、corepack、pnpm 的路径刷新。
4. 一个按钮完成检查、安装、更新、自启动配置和启动。
5. 支持 npm 包模式和源码模式。
6. 使用当前桌面用户会话启动，避免 NSSM/Windows Service Session 0 导致的工作区、沙箱、本地文件访问异常。
7. 将配置、日志、缓存和 `DSH_HOME` 固定在用户选择的目录下，便于迁移和排查。

## 最少需要的文件

普通用户只需要：

```text
DeepSeekHarnessSetup.exe
```

维护或二次开发时再保留这些文件：

```text
DeepSeekHarnessSetup.cs
Start-DeepSeekHarness.ps1
Migrate-DshHome.ps1
Build.ps1
README.md
```

说明：

- `Start-DeepSeekHarness.ps1` 已嵌入 exe。安装器运行时会自动释放到安装目录。
- `Migrate-DshHome.ps1` 用于从旧 npm 包模式迁移用户数据到源码模式。
- `manager-settings.json` 不随项目发布，运行后自动生成到 `%LOCALAPPDATA%\DeepSeekHarnessSetup`。

## 使用方式

1. 双击 `DeepSeekHarnessSetup.exe`。
2. 允许 Windows UAC 提权。
3. 确认或修改安装目录。
4. 点击 `Check, Update and Start`。
5. 等待进度条完成。首次安装 Node.js、下载依赖或源码构建时可能需要几分钟。
6. Web UI ready 后，管理器会打开 `http://127.0.0.1:3080`。

`http://127.0.0.1:3080` 能访问只代表 Web 服务端口已启动。DeepSeek Harness 还需要运行在当前桌面用户环境中，才能正常访问工作区、本地文件、用户凭据、Git/SSH 配置、OneDrive、网络盘等资源。本工具的自启动方案围绕这个前提设计。

## 主按钮行为

`Check, Update and Start` 会依次执行：

1. 释放内置启动脚本到安装目录。
2. 写入启动配置 `config.json`。
3. 检查 Node.js/npm/npx。
4. 缺少 Node.js 时安装 Node.js LTS。
5. 停止当前 DeepSeek Harness 任务和占用 `3080` 的旧进程。
6. 判断运行模式并同步依赖或源码。
7. 创建或刷新自启动入口。
8. 启动 DeepSeek Harness。
9. 等待 Web UI ready 后打开浏览器。

重复点击不会清空用户配置、会话、附件、工作区记录或 npm cache。

## 运行模式

### npm 包模式

适合大多数用户。选择普通目录时自动使用 npm 包模式。

安装目录示例：

```text
D:\deepseek-harness
```

DeepSeek Harness 会安装到：

```text
<安装目录>\runtime
```

启动入口：

```text
<安装目录>\runtime\node_modules\.bin\dsh.cmd web
```

每次点击 `Check, Update and Start` 都会执行：

```text
npm install @deepseek-ai/dsh@latest
```

npm 会自行判断是否已有最新版。已是最新版时，这一步通常很快结束。

### 源码模式

选择 DeepSeek Harness 源码仓库根目录时自动使用源码模式。

源码目录识别条件：

```text
package.json 的 name 为 @deepseek-ai/dsh-root
存在 pnpm-workspace.yaml
存在 apps\cli\src\bin.ts
package.json 中存在 dsh 脚本
```

源码模式会在源码根目录执行：

```text
corepack pnpm install --no-frozen-lockfile
corepack pnpm run build
corepack pnpm run dsh web
```

如果系统没有 `corepack.cmd`，但有 `pnpm.cmd`，会改用：

```text
pnpm install --no-frozen-lockfile
pnpm run build
pnpm run dsh web
```

源码目录是 Git 仓库且 tracked 文件没有本地改动时，管理器会先执行：

```text
git pull --ff-only
```

如果检测到本地改动，会跳过 `git pull`，避免覆盖用户源码。

`pnpm run build` 只在源码更新后，或关键构建产物缺失时执行。源码首次运行通常必须构建，否则 Web 服务可能无法启动。

## 目录结构

安装目录会保存：

```text
config.json                 启动配置
Start-DeepSeekHarness.ps1   后台启动脚本，由 exe 自动释放
home\                       DSH_HOME，保存配置、凭据、会话、附件、profiles
logs\latest.log             最新启动日志
logs\deepseek-harness-*.log  历史启动日志
logs\dsh-web*.log           DeepSeek Harness Web 服务日志
downloads\                  Node.js MSI 下载缓存
npm-cache\                  npm 下载缓存
runtime\                    npm 包模式下的 dsh 本地安装目录
```

两种运行模式都会设置：

```text
DSH_HOME=<安装目录>\home
npm_config_cache=<安装目录>\npm-cache
```

Node.js 本身仍按 Windows 标准方式安装到系统目录，例如：

```text
C:\Program Files\nodejs
```

## 自启动方式

管理器优先创建当前用户登录触发的计划任务：

```text
DeepSeekHarness
```

任务动作：

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "<安装目录>\Start-DeepSeekHarness.ps1" -InstallDir "<安装目录>"
```

计划任务会在当前登录用户的交互会话中运行，和浏览器、工作区、本地文件访问保持同一个用户上下文。

如果任务计划程序被系统策略拦截，管理器会创建当前用户注册表启动项作为 fallback：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DeepSeekHarness
```

这只是兼容路径。推荐状态仍然是计划任务创建成功。

## 从 npm 包模式迁移到源码模式

旧版或手动运行的 npm 包版 `dsh`，用户数据常见位置是：

```text
%USERPROFILE%\.dsh
```

源码模式默认使用：

```text
D:\deepseek-harness\home
```

迁移命令：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Migrate-DshHome.ps1 -SourceHome "$env:USERPROFILE\.dsh" -TargetHome "D:\deepseek-harness\home" -SourceDir "D:\deepseek-harness" -StopRunning
```

脚本会先备份目标 `home` 到：

```text
D:\deepseek-harness\migration-backups
```

然后迁移配置、凭据、工作区、会话、附件和 profile 用户文件。`node_modules` 和 `.dsh-module-fallback` 会被排除，因为它们是可重新生成的依赖目录。

迁移完成后，脚本会对每个 profile 执行：

```text
dsh plugin --profile <name> install
```

这样源码模式会按新的 `DSH_HOME` 重建 profile 依赖和本地插件链接。

不要把旧的 `runtime\node_modules` 合并到源码目录。`runtime` 只属于 npm 包模式。

## 依赖

- Windows 10/11，或带桌面体验的 Windows Server。
- .NET Framework 4.x，用于运行 WinForms 管理器。
- Windows PowerShell 5.1，用于隐藏启动脚本和计划任务动作。
- 网络需要能访问 `nodejs.org` 和 npm registry。
- npm 包模式不需要 Git、pnpm、Python 或 Visual Studio。
- 源码模式建议安装 Git。
- 源码模式需要 Corepack 或 pnpm。Node.js 22+ 通常自带 Corepack。

Node.js 安装策略：

1. 优先检测本机已有 Node.js。
2. 如果没有 Node.js，优先尝试 winget 安装 `OpenJS.NodeJS.LTS`。
3. winget 不可用或失败时，从 `nodejs.org` 下载 LTS MSI 安装。
4. 安装器会按 UTF-8 读取 winget、npm、node 输出，避免中文日志乱码。
5. Node.js 刚安装完成后，会给 npm 子进程显式注入 Node.js 路径，避免依赖构建脚本提示 `node` 不是内部或外部命令。

## 常见问题

### Web 页面打不开

点击 `Open Logs` 查看：

```text
<安装目录>\logs\latest.log
```

再确认 `3080` 端口没有被其他进程占用。

### 源码模式启动失败

优先查看 `latest.log`。常见原因：

- 源码还没有 build。
- Corepack/pnpm 不可用。
- 迁移后的 profile 引用了本地插件，但 profile 依赖尚未重建。

通常重新点击 `Check, Update and Start` 可以完成依赖同步和构建。

### 第三方 API 或 token 没生效

检查迁移后的：

```text
<安装目录>\home\settings.yaml
<安装目录>\home\.credentials.yaml
```

如果配置里引用的是环境变量，例如 `OPENAI_API_KEY`，还需要确认当前 Windows 用户环境变量存在。

### 不想自启动

点击 `Remove Task`。管理器会删除计划任务，并同时清理 HKCU Run fallback 项。

## 重新构建

普通用户不需要执行。

维护者重新生成 exe：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

构建脚本使用 Windows 自带或 .NET Framework 附带的 `csc.exe`，输出类型是 `winexe`，因此生成的程序不会显示控制台窗口。
