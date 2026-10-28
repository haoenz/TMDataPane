# DataPane 0.4.0 测试记录

日期：2026-10-28。工具链版本见 [环境记录](environment.md)。

## 本轮结果

| 检查 | 结果与证据 |
| --- | --- |
| MSVC x64 DLL、测试宿主和 .NET 10 自包含采集程序 | 构建通过；产物位于 `build/datapane/` |
| 核心逻辑 | `test-core.ps1`：26 项通过 |
| 配置界面 | 46 项通过；`artifacts/settings-de15e2eb4e4b4ea7a61aa49fe651f3c1/settings-results.json` |
| 原生显示项 | 稀疏槽位、重名、稳定 ID/指针、改名、重启后增删、空配置及首次添加通过；`artifacts/names-27a73d29c8bb4f03bc3610433131282a` |
| 采集进程生命周期 | 无源不启动、自动启动、快照、卸载及强杀清理通过；`artifacts/lifecycle-748b40bdc7de41dc87b6aaf09cd6bda3` |
| 浏览器进程树 | 观察到 WebView2 子进程，正常卸载和强杀后均清理；`artifacts/browser-lifecycle-4ec7ee79966f47d196037ca02a8e17f9` |
| 安装器 | 276 字符备份路径、连续两次安装、独立备份和数据保留通过；`artifacts/install-373f142b896c49afbea219f57d041507` |

名称缓存使用 DPN1 标识。测试覆盖 DataPane.dll、DataPane.Collector.exe、DataPane 配置目录和 DataPane.Slot 显示项 ID 的协作。
安装测试使用工作区内的假 PE 和隔离目录，仅在该测试作用域模拟宿主未运行，不修改真实 TrafficMonitor。

核心检查覆盖提取、数字处理、十进制表达式及资源边界、Codex 窗口识别和令牌重读、DeepSeek 币种精度、请求共享、配置校验及失败旧值标记。
界面检查覆盖新增数据、编辑草稿、外部修改冲突、测试弹窗、敏感值保护、显示名称缓存及增删显示项后的重启提示。

## 桌面环境待确认

本轮界面测试记录 `DpapiAvailable=false`，只验证了加密失败时保留输入且不写入配置的行为，未将加密成功分支标记为通过。
普通 Windows PowerShell 中需运行 `scripts/test-web.ps1`，核实 DataPane 进程下的静态 CSS、延迟 DOM、输入框 value、超时、跨进程 localStorage 复用及 DPAPI 往返。该测试使用 `datapane.test` 映射本地 HTML，不访问真实账户。
本会话的 WebView2 渲染和 DPAPI 受沙箱限制；浏览器进程树清理通过不代表网页读取通过。

安装后需在真实 TrafficMonitor 中确认 DataPane 插件入口、首次配置、显示项选择及正常退出。真实 API 凭据、实际站点登录和高 DPI 布局未在本轮自动检查中验证。

## 运行方式

依次运行 README 中的构建和测试命令。原生测试针对 Release 输出，使用临时配置目录。所有日志、截图和临时测试配置位于被 Git 忽略的 `artifacts/` 中。
