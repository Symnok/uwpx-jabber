using System;
using System.Diagnostics;
using Windows.Storage;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.ExtendedExecution;
using Windows.Foundation.Metadata;
using Windows.Devices.Geolocation;
using Windows.UI.Core;
using Logging;

namespace UWP_XMPP_Client.Classes
{
    /// <summary>
    /// Always-on background support for Windows 10 Mobile, ported from the
    /// Unogram (W10M Telegram) client.
    ///
    /// W10M suspends an app within seconds of it leaving the foreground, which
    /// tears down the XMPP StreamSocket. The only extended-execution mode that
    /// keeps running with the screen locked on Mobile is LocationTracking, and
    /// the system only honours it while a Geolocator subscription is live — so
    /// we hold the cheapest possible subscription purely to justify the session.
    /// The reported position is never read or transmitted.
    ///
    /// This runs IN THE APP PROCESS by necessity: the session keeps *this*
    /// process alive, so it cannot be moved to a separate background-task
    /// process. Cost: a location prompt and higher battery, hence OFF by default.
    /// </summary>
    public sealed class BackgroundService
    {
        private static BackgroundService _instance;
        public static BackgroundService Instance
        {
            get { return _instance ?? (_instance = new BackgroundService()); }
        }
        private BackgroundService() { }

        private ExtendedExecutionSession _keepAliveSession;   // always-on (LocationTracking)
        private ExtendedExecutionSession _graceSession;       // short suspend grace window
        private Geolocator _geolocator;

        // The UI thread's dispatcher, captured when a session is created.
        // ExtendedExecutionSession and Geolocator are WinRT objects with thread
        // affinity: they are created on the UI thread, so they must also be
        // disposed / unsubscribed there. The Revoked event arrives on an
        // arbitrary pool thread, and cleaning up directly from it throws
        // "The application called an interface that was marshalled for a
        // different thread" (RPC_E_WRONG_THREAD, 0x8001010E). Nothing catches
        // an exception thrown on an event callback, so that killed the app.
        private CoreDispatcher _dispatcher;

        // Serialises Start/Stop so a fast off->on->suspend sequence can never
        // create two overlapping sessions (a cause of the earlier slowness).
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>App is on screen. Maintained from App.xaml.cs.</summary>
        public static bool IsInForeground = true;

        /// <summary>Whether the always-on session is currently held.</summary>
        public bool KeepAliveActive { get { return _keepAliveSession != null; } }

        // ------------------------------------------------------------------
        // Always-on keep-alive (LocationTracking extended execution)
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts the always-on background session. Idempotent and serialised —
        /// calling it when a session is already held is a no-op that returns
        /// true. Returns false only if location was refused without coarse
        /// fallback, or the system denied the session.
        /// </summary>
        public async Task<bool> StartKeepAliveAsync()
        {
            // Captured while we are still on the UI thread (StartKeepAliveAsync
            // is always called from App.xaml.cs on activation/launch).
            captureDispatcher();

            await _gate.WaitAsync();
            try
            {
                if (_keepAliveSession != null) return true;   // already running

                bool coarseAvailable = false;
                try
                {
                    _geolocator = new Geolocator();
                    _geolocator.DesiredAccuracy = PositionAccuracy.Default;
                    _geolocator.DesiredAccuracyInMeters = 3000;
                    _geolocator.MovementThreshold = 1000;   // metres
                    _geolocator.ReportInterval = 600000;    // 10 minutes; a hint only

                    if (ApiInformation.IsMethodPresent(
                            "Windows.Devices.Geolocation.Geolocator",
                            "AllowFallbackToConsentlessPositions"))
                    {
                        _geolocator.AllowFallbackToConsentlessPositions();
                        coarseAvailable = true;
                    }

                    _geolocator.PositionChanged += OnPositionChanged;
                    _geolocator.StatusChanged += OnGeolocatorStatusChanged;
                }
                catch (Exception ex)
                {
                    Diag("KeepAlive: geolocator setup failed: " + ex.Message);
                    StopGeolocator();
                    return false;
                }

                GeolocationAccessStatus access = GeolocationAccessStatus.Unspecified;
                try { access = await Geolocator.RequestAccessAsync(); }
                catch (Exception ex) { Diag("KeepAlive: RequestAccessAsync failed: " + ex.Message); }

                if (access != GeolocationAccessStatus.Allowed)
                {
                    Diag("KeepAlive: location access " + access
                         + (coarseAvailable ? ", continuing with coarse positions" : ", giving up"));
                    if (!coarseAvailable) { StopGeolocator(); return false; }
                }

                var session = new ExtendedExecutionSession();
                session.Reason = ExtendedExecutionReason.LocationTracking;
                session.Description = "Keeping you connected to chat";
                session.Revoked += OnKeepAliveRevoked;

                try
                {
                    var result = await session.RequestExtensionAsync();
                    if (result == ExtendedExecutionResult.Allowed)
                    {
                        _keepAliveSession = session;
                        Diag("KeepAlive: ALLOWED (location tracking)");
                        return true;
                    }
                    Diag("KeepAlive: DENIED");
                }
                catch (Exception ex)
                {
                    Diag("KeepAlive: RequestExtensionAsync failed: " + ex.Message);
                }

                try { session.Revoked -= OnKeepAliveRevoked; session.Dispose(); } catch { }
                StopGeolocator();
                return false;
            }
            finally
            {
                _gate.Release();
            }
        }

        public void StopKeepAlive()
        {
            // Non-async stop; safe to call from anywhere.
            if (_keepAliveSession != null)
            {
                try
                {
                    _keepAliveSession.Revoked -= OnKeepAliveRevoked;
                    _keepAliveSession.Dispose();
                }
                catch { }
                _keepAliveSession = null;
                Diag("KeepAlive: stopped");
            }
            StopGeolocator();
        }

        private void StopGeolocator()
        {
            if (_geolocator == null) return;
            try
            {
                _geolocator.PositionChanged -= OnPositionChanged;
                _geolocator.StatusChanged -= OnGeolocatorStatusChanged;
            }
            catch { }
            _geolocator = null;
        }

        // Position is unused and never transmitted — the subscription exists
        // solely to keep the LocationTracking session justified.
        private void OnPositionChanged(Geolocator sender, PositionChangedEventArgs args) { }

        private void OnGeolocatorStatusChanged(Geolocator sender, StatusChangedEventArgs args)
        {
            if (args.Status == PositionStatus.Disabled || args.Status == PositionStatus.NotAvailable)
                Diag("KeepAlive: geolocator status " + args.Status);
        }

        private void OnKeepAliveRevoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            Diag("KeepAlive: REVOKED " + args.Reason);
            runOnUiThread(StopKeepAlive);
            // No auto-retry: on this hardware the OS revokes the LocationTracking
            // session under SystemPolicy within seconds and will keep doing so, so
            // re-requesting just churns the battery. The periodic catch-up
            // TimeTrigger task is the reliable background-delivery fallback.
        }

        // ------------------------------------------------------------------
        // Short suspend grace window (used when keep-alive is OFF)
        // ------------------------------------------------------------------

        public async Task<bool> RequestGraceWindowAsync()
        {
            captureDispatcher();
            if (_keepAliveSession != null) return true;   // already held
            ClearGrace();

            if (await TryRequestGraceAsync(ExtendedExecutionReason.Unspecified)) return true;
            return await TryRequestGraceAsync(ExtendedExecutionReason.SavingData);
        }

        private async Task<bool> TryRequestGraceAsync(ExtendedExecutionReason reason)
        {
            var session = new ExtendedExecutionSession();
            session.Reason = reason;
            session.Description = "Finishing up";
            session.Revoked += OnGraceRevoked;
            try
            {
                if (await session.RequestExtensionAsync() == ExtendedExecutionResult.Allowed)
                {
                    _graceSession = session;
                    return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine("[BG] grace request failed (" + reason + "): " + ex.Message); }
            try { session.Revoked -= OnGraceRevoked; session.Dispose(); } catch { }
            return false;
        }

        public void ReleaseGraceWindow() { ClearGrace(); }

        private void ClearGrace()
        {
            if (_graceSession == null) return;
            try { _graceSession.Revoked -= OnGraceRevoked; _graceSession.Dispose(); } catch { }
            _graceSession = null;
        }

        private void OnGraceRevoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            Diag("Grace: REVOKED " + args.Reason);
            runOnUiThread(ClearGrace);
        }

        /// <summary>
        /// Runs the given cleanup on the UI thread. Revoked events arrive on an
        /// arbitrary thread, but the objects being cleaned up have UI thread
        /// affinity. Never lets an exception escape onto the callback's thread -
        /// there is nothing above it to catch one, so it would crash the app.
        /// </summary>
        private void runOnUiThread(Action action)
        {
            try
            {
                CoreDispatcher dispatcher = _dispatcher;
                if (dispatcher != null && !dispatcher.HasThreadAccess)
                {
                    var ignored = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        try { action(); }
                        catch (Exception ex) { Logger.Error("[BG] Deferred cleanup failed.", ex); }
                    });
                    return;
                }

                action();
            }
            catch (Exception ex)
            {
                Logger.Error("[BG] Failed to dispatch cleanup.", ex);
            }
        }

        private void captureDispatcher()
        {
            if (_dispatcher != null) return;
            try
            {
                CoreWindow window = CoreWindow.GetForCurrentThread();
                if (window != null)
                {
                    _dispatcher = window.Dispatcher;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[BG] Could not capture the UI dispatcher.", ex);
            }
        }

        // Visible in RELEASE too: writes to LocalFolder\\uwpx_bg\\bglog.txt and
        // stashes the last line in LocalSettings["bg_last"] so the background
        // session's behaviour (REVOKED <reason>, ALLOWED, DENIED) can be read
        // from the device without a debugger attached.
        private static void Diag(string msg)
        {
            Debug.WriteLine("[BG] " + msg);
            // Also goes to the normal log so background-session behaviour shows
            // up in the same timeline as everything else.
            Logger.Info("[BG] " + msg);
            string line = DateTime.Now.ToString("MM-dd HH:mm:ss") + "  " + msg;
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                values["bg_last"] = line;
                int n = values.ContainsKey("bg_count") ? (int)values["bg_count"] : 0;
                values["bg_count"] = n + 1;
            }
            catch { }
            // Fire-and-forget file append; never throw onto the caller.
            var ignored = AppendDiagFileAsync(line);
        }

        private static async System.Threading.Tasks.Task AppendDiagFileAsync(string line)
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "uwpx_bg", CreationCollisionOption.OpenIfExists);
                var file = await folder.CreateFileAsync(
                    "bglog.txt", CreationCollisionOption.OpenIfExists);
                await Windows.Storage.FileIO.AppendTextAsync(file, line + "\r\n");
            }
            catch { }
        }
    }
}