# 环境与依赖

检查日期：2026-10-28；工作目录 `C:\Users\zhanghaoen\Documents\TMP`。

| 项目 | 已核实值 |
| --- | --- |
| Windows | 10.0.26200.0 |
| TrafficMonitor | 1.8.6.0，PE Machine 0x8664 / x64 |
| 安装目录 | `C:\Users\zhanghaoen\AppData\Local\Microsoft\WinGet\Packages\zhongyang219.TrafficMonitor.Full_Microsoft.Winget.Source_8wekyb3d8bbwe\TrafficMonitor` |
| Visual Studio Build Tools | 2022 / 17.14.42 |
| MSVC | 工具目录 14.44.35207，编译器 19.44.35229.0 |
| Windows SDK | 10.0.26100.0 |
| 实际使用的 CMake | Visual Studio 附带 3.31.6-msvc6 |
| .NET SDK / runtime | 10.0.403 / 10.0.14 |
| WebView2 Runtime | 155.0.4283.62；另有 151.0.4129.107 目录 |
| 插件接口 | TrafficMonitor V1.86 标签，API 7 |
| AngleSharp | 1.8.6 / MIT |
| Microsoft.Web.WebView2 SDK | 1.0.4314.35 / 包附带许可证 |

初次接手时无开发工具，用户安装后本会话 PATH 未刷新；构建脚本会定位标准 .NET 安装与 VS 自带 CMake。
WinGet 独立 CMake 目录对本沙箱不可读，使用已安装的 VS CMake 即可，不需重复安装。
MSBuild 默认临时目录和 NuGet 用户目录受限，脚本把 TEMP、APPDATA、DOTNET_CLI_HOME 和 NuGet 缓存限定到工作区 artifacts，设置仅作用于构建子进程。
依赖首次下载由用户在正常 PowerShell 执行恢复脚本完成，之后可用本地缓存构建。

## 架构差异与依据

沿用 C++ DLL + .NET Windows Forms。选择 16 个永久槽位及稳定 ID `UsageBridge.Slot.1` 至 `.16`，配置可更换标签和指标，无需替换宿主保留的对象指针。主窗口/任务栏自行选择要显示的槽位。
共享内存快照替代命名管道传值；命名事件用于配置请求。DLL 无后台线程、无网络/COM 初始化；DataRequired 的共享锁使用零等待。
Job 句柄不继承，启用 KILL_ON_JOB_CLOSE，使用 PROC_THREAD_ATTRIBUTE_JOB_LIST 在创建进程时纳入 Job。
DllMain 卸载只关闭 Job，不等待异步线程。实际网页/请求取消由采集程序正常关闭路径负责。

宿主 V1.86 先传入 EI_CONFIG_DIR，再调用 OnInitialize。保持官方接口声明，不按 master 修改 ABI。
接口及许可证已放在 third_party/TrafficMonitor；来源与本地哈希一并记录。
不引入任意脚本或表达式库，计算器采用有长度、深度和操作数上限的独立十进制解析器。

## 来源

- [TrafficMonitor V1.86](https://github.com/zhongyang219/TrafficMonitor/tree/V1.86)
- [Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [WebView2 线程模型](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/threading-model)
- [DeepSeek 余额接口](https://api-docs.deepseek.com/api/get-user-balance/)
- [AngleSharp 1.8.6](https://www.nuget.org/packages/AngleSharp/1.8.6)
- [WebView2 SDK 1.0.4314.35](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35)

Codex 用量端点不是承诺稳定的公开 API，逻辑集中于 Fetcher/Presets，便于替换。当前不主动刷新 OAuth，只读取用户已有登录文件。
