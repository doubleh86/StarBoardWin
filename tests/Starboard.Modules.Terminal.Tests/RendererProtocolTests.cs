using System.Text.Json;
using Starboard.Modules.Terminal.Application;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class RendererProtocolTests
{
    [TestMethod]
    public void TryParseWithValidResizeReturnsDimensions()
    {
        const string Json = """
            {"version":1,"type":"resize","payload":{"columns":132,"rows":42}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(message);
        Assert.AreEqual(RendererMessageType.Resize, message.Type);
        Assert.AreEqual(132, message.Columns);
        Assert.AreEqual(42, message.Rows);
    }

    [TestMethod]
    public void TryParseWithUnknownVersionRejectsMessage()
    {
        const string Json = """
            {"version":2,"type":"ready","payload":{}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }

    [TestMethod]
    public void TryParseWithOversizedInputRejectsMessage()
    {
        var data = new string('x', RendererProtocol.MaximumMessageLength + 1);
        var json = JsonSerializer.Serialize(
            new
            {
                version = 1,
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
            {"version":1,"type":"input","payload":{"data":42}}
            """;

        var parsed = RendererProtocol.TryParse(Json, out var message);

        Assert.IsFalse(parsed);
        Assert.IsNull(message);
    }
}
