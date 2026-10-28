# 配置 v2

配置位于宿主传入的插件配置目录的 `UsageBridge/config.json`。
旧生命周期原型 v1 配置不兼容；发布前原型只使用临时测试配置，没有迁移用户数据。
配置面板包含 JSON 编辑器、预设、启用开关、保存、刷新、源测试、网页登录和凭据保存。
未保存的改动仅用于测试。立即刷新作用于已保存配置。添加源后勾选启用再保存。

## 0.2.1 的草稿操作与位置

清空编辑框后，可以直接点任意“添加…”按钮创建新配置；纯空白不会再导致 JSON 解析报错。
“新建空配置”重置编辑草稿，不立即修改磁盘；“恢复已保存”放弃未保存编辑，重新读取 config.json。
空白编辑框直接保存会明确提示先添加源或恢复配置。若要有意清除所有源，点击“新建空配置”后再保存。
非空但不合法的 JSON 不会被自动覆盖，错误提示包含行号。

文件路径显示在配置面板顶部，可复制，或点击“打开配置目录”。首次“保存应用”成功前，config.json 可以不存在。
本机检查到 TrafficMonitor 的 global_cfg.ini 为 portable_mode=true，路径为：

```text
C:\Users\zhanghaoen\AppData\Local\Microsoft\WinGet\Packages\zhongyang219.TrafficMonitor.Full_Microsoft.Winget.Source_8wekyb3d8bbwe\TrafficMonitor\plugins\UsageBridge\config.json
```

这不是工作区 examples 中的示例，也不是 `UsageBridge.Collector.runtimeconfig.json`（后者是 .NET 运行时文件）。
切换 TrafficMonitor 的配置存储模式后，配置路径会随宿主变化，以面板显示为准。

## 从空配置添加 Codex

1. 点击“添加 Codex”；通常不用改 authPath 或其他自动生成字段。
2. 点击“测试所选源”，确认能返回数据；若提示凭据或网络错误，先按提示排查。
3. 勾选“启用采集”，点击“保存应用”。
4. 在 TrafficMonitor 的显示项目设置中勾选 UsageBridge 1 和 2，分别对应 5 小时和周剩余量。

若已有其他数据源，槽位可能不是 1/2，以添加后的提示或 views.slot 为准。
DeepSeek 需要先通过“保存凭据”存入名称 deepseek 的 API Key。
JSON/HTML/DOM 预设使用占位网址，必须修改 url、fields.path；它们不会自动推断要采集的字段。

## 三层模型

- `sources`：`id`、`kind`、`enabled`、`intervalSeconds`（5–86400）、`timeoutSeconds`（1–120），以及具体请求和提取参数。
- `metrics`：`id`、`source`、`input` 或 `expression`。`text=true` 时按文本显示 input，否则按 decimal 转换/计算。
- `views`：`slot`（1–16，不能重复）、`metric`、`label`、`format`（例如 `0.00`）和 `unit`。

`version` 必须为 2，顶层 `enabled` 为总开关。最多 32 个源、128 个指标、16 个显示槽位。
同源的所有字段共用一次采集。显示 ID 固定，修改槽位内容可保存生效；宿主中的勾选配置保持不变。

## 类型与预设

| kind | 参数与输出 |
| --- | --- |
| codex | authPath 留空：优先 CODEX_HOME/auth.json，否则用户目录 .codex/auth.json；每次重读 tokens.access_token。输出 h5Remaining、weekRemaining、h5ResetAt、weekResetAt（缺失则不生成）。默认 60 秒。 |
| deepseek | credential 为 DPAPI 凭据名称，currency 为 CNY 或 USD。输出 total_balance、granted_balance、topped_up_balance、currency、isAvailable。不会合计不同币种。 |
| http_json | url/method/headers/secretHeaders/body + fields。JSONPath 为受限子集。 |
| http_html | 同上；fields.path 为 CSS 选择器，使用 AngleSharp。静态 HTML 没有页面脚本执行。 |
| browser_dom | url、successSelector、fields。复用插件独占 profile，等待选择器与非空文本，按超时结束。 |
| demo | 本地模拟 value=42，不访问网络。 |

示例：[Codex/DeepSeek](../examples/codex-deepseek.json)、[自定义 JSON](../examples/custom-json.json)、[动态网页](../examples/browser-dom.json)。
示例默认关闭，须替换目标地址/选择器后启用。静态网页可将 browser_dom 示例的 kind 改成 http_html。

## 提取与计算

JSONPath 支持 `$`、`.name`、`[0]`、`['name.with.dots']` 的组合；不支持通配符、递归、筛选表达式或转义引号。
结果必须为非 null 标量，缺字段不会变成零。
HTML/DOM 默认要求恰好一个元素，`firstMatch=true` 明确选择首个；attribute 留空读取 textContent，其他值读取属性，动态 DOM 的 `attribute="value"` 读取控件值。
`requireNonEmpty` 默认 true。动态网页对缺元素/空文本持续等待；匹配过多与属性缺失立即报错。
`successSelector` 不为空且始终缺失时显示需要登录。

fields.steps 按顺序执行：

```json
[{"kind":"trim"},{"kind":"replace","pattern":",","replacement":""},{"kind":"regex","pattern":"([0-9.]+)","group":1}]
```

正则匹配限时 100ms；文本与替换结果最多 65536 字符。小数点固定为 `.`，不自动识别千位分隔。
表达式支持变量、四则运算、括号、abs、min、max、round；例如 `round(5000 - used, 2)`。
金额用 decimal；round 中点远离零。表达式最多 4096 字符、32 层嵌套、1024 次解析操作。
没有文件、网络、系统命令、反射、循环，也不把计算表达式送入已登录网页。

## 凭据、网页与错误

“保存凭据”把值用当前 Windows 用户 DPAPI 加密。DeepSeek 保存原始 API Key；自定义 secretHeaders 的值为凭据名称，例如 `{"Authorization":"my-token"}`，该凭据保存完整的 `Bearer ...`。
headers 禁止常见认证头明文值。分享配置前仍应检查 URL、请求体和普通字段中是否有个人信息。
测试预览仅输出提取字段、计算值和最终显示，不记录完整认证响应；认证字段及本次请求所用凭据被脱敏。

HTTP 支持系统默认代理；响应上限 2 MiB，不自动跟随重定向。需要重定向的 API 应直接配置最终 URL。当前自定义 body 默认使用 application/json，可用 headers 的 Content-Type 覆盖。
网页数据仅在 browser_dom 启用或主动网页登录时启动 WebView2；HTTP JSON/静态 HTML 不启动浏览器。
browser_dom 使用页面 GET 导航；method/headers/body 只用于 HTTP 类型，不用于模拟网页登录请求。
登录由用户在同一 WebView2 窗口手动完成；关闭登录窗口后继续采集。当前不支持弹出式 OAuth、多层 iframe、Shadow DOM 或下载。
同配置目录通过独占锁拒绝第二实例，避免 Profile 并发占用。不同源共用该 Profile 的会话，不向 HTTP 客户端导出 Cookie。

失败旧值前加 `~`，提示包含原因和最后成功时间。首次失败或字段缺失显示 `--`。重置倒计时到零不会伪造额度恢复。
关闭总开关或所有数据源后保存，将取消采集、关闭浏览器、退出程序；再打开插件设置可重新启用。
`collection.enabled` 是配置面板维护的启动提示文件。请通过面板保存配置；直接在磁盘手改 JSON 不会同步此提示。

## IPC

协议 v2 为 22544 字节：uint32 版本、uint32 PID、uint64 系统 uptime，然后 16 个固定显示记录。
每条为 label[64]、value[128]、status[512] UTF-16 字符数组，总计 1408 字节，含零终止。
快照互斥锁零等待；心跳超 5 秒显示 Stale。实例使用随机 GUID 和宿主 PID，Job 句柄只由 DLL 持有。
