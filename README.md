# UsageBridge 0.2.1-dev

TrafficMonitor 1.86 x64 的通用用量与网页数据插件。原生 DLL 自动管理 .NET 采集程序，退出或实际卸载 DLL 时通过 Windows Job 清理子进程树。

## 已实现

- Codex 5 小时/周剩余比例与重置倒计时，按窗口时长识别，每次重读凭据文件。
- DeepSeek 按币种提取总额、赠送和充值余额，保留十进制精度及 is_available 状态。
- 通用 HTTP JSON、静态 HTML CSS 选择器、WebView2 渲染后 DOM 与手动登录窗口。
- 文本 trim/replace/正则捕获、数字转换、四则运算、括号及 round/abs/min/max。
- 配置预设、JSON 编辑、测试预览、启用开关、立即刷新及 Windows 用户 DPAPI 凭据存储。
- 16 个稳定显示槽位；同一个数据源请求供多个指标使用。失败时保留旧值并添加 `~`，提示中说明状态及最后成功时间。

## 验证状态（2026-10-28）

- MSVC x64 和 .NET 10 自包含发布构建通过。
- 26 项核心检查通过，包括缺字段、非法数字、除零、币种、窗口顺序、令牌重读与过期状态。
- 独立测试宿主验证了自动启动、无源不启动、快照、正常卸载与强杀后的清理。
- WebView2 子进程树创建及清理测试通过。
- 用户在普通 Windows PowerShell 执行网页测试成功；日志验证延迟 DOM、输入框 value、静态 CSS、多匹配拒绝、超时、跨采集进程 localStorage 复用、DPAPI。
- **真实 TrafficMonitor 加载和真实账户 API/目标网页仍须实机确认。** 本机沙箱的 WebView2 渲染进程会退出，网页成功证据来自正常桌面环境。

## 安装

包内 `plugins/UsageBridge.dll` 为唯一宿主入口，其余依赖放在 `plugins/UsageBridge/`。
先退出 TrafficMonitor，然后运行包内安装脚本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -TrafficMonitorDirectory '你的 TrafficMonitor 目录'
```

安装器检查 x64，拒绝覆盖正在运行的宿主；已有 UsageBridge 会复制到时间戳备份目录。
重新启动后打开“插件管理 → UsageBridge → 选项”，添加 Codex 或其他源，勾选“启用采集”后保存。
在任务栏/主窗口设置选择对应的 `UsageBridge 1`、`UsageBridge 2` 等槽位。
DeepSeek 先通过“保存凭据”存入名称 `deepseek` 的 API Key。
默认无数据源，不运行后台采集。配置窗口关闭后，启用的数据源继续采集。

本包包含 .NET 运行时；网页功能仍需要已安装的 Microsoft Edge WebView2 Runtime。
TrafficMonitor 自身禁用插件需要重启生效。隐藏一个显示项不等于停止采集。

## 第一次使用

1. 在插件选项点击“添加 Codex”。它自动创建 5 小时和周剩余比例两个指标，凭据路径通常保留空白。
2. 点击“测试所选源”，在下方查看提取值、计算值和显示槽位。
3. 勾选“启用采集”，点击“保存应用”。仅点击添加或编辑 JSON 不会保存到磁盘。
4. 返回 TrafficMonitor 的任务栏/主窗口显示设置，勾选刚才提示的 UsageBridge 槽位（从空配置添加 Codex 时为 1 和 2）。
5. 关闭配置窗口后继续采集；要停止，在选项取消“启用采集”并保存。

DeepSeek：点击“保存凭据”，名称填 `deepseek`、值填 API Key；再添加 DeepSeek、测试、启用和保存。
自定义数据：添加 JSON/静态网页/动态网页，填写真实 URL 和字段路径或 CSS 选择器；动态网页需登录时点击“网页登录”。参见 [配置说明](docs/configuration.md)。

0.2.1 修复了清空编辑框后无法添加预设的问题。空白草稿可直接添加任意源；非空但格式错误的 JSON 仍会提示修正。
“新建空配置”只重置草稿，“恢复已保存”读取磁盘内容。直接保存空白编辑框会拒绝，避免误清除已有配置。
配置面板显示真实文件路径，并提供“打开配置目录”。文件首次保存时创建。

## 开发

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\restore-dependencies.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -SelfContained
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-core.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-settings.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-lifecycle.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-web.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-browser-lifecycle.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
```

ExecutionPolicy 参数只作用于该 PowerShell 进程。省略 `-SelfContained` 时生成依赖 .NET 10 Desktop Runtime 的开发构建。
工具版本见 [环境记录](docs/environment.md)，配置语法见 [配置说明](docs/configuration.md)，测试证据见 [测试记录](docs/testing.md)。
完整原始需求保留在 [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)。
