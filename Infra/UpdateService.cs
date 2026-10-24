using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using TrayClockApp.Core;

namespace TrayClockApp.Infra;

public enum UpdateCheckStatus
{
    /// <summary>已是最新版本。</summary>
    UpToDate,

    /// <summary>有新版本且可自动下载。</summary>
    UpdateAvailable,

    /// <summary>有新版本但更新源未提供安装包，只能手工下载。</summary>
    ManualOnly,

    /// <summary>检查失败（网络、解析等）。</summary>
    Failed
}

public sealed record UpdatePackage(Version Version, string DownloadUrl, long Size, string Source);

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string Message,
    string? LatestTag = null,
    UpdatePackage? Package = null);

public sealed record DownloadProgress(long Received, long Total)
{
    public double Percent => Total > 0 ? Math.Clamp(Received * 100.0 / Total, 0, 100) : 0;
}

public sealed record ApplyResult(bool Success, string Message, bool RolledBack = false);

/// <summary>
/// 版本检查、安装包下载与本地替换。
/// 更新源：GitHub（提供安装包）+ Gitee（仅提供版本号，用于 GitHub 不可达时的兜底）。
/// </summary>
public static class UpdateService
{
    public const string ReleasesPage = "https://github.com/snownnlee/TrayClockApp/releases";

    private const string GitHubLatestApi = "https://api.github.com/repos/snownnlee/TrayClockApp/releases/latest";
    private const string GiteeLatestApi = "https://gitee.com/api/v5/repos/snownnlee/TrayClockApp/releases/latest";

    /// <summary>下载中的临时后缀，避免用户双击到半成品。</summary>
    private const string DownloadSuffix = ".download";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TrayClockApp-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed record RemoteRelease(Version Version, string Tag, string? DownloadUrl, long Size, string Source);

    // ---------------------------------------------------------------- 检查

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var githubTask = FetchGitHubAsync(cancellationToken);
        var giteeTask = FetchGiteeAsync(cancellationToken);
        await Task.WhenAll(githubTask, giteeTask).ConfigureAwait(false);

        var github = githubTask.Result;
        var gitee = giteeTask.Result;

        if (github is null && gitee is null)
            return new UpdateCheckResult(UpdateCheckStatus.Failed,
                "无法连接更新服务器（GitHub 与 Gitee 均不可达）。请检查网络或代理设置后重试。");

        var newest = new[] { github, gitee }
            .Where(release => release is not null)
            .Select(release => release!)
            .OrderByDescending(release => AppVersion.Normalize(release.Version))
            .First();

        if (AppVersion.Normalize(newest.Version) <= AppVersion.Normalize(AppVersion.CurrentVersion))
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate,
                $"当前已是最新版本（{AppVersion.Display}）。", newest.Tag);

        // 版本更高但没拿到安装包：Gitee 的 Release 不传附件，此时只能引导手工下载
        if (newest.DownloadUrl is null)
            return new UpdateCheckResult(UpdateCheckStatus.ManualOnly,
                $"发现新版本 {newest.Tag}，但更新源（{newest.Source}）未提供安装包，无法自动下载。\n"
                + $"请前往发布页手动下载：{ReleasesPage}", newest.Tag);

        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable,
            $"发现新版本 {newest.Tag}（当前 {AppVersion.Display}）。",
            newest.Tag,
            new UpdatePackage(newest.Version, newest.DownloadUrl, newest.Size, newest.Source));
    }

    private static async Task<RemoteRelease?> FetchGitHubAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(GitHubLatestApi, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement)) return null;

            var tag = tagElement.GetString();
            if (!AppVersion.TryParse(tag, out var version)) return null;

            string? downloadUrl = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                    downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                        ? urlElement.GetString()
                        : null;
                    if (asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes))
                        size = bytes;
                    break;
                }

            return new RemoteRelease(version, tag ?? version.ToString(), downloadUrl, size, "GitHub");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Update] GitHub 检查失败: {ex.Message}");
            return null;
        }
    }

    private static async Task<RemoteRelease?> FetchGiteeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(GiteeLatestApi, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("tag_name", out var tagElement)) return null;

            var tag = tagElement.GetString();
            if (!AppVersion.TryParse(tag, out var version)) return null;

            // Gitee 侧按发布脚本约定不上传附件，因此只贡献版本号
            return new RemoteRelease(version, tag ?? version.ToString(), null, 0, "Gitee");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Update] Gitee 检查失败: {ex.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------- 下载

    /// <summary>下载安装包到应用目录，返回临时文件名（带 .download 后缀）。</summary>
    public static async Task<string> DownloadAsync(
        UpdatePackage package,
        string targetDirectory,
        string targetExeName,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);

        var tempPath = Path.Combine(targetDirectory, targetExeName + DownloadSuffix);
        TryDelete(tempPath);

        try
        {
            using var response = await Http
                .GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength is { } length && length > 0 ? length : package.Size;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                             FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                long lastReported = 0;

                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0) break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;

                    if (received - lastReported < 512 * 1024 && received != total) continue;
                    lastReported = received;
                    progress?.Report(new DownloadProgress(received, total));
                }

                progress?.Report(new DownloadProgress(received, total > 0 ? total : received));
            }

            ValidatePackage(tempPath, package.Size);
            return tempPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void ValidatePackage(string path, long expectedSize)
    {
        var length = new FileInfo(path).Length;
        if (length < 64 * 1024)
            throw new InvalidOperationException($"下载的安装包不完整（{length:N0} 字节），请重试。");

        if (expectedSize > 0 && length != expectedSize)
            throw new InvalidOperationException(
                $"下载的安装包大小不一致（期望 {expectedSize:N0} 字节，实际 {length:N0} 字节），请重试。");

        Span<byte> header = stackalloc byte[2];
        using (var stream = File.OpenRead(path)) stream.ReadExactly(header);
        if (header[0] != (byte)'M' || header[1] != (byte)'Z')
            throw new InvalidOperationException("下载的文件不是有效的 Windows 可执行程序，已丢弃。");
    }

    /// <summary>
    /// 从安装包名推导落地的 exe 名：TrayClockApp-v1.6.0.exe → TrayClockApp.exe（去掉版本号）。
    /// 推导不出时退回当前运行的可执行文件名。
    /// </summary>
    public static string GetTargetExeName(UpdatePackage package)
    {
        try
        {
            var assetName = Path.GetFileName(new Uri(package.DownloadUrl).AbsolutePath);
            var withoutExtension = Path.GetFileNameWithoutExtension(assetName);
            var baseName = StripVersionSuffix(withoutExtension);
            return string.IsNullOrWhiteSpace(baseName) ? ExecutableFileName : baseName + ".exe";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Update] 解析安装包名称失败: {ex.Message}");
            return ExecutableFileName;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex VersionSuffixPattern = new(
        @"^(?<base>.+?)[-_]v?\d+(?:\.\d+){1,3}$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string StripVersionSuffix(string name)
    {
        var match = VersionSuffixPattern.Match(name);
        return match.Success ? match.Groups["base"].Value : name;
    }

    // ---------------------------------------------------------------- 替换

    /// <summary>
    /// 备份当前 exe（加版本号改名）→ 安装新版 → 清理旧备份 → 启动新版。
    /// 任一步失败均回滚到可运行的原版本，并把原因写入 ApplyResult。
    /// </summary>
    public static ApplyResult ApplyAndRestart(string downloadedPath, string targetExeName)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return new ApplyResult(false, "无法定位当前程序路径，更新中止。", true);

        var directory = Path.GetDirectoryName(processPath)!;
        var targetPath = Path.Combine(directory, targetExeName);
        var baseName = Path.GetFileNameWithoutExtension(targetExeName);
        var backupPath = Path.Combine(directory, $"{baseName}-v{AppVersion.Current}.exe");

        var samePath = PathsEqual(processPath, targetPath);
        var backupIsSelf = PathsEqual(processPath, backupPath);

        var originalMoved = false;
        try
        {
            if (samePath)
            {
                // 主流路径：运行中的 TrayClockApp.exe 改名为 TrayClockApp-v<当前版本>.exe 作为备份
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(processPath, backupPath);
                originalMoved = true;

                File.Move(downloadedPath, targetPath);
            }
            else
            {
                // 运行的 exe 名与目标名不同（例如用户直接运行了带版本号的备份）
                if (!backupIsSelf)
                {
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Move(processPath, backupPath);
                    originalMoved = true;
                }

                File.Move(downloadedPath, targetPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            var rolledBack = Rollback(processPath, targetPath, backupPath, originalMoved, downloadedPath);
            return new ApplyResult(false,
                $"切换新版本失败：{ex.Message}"
                + (rolledBack ? "\n已回滚，原版本仍可正常使用。" : "\n回滚未完全成功，请检查程序目录。"),
                rolledBack);
        }

        CleanupOldBackups(directory, baseName, Path.GetFileName(backupPath));

        try
        {
            Process.Start(new ProcessStartInfo(targetPath)
            {
                UseShellExecute = true,
                WorkingDirectory = directory
            });
        }
        catch (Exception ex)
        {
            var rolledBack = Rollback(processPath, targetPath, backupPath, originalMoved: true, downloadedPath: null);
            return new ApplyResult(false,
                $"新版本已就位但启动失败：{ex.Message}"
                + (rolledBack ? "\n已回滚到原版本。" : "\n回滚未完全成功，请检查程序目录。"),
                rolledBack);
        }

        Console.WriteLine($"[Update] 已备份为 {Path.GetFileName(backupPath)}，并启动新版本 {targetPath}");
        return new ApplyResult(true, "更新完成，正在启动新版本。");
    }

    private static bool Rollback(string processPath, string targetPath, string backupPath,
        bool originalMoved, string? downloadedPath)
    {
        try
        {
            if (downloadedPath is not null) TryDelete(downloadedPath);

            if (!originalMoved) return true;

            // 目标位置若已放入新文件，先移除（该文件尚未被加载，可删除）
            if (File.Exists(targetPath) && !PathsEqual(targetPath, backupPath))
            {
                try
                {
                    File.Delete(targetPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Update] 回滚时删除新文件失败: {ex.Message}");
                }
            }

            if (!File.Exists(processPath) && File.Exists(backupPath)) File.Move(backupPath, processPath);

            return File.Exists(processPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Update] 回滚失败: {ex.Message}");
            return false;
        }
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>只保留本次备份，删除更早的 {baseName}-v*.exe，避免目录随版本累积。</summary>
    private static void CleanupOldBackups(string directory, string baseName, string keepFileName)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, $"{baseName}-v*.exe"))
            {
                if (string.Equals(Path.GetFileName(file), keepFileName, StringComparison.OrdinalIgnoreCase)) continue;
                TryDelete(file);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Update] 清理旧备份失败: {ex.Message}");
        }
    }

    public static void TryCleanup(string? downloadedPath)
    {
        if (!string.IsNullOrWhiteSpace(downloadedPath)) TryDelete(downloadedPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Update] 删除文件失败 {path}: {ex.Message}");
        }
    }

    private static string ExecutableFileName =>
        Path.GetFileName(Environment.ProcessPath ?? "TrayClockApp.exe");

    /// <summary>打开发布页（用于只能手工下载的场景）。</summary>
    public static void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Update] 打开发布页失败: {ex.Message}");
        }
    }
}
