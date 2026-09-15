using System.Reflection;
using System.Xml.Linq;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Preferences;
using Starboard.Modules.Preferences.Contracts;
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
    public void CommandCompletionCoordinationContractsStayModuleOwnedAndMetadataOnly()
    {
        Assert.AreSame(typeof(TerminalModule).Assembly, typeof(TerminalCommandCompletion).Assembly);
        Assert.AreSame(typeof(DesktopIntegrationModule).Assembly,
                       typeof(CommandCompletionNotificationRequest).Assembly);
        Assert.AreSame(typeof(PreferencesModule).Assembly,
                       typeof(CommandCompletionNotificationPreferenceTransition).Assembly);

        var crossModulePayloadTypes = new[]
        {
            typeof(TerminalCommandCompletion),
            typeof(TerminalCommandCompletedEventArgs),
            typeof(CommandCompletionNotificationRequest),
            typeof(CommandCompletionNotificationSettings),
        };
        var stringProperties = crossModulePayloadTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => $"{property.DeclaringType?.FullName}.{property.Name}")
            .ToArray();

        Assert.AreEqual(0, stringProperties.Length,
                        "Command completion coordination must not carry command text, output, or arbitrary labels.");

        var request = new CommandCompletionNotificationRequest(
            Guid.Parse("10000000-0000-0000-0000-000000000001"), 4,
            Guid.Parse("20000000-0000-0000-0000-000000000001"), 0);

        Assert.IsTrue(request.IsCurrent(request.SessionId, 4));
        Assert.IsFalse(request.IsCurrent(request.SessionId, 5));
        Assert.IsTrue(request.Succeeded);
    }

    [TestMethod]
    public void HostUsesTerminalCompletionPublicBoundaryWithoutImplementationAccess()
    {
        var completionEvent = typeof(TerminalModule).GetEvent(nameof(TerminalModule.CommandCompleted),
                                                              BindingFlags.Instance | BindingFlags.Public);
        var validationMethod = typeof(TerminalModule).GetMethod(nameof(TerminalModule.IsCurrentSession),
                                                                BindingFlags.Instance | BindingFlags.Public);

        Assert.IsNotNull(completionEvent);
        Assert.AreEqual(typeof(EventHandler<TerminalCommandCompletedEventArgs>), completionEvent.EventHandlerType);
        Assert.IsNotNull(validationMethod);
        Assert.AreEqual(typeof(bool), validationMethod.ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(TerminalSessionReference) },
                                  validationMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        var root = FindSolutionRoot();
        var hostCoordinatorPath = Path.Combine(root, "src", "Starboard.Windows", "Composition", "AppCoordinator.cs");
        var hostCoordinatorSource = File.ReadAllText(hostCoordinatorPath);
        StringAssert.Contains(hostCoordinatorSource, "terminalModule.CommandCompleted += HandleCommandCompleted;");
        StringAssert.Contains(hostCoordinatorSource, "terminalModule.IsCurrentSession");
        Assert.IsFalse(hostCoordinatorSource.Contains("Reflection", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SavedTabContractsRemainOwnedByTerminalAndSeparateFromHostComposition()
    {
        Assert.AreSame(typeof(TerminalModule).Assembly, typeof(TerminalSavedTab).Assembly);
        Assert.AreEqual("Starboard.Modules.Terminal.Contracts", typeof(TerminalSavedTabRequestId).Namespace);

        var root = FindSolutionRoot();
        var hostRoot = Path.Combine(root, "src", "Starboard.Windows");
        var savedTabImplementationReferences = Directory
            .EnumerateFiles(hostRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadLines(path).Any(line =>
                line.Contains("TerminalSavedTabService", StringComparison.Ordinal) ||
                line.Contains("FileTerminalSavedTabStore", StringComparison.Ordinal)))
            .ToArray();

        Assert.AreEqual(0, savedTabImplementationReferences.Length,
                        "Saved-tab storage and lifecycle composition must remain inside TerminalModule.");
    }

    [TestMethod]
    public void LaunchAndHeightCoordinationContractsRemainOnTerminalPublicBoundary()
    {
        var contractTypes = new[]
        {
            typeof(TerminalLaunchProfile),
            typeof(TerminalLaunchProfileQueryResult),
            typeof(TerminalTabRequestId),
            typeof(TerminalNewTabRequest),
            typeof(TerminalTabDuplicateRequest),
            typeof(TerminalCollapsedHeightChangeRequest),
            typeof(TerminalCollapsedHeightChangeResult),
            typeof(TerminalCollapsedHeightChangeCallback),
        };

        foreach (var type in contractTypes)
        {
            Assert.AreSame(typeof(TerminalModule).Assembly, type.Assembly);
            Assert.AreEqual("Starboard.Modules.Terminal.Contracts", type.Namespace);
            Assert.IsTrue(type.IsPublic);
        }

        var callback = typeof(TerminalCollapsedHeightChangeCallback).GetMethod("Invoke");
        Assert.IsNotNull(callback);
        Assert.AreEqual(typeof(ValueTask<TerminalCollapsedHeightChangeResult>), callback.ReturnType);
        CollectionAssert.AreEqual(new[]
                                  {
                                      typeof(TerminalCollapsedHeightChangeRequest),
                                      typeof(CancellationToken),
                                  },
                                  callback.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        var requestMethod = typeof(TerminalModule).GetMethod(
            nameof(TerminalModule.RequestCollapsedHeightChangeAsync), BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(requestMethod);
        Assert.AreEqual(typeof(ValueTask<TerminalCollapsedHeightChangeResult>), requestMethod.ReturnType);
        CollectionAssert.AreEqual(new[]
                                  {
                                      typeof(TerminalCollapsedHeightChangeRequest),
                                      typeof(CancellationToken),
                                  },
                                  requestMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [TestMethod]
    public void HostCoordinatesPanelResizeThroughModuleContractsAndResultBearingWindowHook()
    {
        var root = FindSolutionRoot();
        var coordinatorPath = Path.Combine(root, "src", "Starboard.Windows", "Composition", "AppCoordinator.cs");
        var mainWindowPath = Path.Combine(root, "src", "Starboard.Windows", "Shell", "MainWindow.xaml.cs");
        var coordinatorSource = File.ReadAllText(coordinatorPath);
        var mainWindowSource = File.ReadAllText(mainWindowPath);

        StringAssert.Contains(coordinatorSource,
                              "desktopIntegrationModule.PanelCollapsedHeightChangeRequested +=");
        StringAssert.Contains(coordinatorSource, "terminalModule.RequestCollapsedHeightChangeAsync");
        StringAssert.Contains(mainWindowSource, "module.HandleWindowMessage(windowMessage, out result)");
        StringAssert.Contains(mainWindowSource, "return result;");
        Assert.IsFalse(coordinatorSource.Contains("WM_SIZING", StringComparison.Ordinal));
        Assert.IsFalse(coordinatorSource.Contains("wsl.exe", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TerminalPanelVisibilityLifecycleIsExposedOnlyThroughModuleEntryPoint()
    {
        var visibilityMethod = typeof(TerminalModule).GetMethod(nameof(TerminalModule.NotifyPanelVisibilityChanged),
                                                                BindingFlags.Instance | BindingFlags.Public);

        Assert.IsNotNull(visibilityMethod);
        Assert.AreEqual(typeof(void), visibilityMethod.ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(bool) },
                                  visibilityMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        var root = FindSolutionRoot();
        var hostCoordinatorPath = Path.Combine(root, "src", "Starboard.Windows", "Composition", "AppCoordinator.cs");
        var hostCoordinatorSource = File.ReadAllText(hostCoordinatorPath);
        StringAssert.Contains(hostCoordinatorSource, "terminalModule.NotifyPanelVisibilityChanged(isVisible);");
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
    public void VirtualDesktopIntegrationExposesDocumentedOperationsWithoutPinningEntryPoint()
    {
        var contractTypes = new[]
        {
            typeof(VirtualDesktopCapabilities),
            typeof(VirtualDesktopWindowStateStatus),
            typeof(VirtualDesktopWindowState),
            typeof(VirtualDesktopMoveStatus),
        };

        foreach (var type in contractTypes)
        {
            Assert.AreSame(typeof(DesktopIntegrationModule).Assembly, type.Assembly);
            Assert.AreEqual("Starboard.Modules.DesktopIntegration.Contracts", type.Namespace);
            Assert.IsTrue(type.IsPublic);
        }

        var captureMethod = typeof(DesktopIntegrationModule).GetMethod(
            nameof(DesktopIntegrationModule.CapturePanelVirtualDesktopState), BindingFlags.Instance | BindingFlags.Public);
        var moveMethod = typeof(DesktopIntegrationModule).GetMethod(
            nameof(DesktopIntegrationModule.MovePanelToVirtualDesktop), BindingFlags.Instance | BindingFlags.Public);
        var pinningMethods = typeof(DesktopIntegrationModule)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.Contains("Pin", StringComparison.OrdinalIgnoreCase))
            .Select(method => method.Name)
            .ToArray();

        Assert.IsNotNull(captureMethod);
        Assert.AreEqual(typeof(VirtualDesktopWindowState), captureMethod.ReturnType);
        Assert.IsNotNull(moveMethod);
        Assert.AreEqual(typeof(VirtualDesktopMoveStatus), moveMethod.ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(Guid) },
                                  moveMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.AreEqual(0, pinningMethods.Length,
                        "The documented IVirtualDesktopManager contract does not provide window pinning.");
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
