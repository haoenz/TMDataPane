# DataPane 0.4.0 测试记录

## 2026-10-29 公共重置背景提醒

- Release x64 DLL 与 .NET 10 自包含采集程序构建通过；共享快照 v3（22608 字节）。
- 核心检查 71 项通过：六小时边界、预测开关/过期、时间未定/已过但未执行、banked 区分、历史事件不闪绿、重复事件去重、304、429 退避、503、过期取消底色、公共请求无账户凭据、公共接口失败不影响个人额度。
- 配置界面 51 项通过，覆盖默认 360 分钟、开关与提前量保存、Codex 专属自绘缓存；本次 DPAPI 往返也通过。记录：`artifacts/settings-0cd23071f79f451f97ad444ffb615b8a`。
- `build/datapane/native/Release/RenderTests.exe` 通过：各提醒色、橙红色、暗色模式、三种字体大小、区域裁剪及绘图状态恢复。原生显示名称和稳定槽位检查通过。
- 生命周期检查通过：自动启动、快照、正常卸载、强杀子进程清理。记录：`artifacts/lifecycle-1d403f02ca204caabba94c065d7c8abc`。
- 只读调用公共 `/api/v1/status` 返回 HTTP 200，结构匹配；当时 `scheduled_reset`、`active_watch` 均为空。颜色分支使用模拟响应及离屏 GDI 验证，没有伪造真实重置事件。
- 未替换正在使用的 TrafficMonitor 安装。真实宿主的皮肤/透明任务栏、高 DPI 和实际通知显示仍需安装后人工确认；离屏字体测试不等同于真实 DPI 验收。

## 2026-10-28 基础功能

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
