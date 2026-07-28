using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Data_Manager2.Classes;
using Logging;
using XMPP_API.Classes;

namespace UWP_XMPP_Client.Classes
{
    /// <summary>
    /// Sends an XMPP whitespace keep-alive (a single space — a valid no-op in an
    /// XML stream) to every connected account, so a connection held open in the
    /// background is not dropped by the server or an NAT idle timeout. UWPX has
    /// no built-in ping.
    ///
    /// Design notes (these matter for performance on low-RAM W10M devices):
    ///  - Only ever runs while the app is BACKGROUNDED with keep-alive active.
    ///    In the foreground it is stopped, so it never competes with the UI.
    ///  - A single re-entrancy guard prevents overlapping ticks if a send is
    ///    slow — the previous cause of sluggishness/freezes was ticks stacking
    ///    up and contending with the connection threads.
    ///  - Sends are fire-and-forget per client and wrapped so one bad socket
    ///    cannot stall the others or throw onto the timer thread.
    ///  - Interval is 90 s: under typical server/NAT idle windows (120-300 s)
    ///    but light enough not to churn the radio.
    /// </summary>
    public sealed class XmppKeepAlive
    {
        private static XmppKeepAlive _instance;
        public static XmppKeepAlive Instance
        {
            get { return _instance ?? (_instance = new XmppKeepAlive()); }
        }
        private XmppKeepAlive() { }

        private Timer _timer;
        private readonly object _lock = new object();
        private int _tickInProgress;   // 0 = idle, 1 = a tick is running

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(90);

        public void Start()
        {
            return;

        }

        public void Start_old()
        {
            return;

            lock (_lock)
            {
                if (_timer != null) return;
                // First tick after one full interval (not immediately) so it
                // never fires during the suspend transition itself.
                _timer = new Timer(OnTick, null, Interval, Interval);
                Logger.Info("XMPP whitespace keep-alive started.");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_timer == null) return;
                _timer.Dispose();
                _timer = null;
                Logger.Info("XMPP whitespace keep-alive stopped.");
            }
        }

        private async void OnTick(object state)
        {
            // Skip this tick entirely if the previous one has not finished.
            if (Interlocked.CompareExchange(ref _tickInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                // Only act while genuinely backgrounded. If we are in the
                // foreground for any reason, do nothing this tick.
                if (BackgroundService.IsInForeground)
                {
                    return;
                }

                List<XMPPClient> clients;
                try { clients = ConnectionHandler.INSTANCE.getClients(); }
                catch (Exception ex) { Logger.Error("KeepAlive: could not enumerate clients.", ex); return; }

                if (clients == null) return;

                // Snapshot to a local array so we never enumerate a list that
                // another thread may mutate.
                XMPPClient[] snapshot;
                try { snapshot = clients.ToArray(); }
                catch { return; }

                foreach (XMPPClient client in snapshot)
                {
                    if (client == null) continue;
                    try
                    {
                        if (client.isConnected())
                        {
                            await client.sendWhitespaceKeepAliveAsync().ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("KeepAlive: whitespace ping failed for a client: " + ex.Message);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _tickInProgress, 0);
            }
        }
    }
}
