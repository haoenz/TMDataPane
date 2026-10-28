# TrafficMonitor 通用用量与网页数据插件实施计划

交接日期：2026-10-28。面向在 Windows 环境中接手的新 Codex。

目标是开发一个 TrafficMonitor 插件，合并 Usage 专用插件和通用 HTTP 数据抓取插件的能力：提供 Codex 用量、DeepSeek API 余额预设，支持自定义 JSON 和 HTML 数据提取，对提取结果进行处理与运算，再显示到任务栏或主窗口。

本文件记录已确认需求、建议架构、实施顺序与验收条件。目前只完成了需求讨论和公开源码核对，尚未创建项目代码、安装依赖、编译或进行 Windows 运行测试。请先核实 Windows 工具链和目标 TrafficMonitor 版本，再按此计划实施；不要把计划中的示例语法当成已经实现的接口。

## 已确认的需求与范围

| 项目 | 用户已确认的要求 |
| --- | --- |
| 内置预设 | 首批提供 Codex 用量、DeepSeek API 余额 |
| 通用 JSON | 保留类似 TMFetchPlugin 的 URL、方法、请求头、请求体、刷新间隔、超时与 JSON 字段提取能力 |
| HTML | 支持通过选择器提取指定元素；兼顾静态 HTML 和 JavaScript 渲染后的 DOM |
| 登录 | 最小实现即可：打开内嵌网页让用户手动登录，保存并复用会话；不预先解决拒绝嵌入式浏览器等特殊情况 |
| 后处理 | 支持文本处理、数字转换、运算及格式化，而非只能显示原始字段或元素 |
| 自动启动 | 打开 TrafficMonitor 且插件已启用时，自动完成采集到显示的链条，不要求用户手动启动额外程序 |
| 自动退出 | 关闭 TrafficMonitor 或实际卸载插件后，不得留下继续采集的后台程序 |
| 禁用插件 | 用户明确接受 TrafficMonitor 自带的禁用操作需要重启生效 |
| Windows 迁移 | 用户准备在 Windows 安装 Codex CLI，由新会话核实环境并实施 |

用户接受“随插件自动管理的采集子进程”。这不意味着需要安装独立 Windows 服务、配置开机任务或让用户启动一个常驻服务。

第一版不要求复杂账号管理、验证码自动化、浏览器指纹处理、拒绝内嵌浏览器的站点兼容、浏览器扩展、任意网页自动操作、多层 iframe 或 Shadow DOM 支持。遇到真实需求再扩展，不把这些内容作为开始开发的阻碍。

## 已核实的现有项目行为

本节基于讨论时读取的公开源码，Windows 实施前应记录实际采用版本或 commit，必要时复核。

### TMFetchPlugin

- 支持通用 HTTP 请求、静态请求头、JSONPath 子集、字符串模板、多个显示项和刷新间隔。
- 显示模板只替换字段，不执行算术。`100 - $.used_percent` 会显示类似 `100 - 20` 的文本，不会得到 `80`。
- 没有直接从 Codex 的本地 `auth.json` 动态读取令牌的配置能力。
- 通用 HTTP 抓取和模板思路可以复用，但需要增加凭据解析、HTML 提取和独立计算层。

### CodexUsage Plugin

- 每次采集读取 `auth.json` 中的 `tokens.access_token`。
- 默认路径优先采用 `%CODEX_HOME%\auth.json`，否则为 `%USERPROFILE%\.codex\auth.json`；支持显式文件路径。
- 调用 `https://chatgpt.com/backend-api/wham/usage`，请求头包含 `Authorization: Bearer <access_token>`。
- 从 `rate_limit.primary_window` 和 `secondary_window` 取数据，按 `limit_window_seconds` 区分窗口：18000 秒为 5 小时，604800 秒为一周。不要把 primary/secondary 与窗口类型永久绑定。
- 将 `used_percent` 限制在 0～100 后计算剩余比例，读取重置时间并展示提示。
- 读取文件中的新令牌不等于主动刷新 OAuth 令牌。第一版不新增登录协议实现，令牌过期后提示用户通过 Codex 恢复登录。
- 上述接口来自现有第三方插件实现，不应假设为有稳定兼容性保证的公开 API。将相关逻辑集中在可替换适配器中。

### TrafficMonitor 生命周期

- 当前源码的 `OnPluginDisable()` 修改禁用配置并提示重启，不立即卸载 DLL，也不在该操作中通知插件停止。
- 加载阶段跳过已禁用的插件；插件管理器析构时调用 `FreeLibrary`。
- 用户已接受禁用后重启，因此第一版不修改 TrafficMonitor，也不通过钩子或轮询宿主内部配置模拟即时禁用。
- 不把“隐藏显示项”当成“卸载插件”。隐藏后的采集策略可另行设置，但不影响退出时必须停止采集的要求。

## 建议架构

已讨论的方向是轻量 C++ DLL 加独立采集程序。具体框架版本、项目名和表达式库尚未锁定，可由 Windows 会话结合环境选择。

| 组件 | 建议技术 | 职责 |
| --- | --- | --- |
| TrafficMonitor 插件 | 原生 C++ DLL | 接入插件 ABI、注册显示项、显示缓存、启动配置窗口、管理采集进程生命周期 |
| 采集程序 | C#/.NET Windows 程序 | 配置界面、HTTP、HTML、浏览器、凭据读取、表达式、调度与结果缓存 |
| 静态 HTML | AngleSharp | HTML 解析和 CSS 选择器 |
| 动态网页 | WebView2 | 手动登录、持久化会话、读取渲染后的 DOM |
| 通信 | 本地命名管道等轻量 IPC | 配置命令和结果快照；不开放常驻 HTTP 服务作为必要依赖 |

原生 DLL 的架构必须匹配 TrafficMonitor。采集进程通过 IPC 与 DLL 通信，不受同一进程 ABI 位数约束，但发布包仍需匹配目标 Windows 平台。

WebView2 在采集程序的 STA UI 线程和消息循环中运行。网络与解析采用异步机制；TrafficMonitor 的绘制、取值回调只读取最新缓存，不同步等待 HTTP、网页加载或表达式运算。

DLL 中尽量减少自建长生命周期线程。若采用异步 IPC 线程或回调，必须明确其取消和卸载安全性，不得留下执行已卸载 DLL 代码的线程。必要时将通信方案调整为宿主回调中可快速读取的快照机制。

## 自动启动与退出机制

### 用户可见行为

| 操作 | 预期行为 |
| --- | --- |
| 启动 TrafficMonitor，插件及数据源已启用 | 自动启动一个对应插件实例的采集进程并开始采集 |
| 打开插件配置 | 使用同一实例的配置界面，不重复启动采集进程 |
| 关闭配置窗口或登录窗口 | 按配置继续采集 |
| 在插件设置中关闭“启用采集” | 停止调度、取消请求、关闭浏览器、退出采集进程；DLL 保留已停止状态 |
| 再次打开配置以启用采集 | 按需启动配置程序，启用后恢复采集 |
| 没有启用的数据源 | 不启动后台采集；用户主动打开配置窗口时可临时运行配置程序 |
| 退出 TrafficMonitor | 采集程序及本实例所属浏览器进程结束 |
| TrafficMonitor 崩溃或被强制结束 | 系统级兜底清理采集进程树 |
| 实际卸载 DLL | 结束其拥有的采集进程树 |
| 在 TrafficMonitor 中禁用插件并重启 | 旧实例退出，新实例不启动该插件及采集程序 |

### 进程所有权

使用 Windows Job Object 管理采集进程及其子进程，设置 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`。

- Job 管理句柄由插件独占持有，不继承或复制给采集进程，否则宿主退出时可能无法触发清理。
- 在子进程开始实际工作前将其纳入 Job。优先核实目标 Windows 对创建时分配 Job 的支持；若使用挂起创建再分配，必须处理分配失败和启动中断，确保不留下失控进程。
- 正常停止优先发送停止命令，取消采集并关闭浏览器；对无响应进程使用 Job 兜底。
- 实际 DLL 卸载路径只做安全的最小句柄清理，不在 `DllMain` 内等待 IPC、网络、线程或浏览器退出。
- 不在 `DllMain` 内启动子进程或初始化 COM；在取得必要配置之后的正常插件回调中初始化。
- 子进程可额外观察宿主进程句柄或 IPC 连接作为辅助退出信号，但不能仅依赖父进程 PID 轮询或正常退出消息。
- 每个宿主实例拥有自己的采集进程和 IPC 标识。第一版不做跨 TrafficMonitor 实例共享采集服务。
- 浏览器数据目录归本插件所有。避免不同活跃采集实例错误共享同一目录；可拒绝第二实例占用，而不是复杂化为共享服务。
- 实测 WebView2 进程树与 Job 的兼容性及退出结果。不能仅凭采集 EXE 已退出就宣称没有浏览器残留。

## 数据模型与配置

将配置分为三层，避免把每个显示项目都实现成一次独立抓取。

1. `sources`：请求或网页、凭据、提取规则、刷新间隔和超时。
2. `metrics`：输入字段、类型转换、表达式和单位。
3. `views`：任务栏文字、鼠标提示和数值格式。

一次 Codex 请求同时生成多个窗口指标；一个 HTML 页面中的多个元素也应共享一次页面采集。显示项使用稳定 ID，修改配置时保持 TrafficMonitor 缓存的对象和指针有效。对新增、删除显示项，如果宿主不支持安全热更新，第一版明确要求重启；已有数据源参数尽量支持应用后生效。

推荐用带版本号的 JSON 配置，而不是继续扩展平面 INI。凭据使用引用，不把真实令牌放进可分享的预设；API Key 使用 Windows 凭据存储或 DPAPI 等机制。Codex 配置只保存令牌文件路径。日志和测试预览对认证头脱敏。

### 数据源类型

| 类型 | 场景 | 提取 |
| --- | --- | --- |
| `http_json` | JSON API | JSONPath，明确支持的子集或所用库语义 |
| `http_html` | HTTP 响应中已有目标内容 | CSS 选择器和文本或属性读取 |
| `browser_dom` | JS 渲染或登录后网页 | WebView2 中的 DOM 选择器 |

保留方法、请求头、请求体、间隔、超时等基础选项。默认代理可采用系统设置；是否补齐手动代理与直连作为实施时的范围选择，不影响核心功能。

静态和动态 HTML 先支持 CSS 选择器，读取 `textContent`、指定属性；动态 DOM 可增加控件 `value`。匹配不到与匹配过多应有明确错误或明确的单值/集合选择规则，避免默默取错值。不要用正则代替 HTML 解析。

动态页面支持等待选择器出现、目标文本非空及超时。导航完成不代表业务数据已经加载。第一版无需“点选元素生成选择器”工具，允许手动填写选择器并测试。

### 最小登录实现

- 第一版提供一个持久化浏览器 Profile，用户在 WebView2 窗口中手动登录。
- 后续采集复用该会话；会话失效时提示重新登录，用户主动打开登录窗口。
- 不做自动输入密码、验证码处理或第三方 OAuth 协议适配。
- 不把浏览器 Cookie 自动导出到 HTTP 客户端作为默认路径。
- 登录状态的最终判断以配置的成功标志和目标数据是否可提取为准，避免把登录页 HTTP 200 当成采集成功。
- 仅使用 JSON 或静态 HTML 数据源时不启动 WebView2；运行浏览器的数据源关闭后释放不再需要的浏览器资源。

### 后处理

第一版至少支持：去空白、替换、正则捕获、数字转换、四则运算、括号、舍入和数字格式化。条件、集合筛选、时间格式化可以通过少量内置函数补齐实际预设需要，不先实现庞大的脚本平台。

流程为“原始响应 → 提取命名字段 → 类型转换 → 指标计算 → 显示格式化”。网页脚本只负责读取 DOM，计算在与页面分开的表达式环境执行，不把用户表达式直接送入已登录网页执行。

选择可限制函数和执行资源的表达式库；不开放系统命令、文件、网络或任意宿主反射能力。表达式和正则设置超时或执行限额。金额使用十进制数，并保留币种。明确千位分隔和小数点的解析规则。

空值、提取失败、非法数字、除零和超时必须产生可识别状态，不能自动变成余额为零或用量为零。

## 首批预设

### Codex 用量

- 用户选择 `auth.json` 路径或使用约定的默认路径。
- 每次采集读取当前 `tokens.access_token`，向已有插件采用的用量接口发起请求。
- 根据窗口时长识别 5 小时和周窗口，生成剩余百分比。
- 保留 `reset_at`，用本地时钟计算重置倒计时，无需为倒计时额外请求网络。
- 缺少窗口显示不可用，不制造默认零值。倒计时到零不代表配额已自动恢复，下一次成功请求再更新用量。
- 文件不可读、缺少令牌、HTTP 401、响应格式变化分别报告。
- 建议初始刷新周期为 60 秒，可配置。

### DeepSeek API 余额

- 官方接口：`GET https://api.deepseek.com/user/balance`，使用 API Key 的 Bearer 认证。
- 读取 `balance_infos` 中的 `currency`、`total_balance`、`granted_balance`、`topped_up_balance`。
- 金额字段是字符串，显式转换为十进制金额。
- 按币种选择记录，不假设数组第一项固定为 CNY，也不直接合计不同币种。
- 默认展示总余额，可选择展示赠送和充值余额；保留 `is_available` 状态。
- 刷新周期可配置，不要求高频轮询。

## 状态与最小配置界面

每个数据源保留当前状态、最后成功时间、最后成功值和简明错误。失败时允许保留旧值，但必须标记为过期，避免让用户误认为仍是实时数据。

建议最小状态集：初始化、正常、已停止、数据过期、需要登录、请求失败、提取失败、计算失败。

配置界面至少包含：

- 添加 Codex、DeepSeek 或自定义数据源。
- 编辑请求参数、选择器或 JSONPath、计算表达式和显示模板。
- 启用采集、立即刷新、保存应用。
- 打开网页登录窗口。
- 测试配置并展示原始响应或选中文本、提取值、计算值、最终显示；认证信息脱敏。

第一版不要求元素点选器、复杂图表、历史数据库、通知系统或云同步。

## 实施步骤与交付物

### 第一步 核实 Windows 环境并初始化工程

- 读取工作目录内适用的 `AGENTS.md`，保留用户已有文件。
- 确认目标 TrafficMonitor 路径、版本及 x86/x64 架构；环境能查到的先自行检查，缺失且确实阻碍工作时再询问用户。
- 检查 MSVC C++ 构建工具、Windows SDK、CMake、.NET SDK、WebView2 Runtime。
- 选择与环境兼容的稳定目标框架，记录工具链版本。
- 核对目标版本的插件接口、初始化回调和配置目录传递方式。
- 建立项目结构和 Windows 构建脚本；记录引用的第三方版本与许可证。没有必须发布或推送远程仓库的要求。

建议目录结构，允许合理调整：

```text
src/
  Plugin.Native/
  Collector.Core/
  Collector.Windows/
tests/
  Core.Tests/
  Fixtures/
scripts/
  build.ps1
  package.ps1
docs/
  configuration.md
  testing.md
```

### 第二步 先打通宿主生命周期

- 创建最小 DLL 和采集程序，用模拟值完成自动启动、IPC 与任务栏显示。
- 完成 Job Object 所有权与退出逻辑。
- 验证正常退出、强杀宿主、禁用后重启、重复打开配置不重复启动。
- 确认无启用数据源时不运行后台采集。

退出可靠性是核心需求，不能留到所有功能完成后才首次测试。

### 第三步 完成 JSON 与 Usage 预设

- 实现配置模型、HTTP 客户端、调度、提取、计算、缓存和状态。
- 实现 Codex 和 DeepSeek 适配及基础配置界面。
- 用脱敏 fixture 覆盖成功、缺字段、认证失败和格式变化；有用户本地凭据时再进行真实接口验证。

### 第四步 加入 HTML 与最小浏览器会话

- 静态 HTML 使用 CSS 选择器提取，并复用现有计算与展示层。
- 实现 WebView2 手动登录、Profile 持久化、等待选择器和 DOM 提取。
- 先用本地受控测试页验证动态渲染、登录与会话复用，再选择一个实际目标网页验证。
- 不因尚未提供真实目标网站而暂停全部开发；特殊站点兼容不属于第一版前置条件。
- 补测关闭宿主后的 WebView2 子进程清理。

### 第五步 打包与 Windows 验收

- 发布包包含匹配架构的 DLL、随附采集程序及必要依赖说明。
- 采集程序放在专属子目录，避免依赖 DLL 被 TrafficMonitor 当成插件扫描。
- 提供一键构建与打包脚本、安装方法、配置说明和排错信息。
- 在目标 TrafficMonitor 中实际加载并检查显示、配置、登录、停止和退出行为。
- Windows CI 是可选补充，不替代本机验收，也不默认授权创建远程仓库或发布 Release。

## 验收清单

- [ ] 打开 TrafficMonitor 后无需手动启动其他程序即可看到采集结果。
- [ ] Codex 正确显示 5 小时和周剩余比例，窗口顺序变化不影响识别。
- [ ] DeepSeek 正确显示选定币种余额，保留金额精度。
- [ ] 自定义 JSON 请求及字段提取可配置并测试。
- [ ] 静态 HTML 能按选择器读取字段并参与计算。
- [ ] 动态 DOM 在延迟加载后可提取，等待超时有明确提示。
- [ ] 手动登录后可采集；正常重启后持久化会话在站点允许时可复用。
- [ ] 计算示例 `5000 - 1234` 得到 `3766`；非法值与除零不显示为正常数据。
- [ ] 一次抓取可供多个显示项使用，不为每个字段重复发请求。
- [ ] 慢请求不冻结 TrafficMonitor，失败旧数据有过期标记。
- [ ] 重复打开配置不会产生重复采集进程。
- [ ] 关闭采集开关后取消调度并结束后台采集，重新启用能恢复。
- [ ] 正常退出 TrafficMonitor 后采集 EXE 及所属 WebView2 进程全部结束。
- [ ] 强制结束 TrafficMonitor 后不会留下继续采集的进程。
- [ ] 真实 DLL 卸载路径不阻塞、不死锁，也不留下执行已卸载代码的线程。
- [ ] 禁用插件并重启后，不加载插件、不启动采集。
- [ ] 无启用数据源且配置窗口已关闭时，没有采集进程。
- [ ] 发布包在声明的目标架构上可安装运行，凭据不会写入日志或可分享示例。

真实 DLL 热卸载若目标 TrafficMonitor 不提供入口，可用小型测试宿主验证；不为此修改用户的 TrafficMonitor。第一版只承诺实际验收过的平台与版本。

## 环境交接与执行说明

原会话运行于 WSL2 Debian 13。工作目录对应 Windows 的 `C:\Users\zhanghaoen\Documents\TMP`，WSL 路径为 `/mnt/c/users/zhanghaoen/documents/tmp`。请在 Windows 新会话中确认实际路径。

原环境检查到 Git 和 G++，PATH 中未发现 CMake、MinGW、.NET SDK 或 Wine。虽然能定位 Windows 的 `cmd.exe` 和 PowerShell，但调用 `cmd.exe` 返回 `UtilBindVsockAnyPort: socket failed 1`。这些是原会话的环境事实，不代表 Windows 新环境也缺少工具。

截至本次交接，工作目录没有项目实现代码，本计划文件是交付物；不要假设已有构建成果。用户当前请求是总结并落盘，以便 Windows 上的新 Codex 确认与实施，本次没有开始功能开发。

接手时先核实本计划与工具链，简要说明采用的具体方案和必要差异。用户确认开始实施后，按阶段完成，不重复询问已确定的“需要重启才能禁用”等偏好。普通可逆实现选择可自行决定；只有实际缺失的关键条件才需要用户补充。

报告进度时区分“代码已完成”“构建通过”“自动测试通过”“Windows 实机通过”，不要将它们混为一谈。保留构建命令和测试结果，列出真实尚未验证的站点或场景。

## 参考资料

- [TrafficMonitor 插件下载页](https://gitee.com/zhongyang219/TrafficMonitorPlugins/blob/main/download/plugin_download.md)
- [TrafficMonitor 插件开发指南](https://github.com/zhongyang219/TrafficMonitor/wiki/插件开发指南)
- [TMFetchPlugin 说明](https://github.com/bestK/TMFetchPlugin/blob/main/README.md)
- [TMFetchPlugin 模板实现](https://github.com/bestK/TMFetchPlugin/blob/main/src/JsonPath.cpp)
- [CodexUsage 说明](https://github.com/HCLonely/TrafficMonitor_Codex_Plugin/blob/main/README.md)
- [CodexUsage 采集实现](https://github.com/HCLonely/TrafficMonitor_Codex_Plugin/blob/main/src/CodexUsageFetcher.cpp)
- [CodexUsage 窗口解析与剩余量计算](https://github.com/HCLonely/TrafficMonitor_Codex_Plugin/blob/main/src/CodexUsageCore.cpp)
- [TrafficMonitor 插件管理及卸载](https://github.com/zhongyang219/TrafficMonitor/blob/master/TrafficMonitor/PluginManager.cpp)
- [TrafficMonitor 禁用操作](https://github.com/zhongyang219/TrafficMonitor/blob/master/TrafficMonitor/PluginManagerDlg.cpp)
- [DeepSeek 官方余额接口](https://api-docs.deepseek.com/api/get-user-balance/)
- [AngleSharp](https://github.com/AngleSharp/AngleSharp)
- [WebView2 用户数据目录](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder)
- [WebView2 页面脚本执行](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/javascript)
- [WebView2 线程模型](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/threading-model)
- [Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [Windows DLL 最佳实践](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-best-practices)
