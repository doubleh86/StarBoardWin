using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Windows.Shell;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class ShortcutGuideWindowIntegrationTests
{
    [TestMethod]
    [DataRow("button")]
    [DataRow("escape")]
    [DataRow("window")]
    [Timeout(10_000)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707", Justification = "Repository test naming convention.")]
    public async Task Close_ModelessGuide_ClosesOnlyGuide(string closeAction)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Window? panel = null;
            ShortcutGuideWindow? guide = null;
            try
            {
                panel = new Window { ShowActivated = false, ShowInTaskbar = false, Width = 100, Height = 100 };
                panel.Show();
                var expand = new GlobalShortcutRegistrationState("Ctrl+Alt+E", "Ctrl+Alt+E", GlobalShortcutRegistrationStatus.Registered);
                var activation = new GlobalShortcutRegistrationState("Ctrl+Alt+S", "Ctrl+Alt+S", GlobalShortcutRegistrationStatus.Registered);
                var snapshot = new GlobalShortcutRegistrationSnapshot(expand, activation);
                guide = new ShortcutGuideWindow(snapshot) { ShowActivated = false, ShowInTaskbar = false };
                var closedCount = 0;
                guide.Closed += (_, _) => closedCount++;
                guide.Show();
                var closeButton = (Button)guide.FindName("CloseButton");

                var otherKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(guide), 0, Key.A)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };
                closeButton.RaiseEvent(otherKey);
                Assert.IsTrue(guide.IsVisible);
                Assert.IsFalse(otherKey.Handled);

                switch (closeAction)
                {
                    case "button":
                        closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "escape":
                        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(guide), 0, Key.Escape)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        closeButton.RaiseEvent(escape);
                        Assert.IsTrue(escape.Handled);
                        break;
                    case "window":
                        guide.Close();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(closeAction));
                }

                Assert.AreEqual(1, closedCount);
                Assert.IsFalse(guide.IsVisible);
                Assert.IsTrue(panel.IsVisible);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                guide?.Close();
                panel?.Close();
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(1)));
    }
}
