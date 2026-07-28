using Logging;
using System.Diagnostics;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;

namespace UWP_XMPP_Client.Classes
{
    static class BackgroundTaskHelper
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public const string TOAST_BACKGROUND_TASK_NAME = "ToastBackgroundTask";

        // Periodic catch-up: a single-process TimeTrigger task. No EntryPoint is
        // set, so activation arrives in App.OnBackgroundActivated (no separate
        // background-task project required). The OS minimum interval is 15 min.
        public const string CATCH_UP_TASK_NAME = "UwpxCatchUpTask";
        private const uint CATCH_UP_INTERVAL_MINUTES = 15;
        // Bump when the task shape changes so a stale registration is replaced.
        private const int CATCH_UP_REG_VERSION = 1;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--


        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--


        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--

        // Mirror key outcomes into the same bglog.txt that BackgroundService writes,
        // so registration success/denial is visible on-device without a debugger.
        private static void BgDiag(string msg)
        {
            Debug.WriteLine("[BG] " + msg);
            string line = DateTime.Now.ToString("MM-dd HH:mm:ss") + "  " + msg;
            try
            {
                var v = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                v["bg_last"] = line;
            }
            catch { }
            var ignored = AppendBgAsync(line);
        }

        private static async Task AppendBgAsync(string line)
        {
            try
            {
                var folder = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "uwpx_bg", Windows.Storage.CreationCollisionOption.OpenIfExists);
                var file = await folder.CreateFileAsync(
                    "bglog.txt", Windows.Storage.CreationCollisionOption.OpenIfExists);
                await Windows.Storage.FileIO.AppendTextAsync(file, line + "\r\n");
            }
            catch { }
        }

        public async static Task registerToastBackgroundTaskAsync()
        {
            // If background task is already registered, do nothing
            if (BackgroundTaskRegistration.AllTasks.Any(i => i.Value.Name.Equals(TOAST_BACKGROUND_TASK_NAME)))
            {
                Logger.Info(TOAST_BACKGROUND_TASK_NAME + " background task already registered.");
                return;
            }

            // Otherwise request access
            BackgroundAccessStatus status = await BackgroundExecutionManager.RequestAccessAsync();

            // Create the background task
            BackgroundTaskBuilder builder = new BackgroundTaskBuilder
            {
                Name = TOAST_BACKGROUND_TASK_NAME
            };

            // Assign the toast action trigger
            builder.SetTrigger(new ToastNotificationActionTrigger());

            // And register the task
            builder.Register();

            Logger.Info("Registered " + TOAST_BACKGROUND_TASK_NAME + " background task.");
        }

        /// <summary>
        /// Registers the periodic catch-up TimeTrigger. Single-process: activation
        /// is handled in App.OnBackgroundActivated. Safe to call on every launch —
        /// it only (re-)registers when missing or when the version changed.
        /// </summary>
        public async static Task registerCatchUpTaskAsync()
        {
            BackgroundAccessStatus status;
            try
            {
                status = await BackgroundExecutionManager.RequestAccessAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Catch-up: RequestAccessAsync failed.", ex);
                BgDiag("CatchUp: RequestAccessAsync THREW: " + ex.Message);
                return;
            }

            BgDiag("CatchUp: RequestAccessAsync -> " + status);
            if (status == BackgroundAccessStatus.DeniedByUser ||
                status == BackgroundAccessStatus.DeniedBySystemPolicy ||
                status == BackgroundAccessStatus.Unspecified)
            {
                Logger.Warn("Catch-up: background access denied (" + status + ").");
                BgDiag("CatchUp: access DENIED (" + status + ")");
                return;
            }

            var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            int registeredVersion = settings.ContainsKey("catchup_reg_version")
                ? Convert.ToInt32(settings["catchup_reg_version"]) : 0;

            bool exists = BackgroundTaskRegistration.AllTasks.Any(i => i.Value.Name.Equals(CATCH_UP_TASK_NAME));
            if (exists && registeredVersion == CATCH_UP_REG_VERSION)
            {
                Logger.Info("Catch-up task already registered (v" + registeredVersion + ").");
                BgDiag("CatchUp: already registered");
                return;
            }

            // Remove a stale registration before re-creating.
            foreach (var t in BackgroundTaskRegistration.AllTasks)
            {
                if (t.Value.Name.Equals(CATCH_UP_TASK_NAME))
                {
                    t.Value.Unregister(true);
                }
            }

            BackgroundTaskBuilder builder = new BackgroundTaskBuilder
            {
                Name = CATCH_UP_TASK_NAME
                // No TaskEntryPoint -> single-process, handled in OnBackgroundActivated.
            };
            builder.SetTrigger(new TimeTrigger(CATCH_UP_INTERVAL_MINUTES, false));
            builder.Register();

            settings["catchup_reg_version"] = CATCH_UP_REG_VERSION;
            Logger.Info("Registered " + CATCH_UP_TASK_NAME + " (" + CATCH_UP_INTERVAL_MINUTES + " min).");
            BgDiag("CatchUp: REGISTERED (" + CATCH_UP_INTERVAL_MINUTES + " min), access=" + status);
        }

        /// <summary>
        /// Unregisters every background task this app owns, so nothing wakes the
        /// process after the user has exited. Called only from the explicit exit
        /// path - suspending must NOT do this, or backgrounded message delivery
        /// would stop.
        /// The next launch registers them again (registerToastBackgroundTaskAsync
        /// and registerCatchUpTaskAsync both re-create a missing registration).
        /// </summary>
        public static void unregisterAllTasks()
        {
            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (!task.Value.Name.Equals(TOAST_BACKGROUND_TASK_NAME) && !task.Value.Name.Equals(CATCH_UP_TASK_NAME))
                {
                    continue;
                }

                try
                {
                    // true: also cancel an instance that is running right now.
                    task.Value.Unregister(true);
                    Logger.Info("Unregistered " + task.Value.Name + " background task.");
                    BgDiag("Unregistered " + task.Value.Name + " (app exit)");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to unregister the " + task.Value.Name + " background task.", ex);
                }
            }

            // Drop the version stamp too, so a future change to
            // CATCH_UP_REG_VERSION can never be mistaken for "already
            // registered" against a registration that no longer exists.
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove("catchup_reg_version");
            }
            catch { }
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