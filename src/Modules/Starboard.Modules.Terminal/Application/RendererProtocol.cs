using System.Text.Json;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Application;

internal static class RendererProtocol
{
    private static readonly HashSet<string> _globalHostMessageTypes =
    [
        "initialize",
        "apply-appearance",
        "workspace-save-status",
        "saved-tabs-snapshot",
        "saved-tab-operation-result",
        "saved-tab-launch-result",
        "launch-profiles-result",
        "collapsed-height-change-result",
    ];

    private static readonly HashSet<string> _sessionHostMessageTypes =
    [
        "session-upsert",
        "activate-session",
        "output",
        "paste",
        "reset",
        "remove-session",
        "session-error",
        "confirmation-request",
        "confirmation-cancel",
        "path-drop-result",
        "url-open-result",
        "new-output-state",
    ];

    internal const int CurrentVersion = 2;
    internal const int MaximumMessageLength = 1_048_576;

    internal static string SerializeGlobalMessage(string type, object payload)
    {
        ValidateType(type, _globalHostMessageTypes);
        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(new
                                        {
                                            version = CurrentVersion,
                                            type,
                                            payload,
                                        },
                                        JsonSerializerOptions.Web);
    }

    internal static string SerializeSessionMessage(string type, TerminalSessionId sessionId, object payload)
    {
        ValidateType(type, _sessionHostMessageTypes);
        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("The renderer session identifier cannot be empty.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(new
                                        {
                                            version = CurrentVersion,
                                            type,
                                            sessionId = sessionId.ToString(),
                                            payload,
                                        },
                                        JsonSerializerOptions.Web);
    }

    internal static string SerializeSavedTabsSnapshot(TerminalSavedTabsSnapshot snapshot, int runningTabCount)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateRunningTabCount(runningTabCount);

        return SerializeGlobalMessage("saved-tabs-snapshot", CreateSavedTabsPayload(snapshot, runningTabCount));
    }

    internal static string SerializeSavedTabOperationResult(TerminalSavedTabOperationResult result,
                                                            int runningTabCount)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateRunningTabCount(runningTabCount);
        var snapshot = result.Snapshot is null ? null : CreateSavedTabsPayload(result.Snapshot, runningTabCount);

        return SerializeGlobalMessage("saved-tab-operation-result",
                                      new
                                      {
                                          requestId = result.RequestId.ToString(),
                                          operation = FormatOperation(result.Operation),
                                          status = FormatOperationStatus(result.Status),
                                          result.FailureMessage,
                                          snapshot,
                                      });
    }

    internal static string SerializeSavedTabLaunchResult(TerminalSavedTabLaunchResult result, int runningTabCount)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateRunningTabCount(runningTabCount);

        return SerializeGlobalMessage("saved-tab-launch-result",
                                      new
                                      {
                                          requestId = result.RequestId.ToString(),
                                          savedTabId = result.SavedTabId.ToString(),
                                          status = FormatLaunchStatus(result.Status),
                                          sessionId = result.Session?.SessionId.ToString("N"),
                                          sessionGeneration = result.Session?.Generation,
                                          result.FailureMessage,
                                          runningTabCount,
                                          maximumRunningTabs = TerminalSavedTabsContract.MaximumRunningTabs,
                                      });
    }

    internal static string SerializeLaunchProfileQueryResult(TerminalLaunchProfileQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var profiles = result.Profiles.Select(profile => new
        {
            profileId = profile.ProfileId.ToString(),
            kind = FormatLaunchProfileKind(profile.Kind),
            profile.DisplayName,
            shellKind = profile.ShellKind is null ? null : FormatShellKind(profile.ShellKind.Value),
            profile.WslDistributionName,
        }).ToArray();

        return SerializeGlobalMessage("launch-profiles-result",
                                      new
                                      {
                                          status = FormatLaunchProfileQueryStatus(result.Status),
                                          profiles,
                                          result.FailureMessage,
                                      });
    }

    internal static string SerializeCollapsedHeightChangeResult(TerminalCollapsedHeightChangeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return SerializeGlobalMessage("collapsed-height-change-result",
                                      new
                                      {
                                          requestId = result.RequestId.ToString(),
                                          status = FormatCollapsedHeightChangeStatus(result.Status),
                                          result.AppliedHeightDip,
                                          result.FailureMessage,
                                      });
    }

    internal static bool TryParse(string json, out RendererMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(json) == true || json.Length > MaximumMessageLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("version", out var version) == false ||
                version.TryGetInt32(out var versionNumber) == false || versionNumber != CurrentVersion ||
                root.TryGetProperty("type", out var typeElement) == false ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var type = typeElement.GetString();
            root.TryGetProperty("payload", out var payload);

            message = type switch
            {
                "ready" => ParseRendererReady(root, payload),
                "new-tab" => ParseNewTab(root, payload),
                "duplicate-tab" => ParseDuplicateTab(root, payload),
                "change-collapsed-height" => ParseCollapsedHeightChange(root, payload),
                "select-session" => ParseSession(RendererMessageType.SelectSession, root, payload),
                "select-next" => ParseGlobal(RendererMessageType.SelectNext, root, payload),
                "select-previous" => ParseGlobal(RendererMessageType.SelectPrevious, root, payload),
                "input" => ParseData(RendererMessageType.Input, root, payload),
                "resize" => ParseResize(root, payload),
                "copy" => ParseData(RendererMessageType.Copy, root, payload),
                "paste-request" => ParseSession(RendererMessageType.PasteRequest, root, payload),
                "drop-paths" => ParsePathDrop(root, payload),
                "open-url-request" => ParseUrlOpen(root, payload),
                "close-session" => ParseSession(RendererMessageType.CloseSession, root, payload),
                "restart-session" => ParseSession(RendererMessageType.RestartSession, root, payload),
                "rename-session" => ParseData(RendererMessageType.RenameSession, root, payload, "name", 128),
                "move-session" => ParseData(RendererMessageType.MoveSession, root, payload, "direction", 5),
                "set-starting-directory" => ParseData(RendererMessageType.SetStartingDirectory, root, payload,
                                                      "startingDirectory", 32_767),
                "session-error" => ParseSession(RendererMessageType.SessionError, root, payload),
                "renderer-error" => ParseGlobal(RendererMessageType.RendererError, root, payload),
                "confirmation-response" => ParseConfirmationResponse(root, payload),
                "create-saved-tab" => ParseCreateSavedTab(root, payload),
                "update-saved-tab" => ParseUpdateSavedTab(root, payload),
                "delete-saved-tab" => ParseDeleteSavedTab(root, payload),
                "launch-saved-tab" => ParseLaunchSavedTab(root, payload),
                "cancel-saved-tab-launch" => ParseCancelSavedTabLaunch(root, payload),
                _ => null,
            };

            return message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static RendererMessage? ParseNewTab(JsonElement root, JsonElement payload)
    {
        if (TryParseTabRequestToken(root, payload, out var token) == false ||
            payload.TryGetProperty("profileId", out var profileIdElement) == false ||
            profileIdElement.ValueKind != JsonValueKind.String ||
            TerminalLaunchProfileId.TryCreate(profileIdElement.GetString(), out var profileId) == false)
        {
            return null;
        }

        var request = new TerminalNewTabRequest(token, profileId);
        return new RendererMessage(RendererMessageType.NewTab, new TerminalSessionId(token.Session.SessionId),
                                   SessionGeneration: token.Session.Generation, NewTabRequest: request);
    }

    private static RendererMessage? ParseDuplicateTab(JsonElement root, JsonElement payload)
    {
        if (TryParseTabRequestToken(root, payload, out var token) == false)
        {
            return null;
        }

        var request = new TerminalTabDuplicateRequest(token);
        return new RendererMessage(RendererMessageType.DuplicateTab, new TerminalSessionId(token.Session.SessionId),
                                   SessionGeneration: token.Session.Generation, TabDuplicateRequest: request);
    }

    private static RendererMessage? ParseCollapsedHeightChange(JsonElement root, JsonElement payload)
    {
        if (root.TryGetProperty("sessionId", out _) == true || payload.ValueKind != JsonValueKind.Object ||
            TryParseCompactRequestId(payload, out var requestIdentifier) == false ||
            payload.TryGetProperty("phase", out var phaseElement) == false ||
            phaseElement.ValueKind != JsonValueKind.String ||
            TryParseCollapsedHeightChangePhase(phaseElement.GetString(), out var phase) == false ||
            payload.TryGetProperty("requestedHeightDip", out var requestedHeightElement) == false ||
            requestedHeightElement.TryGetDouble(out var requestedHeightDip) == false ||
            payload.TryGetProperty("lastSavedHeightDip", out var lastSavedHeightElement) == false ||
            lastSavedHeightElement.TryGetDouble(out var lastSavedHeightDip) == false)
        {
            return null;
        }

        try
        {
            var request = new TerminalCollapsedHeightChangeRequest(
                new TerminalCollapsedHeightChangeRequestId(requestIdentifier), phase,
                requestedHeightDip, lastSavedHeightDip);
            return new RendererMessage(RendererMessageType.ChangeCollapsedHeight,
                                       CollapsedHeightChangeRequest: request);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static RendererMessage? ParseCreateSavedTab(JsonElement root, JsonElement payload)
    {
        if (TryParseGlobalSavedTabDefinition(root, payload, requireSavedTabId: false, out var requestId,
                                             out _, out var name, out var startingDirectory,
                                             out var shellKind) == false)
        {
            return null;
        }

        var request = new TerminalSavedTabCreateRequest(requestId, name, startingDirectory, shellKind);
        return new RendererMessage(RendererMessageType.CreateSavedTab, SavedTabCreateRequest: request);
    }

    private static RendererMessage? ParseUpdateSavedTab(JsonElement root, JsonElement payload)
    {
        if (TryParseGlobalSavedTabDefinition(root, payload, requireSavedTabId: true, out var requestId,
                                             out var savedTabId, out var name, out var startingDirectory,
                                             out var shellKind) == false)
        {
            return null;
        }

        var savedTab = new TerminalSavedTab(savedTabId, name, startingDirectory, shellKind);
        var request = new TerminalSavedTabUpdateRequest(requestId, savedTab);
        return new RendererMessage(RendererMessageType.UpdateSavedTab, SavedTabUpdateRequest: request);
    }

    private static RendererMessage? ParseDeleteSavedTab(JsonElement root, JsonElement payload)
    {
        if (TryParseGlobalSavedTabTarget(root, payload, out var requestId, out var savedTabId) == false)
        {
            return null;
        }

        var request = new TerminalSavedTabDeleteRequest(requestId, savedTabId);
        return new RendererMessage(RendererMessageType.DeleteSavedTab, SavedTabDeleteRequest: request);
    }

    private static RendererMessage? ParseLaunchSavedTab(JsonElement root, JsonElement payload)
    {
        if (TryParseGlobalSavedTabTarget(root, payload, out var requestId, out var savedTabId) == false)
        {
            return null;
        }

        var request = new TerminalSavedTabLaunchRequest(requestId, savedTabId);
        return new RendererMessage(RendererMessageType.LaunchSavedTab, SavedTabLaunchRequest: request);
    }

    private static RendererMessage? ParseCancelSavedTabLaunch(JsonElement root, JsonElement payload)
    {
        if (TryParseGlobalRequestId(root, payload, out var requestId) == false)
        {
            return null;
        }

        var cancellation = new TerminalSavedTabLaunchCancellation(requestId);
        return new RendererMessage(RendererMessageType.CancelSavedTabLaunch,
                                   SavedTabLaunchCancellation: cancellation);
    }

    private static bool TryParseGlobalSavedTabDefinition(JsonElement root, JsonElement payload,
                                                         bool requireSavedTabId,
                                                         out TerminalSavedTabRequestId requestId,
                                                         out TerminalSavedTabId savedTabId,
                                                         out string name, out string startingDirectory,
                                                         out TerminalShellKind shellKind)
    {
        requestId = default;
        savedTabId = default;
        name = string.Empty;
        startingDirectory = string.Empty;
        shellKind = default;
        if (TryParseGlobalRequestId(root, payload, out requestId) == false ||
            payload.TryGetProperty("name", out var nameElement) == false ||
            nameElement.ValueKind != JsonValueKind.String ||
            payload.TryGetProperty("startingDirectory", out var startingDirectoryElement) == false ||
            startingDirectoryElement.ValueKind != JsonValueKind.String ||
            payload.TryGetProperty("shellKind", out var shellKindElement) == false ||
            shellKindElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        name = nameElement.GetString() ?? string.Empty;
        startingDirectory = startingDirectoryElement.GetString() ?? string.Empty;
        if (TryParseShellKind(shellKindElement.GetString(), out shellKind) == false)
        {
            return false;
        }

        if (requireSavedTabId == true && TryParseSavedTabId(payload, out savedTabId) == false)
        {
            return false;
        }

        try
        {
            TerminalSavedTabContractValidator.ValidateDefinition(name, startingDirectory, shellKind);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseGlobalSavedTabTarget(JsonElement root, JsonElement payload,
                                                     out TerminalSavedTabRequestId requestId,
                                                     out TerminalSavedTabId savedTabId)
    {
        savedTabId = default;
        return TryParseGlobalRequestId(root, payload, out requestId) &&
               TryParseSavedTabId(payload, out savedTabId);
    }

    private static bool TryParseGlobalRequestId(JsonElement root, JsonElement payload,
                                                out TerminalSavedTabRequestId requestId)
    {
        requestId = default;
        if (root.TryGetProperty("sessionId", out _) == true || payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("requestId", out var requestIdElement) == false ||
            requestIdElement.ValueKind != JsonValueKind.String ||
            TryParseCompactGuid(requestIdElement.GetString(), out var identifier) == false)
        {
            return false;
        }

        requestId = new TerminalSavedTabRequestId(identifier);
        return true;
    }

    private static bool TryParseSavedTabId(JsonElement payload, out TerminalSavedTabId savedTabId)
    {
        savedTabId = default;
        if (payload.TryGetProperty("savedTabId", out var savedTabIdElement) == false ||
            savedTabIdElement.ValueKind != JsonValueKind.String ||
            TryParseCompactGuid(savedTabIdElement.GetString(), out var identifier) == false)
        {
            return false;
        }

        savedTabId = new TerminalSavedTabId(identifier);
        return true;
    }

    private static bool TryParseCompactGuid(string? value, out Guid identifier)
    {
        return Guid.TryParseExact(value, "N", out identifier) && identifier != Guid.Empty;
    }

    private static bool TryParseCompactRequestId(JsonElement payload, out Guid requestId)
    {
        requestId = default;
        return payload.TryGetProperty("requestId", out var requestIdElement) &&
               requestIdElement.ValueKind == JsonValueKind.String &&
               TryParseCompactGuid(requestIdElement.GetString(), out requestId);
    }

    private static bool TryParseTabRequestToken(JsonElement root, JsonElement payload,
                                                out TerminalTabRequestToken token)
    {
        token = default;
        if (TryParseSessionId(root, out var sessionId) == false || payload.ValueKind != JsonValueKind.Object ||
            TryParseCompactRequestId(payload, out var requestId) == false ||
            payload.TryGetProperty("sessionGeneration", out var generationElement) == false ||
            generationElement.TryGetInt64(out var sessionGeneration) == false || sessionGeneration < 1)
        {
            return false;
        }

        token = new TerminalTabRequestToken(new TerminalTabRequestId(requestId),
                                            new TerminalSessionReference(sessionId.Value, sessionGeneration));
        return true;
    }

    private static bool TryParseShellKind(string? value, out TerminalShellKind shellKind)
    {
        shellKind = value switch
        {
            "automatic" => TerminalShellKind.Automatic,
            "pwsh" => TerminalShellKind.Pwsh,
            "powershell" => TerminalShellKind.PowerShell,
            "cmd" => TerminalShellKind.Cmd,
            _ => default,
        };

        return value is "automatic" or "pwsh" or "powershell" or "cmd";
    }

    private static object CreateSavedTabsPayload(TerminalSavedTabsSnapshot snapshot, int runningTabCount)
    {
        var tabs = snapshot.Tabs
            .Select(tab => new
            {
                savedTabId = tab.SavedTabId.ToString(),
                tab.Name,
                tab.StartingDirectory,
                shellKind = FormatShellKind(tab.ShellKind),
            })
            .ToArray();

        return new
        {
            snapshot.SchemaVersion,
            maximumSavedTabs = TerminalSavedTabsContract.MaximumSavedTabs,
            runningTabCount,
            maximumRunningTabs = TerminalSavedTabsContract.MaximumRunningTabs,
            tabs,
        };
    }

    private static string FormatShellKind(TerminalShellKind shellKind)
    {
        return shellKind switch
        {
            TerminalShellKind.Automatic => "automatic",
            TerminalShellKind.Pwsh => "pwsh",
            TerminalShellKind.PowerShell => "powershell",
            TerminalShellKind.Cmd => "cmd",
            _ => throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind,
                                                       "The renderer cannot serialize an unsupported shell kind."),
        };
    }

    private static string FormatLaunchProfileKind(TerminalLaunchProfileKind kind)
    {
        return kind switch
        {
            TerminalLaunchProfileKind.BuiltInShell => "built-in-shell",
            TerminalLaunchProfileKind.WslDistribution => "wsl-distribution",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                                                       "The renderer cannot serialize an unsupported profile kind."),
        };
    }

    private static string FormatLaunchProfileQueryStatus(TerminalLaunchProfileQueryStatus status)
    {
        return status switch
        {
            TerminalLaunchProfileQueryStatus.Succeeded => "succeeded",
            TerminalLaunchProfileQueryStatus.WslUnavailable => "wsl-unavailable",
            TerminalLaunchProfileQueryStatus.WslDiscoveryFailed => "wsl-discovery-failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                                                       "The renderer cannot serialize an unsupported profile " +
                                                       "query status."),
        };
    }

    private static string FormatCollapsedHeightChangeStatus(TerminalCollapsedHeightChangeStatus status)
    {
        return status switch
        {
            TerminalCollapsedHeightChangeStatus.Applied => "applied",
            TerminalCollapsedHeightChangeStatus.Saved => "saved",
            TerminalCollapsedHeightChangeStatus.Reverted => "reverted",
            TerminalCollapsedHeightChangeStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                                                       "The renderer cannot serialize an unsupported height " +
                                                       "change status."),
        };
    }

    private static string FormatOperation(TerminalSavedTabOperation operation)
    {
        return operation switch
        {
            TerminalSavedTabOperation.Create => "create",
            TerminalSavedTabOperation.Update => "update",
            TerminalSavedTabOperation.Delete => "delete",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation,
                                                       "The renderer cannot serialize an unsupported " +
                                                       "saved tab operation."),
        };
    }

    private static string FormatOperationStatus(TerminalSavedTabOperationStatus status)
    {
        return status switch
        {
            TerminalSavedTabOperationStatus.Succeeded => "succeeded",
            TerminalSavedTabOperationStatus.Cancelled => "cancelled",
            TerminalSavedTabOperationStatus.SavedTabLimitReached => "saved-tab-limit-reached",
            TerminalSavedTabOperationStatus.SavedTabNotFound => "saved-tab-not-found",
            TerminalSavedTabOperationStatus.InvalidName => "invalid-name",
            TerminalSavedTabOperationStatus.InvalidStartingDirectory => "invalid-starting-directory",
            TerminalSavedTabOperationStatus.UnsupportedShell => "unsupported-shell",
            TerminalSavedTabOperationStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                                                       "The renderer cannot serialize an unsupported " +
                                                       "saved tab status."),
        };
    }

    private static string FormatLaunchStatus(TerminalSavedTabLaunchStatus status)
    {
        return status switch
        {
            TerminalSavedTabLaunchStatus.Started => "started",
            TerminalSavedTabLaunchStatus.DuplicateRequest => "duplicate-request",
            TerminalSavedTabLaunchStatus.Cancelled => "cancelled",
            TerminalSavedTabLaunchStatus.RunningTabLimitReached => "running-tab-limit-reached",
            TerminalSavedTabLaunchStatus.SavedTabNotFound => "saved-tab-not-found",
            TerminalSavedTabLaunchStatus.StartingDirectoryUnavailable => "starting-directory-unavailable",
            TerminalSavedTabLaunchStatus.ShellUnavailable => "shell-unavailable",
            TerminalSavedTabLaunchStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                                                       "The renderer cannot serialize an unsupported " +
                                                       "saved tab launch status."),
        };
    }

    private static void ValidateRunningTabCount(int runningTabCount)
    {
        if (runningTabCount < 0 || runningTabCount > TerminalSavedTabsContract.MaximumRunningTabs)
        {
            throw new ArgumentOutOfRangeException(nameof(runningTabCount), runningTabCount,
                                                  "The renderer running tab count is outside the supported range.");
        }
    }

    private static RendererMessage? ParseData(RendererMessageType type, JsonElement root, JsonElement payload,
                                              string propertyName = "data", int maximumLength = MaximumMessageLength)
    {
        if (TryParseSessionId(root, out var sessionId) == false)
        {
            return null;
        }

        if (payload.ValueKind != JsonValueKind.Object || payload.TryGetProperty(propertyName, out var dataElement) == false ||
            dataElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var data = dataElement.GetString();
        if (data is null || data.Length > maximumLength)
        {
            return null;
        }

        return new RendererMessage(type, sessionId, data);
    }

    private static RendererMessage? ParseResize(JsonElement root, JsonElement payload)
    {
        if (TryParseSessionId(root, out var sessionId) == false)
        {
            return null;
        }

        if (payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("columns", out var columnsElement) == false ||
            payload.TryGetProperty("rows", out var rowsElement) == false ||
            columnsElement.TryGetInt32(out var columns) == false || rowsElement.TryGetInt32(out var rows) == false ||
            columns < 2 || columns > 500 || rows < 1 || rows > 300)
        {
            return null;
        }

        return new RendererMessage(RendererMessageType.Resize, sessionId, null, columns, rows);
    }

    private static RendererMessage? ParseRendererReady(JsonElement root, JsonElement payload)
    {
        if (root.TryGetProperty("sessionId", out _) == true || payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("rendererInstanceId", out var instanceElement) == false ||
            instanceElement.ValueKind != JsonValueKind.String ||
            TryParseCompactGuid(instanceElement.GetString(), out var rendererInstanceId) == false)
        {
            return null;
        }

        return new RendererMessage(RendererMessageType.Ready, RendererInstanceId: rendererInstanceId);
    }

    private static RendererMessage? ParsePathDrop(JsonElement root, JsonElement payload)
    {
        if (TryParseSessionId(root, out var sessionId) == false || payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("rendererInstanceId", out var instanceElement) == false ||
            instanceElement.ValueKind != JsonValueKind.String ||
            TryParseCompactGuid(instanceElement.GetString(), out var rendererInstanceId) == false ||
            payload.TryGetProperty("sessionGeneration", out var generationElement) == false ||
            generationElement.TryGetInt64(out var generation) == false || generation < 1)
        {
            return null;
        }

        return new RendererMessage(RendererMessageType.DropPaths, sessionId,
                                   RendererInstanceId: rendererInstanceId, SessionGeneration: generation);
    }

    private static RendererMessage? ParseUrlOpen(JsonElement root, JsonElement payload)
    {
        if (TryParseSessionId(root, out var sessionId) == false || payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("rendererInstanceId", out var instanceElement) == false ||
            instanceElement.ValueKind != JsonValueKind.String ||
            TryParseCompactGuid(instanceElement.GetString(), out var rendererInstanceId) == false ||
            payload.TryGetProperty("sessionGeneration", out var generationElement) == false ||
            generationElement.TryGetInt64(out var generation) == false || generation < 1 ||
            payload.TryGetProperty("activation", out var activationElement) == false ||
            activationElement.ValueKind != JsonValueKind.String ||
            string.Equals(activationElement.GetString(), "ctrl-click", StringComparison.Ordinal) == false ||
            payload.TryGetProperty("url", out var urlElement) == false ||
            urlElement.ValueKind != JsonValueKind.String ||
            TerminalUrlOpenTarget.TryCreate(urlElement.GetString(), out var target) == false)
        {
            return null;
        }

        return new RendererMessage(RendererMessageType.OpenUrlRequest, sessionId,
                                   RendererInstanceId: rendererInstanceId, SessionGeneration: generation,
                                   UrlOpenTarget: target);
    }

    private static RendererMessage? ParseGlobal(RendererMessageType type, JsonElement root, JsonElement payload)
    {
        if (root.TryGetProperty("sessionId", out _) == true || payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new RendererMessage(type);
    }

    private static RendererMessage? ParseConfirmationResponse(JsonElement root, JsonElement payload)
    {
        if (TryParseSessionId(root, out var sessionId) == false || payload.ValueKind != JsonValueKind.Object ||
            payload.TryGetProperty("requestId", out var requestIdElement) == false ||
            requestIdElement.ValueKind != JsonValueKind.String ||
            payload.TryGetProperty("sessionGeneration", out var generationElement) == false ||
            generationElement.TryGetInt64(out var generation) == false ||
            payload.TryGetProperty("result", out var resultElement) == false ||
            resultElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var requestIdText = requestIdElement.GetString();
        if (Guid.TryParseExact(requestIdText, "N", out var requestId) == false || requestId == Guid.Empty ||
            generation < 1 || TryParseConfirmationResult(resultElement.GetString(), out var result) == false)
        {
            return null;
        }

        var token = new TerminalConfirmationToken(new TerminalConfirmationRequestId(requestId),
                                                  new TerminalSessionReference(sessionId.Value, generation));
        return new RendererMessage(RendererMessageType.ConfirmationResponse, sessionId,
                                   ConfirmationResponse: new TerminalConfirmationResponse(token, result));
    }

    private static RendererMessage? ParseSession(RendererMessageType type, JsonElement root, JsonElement payload)
    {
        if (TryParseSessionId(root, out var sessionId) == false || payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new RendererMessage(type, sessionId);
    }

    private static bool TryParseSessionId(JsonElement root, out TerminalSessionId sessionId)
    {
        sessionId = default;
        if (root.TryGetProperty("sessionId", out var sessionIdElement) == false ||
            sessionIdElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = sessionIdElement.GetString();
        if (Guid.TryParseExact(value, "N", out var identifier) == false || identifier == Guid.Empty)
        {
            return false;
        }

        sessionId = new TerminalSessionId(identifier);

        return true;
    }

    private static void ValidateType(string type, HashSet<string> allowedTypes)
    {
        if (string.IsNullOrWhiteSpace(type) == true)
        {
            throw new ArgumentException("The renderer message type cannot be empty.", nameof(type));
        }

        if (allowedTypes.Contains(type) == false)
        {
            throw new ArgumentException("The renderer message type is not valid for this message scope.", nameof(type));
        }
    }

    private static bool TryParseConfirmationResult(string? value, out TerminalConfirmationResult result)
    {
        result = value switch
        {
            "confirmed" => TerminalConfirmationResult.Confirmed,
            "cancelled" => TerminalConfirmationResult.Cancelled,
            _ => default,
        };

        return value is "confirmed" or "cancelled";
    }

    private static bool TryParseCollapsedHeightChangePhase(string? value,
                                                           out TerminalCollapsedHeightChangePhase phase)
    {
        phase = value switch
        {
            "preview" => TerminalCollapsedHeightChangePhase.Preview,
            "commit" => TerminalCollapsedHeightChangePhase.Commit,
            _ => default,
        };

        return value is "preview" or "commit";
    }
}
