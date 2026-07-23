using System.Reflection;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The running build's version string, including the integer build number
/// embedded by the csproj (<c>AssemblyInformationalVersion = "1.0.0+&lt;buildNumber&gt;"</c>).
/// Shown in the window title and the About dialog so the exact build can be
/// confirmed inside the app.
/// </summary>
internal static class AppVersion
{
    /// <summary>e.g. "1.0.0 (build 1)".</summary>
    public static string Display { get; } = Resolve();

    static string Resolve()
    {
        // Prefer the informational version (carries the build number); fall back
        // to the plain assembly version if it is somehow absent.
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }

        // Split "1.0.0+1" into product version and build number.
        int plus = informational.IndexOf('+');
        if (plus < 0) return informational;
        var version = informational[..plus];
        var buildNumber = informational[(plus + 1)..];
        return $"{version} (build {buildNumber})";
    }
}
