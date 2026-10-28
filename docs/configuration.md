# 配置 v2

配置位于宿主传入的插件配置目录的 `UsageBridge/config.json`。
DataPane 从 0.3.7 起采用新名称；配置路径和内部显示项 ID 保留 UsageBridge 命名，以兼容旧版，无需迁移配置或密钥。
旧生命周期原型 v1 配置不兼容；发布前原型只使用临时测试配置，没有迁移用户数据。
配置面板提供数据源列表和分类表单，不提供内置 JSON 编辑。复杂规则在外部编辑 config.json。
“测试数据”使用未保存的改动；“更新数据”作用于后台配置。添加源后勾选启用并保存应用。

## 0.3.3 的草稿操作与位置

点击“添加数据”，选择类型后填写表单。删除数据源会同时删除其指标和显示项，点击保存后才写入磁盘。
“加载配置”经确认后放弃未保存编辑，重新读取 config.json。关闭窗口时可选择保存、放弃或取消。
外部文件为空白或不合法的 JSON 时，重新读取会报错，不会覆盖当前草稿；解析错误包含行号。
表单修改会保留现有请求头、多个字段、正则及计算公式。新增字段、指标和公式请使用外部编辑器。
处理金额时可勾选“移除货币符号 ¥”“移除千位逗号”；当前处理步骤按顺序执行，其他币种仍需自定义规则。
“作为文本显示”跳过数字转换和表达式，直接显示输入字段。数字格式和单位可在显示表单中调整。

点击“更多 → 打开配置目录”访问 config.json，面板不显示路径。首次“保存配置”成功前，config.json 可以不存在。
外部编辑并保存后，点击“加载配置”读入草稿，再点击“保存配置”更新后台；重新读取文件本身不会更新后台采集。
如果文件在外部发生变化，直接保存旧草稿会被拒绝，并提示先重新读取，避免覆盖外部修改。
本机检查到 TrafficMonitor 的 global_cfg.ini 为 portable_mode=true，路径为：

```text
C:\Users\zhanghaoen\AppData\Local\Microsoft\WinGet\Packages\zhongyang219.TrafficMonitor.Full_Microsoft.Winget.Source_8wekyb3d8bbwe\TrafficMonitor\plugins\UsageBridge\config.json
```

这不是工作区 examples 中的示例，也不是 `UsageBridge.Collector.runtimeconfig.json`（后者是 .NET 运行时文件）。
切换 TrafficMonitor 的配置存储模式后，配置路径会随宿主变化，以“打开配置目录”实际打开的位置为准。

## 第一排操作

| 控件 | 用途 |
| --- | --- |
| 添加数据 | 选择类型并添加到草稿，保存后才应用。 |
| 保存配置 | 保存所有表单修改，更新后台采集和启用开关。 |
| 测试数据 | 仅测试选中的源，使用未保存内容；成功或失败结果在独立弹窗显示，不保存配置。 |
| 更新数据 | 请求立即刷新所有已应用且启用的数据源；不保存草稿，不重新读取文件。已有请求尚未结束时不会重叠请求。 |
| 自动更新 | 后台定时采集的总开关，保存配置后生效；关闭后仍可手动测试。每个源还有独立开关。 |
| 更多 → 加载配置 | 从磁盘读取 config.json，放弃草稿前会确认；外部修改读入后仍需保存配置。 |
| 更多 → 配置密钥 | 按名称新增或替换密钥，保存后供下次请求使用；不保存表单配置，不提供凭据列表或删除功能。 |
| 更多 → 打开配置目录 | 打开配置文件所在文件夹，供外部编辑。 |

鼠标悬停第一排控件可查看说明。测试结果窗口关闭即释放，不在主面板持久展示。

## 从空配置添加 Codex

1. 点击“添加数据”并选择 Codex；通常不用改凭据文件路径或其他自动生成字段。
2. 点击“测试数据”，确认能返回数据；若提示凭据或网络错误，先按提示排查。
3. 勾选“自动更新”，点击“保存配置”。
4. 在 TrafficMonitor 的显示项目设置中勾选 Codex 5h 和 Codex 周，分别对应 5 小时和周剩余量。

TM 项目名直接使用 views.label（表单中的“显示名称”），不附加编号；名称可以重复，内部 ID 仍固定绑定槽位。显示名称为空时使用“未命名”。0.3.6 只注册已配置显示项；未配置槽位不在列表中。禁用数据源或关闭自动更新不删除其显示项。
TM 在加载插件时建立列表，插件在本次运行中保持注册项目和对象地址不变。新增、删除或调整显示项槽位后，保存配置并重启 TM；删除项在重启前可能暂时显示为“未配置”。仅改名会在约 1 秒内更新插件返回的名称，关闭并重新打开 TM 显示设置即可看到。
`display-names.bin` 是自动生成的名称缓存，不含凭据；启动采集程序和保存配置时重建。关闭自动更新后，该缓存仍支持 TM 在重启时读取名称。不要手工修改缓存，外部编辑 JSON 后仍需加载并保存配置。
若缓存缺失或损坏，首次枚举不会注册空占位项；请打开插件选项保存配置并重启 TM。即使没有注册显示项，仍可从插件管理打开选项。
DeepSeek 在其表单中填写 API Key，点击“加密保存 API Key”后测试。自定义认证头使用“配置密钥”。
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
