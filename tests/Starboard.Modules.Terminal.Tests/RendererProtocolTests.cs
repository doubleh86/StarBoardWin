using System.Text.Json;
using Starboard.Modules.Terminal.Application;
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
}
