using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServerControlCenter.Views.Features;
using Xunit;

namespace ServerControlCenter.UnitTests;

public sealed class WpfResourceTests
{
    [Fact]
    public void CollectionResources_LoadWithAllDependencies()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var uri = new Uri(
                    "/ServerControlCenter;component/Resources/CollectionControls.xaml",
                    UriKind.Relative);
                var resources = Assert.IsType<ResourceDictionary>(Application.LoadComponent(uri));

                Assert.IsType<SolidColorBrush>(resources["AppBackgroundBrush"]);
                Assert.IsType<Style>(resources["PrimaryButtonStyle"]);
                Assert.IsType<Style>(resources[typeof(DataGrid)]);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void FeatureViews_LoadWithApplicationResources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? application = null;

            try
            {
                application = new Application();
                var uri = new Uri(
                    "/ServerControlCenter;component/Resources/CollectionControls.xaml",
                    UriKind.Relative);
                var resources = Assert.IsType<ResourceDictionary>(Application.LoadComponent(uri));
                application.Resources.MergedDictionaries.Add(resources);

                _ = new ServersSidebarView();
                _ = new DashboardHeaderView();
                _ = new MonitoringSummaryView();
                _ = new TerminalView();
                _ = new OperationsView();
                _ = new CommandsView();
                _ = new ServerInspectorView();
                _ = new RemoteFilesView();
                _ = new LogsView();
                _ = new OverlayHostView();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                application?.Shutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}