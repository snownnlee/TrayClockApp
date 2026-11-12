using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace TrayClockApp.Infra;

/// <summary>开机启动操作结果：Success 表示已完成，Cancelled 表示用户取消了 UAC 授权。</summary>
public sealed record AutoStartResult(bool Success, bool Cancelled, string Message);

/// <summary>开机启动的当前状态。</summary>
public enum AutoStartStatus
{
    /// <summary>任务不存在，或存在但已被禁用。</summary>
    NotRegistered,

    /// <summary>任务存在、已启用，且启动路径就是当前程序。</summary>
    Registered,

    /// <summary>任务存在、已启用，但启动路径指向其他位置的程序。</summary>
    PointsElsewhere
}

/// <summary>
/// 开机启动：以计划任务（登录时触发）注册当前程序。
/// “已注册”的判定为三项同时满足：任务存在、启动路径为当前 exe、任务处于启用状态。
/// 注册通过 schtasks /Create /XML 完成（XML 才能写入工作目录 Start In），需要管理员权限，被拒后以 UAC 提升重试一次。
/// </summary>
public static class AutoStartService
{
    /// <summary>计划任务名，与系统已有任务保持一致。</summary>
    public const string TaskName = "TrayClockApp-AutoStart";

    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>用户在 UAC 授权提示中点了取消。</summary>
    private const int ErrorCancelled = 1223;

    private const string DefaultRunLevel = "LeastPrivilege";

    private static XNamespace Ns => TaskNamespace;

    /// <summary>当前进程的可执行文件路径（计划任务的启动目标）。</summary>
    private static string ExecutablePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("无法定位当前程序路径。");

    /// <summary>工作目录（计划任务的 Start In），与 exe 同目录。</summary>
    private static string WorkingDirectory =>
        Path.GetDirectoryName(ExecutablePath) ?? string.Empty;

    /// <summary>当前开机启动状态：勾选以“任务存在且已启用”为准，路径不一致时单独标记。</summary>
    public static AutoStartStatus GetStatus() => Inspect().Status;

    /// <summary>注册或取消开机启动。</summary>
    public static async Task<AutoStartResult> SetEnabledAsync(bool enable)
    {
        var state = Inspect();
        var existed = enable && state.Exists;
        string arguments;
        string? xmlPath = null;

        try
        {
            if (enable)
            {
                // 已存在则沿用其运行级别，避免覆盖时悄悄改变权限
                xmlPath = Path.Combine(Path.GetTempPath(), $"TrayClockApp-AutoStart-{Guid.NewGuid():N}.xml");
                WriteTaskXml(xmlPath, state.RunLevel);
                arguments = $"/Create /F /TN \"{TaskName}\" /XML \"{xmlPath}\"";
            }
            else
            {
                arguments = $"/Delete /F /TN \"{TaskName}\"";
            }
        }
        catch (Exception ex)
        {
            return new AutoStartResult(false, false, (enable ? "注册开机启动失败：" : "取消开机启动失败：") + ex.Message);
        }

        try
        {
            var attempt = await RunSchtasksAsync(arguments, elevate: false);
            if (attempt.ExitCode == 0) return Ok(enable, existed);

            Console.Error.WriteLine($"[AutoStart] schtasks 首次执行失败（退出码 {attempt.ExitCode}）：{attempt.Output}");

            // 绝大多数失败原因是没有管理员权限，改为 UAC 提升后重试
            var elevated = await RunSchtasksAsync(arguments, elevate: true);
            return elevated.ExitCode == 0 ? Ok(enable, existed) : Fail(enable, elevated.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new AutoStartResult(false, true, "已取消授权，开机启动设置未改变。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AutoStart] 设置开机启动失败: {ex.Message}");
            return new AutoStartResult(false, false, (enable ? "注册开机启动失败：" : "取消开机启动失败：") + ex.Message);
        }
        finally
        {
            if (xmlPath is not null) TryDelete(xmlPath);
        }
    }

    private static AutoStartResult Ok(bool enable, bool existed) => new(true, false, enable
        ? (existed ? "已更新开机启动（已指向当前程序并启用）" : "已注册开机启动")
        : "已取消开机启动");

    private static AutoStartResult Fail(bool enable, int exitCode) =>
        new(false, false,
            (enable ? "注册开机启动失败" : "取消开机启动失败")
            + $"（schtasks 返回 {exitCode}），请以管理员身份运行本程序后重试。");

    /// <summary>读取计划任务当前状态；任务不存在时返回 <see cref="TaskState.Missing"/>。</summary>
    private static TaskState Inspect()
    {
        try
        {
            var (exitCode, output) = RunQuery();
            if (exitCode != 0 || string.IsNullOrWhiteSpace(output)) return TaskState.Missing;

            var root = XDocument.Parse(output).Root;
            if (root is null) return TaskState.Missing;

            var exec = root.Element(Ns + "Actions")?.Elements(Ns + "Exec").FirstOrDefault();
            var command = NormalizePath((string?)exec?.Element(Ns + "Command"));

            // 无 Enabled 节点时任务默认启用
            var enabledNode = root.Element(Ns + "Settings")?.Element(Ns + "Enabled");
            var enabled = enabledNode is null || !bool.TryParse(enabledNode.Value, out var value) || value;

            var runLevel = (string?)root.Element(Ns + "Principals")?.Elements(Ns + "Principal")
                .FirstOrDefault()?.Element(Ns + "RunLevel");

            return new TaskState(true, IsCurrentExecutable(command), enabled,
                string.IsNullOrWhiteSpace(runLevel) ? DefaultRunLevel : runLevel!);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AutoStart] 解析计划任务失败: {ex.Message}");
            return TaskState.Missing;
        }
    }

    private static bool IsCurrentExecutable(string command) =>
        string.Equals(NormalizePath(command), NormalizePath(ExecutablePath), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string? path) => (path ?? string.Empty).Trim().Trim('"');

    private static void WriteTaskXml(string path, string runLevel)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task", new XAttribute("version", "1.3"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Description", "登录 Windows 时自动启动 TrayClockApp")),
                new XElement(Ns + "Triggers",
                    new XElement(Ns + "LogonTrigger",
                        new XElement(Ns + "UserId", user))),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", user),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", runLevel))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "AllowHardTerminate", "true"),
                    new XElement(Ns + "StartWhenAvailable", "false"),
                    new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Ns + "IdleSettings",
                        new XElement(Ns + "StopOnIdleEnd", "false"),
                        new XElement(Ns + "RestartOnIdle", "false")),
                    new XElement(Ns + "AllowStartOnDemand", "true"),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "Hidden", "false"),
                    new XElement(Ns + "RunOnlyIfIdle", "false"),
                    new XElement(Ns + "WakeToRun", "false"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(Ns + "Priority", "7")),
                new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", ExecutablePath),
                        new XElement(Ns + "WorkingDirectory", WorkingDirectory)))));

        // 任务计划 XML 原生为 UTF-16，声明与编码保持一致
        doc.Save(path, SaveOptions.None);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AutoStart] 删除临时文件失败 {path}: {ex.Message}");
        }
    }

    private static (int ExitCode, string Output) RunQuery()
    {
        using var process = Process.Start(CreateStartInfo($"/Query /TN \"{TaskName}\" /XML", elevate: false))
            ?? throw new InvalidOperationException("无法启动 schtasks.exe。");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static async Task<(int ExitCode, string Output)> RunSchtasksAsync(string arguments, bool elevate)
    {
        using var process = Process.Start(CreateStartInfo(arguments, elevate))
            ?? throw new InvalidOperationException("无法启动 schtasks.exe。");

        if (elevate) // 提升后的进程无法重定向输出，只取退出码
        {
            await process.WaitForExitAsync();
            return (process.ExitCode, string.Empty);
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        output += await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }

    private static ProcessStartInfo CreateStartInfo(string arguments, bool elevate) => new("schtasks.exe", arguments)
    {
        UseShellExecute = elevate,
        Verb = elevate ? "runas" : string.Empty,
        CreateNoWindow = !elevate,
        WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardOutput = !elevate,
        RedirectStandardError = !elevate
    };

    /// <summary>计划任务快照。</summary>
    private sealed record TaskState(bool Exists, bool PointsToCurrentExe, bool Enabled, string RunLevel)
    {
        public static readonly TaskState Missing = new(false, false, false, DefaultRunLevel);

        /// <summary>任务存在且启用即为“已注册”，路径不一致时单独标记。</summary>
        public AutoStartStatus Status => !Exists || !Enabled
            ? AutoStartStatus.NotRegistered
            : PointsToCurrentExe
                ? AutoStartStatus.Registered
                : AutoStartStatus.PointsElsewhere;
    }
}
