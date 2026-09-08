using Starboard.Modules.Preferences.Contracts;
using Starboard.Modules.Preferences.Presentation;

namespace Starboard.Modules.Preferences.Tests;

[TestClass]
public sealed class SettingsEditorViewModelTests
{
    [TestMethod]
    public void RestoreDefaultsEditsDraftWithoutCallingSaveHandler()
    {
        var saveHandler = new RecordingSaveHandler();
        var viewModel = new SettingsEditorViewModel(
            new AppSettings
            {
                Theme = "Dark",
                FontSize = 16,
            },
            saveHandler);

        viewModel.RestoreDefaults();

        Assert.AreEqual("Tokyo Night", viewModel.Theme);
        Assert.AreEqual("13", viewModel.FontSizeText);
        Assert.AreEqual(0, saveHandler.SaveCount);
        Assert.IsFalse(viewModel.Completion.IsCompleted);
    }

    [TestMethod]
    public async Task CancelDiscardsDraftAndReturnsOriginalSnapshot()
    {
        var original = new AppSettings { Theme = "Dark" };
        var viewModel = new SettingsEditorViewModel(original, new RecordingSaveHandler());
        viewModel.Theme = "Light";

        viewModel.Cancel();
        var outcome = await viewModel.Completion;

        Assert.AreEqual("Dark", viewModel.Theme);
        Assert.AreEqual(SettingsEditorCompletionKind.Canceled, outcome.CompletionKind);
        Assert.AreEqual("Dark", outcome.Settings.Theme);
    }

    [TestMethod]
    public async Task SaveAsyncInvalidShortcutDoesNotCallHandlerAndShowsFieldError()
    {
        var saveHandler = new RecordingSaveHandler();
        var viewModel = new SettingsEditorViewModel(new AppSettings(), saveHandler)
        {
            ExpandShortcut = "E",
        };

        await viewModel.SaveAsync(CancellationToken.None);

        Assert.AreEqual(0, saveHandler.SaveCount);
        Assert.IsTrue(viewModel.ExpandShortcutError.Length > 0);
        Assert.IsFalse(viewModel.Completion.IsCompleted);
    }

    [TestMethod]
    public async Task SaveAsyncHandlerFailurePreservesDraftAndKeepsEditorOpen()
    {
        var saveHandler = new RecordingSaveHandler
        {
            ExceptionToThrow = new IOException("Write failed."),
        };
        var viewModel = new SettingsEditorViewModel(new AppSettings(), saveHandler)
        {
            Theme = "Light",
        };

        await viewModel.SaveAsync(CancellationToken.None);

        Assert.AreEqual(1, saveHandler.SaveCount);
        Assert.AreEqual("Light", viewModel.Theme);
        Assert.AreEqual("설정을 저장하지 못했습니다. 편집한 값은 그대로 유지됩니다.", viewModel.SaveError);
        Assert.IsFalse(viewModel.Completion.IsCompleted);
    }

    [TestMethod]
    public async Task SaveAsyncValidDraftCompletesWithRequestedSettings()
    {
        var saveHandler = new RecordingSaveHandler();
        var viewModel = new SettingsEditorViewModel(new AppSettings(), saveHandler)
        {
            Theme = "One Dark",
            CollapsedHeightText = "240",
            FontFamily = "Consolas",
            FontSizeText = "15",
            OpacityText = "0.9",
            StartWithWindows = true,
            ExpandShortcut = "Ctrl+Shift+E",
            ActivationShortcut = "Ctrl+Shift+S",
        };

        await viewModel.SaveAsync(CancellationToken.None);
        var outcome = await viewModel.Completion;

        Assert.AreEqual(1, saveHandler.SaveCount);
        Assert.AreEqual(SettingsEditorCompletionKind.Saved, outcome.CompletionKind);
        Assert.AreEqual("One Dark", outcome.Settings.Theme);
        Assert.AreEqual(240, outcome.Settings.CollapsedHeightDip);
        Assert.AreEqual("Ctrl+Shift+S", outcome.Settings.ActivationShortcut);
    }

    private sealed class RecordingSaveHandler : ISettingsEditorSaveHandler
    {
        public int SaveCount { get; private set; }

        public Exception? ExceptionToThrow { get; init; }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            SaveCount++;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.CompletedTask;
        }
    }
}
