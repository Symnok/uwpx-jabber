using Data_Manager2.Classes;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UWP_XMPP_Client.Pages.SettingsPages
{
    public sealed partial class BackgroundTasksSettingsPage : Page
    {
        public BackgroundTasksSettingsPage()
        {
            this.InitializeComponent();
            Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += AbstractBackRequestPage_BackRequested;
            loadSettings();
        }

        private void loadSettings()
        {
            disablePush_tgls.IsOn = Settings.getSettingBoolean(SettingsConsts.DISABLE_PUSH);
        }

        private void AbstractBackRequestPage_BackRequested(object sender, Windows.UI.Core.BackRequestedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                return;
            }
            if (rootFrame.CanGoBack && e.Handled == false)
            {
                e.Handled = true;
                rootFrame.GoBack();
            }
        }

        private void disablePush_tgls_Toggled(object sender, RoutedEventArgs e)
        {
            Settings.setSetting(SettingsConsts.DISABLE_PUSH, disablePush_tgls.IsOn);
        }
    }
}
