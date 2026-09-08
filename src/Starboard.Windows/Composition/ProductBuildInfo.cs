using System.Reflection;

namespace Starboard.Windows.Composition;

internal sealed record ProductBuildInfo(string Version, string? BuildCommit)
{
    private const string _UnknownBuildCommit = "unknown";

    internal static ProductBuildInfo Current { get; } = FromAssembly(
        typeof(ProductBuildInfo).Assembly);

    internal string DisplayText
    {
        get
        {
            if (BuildCommit is null)
            {
                return $"버전 {Version}";
            }

            var shortCommitLength = Math.Min(7, BuildCommit.Length);
            var shortCommit = BuildCommit[..shortCommitLength];
            return $"버전 {Version} · 빌드 {shortCommit}";
        }
    }

    internal bool Matches(string expectedVersion, string expectedBuildCommit)
    {
        return string.Equals(Version, expectedVersion, StringComparison.Ordinal) &&
               string.Equals(BuildCommit, expectedBuildCommit, StringComparison.OrdinalIgnoreCase);
    }

    internal static ProductBuildInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(
                attribute => attribute.Key,
                attribute => attribute.Value,
                StringComparer.Ordinal);
        var version = metadata.GetValueOrDefault("ProductVersion");
        if (string.IsNullOrWhiteSpace(version) == true)
        {
            version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var buildCommit = metadata.GetValueOrDefault("BuildCommit");
        if (string.IsNullOrWhiteSpace(buildCommit) == true ||
            string.Equals(buildCommit, _UnknownBuildCommit, StringComparison.OrdinalIgnoreCase))
        {
            buildCommit = null;
        }

        return new ProductBuildInfo(version, buildCommit);
    }
}
