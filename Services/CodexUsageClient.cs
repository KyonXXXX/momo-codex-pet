using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Momo;

public sealed class CodexUsageClient
{
    public static string? FindExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("MOMO_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        // Prefer the newest installed desktop binary to survive Codex auto-updates.
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(installed))
        {
            var file = Directory.GetFiles(installed, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (file is not null) return file;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var file = Path.Combine(dir.Trim('"'), "codex.exe"); if (File.Exists(file)) return file; } catch (ArgumentException) { }
        }
        return null;
    }

    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var exe = FindExecutable() ?? throw new UsageException("未找到 Codex，请先安装并登录桌面版。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = App.DataDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--stdio");
        // App-server owns authentication. The pet never reads, copies, or persists login tokens.
        try
        {
            process.Start();
            var drainError = DrainAsync(process.StandardError, timeout.Token);
            await SendAsync(process, new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "momo_codex_pet", title = "Momo Codex Pet", version = "1.0.0" } } }, timeout.Token);
            await ReadResponseAsync(process, 1, timeout.Token);
            await SendAsync(process, new { method = "initialized", @params = new { } }, timeout.Token);
            await SendAsync(process, new { id = 2, method = "account/rateLimits/read" }, timeout.Token);
            var result = await ReadResponseAsync(process, 2, timeout.Token);
            return UsageSnapshot.Parse(result, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new UsageException("连接超时，稍后自动重试。"); }
        catch (System.ComponentModel.Win32Exception)
        { throw new UsageException("Codex 暂时无法启动，稍后自动重试。"); }
        finally
        {
            // Only stop our own subprocess, never the user's Codex process or daemon.
            try { if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(250)) process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            timeout.Cancel();
        }
    }
    private static async Task SendAsync(Process process, object message, CancellationToken ct)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
    }
    private static async Task<JsonElement> ReadResponseAsync(Process process, int expectedId, CancellationToken ct)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(ct);
            if (line is null) throw new UsageException("Codex 连接已断开，请确认已登录。");
            JsonDocument document;
            try { document = JsonDocument.Parse(line); } catch (JsonException) { continue; }
            using (document)
            {
                var root = document.RootElement;
                if (UsageSnapshot.Get(root, "id") is not { ValueKind: JsonValueKind.Number } id || !id.TryGetInt32(out int number) || number != expectedId) continue;
                if (UsageSnapshot.Get(root, "error").ValueKind != JsonValueKind.Undefined)
                    throw new UsageException("读取失败，请确认 Codex 已登录 ChatGPT 账号。");
                var result = UsageSnapshot.Get(root, "result");
                if (result.ValueKind != JsonValueKind.Object) throw new UsageException("Codex 返回了无法识别的数据。");
                return result.Clone();
            }
        }
    }
    private static async Task DrainAsync(StreamReader reader, CancellationToken ct)
    { try { while (await reader.ReadLineAsync(ct) is not null) { } } catch (OperationCanceledException) { } catch (IOException) { } catch (ObjectDisposedException) { } }
}
public sealed class UsageException(string message) : Exception(message);
