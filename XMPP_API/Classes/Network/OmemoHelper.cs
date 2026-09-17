using libsignal;
using libsignal.state;
using Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XMPP_API.Classes.Crypto;
using XMPP_API.Classes.Network.Events;
using XMPP_API.Classes.Network.XML.DBEntries;
using XMPP_API.Classes.Network.XML.DBManager;
using XMPP_API.Classes.Network.XML.Messages;
using XMPP_API.Classes.Network.XML.Messages.XEP_0060;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384.Signal;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384.Signal.Session;

namespace XMPP_API.Classes.Network
{
    public class OmemoHelper : IDisposable
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public OmemoHelperState STATE { get; private set; }
        public OmemoDevices DEVICES { get; private set; }

        private readonly XMPPConnection2 CONNECTION;

        private uint tmpDeviceId;
        private MessageResponseHelper<IQMessage> requestDeviceListHelper;
        private MessageResponseHelper<IQMessage> updateDeviceListHelper;
        private MessageResponseHelper<IQMessage> announceBundleInfoHelper;
        private MessageResponseHelper<IQMessage> republishBundleInfoHelper;
        private MessageResponseHelper<IQMessage> configureNodeHelper;
        private Action configureNodeRetry;
        // Nodes that already got reconfigured to an open access model during this connection:
        private readonly HashSet<string> CONFIGURED_NODES = new HashSet<string>();
        // Contacts already granted 'member' access to our OMEMO nodes this connection:
        private readonly HashSet<string> GRANTED_CONTACTS = new HashSet<string>();

        // IQ timeout for all OMEMO related requests (device lists, bundles, ...). The default 5 seconds are too short for mobile connections:
        public const int IQ_TIMEOUT_SEC = 15;

        private MessageResponseHelper<IQMessage> requestDeviceListStatelessHelper;
        private MessageResponseHelper<IQMessage> resetDeviceListStatelessHelper;
        private Action<bool, OmemoDevices> requestDeviceListStatelessOnResult;
        private Action<bool> resetDeviceListStatelessOnResult;

        // Whether publishing with an open access model (publish-options) is supported by the server.
        // Gets set to false once the server rejected the publish-options, the publish is then retried without them.
        private bool openAccessModelSupported;
        private OmemoDevices pendingDeviceList;
        private bool republishBundleInfoPending;

        // Keep sessions during App runtime:
        private readonly Dictionary<string, OmemoSession> OMEMO_SESSIONS;
        private readonly Dictionary<string, Tuple<List<OmemoMessageMessage>, OmemoSessionBuildHelper>> MESSAGE_CACHE;
        private readonly SessionStore SESSION_STORE;
        private readonly PreKeyStore PRE_KEY_STORE;
        private readonly SignedPreKeyStore SIGNED_PRE_KEY_STORE;
        private readonly IdentityKeyStore IDENTITY_STORE;

        public delegate void OmemoSessionBuildErrorEventHandler(OmemoHelper helper, OmemoSessionBuildErrorEventArgs args);
        /// <summary>
        /// Gets invoked once building an OMEMO session for a chat failed and all cached messages for it got dropped.
        /// </summary>
        public event OmemoSessionBuildErrorEventHandler SessionBuildError;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 06/08/2018 Created [Fabian Sauter]
        /// </history>
        public OmemoHelper(XMPPConnection2 connection)
        {
            this.CONNECTION = connection;

            this.OMEMO_SESSIONS = new Dictionary<string, OmemoSession>();
            this.MESSAGE_CACHE = new Dictionary<string, Tuple<List<OmemoMessageMessage>, OmemoSessionBuildHelper>>();
            this.SESSION_STORE = new OmemoSessionStore(connection.account);
            this.PRE_KEY_STORE = new OmemoPreKeyStore(connection.account);
            this.SIGNED_PRE_KEY_STORE = new OmemoSignedPreKeyStore(connection.account);
            this.IDENTITY_STORE = new OmemoIdentityKeyStore(connection.account);
            this.openAccessModelSupported = true;

            reset();
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        private void setState(OmemoHelperState newState)
        {
            if (STATE != newState)
            {
                OmemoHelperState oldState = STATE;
                STATE = newState;
                Logger.Debug("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") " + oldState + " -> " + STATE);
                if (STATE == OmemoHelperState.ERROR)
                {
                    CONNECTION.NewValidMessage -= CONNECTION_ConnectionNewValidMessage;

                }
                else if (oldState == OmemoHelperState.ERROR && STATE != OmemoHelperState.ERROR)
                {
                    reset();
                }

                if (STATE == OmemoHelperState.ENABLED)
                {
                    Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Enabled.");
                }
            }
        }

        public IList<uint> getDevicesIdsForChat(string chatJid)
        {
            return OmemoDeviceDBManager.INSTANCE.getDeviceIds(chatJid, CONNECTION.account.getIdAndDomain());
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        /// <summary>
        /// Creates a new MessageResponseHelper with the OMEMO specific timeout.
        /// </summary>
        internal MessageResponseHelper<IQMessage> newResponseHelper(Func<IQMessage, bool> onMessage, Action onTimeout)
        {
            return new MessageResponseHelper<IQMessage>(CONNECTION, onMessage, onTimeout)
            {
                timeout = TimeSpan.FromSeconds(IQ_TIMEOUT_SEC)
            };
        }

        public SessionCipher loadCipher(SignalProtocolAddress address)
        {
            return new SessionCipher(SESSION_STORE, PRE_KEY_STORE, SIGNED_PRE_KEY_STORE, IDENTITY_STORE, address);
        }

        public bool containsSession(SignalProtocolAddress address)
        {
            return SESSION_STORE.ContainsSession(address);
        }

        public void Dispose()
        {
            CONNECTION.ConnectionStateChanged -= CONNECTION_ConnectionStateChanged;
            CONNECTION.NewValidMessage -= CONNECTION_ConnectionNewValidMessage;
        }

        public void reset()
        {
            setState(OmemoHelperState.DISABLED);

            CONNECTION.ConnectionStateChanged -= CONNECTION_ConnectionStateChanged;
            CONNECTION.ConnectionStateChanged += CONNECTION_ConnectionStateChanged;

            CONNECTION.NewValidMessage -= CONNECTION_ConnectionNewValidMessage;
            CONNECTION.NewValidMessage += CONNECTION_ConnectionNewValidMessage;

            tmpDeviceId = 0;
            pendingDeviceList = null;
            republishBundleInfoPending = false;

            if (requestDeviceListHelper != null)
            {
                requestDeviceListHelper.Dispose();
                requestDeviceListHelper = null;
            }
            if (updateDeviceListHelper != null)
            {
                updateDeviceListHelper.Dispose();
                updateDeviceListHelper = null;
            }
            if (announceBundleInfoHelper != null)
            {
                announceBundleInfoHelper.Dispose();
                announceBundleInfoHelper = null;
            }
            if (republishBundleInfoHelper != null)
            {
                republishBundleInfoHelper.Dispose();
                republishBundleInfoHelper = null;
            }
            if (configureNodeHelper != null)
            {
                configureNodeHelper.Dispose();
                configureNodeHelper = null;
            }
            configureNodeRetry = null;
            lock (GRANTED_CONTACTS) { GRANTED_CONTACTS.Clear(); }
            if (requestDeviceListStatelessHelper != null)
            {
                requestDeviceListStatelessHelper.Dispose();
                requestDeviceListStatelessHelper = null;
            }
            if (resetDeviceListStatelessHelper != null)
            {
                resetDeviceListStatelessHelper.Dispose();
                resetDeviceListStatelessHelper = null;
            }

            requestDeviceListStatelessOnResult = null;
            resetDeviceListStatelessOnResult = null;

            // Sessions are bound to the connection - drop all pending session builds:
            lock (MESSAGE_CACHE)
            {
                foreach (KeyValuePair<string, Tuple<List<OmemoMessageMessage>, OmemoSessionBuildHelper>> pair in MESSAGE_CACHE)
                {
                    pair.Value.Item2.Dispose();
                    onSessionBuildFailed(pair.Key, OmemoSessionBuildError.UNKNOWN, pair.Value.Item1);
                }
                MESSAGE_CACHE.Clear();
            }
            OMEMO_SESSIONS.Clear();
        }

        public SignalProtocolAddress newSession(string chatJid, OmemoBundleInformationResultMessage bundleInfoMsg)
        {
            return newSession(chatJid, bundleInfoMsg.DEVICE_ID, bundleInfoMsg.BUNDLE_INFO.getRandomPreKey(bundleInfoMsg.DEVICE_ID));
        }

        public SignalProtocolAddress newSession(string chatJid, uint recipientDeviceId, PreKeyBundle recipientPreKey)
        {
            SignalProtocolAddress address = new SignalProtocolAddress(chatJid, recipientDeviceId);
            SessionBuilder builder = new SessionBuilder(SESSION_STORE, PRE_KEY_STORE, SIGNED_PRE_KEY_STORE, IDENTITY_STORE, address);
            builder.process(recipientPreKey);
            return address;
        }

        public void sendOmemoMessage(OmemoMessageMessage msg, string chatJid, string accountJid)
        {
            // While enabling OMEMO (requesting the device list, announcing the bundle) the connection is up,
            // so session building can already start. Only refuse if OMEMO is disabled or failed:
            if (STATE == OmemoHelperState.DISABLED || STATE == OmemoHelperState.ERROR)
            {
                Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Unable to send OMEMO message - OMEMO is not enabled (state: " + STATE + ")!");
                onSessionBuildFailed(chatJid, OmemoSessionBuildError.OMEMO_NOT_ENABLED, new List<OmemoMessageMessage>() { msg });
                return;
            }

            // Make sure the contact can read our device list / bundle (whitelist-forcing servers):
            grantOmemoNodeAccess(chatJid);

            OmemoSession session = null;
            lock (MESSAGE_CACHE)
            {
                // Check if already trying to build a new session:
                if (MESSAGE_CACHE.ContainsKey(chatJid))
                {
                    MESSAGE_CACHE[chatJid].Item1.Add(msg);
                    return;
                }
                // Reuse the session established during this connection:
                else if (OMEMO_SESSIONS.ContainsKey(chatJid))
                {
                    session = OMEMO_SESSIONS[chatJid];
                }
                else
                {
                    // If not start a new session build helper:
                    OmemoDeviceListSubscriptionTable subscriptionTable = OmemoDeviceDBManager.INSTANCE.getDeviceListSubscription(chatJid, accountJid);
                    OmemoSessionBuildHelper sessionHelper = new OmemoSessionBuildHelper(chatJid, accountJid, CONNECTION.account.getIdDomainAndResource(), onSessionBuilderResult, CONNECTION, this);
                    MESSAGE_CACHE[chatJid] = new Tuple<List<OmemoMessageMessage>, OmemoSessionBuildHelper>(new List<OmemoMessageMessage>(), sessionHelper);
                    MESSAGE_CACHE[chatJid].Item1.Add(msg);
                    sessionHelper.start(subscriptionTable.state);
                    return;
                }
            }

            Task.Run(() => encryptAndSendAsync(msg, session));
        }

        /// <summary>
        /// Called once a message got received from a chat's device.
        /// Makes sure the device is part of the cached device list, so replies get encrypted for it as well.
        /// Sessions get rebuilt on the next message if the device list changed.
        /// </summary>
        public void onOmemoMessageReceived(string chatJid, uint deviceId)
        {
            string accountJid = CONNECTION.account.getIdAndDomain();
            if (string.Equals(chatJid, accountJid))
            {
                return;
            }
            grantOmemoNodeAccess(chatJid);
            List<uint> devices = OmemoDeviceDBManager.INSTANCE.getDeviceIds(chatJid, accountJid);
            if (!devices.Contains(deviceId))
            {
                Logger.Info("[OMEMO HELPER](" + accountJid + ") Received message from unknown device " + chatJid + ':' + deviceId + " - adding it to the device list.");
                OmemoDevices omemoDevices = new OmemoDevices();
                omemoDevices.DEVICES.AddRange(devices);
                omemoDevices.DEVICES.Add(deviceId);
                OmemoDeviceDBManager.INSTANCE.setDevices(omemoDevices, chatJid, accountJid);
                lock (MESSAGE_CACHE)
                {
                    OMEMO_SESSIONS.Remove(chatJid);
                }
            }
        }

        /// <summary>
        /// Called once a PreKeySignalMessage got decrypted, i.e. a contact used one of our published pre keys.
        /// libsignal removed the used pre key from the store - generate new ones if required and republish the bundle.
        /// </summary>
        public void onPreKeyMessageReceived()
        {
            try
            {
                if (CONNECTION.account.refillOmemoPreKeys())
                {
                    Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Generated new pre keys - republishing bundle information.");
                    CONNECTION.account.onPropertyChanged(nameof(CONNECTION.account.omemoPreKeys));
                    republishBundleInfo();
                }
            }
            catch (Exception e)
            {
                Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to refill pre keys.", e);
            }
        }

        /// <summary>
        /// Grants a contact 'member' affiliation on our OMEMO device list and bundle nodes.
        /// Required on servers that force a 'whitelist' access model (e.g. xabber.org / ejabberd),
        /// where 'open' access cannot be set and contacts otherwise get 'not-allowed' reading our nodes.
        /// Idempotent per connection.
        /// </summary>
        public void grantOmemoNodeAccess(string contactJid)
        {
            if (STATE != OmemoHelperState.ENABLED || CONNECTION.account.omemoDeviceId == 0)
            {
                return;
            }
            string bare = Utils.getBareJidFromFullJid(contactJid);
            if (string.IsNullOrEmpty(bare) || string.Equals(bare, CONNECTION.account.getIdAndDomain()))
            {
                return;
            }
            lock (GRANTED_CONTACTS)
            {
                if (!GRANTED_CONTACTS.Add(bare))
                {
                    return;
                }
            }
            Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Granting " + bare + " member access to our OMEMO nodes.");
            string from = CONNECTION.account.getIdDomainAndResource();
            string bundleNode = Consts.XML_XEP_0384_BUNDLE_INFO_NODE + CONNECTION.account.omemoDeviceId;
            Task.Run(async () =>
            {
                try
                {
                    await CONNECTION.sendAsync(new PubSubSetNodeAffiliationMessage(from, null, Consts.XML_XEP_0384_DEVICE_LIST_NODE, bare, "member"), false, false);
                    await CONNECTION.sendAsync(new PubSubSetNodeAffiliationMessage(from, null, bundleNode, bare, "member"), false, false);
                }
                catch (Exception e)
                {
                    Logger.Warn("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to grant OMEMO node access to " + bare + ": " + e.Message);
                }
            });
        }

        /// <summary>
        /// Called when an OMEMO message arrives from another of our own devices (Note to Self). Ensures that
        /// device is present in our own OMEMO device list and republishes it if it was missing, so our devices
        /// encrypt for each other. Republishing also nudges the other device to re-read the (now complete) list.
        /// </summary>
        public void onOwnOmemoDeviceSeen(uint deviceId)
        {
            if (STATE != OmemoHelperState.ENABLED || deviceId == 0 || deviceId == CONNECTION.account.omemoDeviceId || DEVICES == null)
            {
                return;
            }
            if (DEVICES.DEVICES.Contains(deviceId))
            {
                return;
            }
            Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Learned own device " + deviceId + " from an incoming message - adding it to the device list.");
            DEVICES.DEVICES.Add(deviceId);
            OmemoSetDeviceListMessage msg = new OmemoSetDeviceListMessage(CONNECTION.account.getIdDomainAndResource(), DEVICES, openAccessModelSupported);
            Task.Run(async () =>
            {
                try
                {
                    await CONNECTION.sendAsync(msg, false, false);
                }
                catch (Exception e)
                {
                    Logger.Warn("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to publish updated device list: " + e.Message);
                }
            });
        }

        public void onOmemoDeviceListEventMessage(OmemoDeviceListEventMessage msg)
        {
            string chatJid = msg.getFrom() == null ? CONNECTION.account.getIdAndDomain() : Utils.getBareJidFromFullJid(msg.getFrom());
            if (string.Equals(chatJid, CONNECTION.account.getIdAndDomain()))
            {
                // Own device list - handled in onOwnDeviceListEventMessageAsync():
                return;
            }
            List<uint> oldDevices = OmemoDeviceDBManager.INSTANCE.getDeviceIds(chatJid, CONNECTION.account.getIdAndDomain());
            OmemoDeviceDBManager.INSTANCE.setDevices(msg.DEVICES, chatJid, CONNECTION.account.getIdAndDomain());
            OmemoDeviceDBManager.INSTANCE.setDeviceListSubscription(new OmemoDeviceListSubscriptionTable(chatJid, CONNECTION.account.getIdAndDomain(), OmemoDeviceListSubscriptionState.SUBSCRIBED, DateTime.Now));

            // Rebuild the session on the next message if the device list changed:
            bool changed = oldDevices.Count != msg.DEVICES.DEVICES.Count;
            if (!changed)
            {
                foreach (uint device in msg.DEVICES.DEVICES)
                {
                    if (!oldDevices.Contains(device))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (changed)
            {
                lock (MESSAGE_CACHE)
                {
                    OMEMO_SESSIONS.Remove(chatJid);
                }
            }
        }

        public void requestDeviceListStateless(Action<bool, OmemoDevices> onResult)
        {
            requestDeviceListStatelessOnResult = onResult;

            if (requestDeviceListStatelessHelper != null)
            {
                requestDeviceListStatelessHelper.Dispose();
                requestDeviceListStatelessHelper = null;
            }
            requestDeviceListStatelessHelper = newResponseHelper(onRequestDeviceListStatelessMessage, onRequestDeviceListStatelessTimeout);
            OmemoRequestDeviceListMessage msg = new OmemoRequestDeviceListMessage(CONNECTION.account.getIdDomainAndResource(), null);
            requestDeviceListStatelessHelper.start(msg);
        }

        public void resetDeviceListStateless(Action<bool> onResult)
        {
            resetDeviceListStatelessOnResult = onResult;
            deleteDeviceListNode();
        }

        #endregion

        #region --Misc Methods (Private)--
        private void deleteDeviceListNode()
        {
            if (resetDeviceListStatelessHelper != null)
            {
                resetDeviceListStatelessHelper.Dispose();
                resetDeviceListStatelessHelper = null;
            }

            resetDeviceListStatelessHelper = newResponseHelper(onDeleteDeviceListNodeMessage, onDeleteDeviceListNodeTimeout);
            PubSubDeleteNodeMessage msg = new PubSubDeleteNodeMessage(CONNECTION.account.getIdDomainAndResource(), null, Consts.XML_XEP_0384_DEVICE_LIST_NODE);
            resetDeviceListStatelessHelper.start(msg);
        }

        private void setDeviceListToOwnDevice()
        {
            if (resetDeviceListStatelessHelper != null)
            {
                resetDeviceListStatelessHelper.Dispose();
                resetDeviceListStatelessHelper = null;
            }
            OmemoDevices devices = new OmemoDevices();
            devices.DEVICES.Add(CONNECTION.account.omemoDeviceId);
            resetDeviceListStatelessHelper = newResponseHelper(onResetDeviceListStatelessMessage, onResetDeviceListStatelessTimeout);
            OmemoSetDeviceListMessage msg = new OmemoSetDeviceListMessage(CONNECTION.account.getIdDomainAndResource(), devices, openAccessModelSupported);
            resetDeviceListStatelessHelper.start(msg);
        }

        private bool onDeleteDeviceListNodeMessage(AbstractMessage msg)
        {
            if (msg is IQErrorMessage || (msg is IQMessage iMsg && string.Equals(iMsg.TYPE, IQMessage.RESULT)))
            {
                setDeviceListToOwnDevice();
                return true;
            }
            return false;
        }

        private void onDeleteDeviceListNodeTimeout()
        {
            setDeviceListToOwnDevice();
        }

        private bool onResetDeviceListStatelessMessage(IQMessage msg)
        {
            if (msg is IQErrorMessage errMsg)
            {
                if (handlePublishError(errMsg, Consts.XML_XEP_0384_DEVICE_LIST_NODE, setDeviceListToOwnDevice))
                {
                    return true;
                }
                resetDeviceListStatelessOnResult?.Invoke(false);
                resetDeviceListStatelessOnResult = null;
                return true;
            }
            else if (msg is IQMessage)
            {
                if (DEVICES == null)
                {
                    DEVICES = new OmemoDevices();
                }
                DEVICES.DEVICES.Clear();
                DEVICES.DEVICES.Add(CONNECTION.account.omemoDeviceId);
                resetDeviceListStatelessOnResult?.Invoke(true);
                resetDeviceListStatelessOnResult = null;
                return true;
            }
            return false;
        }

        private void onResetDeviceListStatelessTimeout()
        {
            resetDeviceListStatelessOnResult?.Invoke(false);
            resetDeviceListStatelessOnResult = null;
        }

        private bool onRequestDeviceListStatelessMessage(IQMessage msg)
        {
            if (msg is OmemoDeviceListResultMessage devMsg)
            {
                requestDeviceListStatelessOnResult?.Invoke(true, devMsg.DEVICES);
                requestDeviceListStatelessOnResult = null;
                return true;
            }
            else if (msg is IQErrorMessage errMsg)
            {
                if (errMsg.ERROR_OBJ.ERROR_NAME == ErrorName.ITEM_NOT_FOUND)
                {
                    requestDeviceListStatelessOnResult?.Invoke(true, new OmemoDevices());
                }
                else
                {
                    requestDeviceListStatelessOnResult?.Invoke(false, null);
                }
                requestDeviceListStatelessOnResult = null;
                return true;
            }
            return false;
        }

        private void onRequestDeviceListStatelessTimeout()
        {
            requestDeviceListStatelessOnResult?.Invoke(false, null);
            requestDeviceListStatelessOnResult = null;
        }

        private void onSessionBuilderResult(OmemoSessionBuildHelper sender, OmemoSessionBuildResult result)
        {
            Tuple<List<OmemoMessageMessage>, OmemoSessionBuildHelper> cache;
            lock (MESSAGE_CACHE)
            {
                if (!MESSAGE_CACHE.TryGetValue(sender.CHAT_JID, out cache))
                {
                    return;
                }
                MESSAGE_CACHE.Remove(sender.CHAT_JID);
                if (result.SUCCESS)
                {
                    OMEMO_SESSIONS[result.SESSION.CHAT_JID] = result.SESSION;
                }
            }
            cache.Item2.Dispose();

            if (result.SUCCESS)
            {
                Task.Run(() => sendAllOutstandingMessagesAsync(result.SESSION, cache.Item1));
            }
            else
            {
                Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to build OMEMO session for " + sender.CHAT_JID + " with: " + result.ERROR);
                onSessionBuildFailed(sender.CHAT_JID, result.ERROR, cache.Item1);
            }
        }

        private void onSessionBuildFailed(string chatJid, OmemoSessionBuildError error, List<OmemoMessageMessage> messages)
        {
            List<string> chatMessageIds = new List<string>();
            foreach (OmemoMessageMessage msg in messages)
            {
                if (msg.chatMessageId != null)
                {
                    chatMessageIds.Add(msg.chatMessageId);
                }
            }
            SessionBuildError?.Invoke(this, new OmemoSessionBuildErrorEventArgs(chatJid, error, chatMessageIds));
        }

        private async Task sendAllOutstandingMessagesAsync(OmemoSession omemoSession, List<OmemoMessageMessage> messages)
        {
            foreach (OmemoMessageMessage msg in messages)
            {
                await encryptAndSendAsync(msg, omemoSession);
            }
            Logger.Info("[OMEMO HELPER] Send all outstanding OMEMO messages for: " + omemoSession.CHAT_JID);
        }

        private async Task encryptAndSendAsync(OmemoMessageMessage msg, OmemoSession omemoSession)
        {
            try
            {
                // Encrypting advances the ratchet of every session, so only one message at a time:
                lock (omemoSession)
                {
                    msg.encrypt(omemoSession, CONNECTION.account.omemoDeviceId);
                }
            }
            catch (Exception e)
            {
                Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to encrypt message for: " + omemoSession.CHAT_JID, e);
                onSessionBuildFailed(omemoSession.CHAT_JID, OmemoSessionBuildError.UNKNOWN, new List<OmemoMessageMessage>() { msg });
                return;
            }
            await CONNECTION.sendAsync(msg, true, false);
        }

        private void requestDeviceList()
        {
            setState(OmemoHelperState.REQUESTING_DEVICE_LIST);
            Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Requesting device list.");
            if (requestDeviceListHelper != null)
            {
                requestDeviceListHelper.Dispose();
            }
            requestDeviceListHelper = newResponseHelper(onRequestDeviceListMsg, onTimeout);
            OmemoRequestDeviceListMessage msg = new OmemoRequestDeviceListMessage(CONNECTION.account.getIdDomainAndResource(), null);
            requestDeviceListHelper.start(msg);
        }

        private bool onRequestDeviceListMsg(AbstractMessage msg)
        {
            if (msg is OmemoDeviceListResultMessage devMsg)
            {
                updateDevicesIfNeeded(devMsg.DEVICES);
                return true;
            }
            else if (msg is IQErrorMessage errMsg)
            {
                if (errMsg.ERROR_OBJ.ERROR_NAME == ErrorName.ITEM_NOT_FOUND)
                {
                    Logger.Warn("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to request OMEMO device list - node does not exist. Creating node.");
                    updateDevicesIfNeeded(null);
                }
                else
                {
                    Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to request OMEMO device list form: " + CONNECTION.account.user.domain + "\n" + errMsg.ERROR_OBJ.ToString());
                    setState(OmemoHelperState.ERROR);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Handles an error reply to a publish request that included publish-options (open access model).
        /// - conflict / precondition-not-met: the node already exists with a different access model.
        ///   Reconfigure it to open (XEP-0060 section 8.2, like Conversations does) and publish again.
        /// - any other error: the server does not support publish-options - publish again without them.
        /// </summary>
        /// <returns>True if the error got handled and the publish will be retried.</returns>
        private bool handlePublishError(IQErrorMessage errMsg, string nodeName, Action retryPublish)
        {
            if (!openAccessModelSupported)
            {
                return false;
            }
            if (errMsg.ERROR_OBJ.ERROR_NAME == ErrorName.CONFLICT && !CONFIGURED_NODES.Contains(nodeName))
            {
                Logger.Warn("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Node " + nodeName + " exists with a different access model - reconfiguring it to open.");
                CONFIGURED_NODES.Add(nodeName);
                configureNodeAccessModel(nodeName, retryPublish);
                return true;
            }
            Logger.Warn("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Server rejected publish-options for " + nodeName + " (" + errMsg.ERROR_OBJ.ToString() + ") - retrying without them.");
            openAccessModelSupported = false;
            retryPublish();
            return true;
        }

        private void configureNodeAccessModel(string nodeName, Action retryPublish)
        {
            if (configureNodeHelper != null)
            {
                configureNodeHelper.Dispose();
            }
            configureNodeRetry = retryPublish;
            configureNodeHelper = newResponseHelper(onConfigureNodeMessage, onConfigureNodeTimeout);
            PubSubConfigureNodeMessage msg = new PubSubConfigureNodeMessage(CONNECTION.account.getIdDomainAndResource(), null, nodeName, PubSubConfigureNodeMessage.getOpenAccessModelConfig());
            configureNodeHelper.start(msg);
        }

        private bool onConfigureNodeMessage(IQMessage msg)
        {
            if (msg is IQErrorMessage errMsg)
            {
                Logger.Warn("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to reconfigure node (" + errMsg.ERROR_OBJ.ToString() + ") - publishing without publish-options.");
                openAccessModelSupported = false;
            }
            else if (msg is IQMessage)
            {
                Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Node access model set to open.");
            }
            else
            {
                return false;
            }
            Action retry = configureNodeRetry;
            configureNodeRetry = null;
            retry?.Invoke();
            return true;
        }

        private void onConfigureNodeTimeout()
        {
            Logger.Warn("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Reconfiguring node timed out - publishing without publish-options.");
            openAccessModelSupported = false;
            Action retry = configureNodeRetry;
            configureNodeRetry = null;
            retry?.Invoke();
        }

        private void announceBundleInfo()
        {
            setState(OmemoHelperState.ANNOUNCING_BUNDLE_INFO);
            Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Announcing bundle information for: " + CONNECTION.account.omemoDeviceId);
            if (announceBundleInfoHelper != null)
            {
                announceBundleInfoHelper.Dispose();
            }
            announceBundleInfoHelper = newResponseHelper(announceBundleInfoMsg, onTimeout);
            OmemoSetBundleInformationMessage msg = new OmemoSetBundleInformationMessage(CONNECTION.account.getIdDomainAndResource(), CONNECTION.account.getOmemoBundleInformation(), CONNECTION.account.omemoDeviceId, openAccessModelSupported);
            announceBundleInfoHelper.start(msg);
        }

        private bool announceBundleInfoMsg(AbstractMessage msg)
        {
            if (msg is IQErrorMessage errMsg)
            {
                if (handlePublishError(errMsg, Consts.XML_XEP_0384_BUNDLE_INFO_NODE + CONNECTION.account.omemoDeviceId, announceBundleInfo))
                {
                    return true;
                }
                Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to announce OMEMO bundle info to: " + CONNECTION.account.user.domain + "\n" + errMsg.ERROR_OBJ.ToString());
                setState(OmemoHelperState.ERROR);
                return true;
            }
            else if (msg is IQMessage)
            {
                Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Bundle info announced.");
                if (!CONNECTION.account.omemoBundleInfoAnnounced)
                {
                    CONNECTION.account.omemoBundleInfoAnnounced = true;
                    CONNECTION.account.onPropertyChanged(nameof(CONNECTION.account.omemoBundleInfoAnnounced));
                }
                onEnabled();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Republishes the bundle information (e.g. after new pre keys got generated) without changing the helper state.
        /// If OMEMO isn't enabled yet, the bundle gets published as part of the enable procedure anyway.
        /// </summary>
        private void republishBundleInfo()
        {
            if (STATE != OmemoHelperState.ENABLED)
            {
                republishBundleInfoPending = true;
                return;
            }
            republishBundleInfoPending = false;
            Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Republishing bundle information for: " + CONNECTION.account.omemoDeviceId);
            if (republishBundleInfoHelper != null)
            {
                republishBundleInfoHelper.Dispose();
            }
            republishBundleInfoHelper = newResponseHelper(republishBundleInfoMsg, onRepublishBundleInfoTimeout);
            OmemoSetBundleInformationMessage msg = new OmemoSetBundleInformationMessage(CONNECTION.account.getIdDomainAndResource(), CONNECTION.account.getOmemoBundleInformation(), CONNECTION.account.omemoDeviceId, openAccessModelSupported);
            republishBundleInfoHelper.start(msg);
        }

        private bool republishBundleInfoMsg(AbstractMessage msg)
        {
            if (msg is IQErrorMessage errMsg)
            {
                if (handlePublishError(errMsg, Consts.XML_XEP_0384_BUNDLE_INFO_NODE + CONNECTION.account.omemoDeviceId, republishBundleInfo))
                {
                    return true;
                }
                Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to republish OMEMO bundle info to: " + CONNECTION.account.user.domain + "\n" + errMsg.ERROR_OBJ.ToString());
                return true;
            }
            else if (msg is IQMessage)
            {
                Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Bundle info republished.");
                return true;
            }
            return false;
        }

        private void onRepublishBundleInfoTimeout()
        {
            Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to republish OMEMO bundle info - timeout!");
        }

        private void updateDevicesIfNeeded(OmemoDevices devicesRemote)
        {
            if (devicesRemote == null)
            {
                devicesRemote = new OmemoDevices();
            }

            bool updateDeviceList = false;
            // Device id hasn't been set. Pick a random, unique one:
            if (CONNECTION.account.omemoDeviceId == 0)
            {
                tmpDeviceId = CryptoUtils.generateOmemoDeviceIds(devicesRemote.DEVICES);
                devicesRemote.DEVICES.Add(tmpDeviceId);
                updateDeviceList = true;
            }
            else
            {
                if (!devicesRemote.DEVICES.Contains(CONNECTION.account.omemoDeviceId))
                {
                    devicesRemote.DEVICES.Add(CONNECTION.account.omemoDeviceId);
                    updateDeviceList = true;
                }
            }

            DEVICES = devicesRemote;
            if (updateDeviceList)
            {
                pendingDeviceList = devicesRemote;
                publishDeviceList();
            }
            else
            {
                // Always (re)announce the bundle on connect. It is a single IQ and makes sure the server
                // has the current pre keys, even if the node got lost or was published by an older version:
                announceBundleInfo();
            }
        }

        private void publishDeviceList()
        {
            setState(OmemoHelperState.UPDATING_DEVICE_LIST);
            Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Updating device list.");
            if (updateDeviceListHelper != null)
            {
                updateDeviceListHelper.Dispose();
            }
            updateDeviceListHelper = newResponseHelper(updateDevicesIfNeededMsg, onTimeout);
            OmemoSetDeviceListMessage msg = new OmemoSetDeviceListMessage(CONNECTION.account.getIdDomainAndResource(), pendingDeviceList, openAccessModelSupported);
            updateDeviceListHelper.start(msg);
        }

        private bool updateDevicesIfNeededMsg(AbstractMessage msg)
        {
            if (msg is IQErrorMessage errMsg)
            {
                if (handlePublishError(errMsg, Consts.XML_XEP_0384_DEVICE_LIST_NODE, publishDeviceList))
                {
                    return true;
                }
                Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to set OMEMO device list to: " + CONNECTION.account.user.domain + "\n" + errMsg.ERROR_OBJ.ToString());
                setState(OmemoHelperState.ERROR);
                return true;
            }
            else if (msg is IQMessage)
            {
                Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Device list updated.");
                pendingDeviceList = null;
                if (CONNECTION.account.omemoDeviceId == 0)
                {
                    CONNECTION.account.omemoDeviceId = tmpDeviceId;
                    CONNECTION.account.onPropertyChanged(nameof(CONNECTION.account.omemoDeviceId));
                }
                announceBundleInfo();
                return true;
            }
            return false;
        }

        private void onEnabled()
        {
            setState(OmemoHelperState.ENABLED);
            if (republishBundleInfoPending)
            {
                republishBundleInfo();
            }
        }

        private void onTimeout()
        {
            switch (STATE)
            {
                case OmemoHelperState.REQUESTING_DEVICE_LIST:
                case OmemoHelperState.UPDATING_DEVICE_LIST:
                case OmemoHelperState.ANNOUNCING_BUNDLE_INFO:
                    Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed in state " + STATE + " - timeout!");
                    setState(OmemoHelperState.ERROR);
                    break;
            }
        }

        /// <summary>
        /// If the own OMEMO device list got changed and does not contain the local device id update it and add it again.
        /// </summary>
        /// <param name="msg">The received OmemoDeviceListEventMessage.</param>
        private async Task onOwnDeviceListEventMessageAsync(OmemoDeviceListEventMessage msg)
        {
            // Only device list events for the own account are relevant here:
            if (msg.getFrom() != null && !string.Equals(Utils.getBareJidFromFullJid(msg.getFrom()), CONNECTION.account.getIdAndDomain()))
            {
                return;
            }
            if (CONNECTION.account.omemoDeviceId == 0)
            {
                return;
            }

            DEVICES = msg.DEVICES;
            if (!msg.DEVICES.DEVICES.Contains(CONNECTION.account.omemoDeviceId))
            {
                Logger.Info("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Own device id missing in the device list - adding it again.");
                msg.DEVICES.DEVICES.Add(CONNECTION.account.omemoDeviceId);
                OmemoSetDeviceListMessage setMsg = new OmemoSetDeviceListMessage(CONNECTION.account.getIdDomainAndResource(), msg.DEVICES, openAccessModelSupported);
                await CONNECTION.sendAsync(setMsg, false, false);
            }
        }

        #endregion

        #region --Misc Methods (Protected)--


        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--
        private async void CONNECTION_ConnectionNewValidMessage(IMessageSender sender, Events.NewValidMessageEventArgs args)
        {
            if (args.MESSAGE is OmemoDeviceListEventMessage eventMsg)
            {
                await onOwnDeviceListEventMessageAsync(eventMsg);
            }
        }

        private void CONNECTION_ConnectionStateChanged(AbstractConnection2 connection, Events.ConnectionStateChangedEventArgs arg)
        {
            // This handler is registered before XMPPClient's, so anything that
            // escapes here aborts the rest of the invocation list and the state
            // change is never reported to the client (e.g. requesting the device
            // list on a socket the server has already closed used to swallow the
            // 'Connected to account' notification entirely).
            try
            {
                switch (arg.newState)
                {
                    case ConnectionState.CONNECTED:
                        if (!CONNECTION.account.hasOmemoKeys())
                        {
                            setState(OmemoHelperState.ERROR);
                            Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed - no keys!");
                        }
                        else if (STATE == OmemoHelperState.DISABLED)
                        {
                            requestDeviceList();
                        }
                        break;

                    case ConnectionState.DISCONNECTED:
                    case ConnectionState.ERROR:
                        reset();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[OMEMO HELPER](" + CONNECTION.account.getIdAndDomain() + ") Failed to handle connection state " + arg.newState + ".", ex);
                setState(OmemoHelperState.ERROR);
            }
        }

        #endregion
    }
}
