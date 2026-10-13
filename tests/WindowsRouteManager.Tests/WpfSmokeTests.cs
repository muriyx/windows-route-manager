using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using windows_route_manager;
using windows_route_manager.ViewModels;
using windows_route_manager.Views;

namespace WindowsRouteManager.Tests;

public sealed class WpfSmokeTests
{
    [Fact]
    public async Task WindowsLoadAndRenderWithFakeServicesWithoutBindingErrors()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var bindingErrors = new StringWriter();
            using var listener = new TextWriterTraceListener(bindingErrors);
            try
            {
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                var app = new App();
                app.InitializeComponent();
                var fake = new ViewModelTests.FakeVpn();
                fake.Profiles[0] = fake.Profiles[0] with
                {
                    ConnectionStatus = "Connected",
                    SplitTunneling = true,
                    Routes = [ViewModelTests.Route("10.0.0.0/8"), ViewModelTests.Route("172.16.0.0/12") with { Metric = 15 }, ViewModelTests.Route("192.168.100.0/24")]
                };
                var ui = new ViewModelTests.FakeUi();
                var vm = new VpnRoutesViewModel(fake, fake, ui);
                // The fake completes synchronously, so no dispatcher pump or real system read is needed.
                Assert.True(vm.RefreshCommand.ExecuteAsync().IsCompletedSuccessfully);
                var window = new MainWindow(vm);
                Render((FrameworkElement)window.Content, 1100, 760, "vpn-routes");
                Render((FrameworkElement)window.Content, 800, 640, "vpn-routes-small");
                var tabs = Descendants((FrameworkElement)window.Content).OfType<TabControl>().Single();
                var systemRoutesTab = Assert.Single(tabs.Items.Cast<TabItem>(), tab => Equals(tab.Header, "System Routes"));
                Assert.Null(systemRoutesTab.Content);
                Assert.Equal(3, Descendants((FrameworkElement)window.Content).OfType<DataGrid>().Single().Items.Count);
                var editor = new RouteEditorWindow(null);
                ((RouteEditorViewModel)editor.DataContext).Destination = "192.168.1.25/24";
                Render((FrameworkElement)editor.Content, 490, 410, "route-editor");
                editor.Close();
                window.Close();
                listener.Flush();
                Assert.Empty(ui.Errors);
                Assert.Equal("", bindingErrors.ToString());
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Render(FrameworkElement content, int width, int height, string name)
    {
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        content.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        content.UpdateLayout();
        Assert.True(content.IsMeasureValid && content.IsArrangeValid);
        var directory = Environment.GetEnvironmentVariable("WRM_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(244, 246, 250)), null, new Rect(0, 0, width, height));
                context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
            }
            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, $"{name}-{scale * 100:0}.png"));
            encoder.Save(output);
        }
    }
}
