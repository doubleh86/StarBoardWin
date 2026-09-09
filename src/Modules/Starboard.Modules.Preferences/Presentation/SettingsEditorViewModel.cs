using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Starboard.Modules.Preferences.Application;
using Starboard.Modules.Preferences.Contracts;

namespace Starboard.Modules.Preferences.Presentation;

internal sealed class SettingsEditorViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private const string CollapsedHeightPropertyName = nameof(CollapsedHeightText);
    private const string FontFamilyPropertyName = nameof(FontFamily);
    private const string FontSizePropertyName = nameof(FontSizeText);
    private const string OpacityPropertyName = nameof(OpacityText);
    private const string ShellExecutablePropertyName = nameof(ShellExecutable);
    private const string ExpandShortcutPropertyName = nameof(ExpandShortcut);
    private const string ActivationShortcutPropertyName = nameof(ActivationShortcut);

    private static readonly Regex ShortcutPattern = new("^(?:(?:Ctrl|Alt|Shift)\\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-9]|2[0-4]))$",
                                                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                                                        TimeSpan.FromMilliseconds(100));

    private readonly ISettingsEditorSaveHandler saveHandler;
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource<SettingsEditorOutcome> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AppSettings originalSettings;
    private string shellExecutable = string.Empty;
    private string theme = "Tokyo Night";
    private string collapsedHeightText = "200";
    private string fontFamily = "Cascadia Mono";
    private string fontSizeText = "13";
    private string opacityText = "0.97";
    private bool startWithWindows;
    private string preferredMonitor = "Taskbar";
    private string expandShortcut = "Ctrl+Alt+E";
    private string activationShortcut = "Ctrl+Alt+S";
    private string? saveError;
    private bool isSaving;
    private bool isCompleted;

    internal SettingsEditorViewModel(AppSettings currentSettings, ISettingsEditorSaveHandler saveHandler)
    {
        originalSettings = SettingsValidator.Normalize(currentSettings);
        this.saveHandler = saveHandler;
        LoadDraft(originalSettings);
        Validate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IReadOnlyList<string> Themes { get; } = ["Dark", "Light", "One Dark", "Tokyo Night"];

    public IReadOnlyList<string> PreferredMonitors { get; } = ["Taskbar"];

    public Task<SettingsEditorOutcome> Completion => completion.Task;

    public bool HasErrors => errors.Count > 0;

    public string ShellExecutable
    {
        get => shellExecutable;
        set => SetDraftProperty(ref shellExecutable, value);
    }

    public string Theme
    {
        get => theme;
        set => SetDraftProperty(ref theme, value);
    }

    public string CollapsedHeightText
    {
        get => collapsedHeightText;
        set => SetDraftProperty(ref collapsedHeightText, value);
    }

    public string FontFamily
    {
        get => fontFamily;
        set => SetDraftProperty(ref fontFamily, value);
    }

    public string FontSizeText
    {
        get => fontSizeText;
        set => SetDraftProperty(ref fontSizeText, value);
    }

    public string OpacityText
    {
        get => opacityText;
        set => SetDraftProperty(ref opacityText, value);
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set => SetDraftProperty(ref startWithWindows, value);
    }

    public string PreferredMonitor
    {
        get => preferredMonitor;
        set => SetDraftProperty(ref preferredMonitor, value);
    }

    public string ExpandShortcut
    {
        get => expandShortcut;
        set => SetDraftProperty(ref expandShortcut, value);
    }

    public string ActivationShortcut
    {
        get => activationShortcut;
        set => SetDraftProperty(ref activationShortcut, value);
    }

    public string? SaveError
    {
        get => saveError;
        private set => SetProperty(ref saveError, value);
    }

    public bool IsSaving
    {
        get => isSaving;
        private set => SetProperty(ref isSaving, value);
    }

    public string CollapsedHeightError => GetFirstError(CollapsedHeightPropertyName);

    public string FontFamilyError => GetFirstError(FontFamilyPropertyName);

    public string FontSizeError => GetFirstError(FontSizePropertyName);

    public string OpacityError => GetFirstError(OpacityPropertyName);

    public string ShellExecutableError => GetFirstError(ShellExecutablePropertyName);

    public string ExpandShortcutError => GetFirstError(ExpandShortcutPropertyName);

    public string ActivationShortcutError => GetFirstError(ActivationShortcutPropertyName);

    public System.Collections.IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is null || errors.TryGetValue(propertyName, out var propertyErrors) == false)
        {
            return Array.Empty<string>();
        }

        return propertyErrors;
    }

    internal void RestoreDefaults()
    {
        if (isCompleted == true || IsSaving == true)
        {
            return;
        }

        LoadDraft(new AppSettings());
        SaveError = null;
        Validate();
    }

    internal void Cancel()
    {
        if (isCompleted == true)
        {
            return;
        }

        isCompleted = true;
        LoadDraft(originalSettings);
        SaveError = null;
        completion.TrySetResult(new SettingsEditorOutcome(SettingsEditorCompletionKind.Canceled, originalSettings));
    }

    internal async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (isCompleted == true || IsSaving == true)
        {
            return;
        }

        var settings = ValidateAndCreateSettings();
        if (settings is null)
        {
            return;
        }

        SaveError = null;
        IsSaving = true;
        try
        {
            await saveHandler.SaveAsync(settings, cancellationToken);
            originalSettings = settings;
            isCompleted = true;
            completion.TrySetResult(new SettingsEditorOutcome(SettingsEditorCompletionKind.Saved, settings));
        }
        catch (OperationCanceledException)
        {
            SaveError = "설정 저장이 취소되었습니다. 값을 확인한 뒤 다시 시도해 주세요.";
        }
        catch (Exception)
        {
            SaveError = "설정을 저장하지 못했습니다. 편집한 값은 그대로 유지됩니다.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private AppSettings? ValidateAndCreateSettings()
    {
        Validate();
        if (HasErrors == true)
        {
            return null;
        }

        return new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            ShellExecutable = string.IsNullOrWhiteSpace(ShellExecutable) == true ? null : ShellExecutable.Trim(),
            Theme = Theme,
            CollapsedHeightDip = ParseNumber(CollapsedHeightText),
            FontFamily = FontFamily.Trim(),
            FontSize = ParseNumber(FontSizeText),
            Opacity = ParseNumber(OpacityText),
            StartWithWindows = StartWithWindows,
            PreferredMonitor = PreferredMonitor,
            ExpandShortcut = ExpandShortcut.Trim(),
            ActivationShortcut = ActivationShortcut.Trim(),
        };
    }

    private void Validate()
    {
        SetErrors(CollapsedHeightPropertyName, ValidateNumber(CollapsedHeightText, 96, 720, "축소 높이는 96에서 720 DIP 사이여야 합니다."));
        SetErrors(FontFamilyPropertyName, string.IsNullOrWhiteSpace(FontFamily) == true ? ["글꼴 이름을 입력해 주세요."] : []);
        SetErrors(FontSizePropertyName, ValidateNumber(FontSizeText, 8, 32, "글꼴 크기는 8에서 32 사이여야 합니다."));
        SetErrors(OpacityPropertyName, ValidateNumber(OpacityText, 0.72, 1, "불투명도는 0.72에서 1.00 사이여야 합니다."));
        SetErrors(ShellExecutablePropertyName, ShellExecutable.Contains('\0') == true ? ["셸 경로에는 널 문자를 사용할 수 없습니다."] : []);

        var expandError = ValidateShortcut(ExpandShortcut);
        var activationError = ValidateShortcut(ActivationShortcut);
        if (string.Equals(ExpandShortcut.Trim(), ActivationShortcut.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
            string.IsNullOrWhiteSpace(expandError) == true && string.IsNullOrWhiteSpace(activationError) == true)
        {
            activationError = "확장 단축키와 호출/숨김 단축키는 서로 달라야 합니다.";
        }

        SetErrors(ExpandShortcutPropertyName, string.IsNullOrWhiteSpace(expandError) == true ? [] : [expandError]);
        SetErrors(ActivationShortcutPropertyName, string.IsNullOrWhiteSpace(activationError) == true ? [] : [activationError]);
    }

    private static IReadOnlyList<string> ValidateNumber(string value, double minimum, double maximum, string rangeMessage)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) == false &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) == false)
        {
            return ["숫자로 입력해 주세요."];
        }

        if (number < minimum || number > maximum)
        {
            return [rangeMessage];
        }

        return [];
    }

    private static string? ValidateShortcut(string value)
    {
        if (ShortcutPattern.IsMatch(value.Trim()) == false)
        {
            return "예: Ctrl+Alt+E 형식으로 입력해 주세요.";
        }

        return null;
    }

    private static double ParseNumber(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) == true)
        {
            return number;
        }

        return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private void LoadDraft(AppSettings settings)
    {
        shellExecutable = settings.ShellExecutable ?? string.Empty;
        theme = settings.Theme;
        collapsedHeightText = settings.CollapsedHeightDip.ToString(CultureInfo.InvariantCulture);
        fontFamily = settings.FontFamily;
        fontSizeText = settings.FontSize.ToString(CultureInfo.InvariantCulture);
        opacityText = settings.Opacity.ToString(CultureInfo.InvariantCulture);
        startWithWindows = settings.StartWithWindows;
        preferredMonitor = settings.PreferredMonitor;
        expandShortcut = settings.ExpandShortcut;
        activationShortcut = settings.ActivationShortcut;

        OnPropertiesChanged(nameof(ShellExecutable), nameof(Theme), nameof(CollapsedHeightText), nameof(FontFamily),
                            nameof(FontSizeText), nameof(OpacityText), nameof(StartWithWindows),
                            nameof(PreferredMonitor), nameof(ExpandShortcut), nameof(ActivationShortcut));
    }

    private void SetDraftProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value) == true)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        SaveError = null;
        Validate();
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value) == true)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void SetErrors(string propertyName, IReadOnlyList<string> propertyErrors)
    {
        if (propertyErrors.Count == 0)
        {
            if (errors.Remove(propertyName) == false)
            {
                return;
            }
        }
        else
        {
            errors[propertyName] = [.. propertyErrors];
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(propertyName + "Error");
        OnPropertyChanged(nameof(HasErrors));
    }

    private string GetFirstError(string propertyName)
    {
        if (errors.TryGetValue(propertyName, out var propertyErrors) == false || propertyErrors.Count == 0)
        {
            return string.Empty;
        }

        return propertyErrors[0];
    }

    private void OnPropertiesChanged(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
