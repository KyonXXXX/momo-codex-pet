# VPet 来源与改动说明

上游：LorisYounger/VPet — https://github.com/LorisYounger/VPet

固定版本：`2e99a42ebeff71d792118f2e8de744b773042f8d`

- 官方核心包默认角色的完整 PNG 动画及食物图片由原始 Git blob 校验，图片本身未修改。文件按 SHA-1 去重后存入 `assets/vpet-full/`，原目录与校验值保留在 `assets/vpet-catalog.json`。
- `pet/vup.lps`、各分层动画 `info.lps`、`food/*.lps` 的配置转为 JSON，保留物品数值、说明、动作时长、工作类型及分层动画轨迹。原始配置适用上游 Apache-2.0 许可，全文随附。
- Momo 的运行逻辑重新实现：独立互动面板、计时结算、动作中断、按需动画缓存、Codex 额度信息和透明窗口边界。未打包上游软件程序或 Steam 平台服务。
- 动画与物品图片适用 `VPet-Animation-License.zh-CN.md`，不能以 Momo 源码的 MIT 许可重新授权，也不能收费分发。
- Momo 是独立社区项目，不代表 VPet 或 OpenAI 官方。
