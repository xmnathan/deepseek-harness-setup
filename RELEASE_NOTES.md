# GitHub Release Notes

Tag:

```text
v1.1
```

Release title:

```text
DeepSeek Harness Setup for Windows v1.1
```

Release body:

```markdown
## DeepSeek Harness Setup for Windows v1.1

这个版本重点优化了安装、更新和启动流程，并补上了源码模式支持。相比 v1.0，现在不再需要用户手动判断是否提权，也不需要区分“安装”和“更新”两个入口。

### 主要变化

- 启动时自动检查管理员权限；如果当前不是管理员权限，会立即触发 Windows UAC 提权。
- 移除 `Restart as Admin` 按钮，避免用户在非管理员状态下继续执行安装或自启动配置。
- 主操作统一为 `Check, Update and Start`，一个按钮完成检查、安装、更新、自启动配置和启动。
- 默认安装目录保持为 `D:\deepseek-harness`，仍支持用户手动选择其他目录。
- `Start-DeepSeekHarness.ps1` 已嵌入 exe，运行时自动释放到安装目录，普通用户只需要下载 `DeepSeekHarnessSetup.exe`。

### 新增源码模式

- 选择 DeepSeek Harness 源码仓库根目录时，管理器会自动切换到源码模式。
- 源码模式会执行 `pnpm install`，并在源码更新或构建产物缺失时自动执行 `pnpm run build`。
- 如果源码目录是 Git 仓库且 tracked 文件没有本地改动，会自动执行 `git pull --ff-only`。
- 如果检测到本地源码改动，会跳过 `git pull`，避免覆盖用户自己的代码。

### 安装和更新体验

- npm 包模式每次点击 `Check, Update and Start` 都会执行 `npm install @deepseek-ai/dsh@latest`，由 npm 自动判断是否需要更新。
- 源码模式每次点击同一按钮会自动同步源码、依赖和构建产物。
- 安装过程保留动态进度条和当前状态提示，减少长时间安装时“程序像卡住了”的感觉。
- 日志不再输出 elapsed 读秒信息，安装状态放在界面上展示。

### 自启动和运行环境

- 继续优先使用当前用户登录触发的计划任务 `DeepSeekHarness`。
- 后台启动使用隐藏 PowerShell，不显示控制台窗口。
- 运行时显式设置：

  ```text
  DSH_HOME=<安装目录>\home
  npm_config_cache=<安装目录>\npm-cache
  ```

- DeepSeek Harness 会运行在当前桌面用户环境中，避免 NSSM/Windows Service Session 0 带来的工作区、沙箱、本地文件访问、用户凭据和网络盘异常。
- 如果任务计划程序被系统策略拦截，会创建 HKCU Run 作为 fallback。

### 修复和改进

- 修复 Node.js 安装完成后，npm 依赖构建脚本可能提示 `node` 不是内部或外部命令的问题。
- 修复 winget、npm、node 输出中的中文乱码问题。
- 修复使用 HKCU Run fallback 后，升级流程可能仍尝试启动旧计划任务的问题。
- 优化停止旧进程逻辑，会尝试根据 pid 文件和 `3080` 端口结束旧的 DeepSeek Harness 进程。
- 源码模式启动参数修正为 `pnpm run dsh web`，避免把多余的 `--` 传给 DeepSeek Harness CLI。
- 源码构建前会先执行 clean，减少旧构建产物导致的启动异常。

### 使用方式

1. 下载本 Release 的 `DeepSeekHarnessSetup.exe`。
2. 双击运行。
3. 允许 Windows UAC 提权。
4. 选择或确认安装目录。
5. 点击 `Check, Update and Start`。
6. 等待安装或更新完成，Web UI ready 后会自动打开 `http://127.0.0.1:3080`。

### 从 v1.0 升级

- 直接下载新版 `DeepSeekHarnessSetup.exe` 覆盖或替换旧文件即可。
- 重新运行后点击 `Check, Update and Start`。
- 用户配置、会话、工作区、附件和 npm cache 不会被清空。

### 发布资产

请至少上传：

```text
DeepSeekHarnessSetup.exe
```

维护者可以同时上传源码包或直接使用 GitHub 自动生成的 Source code 压缩包。
```
