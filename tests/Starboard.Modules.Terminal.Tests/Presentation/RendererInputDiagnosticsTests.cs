using Starboard.Modules.Terminal.Presentation;

namespace Starboard.Modules.Terminal.Tests.Presentation;

[TestClass]
public sealed class RendererInputDiagnosticsTests
{
    [TestMethod]
    public void TryParseBoundedStateProducesOnlyFixedCategoriesAndNumbers()
    {
        const string json = """
            {"version":2,"type":"input-diagnostic","payload":{"route":1,"selection":1,"focus":2,"modal":0,"suppressed":0,"capture":0}}
            """;
        Assert.IsTrue(RendererInputDiagnostics.TryParse(json, out var operation));
        Assert.AreEqual("Input.R1.S1.F2.M0.U0.C0", operation);
        Assert.IsTrue(operation.Length <= 64);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{")]
    [DataRow("{\"version\":2,\"type\":\"input-diagnostic\",\"payload\":{\"route\":\"secret\"}}")]
    [DataRow("{\"version\":2,\"type\":\"input-diagnostic\",\"payload\":{\"route\":6,\"selection\":1,\"focus\":2,\"modal\":0,\"suppressed\":0,\"capture\":0}}")]
    [DataRow("{\"version\":2,\"type\":\"input-diagnostic\",\"payload\":{\"route\":1,\"selection\":1,\"focus\":2,\"modal\":0,\"suppressed\":0,\"text\":\"secret\"}}")]
    public void TryParseUnexpectedShapeOrTextIsRejected(string json)
    {
        Assert.IsFalse(RendererInputDiagnostics.TryParse(json, out var operation));
        Assert.AreEqual(string.Empty, operation);
    }
}
