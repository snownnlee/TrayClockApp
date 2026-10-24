using System.Reflection;

namespace TrayClockApp.Core;

/// <summary>
/// 应用版本号的唯一读取入口，供“关于”菜单与更新检查共用。
/// 取值来源：csproj 的 &lt;Version&gt;（编译后写入 AssemblyInformationalVersion）。
/// </summary>
public static class AppVersion
{
    /// <summary>当前版本号，如 1.5.0（不含 v 前缀、不含构建元数据）。</summary>
    public static string Current { get; } = Resolve();

    /// <summary>用于比较的规范化版本对象。</summary>
    public static Version CurrentVersion { get; } = ParseOrZero(Current);

    public static string Display => "v" + Current;

    public static string Product { get; } = ReadAttribute<AssemblyProductAttribute>(a => a.Product) ?? "TrayClockApp";

    public static string Company { get; } = ReadAttribute<AssemblyCompanyAttribute>(a => a.Company) ?? string.Empty;

    public static string Copyright { get; } = ReadAttribute<AssemblyCopyrightAttribute>(a => a.Copyright) ?? string.Empty;

    /// <summary>把 "v1.5.0" / "1.5.0-beta" / "1.5.0+abc" 这类标签规范成版本对象。</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        value = value.Split('-', '+')[0].Trim();
        if (!Version.TryParse(value, out var parsed)) return false;

        version = parsed;
        return true;
    }

    public static Version ParseOrZero(string? text) => TryParse(text, out var version) ? version : new Version(0, 0, 0);

    /// <summary>补零到四段，避免 1.5 与 1.5.0 比较时被判为不同版本。</summary>
    public static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));

    private static string Resolve()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var core = informational.Split('+')[0].Trim();
            if (core.Length > 0) return core;
        }

        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    private static string? ReadAttribute<T>(Func<T, string> selector) where T : Attribute
        => Assembly.GetExecutingAssembly().GetCustomAttribute<T>() is { } attribute ? selector(attribute) : null;
}
