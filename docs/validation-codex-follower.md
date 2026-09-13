# 跟随 Codex Desktop 启动（本地 2.4.0-local）

默认开启，右键菜单可关闭。独立用户登录项启动无窗口监听模式，2 秒轮询；识别当前用户会话内 OpenAI.Codex Windows 应用包中的 ChatGPT.exe 根进程，按进程 ID 与启动时间记录会话。忽略 CLI、普通 ChatGPT、Electron 子进程，不变更 Codex 进程或快捷方式。

桌宠单实例和监听器单实例分别受命名互斥锁保护；同一次 Codex 会话只触发一次，手动退出桌宠后不自动重新拉起。关闭开关移除专用 Run 项并结束监听，保留独立的原有开机启动选择。原存档自动获得默认开启值，显式关闭可序列化保留。

验证：Release 发布成功。43 项自检通过，包括默认值、关闭设置持久化、应用包升级识别、排除 CLI、会话变化和重复检测、真实桌面版根进程识别、真实额度读取；50 项相关界面与功能检查通过，包括新菜单开关、自动补充和每日额度显示。安装后的监听启动、单实例、手动退出与停用结果记录于 artifacts/installed-codex-follower.json。

本机验证采用正在运行的真实 Codex Desktop，不重启或中断 Codex 中的任务。登录启动项已验证；未为测试退出 Windows 会话。完整动画几何回归的原有问题仍见 validation-daily-remaining.md。
