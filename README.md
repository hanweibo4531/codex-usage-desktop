# Codex Usage Desktop

轻量的 Windows 原生 Codex 用量面板。深色科技风、青蓝额度条、实时账户额度与本机 Token 统计，尽在一个桌面小窗口。

**Windows 10 / 11 · C# / WPF · 免安装 · MIT License**

> 非 OpenAI 官方产品。使用本机 Codex 的登录状态读取额度；本机日志统计不等于账户账单。

## 功能

- 账户多额度池、剩余百分比、下次重置时间及可用重置次数（仅展示）。
- 今天 / 近 7 天的用量记录、输入缓存占比、Token 总量和活动图表。
- 模型筛选、最近 8 条记录和当前筛选范围的 CSV 导出。
- 每 60 秒自动刷新，查询失败时标记带时间的日志快照。
- 窗口置顶、系统托盘、手动刷新与可配置数据目录。
- 深蓝黑背景、青蓝强调、紫色缓存指标、等宽数字和紧凑数据表。

## 下载与运行

在本仓库 **Releases** 下载 `CodexUsage-Windows.zip`，解压后双击 `CodexUsage.exe`。无需 Node.js、Python 或 .NET SDK。

运行条件：Windows 10 / 11、.NET Framework 4.5 或以上，以及已经安装并登录的 Codex。本机历史统计需要 Codex 已生成会话日志。

程序会从 PATH 和 Codex 桌面应用安装目录查找 `codex.exe`。也可在“设置 → 指定 codex.exe”中选择。数据目录默认使用 `CODEX_HOME`，未设置时使用 `%USERPROFILE%/.codex`。

| 操作 | 功能 |
| --- | --- |
| 拖动标题栏 | 移动窗口 |
| 置顶 | 保持在其他窗口前面 |
| — | 收起到系统托盘 |
| 托盘左键单击 | 恢复面板 |
| × / 托盘退出 | 完全退出 |
| 今天 / 近 7 天 | 切换本机统计范围 |
| 模型下拉框 | 筛选统计、图表和列表 |
| 导出 CSV | 导出当前时段与模型的全部记录 |

默认不会设置开机启动。配置保存在 `%LOCALAPPDATA%/CodexUsage/settings.json`。

## 从源码构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建脚本使用 Windows 自带的 .NET Framework C# 编译器。输出在 `dist/`；构建不依赖 NuGet，也不下载第三方运行时。

```powershell
# 回归测试：不访问真实账号或日志
$test = Start-Process .\dist\CodexUsage.exe --self-test -Wait -PassThru
Get-Content .\dist\self-test.txt
if ($test.ExitCode -ne 0) { throw 'Tests failed' }

# 可选：真实连接诊断，结果仅保存在本地
Start-Process .\dist\CodexUsage.exe --diagnose -Wait
```

GitHub Actions 在推送和 Pull Request 时构建、运行解析检查，并提供 Windows 构建产物。

## 数据来源与统计口径

账户额度来自 [Codex App Server](https://learn.chatgpt.com/docs/app-server) 的 `account/rateLimits/read`。可用重置次数只展示，本程序不会消耗重置次数或发起模型任务。

本机统计读取 `sessions/` 和 `archived_sessions/` 下的 JSONL 文件：优先使用 `token_usage_record`，按 `response_id` 去重；旧格式采用 `token_count` 累计值的增量。同一文件不同时累计两种格式。按本地时区划分日期。

Token 总量包括缓存输入。缓存占比 = 缓存输入 Token / 全部输入 Token。用量记录数不是完整 API 调用数；列表圆点仅表示有用量记录。未写入日志的请求、缺失日志和其他设备用量不包含在本机统计内。日志格式可能随 Codex 更新而改变。

不提供无法可靠获取的请求成功率、单次请求耗时或 CPA Keeper 数据。实时查询失败时显示历史快照；没有数据时显示未知。

## 隐私

应用不读取或保存 `auth.json`，认证由本机 Codex 进程处理。不上传对话、不收集遥测，也不向项目作者发送数据。CSV 只包含时间、模型和用量数字。实时额度查询需要 Codex 连接其服务。

诊断文件可能包含用量和套餐信息，请勿直接贴到公开 Issue。构建产物、导出文件、诊断输出、配置与会话目录已由 `.gitignore` 排除。

## 项目结构

```text
App.cs                  数据解析、额度客户端和桌面交互
Main.xaml               深色科技风 WPF 界面
app.manifest            Windows 权限与 DPI 声明
build.ps1               本地构建脚本
.github/workflows/      Windows 自动构建与检查
LICENSE                 MIT 许可证
```

## 贡献与许可证

欢迎通过 Issue 和 Pull Request 反馈问题或提交改进。提交前请运行构建和解析检查，确保不包含个人日志、凭证、配置或真实账号截图。

本项目采用 [MIT License](LICENSE)。