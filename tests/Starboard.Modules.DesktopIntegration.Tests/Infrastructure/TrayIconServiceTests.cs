using System.Drawing;
using Starboard.Modules.DesktopIntegration.Infrastructure;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

#pragma warning disable CA1707

[TestClass]
public sealed class TrayIconServiceTests
{
    [TestMethod]
    public void LoadEmbedded_WithPackagedAsset_LoadsStarboardIcon()
    {
        using var iconAsset = TrayIconAsset.LoadEmbedded();

        Assert.IsFalse(iconAsset.IsFallback);
        Assert.AreNotSame(SystemIcons.Application, iconAsset.Icon);
        Assert.IsTrue(iconAsset.Icon.Width >= 16);
        Assert.IsTrue(iconAsset.Icon.Height >= 16);
    }

    [TestMethod]
    public void LoadEmbedded_AfterPreviousAssetIsDisposed_LoadsStarboardIconAgain()
    {
        using (var firstIconAsset = TrayIconAsset.LoadEmbedded())
        {
            Assert.IsFalse(firstIconAsset.IsFallback);
        }

        using var restoredIconAsset = TrayIconAsset.LoadEmbedded();

        Assert.IsFalse(restoredIconAsset.IsFallback);
        Assert.AreNotSame(SystemIcons.Application, restoredIconAsset.Icon);
    }

    [TestMethod]
    public void Load_WithMissingAsset_UsesApplicationIcon()
    {
        using var iconAsset = TrayIconAsset.Load(() => null);

        Assert.IsTrue(iconAsset.IsFallback);
        Assert.AreSame(SystemIcons.Application, iconAsset.Icon);
    }

    [TestMethod]
    public void Load_WithInvalidAsset_DisposesStreamAndUsesApplicationIcon()
    {
        var stream = new TrackingMemoryStream([0x00, 0x01, 0x02, 0x03]);

        using var iconAsset = TrayIconAsset.Load(() => stream);

        Assert.IsTrue(iconAsset.IsFallback);
        Assert.AreSame(SystemIcons.Application, iconAsset.Icon);
        Assert.IsTrue(stream.IsDisposed);
    }

    [TestMethod]
    public void Dispose_WithLoadedAsset_DisposesOwnedStream()
    {
        using var embeddedStream = typeof(TrayIconService).Assembly.GetManifestResourceStream(
            "Starboard.Modules.DesktopIntegration.Assets.Starboard.ico");
        Assert.IsNotNull(embeddedStream);
        using var copy = new MemoryStream();
        embeddedStream.CopyTo(copy);
        var stream = new TrackingMemoryStream(copy.ToArray());
        var iconAsset = TrayIconAsset.Load(() => stream);

        Assert.IsFalse(stream.IsDisposed);

        iconAsset.Dispose();
        iconAsset.Dispose();

        Assert.IsTrue(stream.IsDisposed);
    }

    private sealed class TrackingMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        internal bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}

#pragma warning restore CA1707
