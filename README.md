# Momo · Codex 桌宠

日系萌系 Windows 桌宠，使用 VPet 原版透明 PNG 序列动画。**160 × 100 的头侧小气泡**常驻显示 Codex 本周剩余 / 已用、credits 余额、同步状态与重置时间。

An anime-style Windows desktop companion with a compact speech bubble showing Codex weekly quota and credit balance. Built with .NET 8 / WPF; reuses the local Codex App Server sign-in. This is an independent community project.

<img src="docs/images/desktop.png" width="420" alt="Momo 头侧额度气泡，演示数据" />

上图使用合成演示数据，不含真实账号的额度信息。默认界面没有名称栏，主文字 10–13 px，辅助文字 9 px。完整窗口从旧版 340 × 552 缩为 350 × 302（逻辑像素），角色保持 256 × 256 的绘制区域；小巧模式为 308 × 238，气泡仍保持可读尺寸。

## 启动

1. 从 [GitHub Releases](https://github.com/KyonXXXX/momo-codex-pet/releases/latest) 下载 Windows ZIP 并完整解压。
2. 确保 Windows 10/11 已安装 **.NET 8 Windows Desktop Runtime（x64）**，并在 Codex 桌面版中登录 ChatGPT 账号。
3. 双击 **Momo.exe**。请将整个解压目录保留在固定位置。

从源码构建时可用 `tools/build.ps1 -DesktopShortcut` 创建 **Momo Codex 桌宠** 桌面快捷方式。

本机已安装的 .NET 8 Windows Desktop 运行时即可运行此版本。Codex 桌面版需要已登录 ChatGPT 账号。桌宠会自动找到 Codex 可执行文件，每 60 秒通过其官方 App Server 的 `account/rateLimits/read` 读取额度。也可以点击卡片上的 ↻ 立即刷新。

主面板永远显示 Codex 主额度桶；一周根据 `windowDurationMins = 10080` 识别，可以在 primary 或 secondary 中。其他额度桶（如 Spark）的信息可从「查看所有额度与同步详情」查看。

余额单位为 **credits**，不是货币；卡片保留两位小数，悬停在数字上可看精确余额。周窗口和重置时间以服务实际返回值为准，不假定每周一重置。时间采用电脑本地时区。

## 使用

| 操作 | 功能 |
| --- | --- |
| 单击角色 / ♡ 摸摸 | 摸头动画与简短互动 |
| 拖动角色或卡片空白处 | 移动桌宠，记住位置 |
| ◷ 专注 | 开始 25 分钟专注，角色陪读；再次点击结束 |
| ☾ 休息 | 休息动画，额度卡片仍显示并刷新 |
| 右键角色 / 卡片或点 ··· | 查看额度详情、置顶、大小、小巧模式、开机启动、素材说明 |
| Ctrl+Alt+M | 显示 / 隐藏桌宠（快捷键注册成功时可用） |
| 双击系统托盘图标 | 找回桌宠 |
| 托盘 → 回到屏幕右下角 | 位置恢复 |
| 菜单 / 托盘 → 退出桌宠 | 彻底退出 |

默认置顶，默认不开机启动。开机启动可在菜单中自行开关，只修改当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MomoCodexPet`。隐藏到托盘是用户主动关闭常驻显示，恢复后继续显示额度。

每周剩余不超过 15% 时，默认每个重置窗口提醒一次（每次启动重新计数），可在菜单关闭提醒。25 分钟结束会弹出系统通知；计时以实际结束时间计算，电脑休眠后也不会停在旧时间。未完成的专注不跨程序重启恢复。

## 数据与连接

- 使用本机 Codex 管理的登录状态；不读取、复制、保存登录 token，也不需要 API key。
- 只启动自己的临时 `codex app-server --stdio` 子进程，并调用 initialize、initialized、account/rateLimits/read。不会创建任务或发起模型请求。
- 每次读完结束自己的子进程，不停止用户的 Codex，不触碰其运行中的任务。
- 断网 / 读取失败保留内存中的上次数据并标记「离线」；悬停同步状态可看最近成功同步时间，详情窗口也会注明旧数据。超过两分钟的数据同样标记离线。启动时不会拿旧缓存假装新数据。
- 服务未返回的数字显示「— / 未提供」，不会伪装成余额为零。应用不会自动购买额度或兑换重置次数。
- 配置保存到 `%LOCALAPPDATA%\MomoCodexPet\settings.json`，包括位置、大小、提醒开关和完成专注次数。账号余额不写进配置。
- 若未自动发现 Codex，可设置用户环境变量 `MOMO_CODEX_PATH` 为真实的 `codex.exe` 绝对路径。也会尊重 Codex 自身使用的 `CODEX_HOME`。
- 如果读取失败，请先在 Codex 中确认 ChatGPT 账号已登录，随后点击 ↻；纯 API key 登录通常无法读取订阅周额度。

## 素材与授权

应用源代码按 [MIT License](LICENSE) 提供。**MIT 不覆盖角色素材**。

角色动画来自 [LorisYounger/VPet](https://github.com/LorisYounger/VPet)，版权所有：**虚拟主播模拟器制作组**。本应用保留原始透明 PNG 帧与原始帧时长。随附的 10 组序列共 111 帧，包含待机、摸头、卖萌、睡眠和陪读所用的过渡 / 循环动画，以及预留的行走序列。当前界面不提供自主行走。

完整动画授权见 [licenses/VPet-Animation-License.zh-CN.md](licenses/VPet-Animation-License.zh-CN.md)，程序右键菜单也提供来源与授权页。所有源路径、固定上游 commit 与逐文件 Git blob 校验值保存在 `assets/animations.json`。

素材在非商用场景下按上游的署名及链接条件使用；如改作商用或重新分发，请遵循随附原文的相应条件。动画素材不得收费出售。

接口参考：[OpenAI Codex App Server](https://learn.chatgpt.com/docs/app-server)。

## 开发与验证

使用 .NET 8 SDK，不依赖额外 NuGet UI 库：

```powershell
dotnet build -c Release
dotnet publish -c Release -o dist/Momo
.\bin\Release\net8.0-windows\Momo.exe --self-test --live
.\bin\Release\net8.0-windows\Momo.exe --capture artifacts/preview.png
.\bin\Release\net8.0-windows\Momo.exe --capture artifacts/preview.png --verify-auto-refresh
.\bin\Release\net8.0-windows\Momo.exe --capture artifacts/demo.png --demo
```

自测覆盖：primary / secondary 周窗口、主桶选择、缺失数据、精确余额、零值、异常时间与动画帧加载；`--live` 额外测试真实 Codex 额度。报告生成于 `artifacts/test-results.txt`。`--capture` 测试真实按钮路由、计时完成、显示 / 隐藏、越界位置、过期状态和缺失数据，然后使用实际 WPF 渲染及实际账号数据导出四种状态，随后退出，不保存设置。附加 `--verify-auto-refresh` 会等候真实的 60 秒刷新周期，验证程序独立更新数据。

也可运行 `tools/build.ps1 -DesktopShortcut` 完成构建、自测、发布及创建桌面快捷方式。

`--demo` 只能与 `--capture` 合用，使用固定合成数据且不读取账号，适合生成公开截图。`--capture` 和 `--live` 产生的真实数据报告仅应留在本地；`artifacts/`、用户配置、凭据文件、构建输出和本机快捷方式均排除在 Git 之外。

## 相似项目

GitHub 上已有 Codex 宠物额度显示工具，包括 Windows 的 Quota Buddy / Codex Pet Dock，以及 macOS 的额度圆环和独立额度桌宠。平台、显示方式及信息差异见 [相似项目检索](docs/similar-projects.md)。

## 素材维护

开发时若需重新下载素材，先将固定 commit 的 GitHub tree API 响应保存为 `vpet-tree.json`，上游 README 保存为 `vpet-readme.md`，再运行 `node tools/download-assets.mjs`。该工具校验每张图的 Git blob SHA-1。

发布文件：`dist/Momo/Momo.exe`；分发时请保留同目录的全部运行文件、`assets` 和 `licenses`。
