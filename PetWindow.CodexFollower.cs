using System;
using System.IO;

namespace Momo;

public partial class PetWindow
{
    private void InitializeCodexFollower()
    {
        try { CodexFollower.Configure(_settings.FollowCodexDesktop); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        { Say("跟随启动设置未能应用，请通过右键菜单重新开启。", false); }
    }

    private void ToggleCodexFollower()
    {
        bool enabled = !_settings.FollowCodexDesktop;
        try
        {
            CodexFollower.Configure(enabled);
            _settings.FollowCodexDesktop = enabled; _settings.Save();
            Say(enabled ? "已开启跟随 Codex Desktop 启动。" : "已关闭跟随 Codex Desktop 启动。", false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        { Say("跟随启动设置失败，请稍后重试。", false); }
    }
}
