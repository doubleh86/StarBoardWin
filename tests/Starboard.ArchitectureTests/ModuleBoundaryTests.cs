using System.Reflection;
using System.Xml.Linq;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Preferences;
using Starboard.Modules.Terminal;
using Starboard.Modules.Terminal.Contracts;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.ArchitectureTests;

[TestClass]
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(DesktopIntegrationModule).Assembly,
        typeof(PreferencesModule).Assembly,
        typeof(TerminalModule).Assembly,
    ];

    [TestMethod]
    public void FeatureModulesDoNotReferenceOtherFeatureModules()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var forbiddenReferences = assembly
                .GetReferencedAssemblies()
                .Where(reference =>
                    reference.Name?.StartsWith("Starboard.Modules.", StringComparison.Ordinal) == true &&
                    string.Equals(reference.Name, assembly.GetName().Name, StringComparison.Ordinal) == false)
                .Select(reference => reference.Name)
                .ToArray();

            Assert.AreEqual(0, forbiddenReferences.Length,
                            $"{assembly.GetName().Name} references another feature module.");
        }
    }

    [TestMethod]
    public void SafetyAndShortcutGuideContractsAreOwnedByTheirFeatureModules()
    {
        Assert.AreSame(typeof(TerminalModule).Assembly, typeof(TerminalConfirmationResponse).Assembly);
        Assert.AreEqual("Starboard.Modules.Terminal.Contracts",
                        typeof(TerminalNewOutputStateSnapshot).Namespace);
        Assert.AreSame(typeof(DesktopIntegrationModule).Assembly,
                       typeof(GlobalShortcutRegistrationSnapshot).Assembly);
        Assert.AreEqual("Starboard.Modules.DesktopIntegration.Contracts",
                        typeof(ShortcutGuideRequestEventArgs).Namespace);
    }

    [TestMethod]
    public void SharedKernelDoesNotReferenceProductionAssemblies()
    {
        var forbiddenReferences = typeof(IDiagnosticLog)
            .Assembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name?.StartsWith("Starboard.", StringComparison.Ordinal) == true)
            .Select(reference => reference.Name)
            .ToArray();

        Assert.AreEqual(0, forbiddenReferences.Length);
    }

    [TestMethod]
    public void FeatureModulePublicTypesAreContractsOrModuleEntryPoints()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var assemblyName = assembly.GetName().Name
                ?? throw new InvalidOperationException("A module assembly has no name.");
            var rootNamespace = assemblyName;
            var invalidTypes = assembly
                .GetExportedTypes()
                .Where(type =>
                    type.Namespace?.EndsWith(".Contracts", StringComparison.Ordinal) == false &&
                    string.Equals(type.Namespace, rootNamespace, StringComparison.Ordinal) == false)
                .Select(type => type.FullName)
                .ToArray();

            Assert.AreEqual(0, invalidTypes.Length, $"{assemblyName} exposes implementation types outside Contracts.");
        }
    }

    [TestMethod]
    public void ProductionProjectsDoNotReferenceTestProjects()
    {
        var root = FindSolutionRoot();
        var sourceRoot = Path.Combine(root, "src");
        var testsRoot = Path.Combine(root, "tests") + Path.DirectorySeparatorChar;

        foreach (var projectPath in Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException("A project path has no parent directory.");
            var document = XDocument.Load(projectPath);
            var testReferences = document
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => string.IsNullOrWhiteSpace(value) == false)
                .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value!)))
                .Where(path => path.StartsWith(testsRoot, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.AreEqual(0, testReferences.Length, $"{projectPath} references a test project.");
        }
    }

    [TestMethod]
    public void HostSourceDoesNotReachIntoModuleImplementationNamespaces()
    {
        var root = FindSolutionRoot();
        var hostRoot = Path.Combine(root, "src", "Starboard.Windows");
        var violations = Directory
            .EnumerateFiles(hostRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) == false &&
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) == false)
            .Where(path =>
            {
                return File.ReadLines(path).Any(line =>
                    line.Contains("Starboard.Modules.", StringComparison.Ordinal) &&
                    (line.Contains(".Application", StringComparison.Ordinal) ||
                     line.Contains(".Domain", StringComparison.Ordinal) ||
                     line.Contains(".Infrastructure", StringComparison.Ordinal) ||
                     line.Contains(".Presentation", StringComparison.Ordinal)));
            })
            .ToArray();

        Assert.AreEqual(0, violations.Length,
                        "The host must compose modules through public contracts and entry points only.");
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Starboard.Windows.sln")) == true)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Starboard.Windows.sln could not be located.");
    }
}
