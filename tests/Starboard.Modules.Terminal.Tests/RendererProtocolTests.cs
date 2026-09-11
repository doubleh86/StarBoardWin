using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class RendererProtocolTests
{
    private static readonly TerminalSessionId SessionId = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));

    [TestMethod]
    public void TryParseWithValidResizeReturnsDimensions()
    {
        const string Json = """
            {"version":2,"type":"resize","sessionId":"10000000000000000000000000000001","payload":{"columns":132,"rows":42}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.Resize, message.Type);
        Assert.AreEqual(SessionId, message.SessionId);
        Assert.AreEqual(132, message.Columns);
        Assert.AreEqual(42, message.Rows);
    }

    [TestMethod]
    public void TryParseRendererReadyCarriesRendererInstanceIdentity()
    {
        const string Json = """
            {"version":2,"type":"ready","payload":{"rendererInstanceId":"20000000000000000000000000000001"}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.Ready, message.Type);
        Assert.AreEqual(Guid.Parse("20000000-0000-0000-0000-000000000001"), message.RendererInstanceId);
    }

    [TestMethod]
    public void TryParsePathDropCarriesExactRendererAndSessionLifetime()
    {
        const string Json = """
            {"version":2,"type":"drop-paths","sessionId":"10000000000000000000000000000001","payload":{"rendererInstanceId":"20000000000000000000000000000001","sessionGeneration":7}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.DropPaths, message.Type);
        Assert.AreEqual(SessionId, message.SessionId);
        Assert.AreEqual(Guid.Parse("20000000-0000-0000-0000-000000000001"), message.RendererInstanceId);
        Assert.AreEqual(7, message.SessionGeneration);
    }

    [TestMethod]
    public void TryParseUrlOpenCarriesValidatedTargetAndExactLifetime()
    {
        const string Json = """
            {"version":2,"type":"open-url-request","sessionId":"10000000000000000000000000000001","payload":{"rendererInstanceId":"20000000000000000000000000000001","sessionGeneration":7,"activation":"ctrl-click","url":"https://example.com/path?q=one"}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.OpenUrlRequest, message.Type);
        Assert.AreEqual(SessionId, message.SessionId);
        Assert.AreEqual(Guid.Parse("20000000-0000-0000-0000-000000000001"), message.RendererInstanceId);
        Assert.AreEqual(7, message.SessionGeneration);
        Assert.AreEqual("https://example.com/path?q=one", message.UrlOpenTarget?.AbsoluteUri);
    }

    [TestMethod]
    [DataRow("click", "https://example.com")]
    [DataRow("ctrl-click", "file:///C:/Windows/System32/calc.exe")]
    [DataRow("ctrl-click", "starboard://settings")]
    [DataRow("ctrl-click", "https://user@example.com/private")]
    [DataRow("ctrl-click", "not a URL")]
    public void TryParseUrlOpenWithoutExplicitSafeWebActivationRejectsMessage(string activation, string url)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = 2,
                                                type = "open-url-request",
                                                sessionId = SessionId.ToString(),
                                                payload = new
                                                {
                                                    rendererInstanceId = "20000000000000000000000000000001",
                                                    sessionGeneration = 1,
                                                    activation,
                                                    url,
                                                },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    [DataRow("00000000000000000000000000000000", 1)]
    [DataRow("20000000000000000000000000000001", 0)]
    public void TryParsePathDropInvalidLifetimeIdentityRejectsMessage(string rendererInstanceId,
                                                                      long sessionGeneration)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = 2,
                                                type = "drop-paths",
                                                sessionId = SessionId.ToString(),
                                                payload = new { rendererInstanceId, sessionGeneration },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    public void TryParseWithUnknownVersionRejectsMessage()
    {
        const string Json = """
            {"version":1,"type":"ready","payload":{}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    public void TryParseWithOversizedInputRejectsMessage()
    {
        var data = new string('x', RendererProtocol.MaximumMessageLength + 1);
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = RendererProtocol.CurrentVersion,
                                                sessionId = SessionId.ToString(),
                                                type = "input",
                                                payload = new { data },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    public void TryParseWithNonStringInputRejectsMessageWithoutThrowing()
    {
        const string Json = """
            {"version":2,"type":"input","sessionId":"10000000000000000000000000000001","payload":{"data":42}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    [DataRow("input", "Input")]
    [DataRow("copy", "Copy")]
    public void TryParseDataMessageReturnsTargetSessionAndData(string type, string expectedType)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = 2,
                                                type,
                                                sessionId = SessionId.ToString(),
                                                payload = new { data = "한글 input" },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(expectedType, message.Type.ToString());
        Assert.AreEqual(SessionId, message.SessionId);
        Assert.AreEqual("한글 input", message.Data);
    }

    [TestMethod]
    [DataRow("input", "{\"data\":\"text\"}")]
    [DataRow("resize", "{\"columns\":80,\"rows\":24}")]
    [DataRow("copy", "{\"data\":\"text\"}")]
    [DataRow("paste-request", "{}")]
    [DataRow("drop-paths", "{\"rendererInstanceId\":\"20000000000000000000000000000001\",\"sessionGeneration\":1}")]
    [DataRow("open-url-request", "{\"rendererInstanceId\":\"20000000000000000000000000000001\",\"sessionGeneration\":1,\"activation\":\"ctrl-click\",\"url\":\"https://example.com\"}")]
    [DataRow("select-session", "{}")]
    [DataRow("close-session", "{}")]
    [DataRow("restart-session", "{}")]
    [DataRow("session-error", "{}")]
    public void TryParseSessionMessageWithoutIdentifierRejectsMessage(string type, string payload)
    {
        var json = $"{{\"version\":2,\"type\":\"{type}\",\"payload\":{payload}}}";

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-a-guid")]
    [DataRow("00000000000000000000000000000000")]
    [DataRow("10000000-0000-0000-0000-000000000001")]
    public void TryParseSessionMessageWithInvalidIdentifierRejectsMessage(string sessionId)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = 2,
                                                type = "paste-request",
                                                sessionId,
                                                payload = new { },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    public void TryParseGlobalMessageWithSessionIdentifierRejectsMessage()
    {
        const string Json = """
            {"version":2,"type":"ready","sessionId":"10000000000000000000000000000001","payload":{}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    [DataRow("select-session", "SelectSession")]
    [DataRow("paste-request", "PasteRequest")]
    [DataRow("close-session", "CloseSession")]
    [DataRow("restart-session", "RestartSession")]
    [DataRow("session-error", "SessionError")]
    public void TryParseSessionCommandReturnsItsIdentifier(string type, string expectedType)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = 2,
                                                type,
                                                sessionId = SessionId.ToString(),
                                                payload = new { },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(expectedType, message.Type.ToString());
        Assert.AreEqual(SessionId, message.SessionId);
    }

    [TestMethod]
    public void TryParseMessageWithoutPayloadRejectsMessage()
    {
        const string Json = """
            {"version":2,"type":"ready"}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    public void SerializeSessionMessageIncludesProtocolVersionAndIdentifier()
    {
        var json = RendererProtocol.SerializeSessionMessage("output", SessionId, new { data = "hello" });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual(2, root.GetProperty("version").GetInt32());
        Assert.AreEqual("output", root.GetProperty("type").GetString());
        Assert.AreEqual(SessionId.ToString(), root.GetProperty("sessionId").GetString());
        Assert.AreEqual("hello", root.GetProperty("payload").GetProperty("data").GetString());
    }

    [TestMethod]
    public void SerializeGlobalMessageOmitsSessionIdentifier()
    {
        var json = RendererProtocol.SerializeGlobalMessage("initialize", new { });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual(2, root.GetProperty("version").GetInt32());
        Assert.IsFalse(root.TryGetProperty("sessionId", out _));
    }

    [TestMethod]
    public void SerializeAppearanceMessageUsesGlobalScope()
    {
        var json = RendererProtocol.SerializeGlobalMessage("apply-appearance",
                                                           new { fontFamily = "Cascadia Mono", fontSize = 15 });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual("apply-appearance", root.GetProperty("type").GetString());
        Assert.IsFalse(root.TryGetProperty("sessionId", out _));
        Assert.AreEqual("Cascadia Mono", root.GetProperty("payload").GetProperty("fontFamily").GetString());
    }

    [TestMethod]
    [DataRow("rename-session", "name", "서버", "RenameSession")]
    [DataRow("move-session", "direction", "left", "MoveSession")]
    [DataRow("set-starting-directory", "startingDirectory", "C:\\Work\\Server", "SetStartingDirectory")]
    public void TryParseWorkspaceTabCommandReturnsSessionScopedPayload(string type, string propertyName, string value,
                                                                       string expectedType)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = RendererProtocol.CurrentVersion,
                                                type,
                                                sessionId = SessionId.ToString(),
                                                payload = new Dictionary<string, string> { [propertyName] = value },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(expectedType, message.Type.ToString());
        Assert.AreEqual(SessionId, message.SessionId);
        Assert.AreEqual(value, message.Data);
    }

    [TestMethod]
    public void SerializeWorkspaceSaveStatusUsesGlobalScope()
    {
        var json = RendererProtocol.SerializeGlobalMessage("workspace-save-status",
                                                           new { state = "failed", message = "구성을 저장하지 못했습니다." });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual("workspace-save-status", root.GetProperty("type").GetString());
        Assert.IsFalse(root.TryGetProperty("sessionId", out _));
        Assert.AreEqual("failed", root.GetProperty("payload").GetProperty("state").GetString());
    }

    [TestMethod]
    public void SerializeSessionMessageWithEmptyIdentifierThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RendererProtocol.SerializeSessionMessage("output", default, new { data = "hello" }));
    }

    [TestMethod]
    public void SerializeGlobalMessageWithSessionTargetTypeThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RendererProtocol.SerializeGlobalMessage("output", new { data = "hello" }));
    }

    [TestMethod]
    public void SerializeSessionMessageWithGlobalTypeThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RendererProtocol.SerializeSessionMessage("initialize", SessionId, new { }));
        Assert.ThrowsExactly<ArgumentException>(() => RendererProtocol.SerializeSessionMessage("apply-appearance", SessionId, new { }));
    }

    [TestMethod]
    [DataRow("confirmed", TerminalConfirmationResult.Confirmed)]
    [DataRow("cancelled", TerminalConfirmationResult.Cancelled)]
    public void TryParseConfirmationResponseReturnsCorrelationAndResult(
        string result, TerminalConfirmationResult expectedResult)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = RendererProtocol.CurrentVersion,
                                                type = "confirmation-response",
                                                sessionId = SessionId.ToString(),
                                                payload = new
                                                {
                                                    requestId = "20000000000000000000000000000001",
                                                    sessionGeneration = 7,
                                                    result,
                                                },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.ConfirmationResponse, message.Type);
        Assert.AreEqual(SessionId, message.SessionId);
        Assert.IsNotNull(message.ConfirmationResponse);
        Assert.AreEqual(new TerminalConfirmationRequestId(
                            Guid.Parse("20000000-0000-0000-0000-000000000001")),
                        message.ConfirmationResponse.Token.RequestId);
        Assert.AreEqual(SessionId.Value, message.ConfirmationResponse.Token.Session.SessionId);
        Assert.AreEqual(7L, message.ConfirmationResponse.Token.Session.Generation);
        Assert.AreEqual(expectedResult, message.ConfirmationResponse.Result);
    }

    [TestMethod]
    [DataRow("00000000000000000000000000000000", 1, "confirmed")]
    [DataRow("not-a-request", 1, "confirmed")]
    [DataRow("20000000000000000000000000000001", 0, "confirmed")]
    [DataRow("20000000000000000000000000000001", 1, "dismissed")]
    public void TryParseConfirmationResponseWithInvalidCorrelationRejectsMessage(string requestId,
                                                                                 long sessionGeneration,
                                                                                 string result)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = RendererProtocol.CurrentVersion,
                                                type = "confirmation-response",
                                                sessionId = SessionId.ToString(),
                                                payload = new { requestId, sessionGeneration, result },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    [DataRow("confirmation-request")]
    [DataRow("confirmation-cancel")]
    [DataRow("path-drop-result")]
    [DataRow("url-open-result")]
    [DataRow("new-output-state")]
    public void SerializeSafetyStateMessageUsesSessionScope(string type)
    {
        var json = RendererProtocol.SerializeSessionMessage(type, SessionId, new { sessionGeneration = 2 });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual(type, root.GetProperty("type").GetString());
        Assert.AreEqual(SessionId.ToString(), root.GetProperty("sessionId").GetString());
        Assert.AreEqual(2, root.GetProperty("payload").GetProperty("sessionGeneration").GetInt32());
    }

    [TestMethod]
    public void TryParseCreateSavedTabFixtureReturnsValidatedGlobalRequest()
    {
        const string Json = """
            {"version":2,"type":"create-saved-tab","payload":{"requestId":"40000000000000000000000000000001","name":"서버","startingDirectory":"C:\\Work\\Server","shellKind":"pwsh"}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.CreateSavedTab, message.Type);
        Assert.IsNull(message.SessionId);
        Assert.IsNotNull(message.SavedTabCreateRequest);
        Assert.AreEqual("40000000000000000000000000000001", message.SavedTabCreateRequest.RequestId.ToString());
        Assert.AreEqual("서버", message.SavedTabCreateRequest.Name);
        Assert.AreEqual("C:\\Work\\Server", message.SavedTabCreateRequest.StartingDirectory);
        Assert.AreEqual(TerminalShellKind.Pwsh, message.SavedTabCreateRequest.ShellKind);
    }

    [TestMethod]
    public void TryParseUpdateSavedTabFixtureReturnsStableIdentity()
    {
        const string Json = """
            {"version":2,"type":"update-saved-tab","payload":{"requestId":"40000000000000000000000000000001","savedTabId":"30000000000000000000000000000001","name":"웹","startingDirectory":"D:\\Work\\Web","shellKind":"cmd"}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.UpdateSavedTab, message.Type);
        Assert.IsNotNull(message.SavedTabUpdateRequest);
        Assert.AreEqual("30000000000000000000000000000001",
                        message.SavedTabUpdateRequest.SavedTab.SavedTabId.ToString());
        Assert.AreEqual("웹", message.SavedTabUpdateRequest.SavedTab.Name);
        Assert.AreEqual(TerminalShellKind.Cmd, message.SavedTabUpdateRequest.SavedTab.ShellKind);
    }

    [TestMethod]
    [DataRow("delete-saved-tab", "DeleteSavedTab")]
    [DataRow("launch-saved-tab", "LaunchSavedTab")]
    public void TryParseSavedTabTargetFixtureReturnsCorrelatedGlobalRequest(string type, string expectedType)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = RendererProtocol.CurrentVersion,
                                                type,
                                                payload = new
                                                {
                                                    requestId = "40000000000000000000000000000001",
                                                    savedTabId = "30000000000000000000000000000001",
                                                },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(expectedType, message.Type.ToString());
        Assert.IsNull(message.SessionId);
        var requestId = message.SavedTabDeleteRequest?.RequestId ?? message.SavedTabLaunchRequest?.RequestId;
        var savedTabId = message.SavedTabDeleteRequest?.SavedTabId ?? message.SavedTabLaunchRequest?.SavedTabId;
        Assert.AreEqual("40000000000000000000000000000001", requestId?.ToString());
        Assert.AreEqual("30000000000000000000000000000001", savedTabId?.ToString());
    }

    [TestMethod]
    public void TryParseCancelSavedTabLaunchFixtureCarriesOnlyPendingRequestIdentity()
    {
        const string Json = """
            {"version":2,"type":"cancel-saved-tab-launch","payload":{"requestId":"40000000000000000000000000000001"}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.CancelSavedTabLaunch, message.Type);
        Assert.IsNotNull(message.SavedTabLaunchCancellation);
        Assert.AreEqual("40000000000000000000000000000001",
                        message.SavedTabLaunchCancellation.RequestId.ToString());
    }

    [TestMethod]
    [DataRow("create-saved-tab")]
    [DataRow("update-saved-tab")]
    [DataRow("delete-saved-tab")]
    [DataRow("launch-saved-tab")]
    [DataRow("cancel-saved-tab-launch")]
    public void TryParseSavedTabCommandWithSessionIdentifierRejectsMessage(string type)
    {
        var json = JsonSerializer.Serialize(new
                                            {
                                                version = RendererProtocol.CurrentVersion,
                                                type,
                                                sessionId = SessionId.ToString(),
                                                payload = new
                                                {
                                                    requestId = "40000000000000000000000000000001",
                                                    savedTabId = "30000000000000000000000000000001",
                                                    name = "서버",
                                                    startingDirectory = "C:\\Work",
                                                    shellKind = "automatic",
                                                },
                                            });

        var parsed = RendererProtocol.TryParse(json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    [DataRow("saved-tabs-snapshot")]
    [DataRow("saved-tab-operation-result")]
    [DataRow("saved-tab-launch-result")]
    public void SerializeSavedTabHostFixtureUsesGlobalScope(string type)
    {
        var json = RendererProtocol.SerializeGlobalMessage(type,
                                                           new
                                                           {
                                                               requestId = "40000000000000000000000000000001",
                                                               maximumSavedTabs = 20,
                                                               maximumRunningTabs = 8,
                                                           });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual(RendererProtocol.CurrentVersion, root.GetProperty("version").GetInt32());
        Assert.AreEqual(type, root.GetProperty("type").GetString());
        Assert.IsFalse(root.TryGetProperty("sessionId", out _));
        Assert.AreEqual(20, root.GetProperty("payload").GetProperty("maximumSavedTabs").GetInt32());
        Assert.AreEqual(8, root.GetProperty("payload").GetProperty("maximumRunningTabs").GetInt32());
    }

    [TestMethod]
    public void SerializeSavedTabsSnapshotFixtureUsesStableNamesAndStringIdentifiers()
    {
        var savedTab = new TerminalSavedTab(
            new TerminalSavedTabId(Guid.Parse("30000000-0000-0000-0000-000000000001")),
            "서버", "C:\\Work\\Server", TerminalShellKind.PowerShell);
        var snapshot = new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion, [savedTab]);

        var json = RendererProtocol.SerializeSavedTabsSnapshot(snapshot, runningTabCount: 3);
        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.GetProperty("payload");
        var tab = payload.GetProperty("tabs")[0];

        Assert.AreEqual(1, payload.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(20, payload.GetProperty("maximumSavedTabs").GetInt32());
        Assert.AreEqual(3, payload.GetProperty("runningTabCount").GetInt32());
        Assert.AreEqual(8, payload.GetProperty("maximumRunningTabs").GetInt32());
        Assert.AreEqual("30000000000000000000000000000001", tab.GetProperty("savedTabId").GetString());
        Assert.AreEqual("서버", tab.GetProperty("name").GetString());
        Assert.AreEqual("C:\\Work\\Server", tab.GetProperty("startingDirectory").GetString());
        Assert.AreEqual("powershell", tab.GetProperty("shellKind").GetString());
    }

    [TestMethod]
    public void SerializeSavedTabLaunchResultFixturePreservesCorrelationAndRuntimeLimit()
    {
        var requestId = new TerminalSavedTabRequestId(
            Guid.Parse("40000000-0000-0000-0000-000000000001"));
        var savedTabId = new TerminalSavedTabId(
            Guid.Parse("30000000-0000-0000-0000-000000000001"));
        var session = new TerminalSessionReference(SessionId.Value, 4);
        var result = new TerminalSavedTabLaunchResult(requestId, savedTabId,
                                                      TerminalSavedTabLaunchStatus.Started, session, null);

        var json = RendererProtocol.SerializeSavedTabLaunchResult(result, runningTabCount: 8);
        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.GetProperty("payload");

        Assert.AreEqual("saved-tab-launch-result", document.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("40000000000000000000000000000001", payload.GetProperty("requestId").GetString());
        Assert.AreEqual("30000000000000000000000000000001", payload.GetProperty("savedTabId").GetString());
        Assert.AreEqual("started", payload.GetProperty("status").GetString());
        Assert.AreEqual(SessionId.ToString(), payload.GetProperty("sessionId").GetString());
        Assert.AreEqual(4, payload.GetProperty("sessionGeneration").GetInt64());
        Assert.AreEqual(8, payload.GetProperty("runningTabCount").GetInt32());
        Assert.AreEqual(8, payload.GetProperty("maximumRunningTabs").GetInt32());
    }

    [TestMethod]
    public void SerializeSavedTabOperationResultFixtureUsesKebabCaseStatus()
    {
        var requestId = new TerminalSavedTabRequestId(
            Guid.Parse("40000000-0000-0000-0000-000000000001"));
        var result = new TerminalSavedTabOperationResult(requestId, TerminalSavedTabOperation.Create,
                                                         TerminalSavedTabOperationStatus.SavedTabLimitReached,
                                                         null, "저장 한도에 도달했습니다.");

        var json = RendererProtocol.SerializeSavedTabOperationResult(result, runningTabCount: 2);
        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.GetProperty("payload");

        Assert.AreEqual("create", payload.GetProperty("operation").GetString());
        Assert.AreEqual("saved-tab-limit-reached", payload.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("snapshot").ValueKind);
        Assert.AreEqual("저장 한도에 도달했습니다.", payload.GetProperty("failureMessage").GetString());
    }
}
