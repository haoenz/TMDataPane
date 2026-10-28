# 测试记录

安装脚本长路径修复：`test-install.ps1` 已验证 272 字符备份路径、连续两次安装、独立备份以及配置、凭据和会话文件保留。目录复制使用 Robocopy，不使用镜像删除；退出码大于等于 8 时停止安装。测试仅使用工作区内的模拟安装目录。

日期：2026-10-28。编译器及依赖版本见 environment.md。

0.2.1 界面修复：自包含构建通过，test-settings.ps1 的 21 项检查通过。
证据：artifacts/settings-0b64a4e41e504a14a96615d3b45304a7/settings-results.json；同目录 settings.png 已查看布局。
覆盖：六种预设从空白草稿创建、自动选择新增源、拒绝覆盖错误 JSON、空白保存不写盘、恢复磁盘配置、
错误草稿中的开关回滚、空白草稿切换启用、新建草稿不写盘、显式保存空配置、显示只读实际路径、无文件时恢复不创建文件。

| 检查 | 结果与证据 |
| --- | --- |
| 原生 DLL / LifecycleHost + .NET 自包含 win-x64 | 构建通过；去除无用 WebView2 WPF 引用后无该冲突警告 |
| 核心逻辑 | test-core.ps1：26 checks 通过 |
| 生命周期原型 | artifacts/lifecycle-e8190d74eea9498896500b6702073651 |
| v2 生命周期 | artifacts/lifecycle-426f1cf8bded4098b75dac0b912a8742，正常卸载及强杀清理通过 |
| 浏览器进程树 | artifacts/browser-lifecycle-5b1bb7cc1c3f49b1b2001b0b04bc4201，枚举并确认采集及 WebView2 子进程消失 |
| 普通 Windows 桌面的网页测试 | 用户运行 test-web.ps1，artifacts/web-68c98b85fa834f38aebc2ffba99753cf/web-results.json：Passed=true、PersistedSession=true |
| 网页测试退出清理 | 读取上述 BrowserProcessIds 后检查，剩余进程数量为 0 |

核心检查覆盖：JSONPath 子集、文本处理、十进制算术、括号/舍入、除零、表达式资源边界、拒绝宿主调用、空/非法数字、Codex 窗口顺序与范围限制、缺窗口/结构变化、币种/金额精度、401、重复槽位、明文认证头、一次请求共享多个指标、刷新间隔、失败旧值标记、令牌文件轮换。

网页检查覆盖：AngleSharp CSS、拒绝多匹配、延迟 DOM 非空等待、value 属性、DOM 结果参与计算、缺元素超时、跨两个采集进程 localStorage 复用、DPAPI 往返。
本地页面会话复用不是对所有站点登录机制的兼容保证，也未覆盖实际站点 Cookie 过期流程。

沙箱内网页失败已诊断：测试 trace 证明所有调用位于 STA / WindowsFormsSynchronizationContext，WebView2 随后报告 GPU、Render、Browser ProcessExited；在用户普通 PowerShell 中同一测试成功。没有关闭 Chromium 沙箱或放宽系统策略。

## 命令

依次执行 README 中的 build、test-core、test-lifecycle、test-web、test-browser-lifecycle。
独立 LifecycleHost 使用临时配置，不修改运行中的 TrafficMonitor。测试只针对 Release 输出。
Web 测试用 WebView2 的虚拟主机映射读取本地 HTML，不连接实际网站，不使用真实凭据。
浏览器生命周期测试使用本机不可用端口；即使导航失败，也必须创建并清理浏览器子进程，未观察到浏览器时测试会失败而不误报通过。

## 尚需实机确认

- 将包安装到真正的 TrafficMonitor 1.86，验证显示槽位、设置、保存关闭、重新启用。
- 同一设置重复打开只复用一个窗口和一个采集进程；关闭所有源后进程消失。
- 正常退出宿主、禁用并重启后无采集进程。
- 实际 Codex 账户与 DeepSeek API Key 成功调用；目前 API 的自动测试使用脱敏 fixture/fake HTTP。
- 用户选定实际网页的手动登录、DOM 和会话失效行为。

发布包属于开发预览；没有把尚未进行的实机验收标记为通过。
