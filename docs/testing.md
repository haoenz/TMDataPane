# 测试记录

## 0.3.7 更名为 DataPane

更新 TM 插件名称、配置及登录窗口标题、采集程序产品信息、HTTP User-Agent、测试结果文字、文档与发布包名称。为兼容已有安装，DLL/EXE 文件名、配置目录、IPC 命名及稳定显示项 ID 保持不变。
自包含构建和现有 46 项界面检查通过：`artifacts/settings-6a50a1ce10424ec08771e0350f57ee76`。DPAPI 成功路径仍受下述沙箱限制。

## 0.3.6 隐藏空显示项

自包含构建通过；46 项界面检查通过，包含增删显示项后提示重启 TM。证据：`artifacts/settings-fc9a039fa4ce46a7a67b1ae5023b5edc`。
原生显示项测试通过：非连续槽位 1/5/16 按配置枚举、重名、ID 保持不变、运行期间新增/删除不替换已注册对象、改名、缓存损坏保护、DLL 重载后移除已删除项并加入新项、空配置无显示项，以及首次添加需要重载后出现。证据：`artifacts/names-93f493e664be491ba13386d2611870cd`。
真实 TM 列表仍需安装后核对；DPAPI 成功路径仍受下述沙箱限制。
采集进程卸载/强杀清理通过：`artifacts/lifecycle-ed36e71355ec4c4c92fb2e8efe89d57c`。

## 0.3.5 TM 项目名称

自包含构建通过；45 项界面/配置检查通过（含名称缓存的 UTF-16 格式、重名、空名称、删除项清理）：`artifacts/settings-5dee3e420a434b679cf7acc423868e00`。
`test-names.ps1` 通过原生插件接口验证中文名称、允许重名、16 个 ID 不变、各槽位返回缓冲区独立、动态改名、删除项清理、损坏缓存拒绝，以及 DLL 重载后在未启动采集的情况下读回名称。证据：`artifacts/names-fa24ed1e06cd4216a7a68ab53dd02aef`。
采集进程卸载/强杀清理通过：`artifacts/lifecycle-ddb0defc515a411aa679d0446906d091`。真实 TM 显示设置列表需升级后重新打开核对；DPAPI 成功路径仍受下述沙箱限制。

## 0.3.3 操作命名与更多菜单

按用户指定名称调整操作入口，加载配置、配置密钥及打开目录移入“更多”菜单，采集与保存语义不变。
自包含构建和 43 项现有界面检查通过，包括从菜单加载配置和菜单项顺序核对。主面板截图已查看。
证据：`artifacts/settings-3832b819584c43d58d6cb0424bea007b`。DPAPI 成功路径仍受下述沙箱限制。

## 0.3.2 简化面板与外部编辑

自包含构建通过，43 项界面检查通过。覆盖移除路径/JSON 控件、独立测试结果弹窗及关闭释放、失败结果弹窗、外部文件加载、复杂规则保留、无效文件不替换草稿、保存时拒绝覆盖外部修改，以及已读取与已应用状态的区分。
证据：`artifacts/settings-26887e0cac63449abdf4d66376de22c8`。主面板默认 780×520，最小 700×440；截图包含主窗口及测试结果窗口。DPAPI 成功路径仍受下述沙箱限制。

## 0.3.1 紧凑窗口

自包含构建和原有 37 项界面检查通过。已检查默认 860×640、最小 760×540 窗口截图，以及测试区折叠后的布局。
证据：`artifacts/settings-568ca965c31948738eab045ef5a690a3`，含 `settings.png`、`settings-compact.png` 和 `settings-minimum.png`。
本轮仅调整布局与版本号；DPAPI 成功路径仍受下述沙箱限制，高 DPI 桌面未实机验证。

## 0.3.0 UX 更新

- 自包含构建通过，无编译警告；配置仍为 v2。
- 37 项界面与预览检查通过：六种类型添加、表单/JSON 往返保留复杂规则、错误草稿保护、异步测试禁用编辑、保存/恢复/关闭确认、货币文本处理、HTML/JSON 原始与处理后字段、预览脱敏、删除关联项、密钥未保存提醒。
- 结果：`artifacts/settings-17ba1b0826cf4ce98e8384789fa8ff84/settings-results.json`；同目录截图已检查。
- 沙箱 DPAPI 不可用，测试明确记录 `DpapiAvailable=false`；验证了密钥保存失败后保留输入且不写入 JSON。加密成功路径须在普通桌面运行 `scripts/test-settings.ps1`，没有将该分支标为已通过。
- 核心检查仍为 26 项，通过。卸载/强杀清理通过：`artifacts/lifecycle-fb9656fa9bd94a7685d80e8a31ffa4e3`。
- 0.3.0 需安装后在真实 TM 中体验新界面；此前 0.2.1 的用户反馈为功能基本正常。

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
