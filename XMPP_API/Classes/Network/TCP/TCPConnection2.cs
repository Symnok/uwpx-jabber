using Logging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography.Certificates;
using Windows.Storage.Streams;
using XMPP_API.Classes.Network.Events;

namespace XMPP_API.Classes.Network.TCP
{
    public class TCPConnection2 : AbstractConnection2
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        /// <summary>
        /// How many characters should get read at once max.
        /// </summary>
        private const int BUFFER_SIZE = 4096;
        /// <summary>
        /// The timeout in ms for TCP connections.
        /// </summary>
        private const int CONNECTION_TIMEOUT_MS = 3000;
        /// <summary>
        /// The timeout for upgrading to a TLS connection in ms.
        /// </summary>
        private const int TLS_UPGRADE_TIMEOUT_MS = 5000;
        /// <summary>
        /// The timeout for sending data.
        /// </summary>
        private const int SEND_TIMEOUT_MS = 1000;
        /// <summary>
        /// How long a send waits for the previous send to finish (default).
        /// </summary>
        public const int DEFAULT_WRITE_LOCK_TIMEOUT_MS = 10000;

        private const int MAX_CONNECTION_TRIES = 3;

        private StreamSocket socket;
        private HostName hostName;

        private DataReader dataReader;
        private DataWriter dataWriter;
        // One write at a time per connection. Per instance (it used to be static and
        // so shared by all accounts) and only ever awaited, never blocked on - a
        // blocking Wait() on the UI thread froze the whole app while a write hung.
        private readonly SemaphoreSlim WRITE_SEMA = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Used to cancel connectAsync().
        /// </summary>
        private CancellationTokenSource connectingCTS;
        private CancellationTokenSource tlsUpgradeCTS;
        /// <summary>
        /// Used to cancel all read operations.
        /// </summary>
        private CancellationTokenSource readingCTS;
        private ConnectionError lastConnectionError;

        public delegate void NewDataReceivedEventHandler(TCPConnection2 connection, NewDataReceivedEventArgs args);

        public event NewDataReceivedEventHandler NewDataReceived;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 05/05/2018 Created [Fabian Sauter]
        /// </history>
        public TCPConnection2(XMPPAccount account) : base(account)
        {
            this.lastConnectionError = null;
            this.connectingCTS = null;
            this.dataReader = null;
            this.dataWriter = null;
            this.socket = null;
            this.readingCTS = null;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        public StreamSocketInformation getSocketInfo()
        {
            return socket?.Information;
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        public async Task connectAsync()
        {
            switch (state)
            {
                case ConnectionState.DISCONNECTED:
                case ConnectionState.ERROR:
                    setState(ConnectionState.CONNECTING);
                    for (int i = 1; i <= MAX_CONNECTION_TRIES; i++)
                    {
                        try
                        {
                            // Cancel connecting if for example disconnectAsync() got called:
                            if (state != ConnectionState.CONNECTING)
                            {
                                return;
                            }

                            // Setup socket:
                            socket = new StreamSocket();
                            socket.Control.KeepAlive = true;
                            socket.Control.QualityOfService = SocketQualityOfService.LowLatency;
                            hostName = new HostName(account.serverAddress);

                            // Add all ignored certificate errors:
                            foreach (ChainValidationResult item in account.connectionConfiguration.IGNORED_CERTIFICATE_ERRORS)
                            {
                                socket.Control.IgnorableServerCertificateErrors.Add(item);
                            }

                            // Connect with timeout:
                            connectingCTS = new CancellationTokenSource(CONNECTION_TIMEOUT_MS);
                            await socket.ConnectAsync(hostName, account.port.ToString()).AsTask(connectingCTS.Token);

                            // Setup stream reader and writer:
                            dataWriter = new DataWriter(socket.OutputStream);
                            dataReader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

                            // Update account connection info:
                            ConnectionInformation connectionInfo = account.CONNECTION_INFO;
                            connectionInfo.socketInfo = socket.Information;

                            // Connection successfully established:
                            if (state == ConnectionState.CONNECTING)
                            {
                                setState(ConnectionState.CONNECTED);
                            }
                            return;
                        }
                        catch (TaskCanceledException e)
                        {
                            Logger.Error("[TCPConnection2]: " + i + " try to connect to " + account.serverAddress + " failed:", e);
                            lastConnectionError = new ConnectionError(ConnectionErrorCode.CONNECT_TIMEOUT, e.Message);
                        }
                        catch (Exception e)
                        {
                            onConnectionError(e, i);
                        }
                    }
                    setState(ConnectionState.ERROR, lastConnectionError);
                    break;

                default:
                    break;
            }
        }

        public void disconnect()
        {
            if (state == ConnectionState.DISCONNECTED)
            {
                return;
            }

            setState(ConnectionState.DISCONNECTING);
            Logger.Info("[TCPConnection2]: Disconnecting");
            connectingCTS?.Cancel();
            readingCTS?.Cancel();
            tlsUpgradeCTS?.Cancel();
            try
            {
                dataReader?.DetachStream();
                dataReader?.Dispose();
                dataReader = null;
                dataWriter?.DetachStream();
                dataWriter?.Dispose();
                dataWriter = null;
            }
            catch (Exception)
            {
            }
            socket?.Dispose();
            socket = null;
            setState(ConnectionState.DISCONNECTED);
            Logger.Info("[TCPConnection2]: Disconnected");
        }

        /// <summary>
        /// Upgrades the connection to TLS 1.2.
        /// Timeout is TLS_UPGRADE_TIMEOUT.
        /// </summary>
        public async Task upgradeToTLSAsync()
        {
            if (state != ConnectionState.CONNECTED)
            {
                throw new InvalidOperationException("[TCPConnection2]: Unable to upgrade to TLS! ConnectionState != Connected! state = " + state);
            }
            DateTime d = DateTime.Now;
            try
            {
                tlsUpgradeCTS = new CancellationTokenSource(TLS_UPGRADE_TIMEOUT_MS);
                await socket.UpgradeToSslAsync(SocketProtectionLevel.Tls12, hostName).AsTask(tlsUpgradeCTS.Token);
            }
            catch (Exception e)
            {
                SocketErrorStatus socketErrorStatus = SocketError.GetStatus(e.GetBaseException().HResult);
                lastConnectionError = new ConnectionError(socketErrorStatus, "TLS upgrade failed after " + (DateTime.Now.Subtract(d).TotalMilliseconds) + "ms!");
                setState(ConnectionState.ERROR, lastConnectionError);
                throw e;
            }
        }

        public Task<bool> sendAsync(string s)
        {
            return sendAsync(s, DEFAULT_WRITE_LOCK_TIMEOUT_MS);
        }

        /// <param name="writeLockTimeoutMs">How long to wait for a previous send to finish before giving up.</param>
        public async Task<bool> sendAsync(string s, int writeLockTimeoutMs)
        {
            if (state != ConnectionState.CONNECTED)
            {
                return false;
            }

            if (!await WRITE_SEMA.WaitAsync(writeLockTimeoutMs).ConfigureAwait(false))
            {
                Logger.Warn("[TCPConnection2]: failed to send - the previous send did not finish within " + writeLockTimeoutMs + "ms.");
                return false;
            }

            try
            {
                DataWriter writer = dataWriter;
                StreamSocket sendSocket = socket;
                if (writer == null || state != ConnectionState.CONNECTED)
                {
                    return false;
                }
                writer.WriteString(s);

                // A socket write sometimes hangs forever and ignores cancellation (a
                // cancelled AsTask(token) only completes once the WinRT operation does).
                // So wait with a real timeout and, if it hits, abort the socket: that is
                // what actually unblocks the stuck write. The reader loop then sees the
                // dead socket and triggers the normal error/reconnect handling.
                Task storeTask = writer.StoreAsync().AsTask();
                if (await Task.WhenAny(storeTask, Task.Delay(SEND_TIMEOUT_MS)).ConfigureAwait(false) != storeTask)
                {
                    observeException(storeTask);
                    Logger.Error("[TCPConnection2]: send timed out after " + SEND_TIMEOUT_MS + "ms - aborting the socket." + (Logger.logLevel >= LogLevel.DEBUG ? " Data: " + s : ""));
                    abortSocket(sendSocket);
                    return false;
                }
                await storeTask.ConfigureAwait(false);

                Task flushTask = writer.FlushAsync().AsTask();
                if (await Task.WhenAny(flushTask, Task.Delay(SEND_TIMEOUT_MS)).ConfigureAwait(false) != flushTask)
                {
                    observeException(flushTask);
                    Logger.Error("[TCPConnection2]: flush timed out after " + SEND_TIMEOUT_MS + "ms - aborting the socket.");
                    abortSocket(sendSocket);
                    return false;
                }
                await flushTask.ConfigureAwait(false);

                Logger.Debug("[TCPConnection2]: Send to (" + account.serverAddress + "):" + s);
                return true;
            }
            catch (Exception e)
            {
                if (Logger.logLevel >= LogLevel.DEBUG)
                {
                    Logger.Error("[TCPConnection2]: failed to send: " + s, e);
                }
                else
                {
                    Logger.Error("[TCPConnection2]: failed to send message!", e);
                }
                return false;
            }
            finally
            {
                WRITE_SEMA.Release();
            }
        }

        /// <summary>
        /// Reads the next chunk of data from the current connection (for callers that do
        /// their own reading instead of using startReaderTask(), e.g. the push connection).
        /// </summary>
        public Task<TCPReadResult> readAsync()
        {
            return readAsync(dataReader, readingCTS?.Token ?? CancellationToken.None);
        }

        /// <summary>
        /// Reads the next chunk of data from the given reader. Bound to one connection:
        /// the reader and the token belong to the reader loop that calls this, so an old
        /// loop can never read from a newer connection's socket.
        /// </summary>
        private async Task<TCPReadResult> readAsync(DataReader reader, CancellationToken token)
        {
            if (reader == null || token.IsCancellationRequested || state != ConnectionState.CONNECTED)
            {
                return new TCPReadResult(TCPReadState.FAILURE, null);
            }

            StringBuilder data = new StringBuilder();

            // Read the first batch (cancelable, so a disconnect ends a pending read):
            uint readCount = await reader.LoadAsync(BUFFER_SIZE).AsTask(token);

            // To close a TCP connection, the opponent sends a 0 length message:
            if (readCount <= 0)
            {
                return new TCPReadResult(TCPReadState.END_OF_STREAM, null);
            }

            while (reader.UnconsumedBufferLength > 0)
            {
                data.Append(reader.ReadString(reader.UnconsumedBufferLength));
            }

            // If there is still data left to read, continue until a timeout occurs or a close got requested:
            while (!token.IsCancellationRequested && state == ConnectionState.CONNECTED && readCount >= BUFFER_SIZE)
            {
                try
                {
                    readCount = await reader.LoadAsync(BUFFER_SIZE).AsTask(token);

                    while (reader.UnconsumedBufferLength > 0)
                    {
                        data.Append(reader.ReadString(reader.UnconsumedBufferLength));
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }

            return new TCPReadResult(TCPReadState.SUCCESS, data.ToString());
        }

        public void startReaderTask()
        {
            if (state != ConnectionState.CONNECTED)
            {
                throw new InvalidOperationException("[TCPConnection2]: Unable to start reader task! ConnectionState != CONNECTED - state = " + state);
            }

            // Ensure no other reader task is running:
            if (readingCTS != null && !readingCTS.IsCancellationRequested)
            {
                readingCTS.Cancel();
            }

            // This loop owns exactly this reader and this token. Once the connection
            // gets torn down (token cancelled) it ends and never touches the state or
            // the socket of a newer connection that reuses this object. Before, an old
            // loop kept going as soon as a reconnect reached CONNECTED again, read from
            // the new socket in parallel with the new loop and killed it.
            CancellationTokenSource cts = new CancellationTokenSource();
            readingCTS = cts;
            CancellationToken token = cts.Token;
            DataReader reader = dataReader;

            try
            {
                Task.Run(async () =>
                {
                    TCPReadResult readResult = null;
                    int lastReadingFailedCount = 0;
                    int errorCount = 0;
                    DateTime lastReadingFailed = DateTime.MinValue;

                    while (!token.IsCancellationRequested && state == ConnectionState.CONNECTED && errorCount < 3)
                    {
                        try
                        {
                            readResult = await readAsync(reader, token);
                            // Check if reading failed:
                            switch (readResult.STATE)
                            {
                                case TCPReadState.SUCCESS:
                                    lastReadingFailedCount = 0;
                                    errorCount = 0;
                                    Logger.Debug("[TCPConnection2]: Received from (" + account.serverAddress + "):" + readResult.DATA);

                                    // Trigger the NewDataReceived event:
                                    NewDataReceived?.Invoke(this, new NewDataReceivedEventArgs(readResult.DATA));
                                    break;

                                case TCPReadState.FAILURE:
                                    if (lastReadingFailedCount++ <= 0)
                                    {
                                        lastReadingFailed = DateTime.Now;
                                    }

                                    // Read 5 empty or null strings in an interval lower than 1 second:
                                    double c = DateTime.Now.Subtract(lastReadingFailed).TotalSeconds;
                                    if (lastReadingFailedCount > 5 && c < 1)
                                    {
                                        lastConnectionError = new ConnectionError(ConnectionErrorCode.READING_LOOP);
                                        errorCount = int.MaxValue;
                                        continue;
                                    }
                                    break;

                                case TCPReadState.END_OF_STREAM:
                                    if (!token.IsCancellationRequested)
                                    {
                                        Logger.Info("Socket closed because received 0-length message from: " + account.serverAddress);
                                        disconnect();
                                    }
                                    break;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            if (token.IsCancellationRequested)
                            {
                                // Our connection got closed - just stop.
                                break;
                            }
                            lastConnectionError = new ConnectionError(ConnectionErrorCode.READING_CANCELED);
                            errorCount++;
                            Logger.Warn("[TCPConnection2]: read canceled (" + errorCount + "/3).");
                        }
                        catch (Exception e)
                        {
                            if (token.IsCancellationRequested)
                            {
                                // Our socket got disposed by a disconnect - just stop.
                                break;
                            }
                            SocketErrorStatus status = SocketErrorStatus.Unknown;
                            if (e is AggregateException aggregateException && aggregateException.InnerException != null)
                            {
                                status = SocketError.GetStatus(e.InnerException.HResult);
                            }
                            else
                            {
                                Exception baseException = e.GetBaseException();
                                if (baseException != null)
                                {
                                    status = SocketError.GetStatus(e.GetBaseException().HResult);
                                }
                                else
                                {
                                    status = SocketError.GetStatus(e.HResult);
                                }
                            }

                            lastConnectionError = new ConnectionError(status, e.Message);
                            switch (status)
                            {
                                // Some kind of connection lost:
                                case SocketErrorStatus.ConnectionTimedOut:
                                case SocketErrorStatus.ConnectionRefused:
                                case SocketErrorStatus.NetworkDroppedConnectionOnReset:
                                case SocketErrorStatus.SoftwareCausedConnectionAbort:
                                case SocketErrorStatus.ConnectionResetByPeer:
                                    errorCount = int.MaxValue;
                                    break;

                                default:
                                    errorCount++;
                                    break;
                            }
                            Logger.Warn("[TCPConnection2]: read failed (" + (errorCount >= 3 ? "fatal" : errorCount + "/3") + ") - " + status + ": " + e.GetType().Name + ": " + e.Message);
                        }
                    }

                    if (token.IsCancellationRequested)
                    {
                        Logger.Debug("[TCPConnection2]: reader stopped - connection closed.");
                        return;
                    }
                    if (errorCount >= 3)
                    {
                        Logger.Warn("[TCPConnection2]: reader gave up - " + describe(lastConnectionError));
                        setState(ConnectionState.ERROR, lastConnectionError);
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                // Reader task got canceled
            }
        }

        #endregion

        #region --Misc Methods (Private)--
        private static string describe(ConnectionError error)
        {
            if (error == null)
            {
                return "unknown";
            }
            return error.ERROR_CODE + (error.ERROR_CODE == ConnectionErrorCode.SOCKET_ERROR ? " (" + error.SOCKET_ERROR + ")" : "") + (string.IsNullOrEmpty(error.ERROR_MESSAGE) ? "" : ": " + error.ERROR_MESSAGE);
        }

        /// <summary>
        /// Disposes the given socket if it is still the current one. Aborts all pending
        /// operations on it (a hung write included); the reader loop notices and reports
        /// the error.
        /// </summary>
        private void abortSocket(StreamSocket s)
        {
            if (s == null || !ReferenceEquals(s, socket))
            {
                return;
            }
            try
            {
                s.Dispose();
            }
            catch (Exception e)
            {
                Logger.Error("[TCPConnection2]: failed to abort the socket.", e);
            }
        }

        /// <summary>Keeps an abandoned task's later exception from going unobserved.</summary>
        private static void observeException(Task t)
        {
            t.ContinueWith(prev => { Exception ignored = prev.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void onConnectionError(Exception e, int connectionTry)
        {
            Logger.Error("[TCPConnection2]: " + connectionTry + " try to connect to " + account?.serverAddress + " failed:", e);
            SocketErrorStatus socketErrorStatus = SocketError.GetStatus(e.GetBaseException().HResult);
            lastConnectionError = new ConnectionError(socketErrorStatus, e.Message);
        }

        #endregion

        #region --Misc Methods (Protected)--


        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--


        #endregion
    }
}
