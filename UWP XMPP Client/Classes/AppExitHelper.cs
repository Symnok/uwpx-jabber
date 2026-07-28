using Data_Manager2.Classes;
using Logging;
using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace UWP_XMPP_Client.Classes
{
    /// <summary>
    /// Full shutdown of the app: everything this process holds is released and
    /// every background registration is removed, so nothing runs until the user
    /// launches the app again.
    ///
    /// This is what BACK on the main screen and the Exit tile in the settings
    /// do. It is deliberately NOT what the HOME button does - that still just
    /// suspends the app, keeps the background machinery registered and keeps
    /// delivering messages.
    /// </summary>
    static class AppExitHelper
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        /// <summary>
        /// True once a shutdown has started. OnSuspending checks this: if the
        /// platform raises Suspending while we are exiting, the normal suspend
        /// work would request a fresh ExtendedExecutionSession and reconnect the
        /// accounts we are in the middle of tearing down.
        /// </summary>
        public static bool IsExiting { get; private set; }

        /// <summary>
        /// How long to wait for the XMPP streams to close before giving up and
        /// terminating anyway. Exiting must never be able to hang.
        /// </summary>
        private const int DISCONNECT_TIMEOUT_MS = 3000;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--


        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--


        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        /// <summary>
        /// Shuts everything down and terminates the process. Never throws - a
        /// failure in any single step must not leave the app half torn down and
        /// still running.
        /// </summary>
        /// <param name="reason">Where the exit came from, for the log.</param>
        public static async Task exitAppAsync(string reason)
        {
            if (IsExiting)
            {
                Logger.Info("Exit already in progress - ignoring the request from: " + reason);
                return;
            }
            IsExiting = true;
            Logger.Info("Exiting the app (" + reason + ")...");

            // 1. The in-process background machinery. StopKeepAlive() disposes
            //    the ExtendedExecutionSession AND unsubscribes the Geolocator,
            //    which is what drops the location indicator.
            try
            {
                BackgroundService.IsInForeground = false;
                XmppKeepAlive.Instance.Stop();
                BackgroundService.Instance.StopKeepAlive();
                BackgroundService.Instance.ReleaseGraceWindow();
                Logger.Info("Exit: keep-alive, location and grace window released.");
            }
            catch (Exception ex)
            {
                Logger.Error("Exit: failed to release the background session.", ex);
            }

            // 2. The OS background registrations. Without this the catch-up
            //    TimeTrigger keeps waking the process every 15 minutes and
            //    raising message toasts long after the user quit.
            try
            {
                BackgroundTaskHelper.unregisterAllTasks();
            }
            catch (Exception ex)
            {
                Logger.Error("Exit: failed to unregister the background tasks.", ex);
            }

            // 3. Close the XMPP streams properly. Skipping </stream:stream>
            //    leaves a ghost session bound to our resource on the server,
            //    which then fights the next connection attempt.
            try
            {
                Task disconnect = ConnectionHandler.INSTANCE.disconnectAllAsync();
                if (await Task.WhenAny(disconnect, Task.Delay(DISCONNECT_TIMEOUT_MS)) != disconnect)
                {
                    Logger.Warn("Exit: disconnecting timed out after " + DISCONNECT_TIMEOUT_MS + "ms - terminating anyway.");
                }
                else
                {
                    Logger.Info("Exit: all accounts disconnected.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exit: failed to disconnect the accounts.", ex);
            }

            // 4. Terminate. Application.Exit() does not reliably raise
            //    Suspending, which is fine - everything above is the shutdown.
            Logger.Info("Exit: terminating.");
            try
            {
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error("Exit: Application.Exit() failed.", ex);
            }
        }

        #endregion

        #region --Misc Methods (Private)--


        #endregion

        #region --Misc Methods (Protected)--


        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--


        #endregion
    }
}
