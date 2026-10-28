# UsageBridge 0.3.4-dev

TrafficMonitor 1.86 x64 的通用用量与网页数据插件。原生 DLL 自动管理 .NET 采集程序，退出或实际卸载 DLL 时通过 Windows Job 清理子进程树。

0.3.4 使用紧凑选项窗口：默认 780×520，可缩至 700×440；标签与输入框同行排列，宽度随窗口调整。测试结果使用独立弹窗，关闭即收起。面板不显示配置路径或提供 JSON 编辑器。尺寸为 100% 显示缩放下的值。

第一排为“添加数据、保存配置、测试数据、更新数据、更多、自动更新”；“更多”菜单包含“加载配置、配置密钥、打开配置目录”。

## 已实现

- Codex 5 小时/周剩余比例与重置倒计时，按窗口时长识别，每次重读凭据文件。
- DeepSeek 按币种提取总额、赠送和充值余额，保留十进制精度及 is_available 状态。
- 通用 HTTP JSON、静态 HTML CSS 选择器、WebView2 渲染后 DOM 与手动登录窗口。
- 文本 trim/replace/正则捕获、数字转换、四则运算、括号及 round/abs/min/max。
- 数据源列表、分类配置表单、独立测试结果窗口、未保存提醒及 Windows 用户 DPAPI 凭据存储。
- 16 个稳定显示槽位；同一个数据源请求供多个指标使用。失败时保留旧值并添加 `~`，提示中说明状态及最后成功时间。

## 验证状态（2026-10-28）

- MSVC x64 和 .NET 10 自包含发布构建通过。
- 26 项核心检查通过，包括缺字段、非法数字、除零、币种、窗口顺序、令牌重读与过期状态。
- 独立测试宿主验证了自动启动、无源不启动、快照、正常卸载与强杀后的清理。
- WebView2 子进程树创建及清理测试通过。
- 用户在普通 Windows PowerShell 执行网页测试成功；日志验证延迟 DOM、输入框 value、静态 CSS、多匹配拒绝、超时、跨采集进程 localStorage 复用、DPAPI。
- 用户已确认 0.2.1 在真实 TrafficMonitor 中的功能基本正常；0.3.0 界面更新需安装后体验。沙箱的 WebView2 渲染和 DPAPI 调用受限，网页成功证据来自此前的普通桌面环境测试。

## 安装

包内 `plugins/UsageBridge.dll` 为唯一宿主入口，其余依赖放在 `plugins/UsageBridge/`。
先退出 TrafficMonitor，然后运行包内安装脚本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -TrafficMonitorDirectory '你的 TrafficMonitor 目录'
```

安装器检查 x64，拒绝覆盖正在运行的宿主；已有 UsageBridge 会复制到时间戳备份目录。
重新启动后打开“插件管理 → UsageBridge → 选项”，点击“添加数据”，勾选“自动更新”后保存配置。
在任务栏/主窗口设置选择对应的 `UsageBridge 1`、`UsageBridge 2` 等槽位。
DeepSeek 在对应表单中填写 API Key 并点击“加密保存 API Key”。
默认无数据源，不运行后台采集。配置窗口关闭后，启用的数据源继续采集。

本包包含 .NET 运行时；网页功能仍需要已安装的 Microsoft Edge WebView2 Runtime。
TrafficMonitor 自身禁用插件需要重启生效。隐藏一个显示项不等于停止采集。

## 第一次使用

1. 点击“添加数据”并选择类型。例如 Codex 自动创建两个指标，并显示是否找到本机凭据文件。
2. 在右侧表单填写地址、提取路径等。DeepSeek 的 API Key 使用专用加密保存按钮；不写入 JSON。
3. 点击“测试数据”，检查读取、提取/文本处理、计算和最终显示。金额包含 `¥` 或千位逗号时，可勾选对应移除选项再测试。
4. 勾选“自动更新”，点击“保存配置”。测试和编辑均不会自动保存。
5. 在 TrafficMonitor 显示设置中勾选表单提示的 UsageBridge 槽位。关闭配置窗口后继续采集；要停止，取消总开关并保存。

左侧列表展示已应用配置的运行状态和首个显示值，悬停可查看完整状态。测试预览使用当前草稿；“更新数据”只刷新后台数据。
动态网页需登录时点击“打开网页登录”。自定义认证头可通过“配置密钥”保存，再在外部配置文件的 secretHeaders 中引用。参见 [配置说明](docs/configuration.md)。

配置仍兼容 v2。复杂请求头、新增字段、正则和公式请通过“打开配置目录”，在外部编辑 config.json，表单不会丢弃这些规则。
外部编辑后点击“加载配置”读取文件，再点击“保存配置”更新后台。无效或空白文件不会替换当前草稿；文件在外部发生变化时，保存会要求先重新读取，避免覆盖外部修改。
关闭窗口时可选择保存、放弃或取消。配置文件首次保存时创建。删除源会同时移除关联指标和显示项，保存后才生效。

## 开发

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\restore-dependencies.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -SelfContained
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-core.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-settings.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-lifecycle.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-web.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-browser-lifecycle.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-install.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
```

ExecutionPolicy 参数只作用于该 PowerShell 进程。省略 `-SelfContained` 时生成依赖 .NET 10 Desktop Runtime 的开发构建。
工具版本见 [环境记录](docs/environment.md)，配置语法见 [配置说明](docs/configuration.md)，测试证据见 [测试记录](docs/testing.md)。
完整原始需求保留在 [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)。
