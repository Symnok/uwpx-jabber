using System;
using UWP_XMPP_Client.Pages;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using UWP_XMPP_Client.Classes;
using Data_Manager2.Classes;
using Data_Manager2.Classes.DBManager;
using System.Threading.Tasks;
using Logging;
using Microsoft.AppCenter.Push;
using Microsoft.AppCenter.Crashes;
using Microsoft.AppCenter.Analytics;
using Microsoft.HockeyApp;
using UWP_XMPP_Client.Dialogs;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Data_Manager2.Classes.ToastActivation;
using Windows.UI.Notifications;
using Data_Manager2.Classes.DBTables;
using System.Text;
using Windows.ApplicationModel.Background;
//using Microsoft.AppCenter.Analytics;

namespace UWP_XMPP_Client
{
    sealed partial class App : Application
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        private readonly string APP_CENTER_SECRET = "523e7039-f6cb-4bf1-9000-53277ed97c53";
        private readonly string HOCKEY_APP_SECRET = "6e35320f3a4142f28060011b25e36f24";

        /// <summary>
        /// Gets or sets (with LocalSettings persistence) the RequestedTheme of the root element.
        /// </summary>
        public static ElementTheme RootTheme
        {
            get
            {
                if (Window.Current.Content is FrameworkElement rootElement)
                {
                    return rootElement.RequestedTheme;
                }

                return ElementTheme.Default;
            }
            set
            {
                if (Window.Current.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = value;
                }
                Settings.setSetting(SettingsConsts.APP_REQUESTED_THEME, value.ToString());
            }
        }

        private bool isRunning;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        public App()
        {
            this.isRunning = false;

            //Crash reports capturing:
            if (!Settings.getSettingBoolean(SettingsConsts.DISABLE_CRASH_REPORTING))
            {
                // Setup Hockey App crashes:
                HockeyClient.Current.Configure(HOCKEY_APP_SECRET);

                // Setup App Center crashes, push:
                setupAppCenter();
            }

            // Init buy content helper:
            BuyContentHelper.INSTANCE.init();

            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.Resuming += App_Resuming;
            this.UnhandledException += App_UnhandledException;

            // Application.UnhandledException only ever sees a stackless
            // System.Exception for errors that start in the WinRT/XAML layer,
            // which is why every RPC_E_WRONG_THREAD report so far has had
            // Stack=[null] and no way to locate it. These two see more:
            //
            //  - UnhandledErrorDetected fires at the WinRT level and can hand
            //    back the ORIGINAL error object (with its stack) via Propagate().
            //  - UnobservedTaskException catches anything that died inside a
            //    fire-and-forget Task, which App.UnhandledException never gets
            //    in a usable form.
            CoreApplication.UnhandledErrorDetected += CoreApplication_UnhandledErrorDetected;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // Perform App update tasks if necessary:
            AppUpdateHandler.onAppStart();
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--


        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--


        #endregion

        #region --Misc Methods (Private)--
        /// <summary>
        /// Sets up App Center crash and push support.
        /// </summary>
        private void setupAppCenter()
        {
            try
            {
                Microsoft.AppCenter.AppCenter.Start(APP_CENTER_SECRET, typeof(Crashes));
#if DEBUG
                Microsoft.AppCenter.AppCenter.Start(APP_CENTER_SECRET, typeof(Analytics), typeof(Push)); // Only enable analytics and push for debug builds
#endif

                if (!Microsoft.AppCenter.AppCenter.Configured)
                {
                    Push.PushNotificationReceived -= Push_PushNotificationReceived;
                    Push.PushNotificationReceived += Push_PushNotificationReceived;
                }
            }
            catch (Exception e)
            {
                Logger.Error("Failed to start APPCenter!", e);
                throw e;
            }
            Logger.Info("App Center crash reporting registered.");
            Logger.Info("App Center push registered.");
        }

        /// <summary>
        /// Inits all db managers in a new task to force event subscriptions.
        /// </summary>
        private void initAllDBManagers()
        {
            Task.Run(() =>
            {
                AccountDBManager.INSTANCE.initManager();
                ChatDBManager.INSTANCE.initManager();
                DiscoDBManager.INSTANCE.initManager();
                ImageDBManager.INSTANCE.initManager();
                MUCDBManager.INSTANCE.initManager();
            });
        }

        /// <summary>
        /// Sets the log level for the logger class.
        /// </summary>
        private void initLogLevel()
        {
            object o = Settings.getSetting(SettingsConsts.LOG_LEVEL);
            if (o is int)
            {
                Logger.logLevel = (LogLevel)o;
            }
            else
            {
                Settings.setSetting(SettingsConsts.LOG_LEVEL, (int)LogLevel.INFO);
                Logger.logLevel = LogLevel.INFO;
            }
        }

        private async Task onActivatedOrLaunchedAsync(IActivatedEventArgs args)
        {
            // Sets the log level:
            initLogLevel();

            // Register background tasks:
            Logger.Info("Registering background tasks...");
            await BackgroundTaskHelper.registerToastBackgroundTaskAsync();
            await BackgroundTaskHelper.registerCatchUpTaskAsync();
            Logger.Info("Finished registering background tasks.");

            // Init all db managers to force event subscriptions:
            initAllDBManagers();

            // Set default background:
            if (!Settings.getSettingBoolean(SettingsConsts.INITIALLY_STARTED))
            {
                Settings.setSetting(SettingsConsts.CHAT_EXAMPLE_BACKGROUND_IMAGE_NAME, "light_bulb.jpeg");
            }
            // Loads all background images into the cache:
            BackgroundImageCache.loadCache();

            // Setup push server connection:
            if (!Settings.getSettingBoolean(SettingsConsts.DISABLE_PUSH))
            {
                Push_App_Server.Classes.PushManager.init();
            }

            isRunning = true;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (!(Window.Current.Content is Frame rootFrame))
            {
                // Create a Frame to act as the navigation context and navigate to the first page:
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (args.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    // TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (args is ProtocolActivatedEventArgs protocolActivationArgs)
            {
                Logger.Info("App activated by protocol activation with: " + protocolActivationArgs.Uri.ToString());

                // If we're currently not on a page, navigate to the main page
                if (rootFrame.Content == null)
                {
                    if (!Settings.getSettingBoolean(SettingsConsts.INITIALLY_STARTED))
                    {
                        rootFrame.Navigate(typeof(AddAccountPage), "App.xaml.cs"); // ToDo add arguments
                    }
                    else
                    {
                        rootFrame.Navigate(typeof(ChatPage), "App.xaml.cs"); // ToDo add arguments
                    }
                }
            }
            else if (args is ToastNotificationActivatedEventArgs toastActivationArgs)
            {
                Logger.Info("App activated by toast with: " + toastActivationArgs.Argument);

                // Opening the chat the toast belongs to is safe again: what used
                // to kill the app here was never the selection itself, it was
                // ChatPage.masterDetail_pnl_SelectionChanged reading
                // e.RemovedItems after an await that had already moved it off
                // the UI thread (RPC_E_WRONG_THREAD). Selecting a chat from a
                // toast is simply the fastest way to trigger that handler.
                // Everything below still degrades to "just show the app" rather
                // than throwing, because an exception on this path is unhandled
                // by definition.
                AbstractToastActivation activation = null;
                if (!string.IsNullOrEmpty(toastActivationArgs.Argument))
                {
                    try
                    {
                        activation = ToastActivationArgumentParser.parseArguments(toastActivationArgs.Argument);
                        Logger.Info("Toast activation parsed as: " + (activation == null ? "null" : activation.GetType().Name));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Failed to parse the toast activation argument.", ex);
                    }
                }
                string toastChatId = (activation as ChatToastActivation)?.CHAT_ID;

                try
                {
                    if (rootFrame.Content is ChatPage chatPage)
                    {
                        // ChatPage is already up. Navigating to it AGAIN builds a
                        // second instance while the first stays alive and
                        // subscribed to every ChatDBManager/MUCDBManager event,
                        // so each toast doubled the UI work done per DB change -
                        // that is what froze the app. Select on the page we
                        // already have instead.
                        if (toastChatId != null)
                        {
                            Logger.Info("Reusing the current ChatPage for toast chat: " + toastChatId);
                            chatPage.showChatFromToast(toastChatId);
                        }
                    }
                    else if (rootFrame.Content == null)
                    {
                        // Cold start. ChatPage picks the chat up from the
                        // navigation parameter in its OnNavigatedTo.
                        if (!Settings.getSettingBoolean(SettingsConsts.INITIALLY_STARTED))
                        {
                            rootFrame.Navigate(typeof(AddAccountPage), "App.xaml.cs");
                        }
                        else if (activation != null)
                        {
                            Logger.Info("Navigating to ChatPage for toast activation.");
                            rootFrame.Navigate(typeof(ChatPage), activation);
                        }
                        else
                        {
                            rootFrame.Navigate(typeof(ChatPage), "App.xaml.cs");
                        }
                    }
                    else
                    {
                        // Some other page is on top (settings, a profile, ...).
                        // Passing null rather than "App.xaml.cs" here keeps the
                        // first-run/what's-new dialogs out of an activation that
                        // is not a cold start.
                        Logger.Info("Navigating to ChatPage for toast activation from " + rootFrame.Content.GetType().Name + ".");
                        rootFrame.Navigate(typeof(ChatPage), (object)activation);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to handle the toast activation - showing the app as it is.", ex);
                }
                Logger.Info("Toast activation handled.");
            }
            else if (args is LaunchActivatedEventArgs launchActivationArgs)
            {
                Push.CheckLaunchedFromNotification(launchActivationArgs);

                // If launched with arguments (not a normal primary tile/applist launch)
                if (launchActivationArgs.Arguments.Length > 0)
                {
                    Logger.Debug(launchActivationArgs.Arguments);
                    // TODO: Handle arguments for cases = launching from secondary Tile, so we navigate to the correct page
                    //throw new NotImplementedException();
                }

                // If we're currently not on a page, navigate to the main page
                if (rootFrame.Content == null)
                {
                    if (!Settings.getSettingBoolean(SettingsConsts.INITIALLY_STARTED))
                    {
                        rootFrame.Navigate(typeof(AddAccountPage), "App.xaml.cs");
                    }
                    else
                    {
                        rootFrame.Navigate(typeof(ChatPage), "App.xaml.cs");
                    }
                }
            }

            // Set requested theme:
            string themeString = Settings.getSettingString(SettingsConsts.APP_REQUESTED_THEME);
            ElementTheme theme = ElementTheme.Dark;
            if (themeString != null)
            {
                Enum.TryParse(themeString, out theme);
            }
            RootTheme = theme;

            Window.Current.Activate();
            Logger.Info("Window activated.");

            // Connect to all clients:
            ConnectionHandler.INSTANCE.connectAll();

            BackgroundService.IsInForeground = true;

            // Establish the always-on background session now, in the FOREGROUND
            // and on the UI thread — this is where ExtendedExecutionSession and
            // the location subscription reliably start (and the location icon
            // appears). It is HELD across suspend/resume; OnSuspending only needs
            // to start the whitespace ping, not create the session in the fragile
            // suspend window. Location is already granted, so no prompt appears.
            // Only start it once we actually have an account (past registration).
            if (Settings.getSettingBoolean(SettingsConsts.INITIALLY_STARTED))
            {
                try
                {
                    Logger.Info("Starting background keep-alive...");
                    bool started = await BackgroundService.Instance.StartKeepAliveAsync();
                    Logger.Info("Background keep-alive start returned: " + started);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to start background keep-alive on launch.", ex);
                }
            }
            Logger.Info("Activation/launch handling finished.");
        }

        #endregion

        #region --Misc Methods (Protected)--
        protected override async void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            var deferral = args.TaskInstance.GetDeferral();
            try
            {

                switch (args.TaskInstance.Task.Name)
                {
                    case BackgroundTaskHelper.CATCH_UP_TASK_NAME:
                        await runCatchUpAsync(args.TaskInstance);
                        break;

                    case BackgroundTaskHelper.TOAST_BACKGROUND_TASK_NAME:
                        ToastNotificationActionTriggerDetail details = args.TaskInstance.TriggerDetails as ToastNotificationActionTriggerDetail;
                        if (details != null)
                        {
                            initLogLevel();

                            string arguments = details.Argument;
                            var userInput = details.UserInput;

                            Logger.Debug("App activated in background through toast with: " + arguments);
                            AbstractToastActivation abstractToastActivation = ToastActivationArgumentParser.parseArguments(arguments);

                            if (abstractToastActivation is MarkChatAsReadToastActivation markChatAsRead)
                            {
                                ToastHelper.removeToastGroup(markChatAsRead.CHAT_ID);
                                ChatDBManager.INSTANCE.markAllMessagesAsRead(markChatAsRead.CHAT_ID);
                            }
                            else if (abstractToastActivation is MarkMessageAsReadToastActivation markMessageAsRead)
                            {
                                ChatDBManager.INSTANCE.markMessageAsRead(markMessageAsRead.CHAT_MESSAGE_ID);
                            }
                            else if (abstractToastActivation is SendReplyToastActivation sendReply)
                            {
                                ChatTable chat = ChatDBManager.INSTANCE.getChat(sendReply.CHAT_ID);
                                if (chat != null && userInput[ToastHelper.TEXT_BOX_ID] != null)
                                {
                                    if (isRunning)
                                    {

                                    }
                                    else
                                    {

                                    }
                                }
                            }
                        }
                        break;

                    default:
                        break;
                }

            }
            finally
            {
                deferral.Complete();
            }
        }

        // How long to stay awake draining the connection on a catch-up wake.
        private const int CATCH_UP_DRAIN_SECONDS = 20;

        private async System.Threading.Tasks.Task runCatchUpAsync(IBackgroundTaskInstance taskInstance)
        {
            initLogLevel();
            Logger.Info("Catch-up task fired.");

            bool cancelled = false;
            taskInstance.Canceled += (s, reason) =>
            {
                cancelled = true;
                Logger.Info("Catch-up cancelled: " + reason);
            };

            try
            {
                // Reconnect every account; incoming stanzas take the normal path
                // and raise their usual toasts.
                ConnectionHandler.INSTANCE.connectAll();

                // Stay awake briefly so sockets connect and drain queued messages
                // before the deferral completes and we are suspended again.
                for (int i = 0; i < CATCH_UP_DRAIN_SECONDS && !cancelled; i++)
                {
                    await System.Threading.Tasks.Task.Delay(1000);
                }

                Logger.Info("Catch-up drain complete.");
            }
            catch (Exception ex)
            {
                Logger.Error("Catch-up run failed.", ex);
            }
        }

        protected async override void OnLaunched(LaunchActivatedEventArgs args)
        {
            await onActivatedOrLaunchedAsync(args);
        }

        protected async override void OnActivated(IActivatedEventArgs args)
        {
            await onActivatedOrLaunchedAsync(args);
        }

        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--
        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            isRunning = false;
            BackgroundService.IsInForeground = false;

            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                // An exit is already tearing everything down. Doing the normal
                // suspend work now would request a FRESH extended execution
                // session and re-enter the disconnect we are in the middle of.
                if (AppExitHelper.IsExiting)
                {
                    Logger.Info("Suspending while exiting - shutdown already handled.");
                    return;
                }

                // ALWAYS close the XMPP streams cleanly before we go away.
                //
                // The keep-alive session cannot be relied on to hold the sockets:
                // the OS revokes the LocationTracking session under SystemPolicy
                // within seconds on this hardware (see BackgroundService), and the
                // whitespace ping is disabled. Suspending without sending
                // </stream:stream> leaves a ghost session on the server bound to
                // our resource, which then fights the next connection attempt for
                // as long as the server keeps it around.
                //
                // Background delivery is the catch-up TimeTrigger task, not a
                // socket held across suspend.
                await BackgroundService.Instance.RequestGraceWindowAsync();
                await ConnectionHandler.INSTANCE.disconnectAllAsync();
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void App_Resuming(object sender, object e)
        {
            BackgroundService.IsInForeground = true;

            // Stop background-only machinery immediately — the ping must never
            // run in the foreground (this is what prevents the UI slowdown).
            XmppKeepAlive.Instance.Stop();
            BackgroundService.Instance.ReleaseGraceWindow();

            // No-op for already-connected accounts (always-on mode); reconnects
            // any that dropped (fallback mode).
            ConnectionHandler.INSTANCE.connectAll();
            isRunning = true;
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private async void Push_PushNotificationReceived(object sender, PushNotificationReceivedEventArgs e)
        {
            // Add the notification message and title to the message:
            StringBuilder pushSummary = new StringBuilder("Push notification received:\n");
            pushSummary.Append($"\tNotification title: {e.Title}\n");
            pushSummary.Append($"\tMessage: {e.Message}");

            // If there is custom data associated with the notification, print the entries:
            if (e.CustomData != null)
            {
                pushSummary.Append("\n\tCustom data:\n");
                foreach (var key in e.CustomData.Keys)
                {
                    pushSummary.Append($"\t\t{key} : {e.CustomData[key]}\n");
                }
            }

            // Log notification summary:
            Logger.Info(pushSummary.ToString());

            // Show push dialog:
            if (e.CustomData.TryGetValue("markdown", out string markdownText))
            {
                // CoreApplication.MainView.Dispatcher, not
                // MainView.CoreWindow.Dispatcher: reading .CoreWindow from a
                // background thread (this handler is one) throws
                // RPC_E_WRONG_THREAD. The CoreApplicationView's own Dispatcher
                // is agile.
                await CoreApplication.MainView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    AppCenterPushDialog dialog = new AppCenterPushDialog(e.Title, markdownText);
                    await UiUtils.showDialogAsyncQueue(dialog);
                });
            }
        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // .NET Native (Release) often strips e.Exception's message, leaving the
            // log blank. e.Message (the event args' own string) usually survives,
            // so log both plus the exception type and full ToString().
            string type = "null";
            string exStr = "null";
            try
            {
                if (e.Exception != null)
                {
                    type = e.Exception.GetType().FullName;
                    exStr = e.Exception.ToString();
                }
            }
            catch { }

            string stack = "null";
            string inner = "null";
            try
            {
                if (e.Exception != null)
                {
                    stack = e.Exception.StackTrace ?? "null";

                    StringBuilder innerBuilder = new StringBuilder();
                    Exception cur = e.Exception.InnerException;
                    while (cur != null)
                    {
                        innerBuilder.Append(cur.GetType().FullName).Append(": ").Append(cur.Message)
                                    .Append(" @ ").Append(cur.StackTrace ?? "no stack").Append(" || ");
                        cur = cur.InnerException;
                    }
                    if (innerBuilder.Length > 0)
                    {
                        inner = innerBuilder.ToString();
                    }
                }
            }
            catch { }

            string detail = "Message=[" + e.Message + "] Type=[" + type + "] Ex=[" + exStr + "]"
                            + " Stack=[" + stack + "] Inner=[" + inner + "]";
            Logger.Error("Unhanded exception: " + detail, e.Exception);
        }

        /// <summary>
        /// Fires for errors the WinRT layer reports, before/instead of
        /// Application.UnhandledException getting anything useful.
        /// Propagate() rethrows the ORIGINAL error on this thread, which is the
        /// only way to see its real type and stack trace - the exception object
        /// handed to Application.UnhandledException for a WinRT-originated
        /// error has neither.
        /// </summary>
        private void CoreApplication_UnhandledErrorDetected(object sender, UnhandledErrorDetectedEventArgs e)
        {
            try
            {
                if (e.UnhandledError == null || e.UnhandledError.Handled)
                {
                    return;
                }
                e.UnhandledError.Propagate();
            }
            catch (Exception ex)
            {
                // Propagate() marks the error handled, so swallowing it here
                // keeps the app alive. That is deliberate while we are hunting
                // this down: the log line below is worth more than the crash,
                // and the app may well carry on fine.
                Logger.Error("CoreApplication unhandled error: " + describeException(ex), ex);
            }
        }

        /// <summary>
        /// Catches exceptions from fire-and-forget Tasks. Those never reach
        /// Application.UnhandledException as themselves - they surface later,
        /// stripped of their stack, when the Task is collected.
        /// </summary>
        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                Logger.Error("Unobserved task exception: " + describeException(e.Exception), e.Exception);
                e.SetObserved();
            }
            catch { }
        }

        /// <summary>
        /// Type, message, stack and the full InnerException chain of the given
        /// exception, on one line, never throwing.
        /// </summary>
        private static string describeException(Exception ex)
        {
            if (ex == null)
            {
                return "null";
            }

            StringBuilder builder = new StringBuilder();
            try
            {
                builder.Append("Type=[").Append(ex.GetType().FullName).Append(']')
                       .Append(" Message=[").Append(ex.Message).Append(']')
                       .Append(" Stack=[").Append(ex.StackTrace ?? "null").Append(']');

                Exception cur = ex.InnerException;
                if (cur != null)
                {
                    builder.Append(" Inner=[");
                    while (cur != null)
                    {
                        builder.Append(cur.GetType().FullName).Append(": ").Append(cur.Message)
                               .Append(" @ ").Append(cur.StackTrace ?? "no stack").Append(" || ");
                        cur = cur.InnerException;
                    }
                    builder.Append(']');
                }
            }
            catch { }
            return builder.ToString();
        }

        #endregion
    }
}