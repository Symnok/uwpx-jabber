using libsignal;
using Logging;
using System;
using System.Collections.Generic;
using XMPP_API.Classes.Network.XML.DBEntries;
using XMPP_API.Classes.Network.XML.DBManager;
using XMPP_API.Classes.Network.XML.Messages.XEP_0060;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0384.Signal.Session
{
    public class OmemoSessionBuildHelper : IDisposable
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public OmemoSessionBuildHelperState STATE { get; private set; }

        private readonly Action<OmemoSessionBuildHelper, OmemoSessionBuildResult> ON_SESSION_RESULT;
        private readonly XMPPConnection2 CONNECTION;
        public readonly string CHAT_JID;
        private readonly string BARE_ACCOUNT_JID;
        private readonly string FULL_ACCOUNT_JID;
        private readonly OmemoHelper OMEMO_HELPER;
        private List<uint> toDoDevicesRemote;
        private List<uint> toDoDevicesOwn;
        private SignalProtocolAddress curAddress;
        private bool deviceListRetried;
        private readonly OmemoSession SESSION;
        private MessageResponseHelper<IQMessage> requestDeviceListHelper;
        private MessageResponseHelper<IQMessage> requestBundleInfoHelper;
        private MessageResponseHelper<IQMessage> subscribeToDeviceListHelper;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 10/08/2018 Created [Fabian Sauter]
        /// </history>
        internal OmemoSessionBuildHelper(string chatJid, string bareAccountJid, string fullAccountJid, Action<OmemoSessionBuildHelper, OmemoSessionBuildResult> onSessionResult, XMPPConnection2 connection, OmemoHelper omemoHelper)
        {
            this.CONNECTION = connection;
            this.ON_SESSION_RESULT = onSessionResult;
            this.CHAT_JID = chatJid;
            this.BARE_ACCOUNT_JID = bareAccountJid;
            this.FULL_ACCOUNT_JID = fullAccountJid;
            this.OMEMO_HELPER = omemoHelper;
            this.STATE = OmemoSessionBuildHelperState.NOT_STARTED;
            this.requestDeviceListHelper = null;
            this.requestBundleInfoHelper = null;
            this.SESSION = new OmemoSession(chatJid);
            this.curAddress = null;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        private void setState(OmemoSessionBuildHelperState newState)
        {
            if (STATE != newState)
            {
                STATE = newState;
            }
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        public void start(OmemoDeviceListSubscriptionState subscriptionState)
        {
            // Request the current device list from the contact. On servers like ejabberd retrieving the
            // node requires a subscription (it answers 'closed-node'/'not-allowed' otherwise), so on a
            // non-fatal error we subscribe and retry once, then fall back to the cached list.
            deviceListRetried = false;
            requestDeviceList();
        }

        public void Dispose()
        {
            requestDeviceListHelper?.Dispose();
            requestBundleInfoHelper?.Dispose();
            subscribeToDeviceListHelper?.Dispose();
        }

        #endregion

        #region --Misc Methods (Private)--
        private void requestDeviceList()
        {
            setState(OmemoSessionBuildHelperState.REQUESTING_DEVICE_LIST);
            if (requestDeviceListHelper != null)
            {
                requestDeviceListHelper?.Dispose();
                requestDeviceListHelper = null;
            }

            requestDeviceListHelper = OMEMO_HELPER.newResponseHelper(onRequestDeviceListMessage, onTimeout);
            OmemoRequestDeviceListMessage msg = new OmemoRequestDeviceListMessage(FULL_ACCOUNT_JID, CHAT_JID);
            requestDeviceListHelper.start(msg);
        }

        /// <summary>
        /// Subscribes to the contact's device list node and retries the device list request once the
        /// subscription is established. ejabberd only serves a PEP node's items to its subscribers.
        /// </summary>
        private void subscribeThenRetryDeviceList()
        {
            setState(OmemoSessionBuildHelperState.SUBSCRIBING_TO_DEVICE_LIST);
            if (subscribeToDeviceListHelper != null)
            {
                subscribeToDeviceListHelper?.Dispose();
                subscribeToDeviceListHelper = null;
            }
            subscribeToDeviceListHelper = OMEMO_HELPER.newResponseHelper(onSubscribeThenRetryMessage, onTimeout);
            OmemoSubscribeToDeviceListMessage msg = new OmemoSubscribeToDeviceListMessage(FULL_ACCOUNT_JID, BARE_ACCOUNT_JID, CHAT_JID);
            subscribeToDeviceListHelper.start(msg);
        }

        private void subscribeToDeviceList()
        {
            if (subscribeToDeviceListHelper != null)
            {
                subscribeToDeviceListHelper?.Dispose();
                subscribeToDeviceListHelper = null;
            }

            // Runs in parallel to the bundle requests, so it must not touch STATE or the shared onTimeout():
            subscribeToDeviceListHelper = OMEMO_HELPER.newResponseHelper(onSubscribeToDeviceListMessage, onSubscribeToDeviceListTimeout);
            OmemoSubscribeToDeviceListMessage msg = new OmemoSubscribeToDeviceListMessage(FULL_ACCOUNT_JID, BARE_ACCOUNT_JID, CHAT_JID);
            subscribeToDeviceListHelper.start(msg);
        }

        private void requestBundleInformation()
        {
            setState(OmemoSessionBuildHelperState.REQUESTING_BUNDLE_INFORMATION);
            if (requestBundleInfoHelper != null)
            {
                requestBundleInfoHelper?.Dispose();
                requestBundleInfoHelper = null;
            }

            requestBundleInfoHelper = OMEMO_HELPER.newResponseHelper(onRequestBundleInformationMessage, onTimeout);
            OmemoRequestBundleInformationMessage msg = new OmemoRequestBundleInformationMessage(FULL_ACCOUNT_JID, curAddress.getName(), curAddress.getDeviceId());
            requestBundleInfoHelper.start(msg);
        }

        private void onTimeout()
        {
            switch (STATE)
            {
                case OmemoSessionBuildHelperState.REQUESTING_DEVICE_LIST:
                    Logger.Warn("[OmemoSessionBuildHelper] " + CHAT_JID + " didn't respond in time to the device list request - using the cached device list.");
                    useCachedDeviceList(OmemoSessionBuildError.REQUEST_DEVICE_LIST_TIMEOUT);
                    break;

                case OmemoSessionBuildHelperState.SUBSCRIBING_TO_DEVICE_LIST:
                    Logger.Warn("[OmemoSessionBuildHelper] Subscribing to the device list node timed out for " + CHAT_JID + " - using the cached device list.");
                    useCachedDeviceList(OmemoSessionBuildError.SUBSCRIBE_TO_DEVICE_LIST_TIMEOUT);
                    break;

                case OmemoSessionBuildHelperState.REQUESTING_BUNDLE_INFORMATION:
                    Logger.Error("[OmemoSessionBuildHelper] Failed to fetch bundle information - " + curAddress.getName() + ':' + curAddress.getDeviceId() + " didn't respond in time!");
                    createSessionForNextDevice();
                    break;
            }
        }

        private void onSubscribeToDeviceListTimeout()
        {
            Logger.Warn("[OmemoSessionBuildHelper] Failed to subscribe to device list node - " + CHAT_JID + " didn't respond in time!");
            OmemoDeviceDBManager.INSTANCE.setDeviceListSubscription(new OmemoDeviceListSubscriptionTable(CHAT_JID, BARE_ACCOUNT_JID, OmemoDeviceListSubscriptionState.NONE, DateTime.Now));
        }

        /// <summary>
        /// Falls back to the device list stored in the DB, if requesting it from the contact failed.
        /// </summary>
        private void useCachedDeviceList(OmemoSessionBuildError errorIfEmpty)
        {
            List<uint> devices = OmemoDeviceDBManager.INSTANCE.getDeviceIds(CHAT_JID, BARE_ACCOUNT_JID);
            if (devices.Count > 0)
            {
                createSessionsForDevices(devices);
            }
            else
            {
                Logger.Error("[OmemoSessionBuildHelper] Failed to establish session - no cached devices for: " + CHAT_JID);
                setState(OmemoSessionBuildHelperState.ERROR);
                ON_SESSION_RESULT(this, new OmemoSessionBuildResult(errorIfEmpty));
            }
        }

        private void createSessionsForDevices(List<uint> remoteDevices)
        {
            // Add remote devices:
            toDoDevicesRemote = new List<uint>(remoteDevices);

            // Add own devices (all other devices of the local account), so the message shows up there too:
            toDoDevicesOwn = new List<uint>();
            if (OMEMO_HELPER.DEVICES != null)
            {
                foreach (uint deviceId in OMEMO_HELPER.DEVICES.DEVICES)
                {
                    if (deviceId != CONNECTION.account.omemoDeviceId)
                    {
                        toDoDevicesOwn.Add(deviceId);
                    }
                }
            }

            createSessionForNextDevice();
        }

        private void createSessionForNextDevice()
        {
            if ((toDoDevicesRemote == null || toDoDevicesRemote.Count <= 0) && (toDoDevicesOwn == null || toDoDevicesOwn.Count <= 0))
            {
                // All sessions created:
                if (SESSION.DEVICE_SESSIONS.Count <= 0)
                {
                    setState(OmemoSessionBuildHelperState.ERROR);
                    ON_SESSION_RESULT(this, new OmemoSessionBuildResult(OmemoSessionBuildError.TARGET_DOES_NOT_SUPPORT_OMEMO));
                }
                else
                {
                    setState(OmemoSessionBuildHelperState.ESTABLISHED);
                    ON_SESSION_RESULT(this, new OmemoSessionBuildResult(SESSION));
                }
            }
            else
            {
                if (toDoDevicesRemote == null || toDoDevicesRemote.Count <= 0)
                {
                    curAddress = new SignalProtocolAddress(BARE_ACCOUNT_JID, toDoDevicesOwn[0]);
                    toDoDevicesOwn.RemoveAt(0);
                }
                else
                {
                    curAddress = new SignalProtocolAddress(CHAT_JID, toDoDevicesRemote[0]);
                    toDoDevicesRemote.RemoveAt(0);
                }

                if (SESSION.DEVICE_SESSIONS.ContainsKey(curAddress.getDeviceId()))
                {
                    // Same device id for an own and a remote device - can't be represented in one OMEMO header:
                    Logger.Warn("[OmemoSessionBuildHelper] Skipping device " + curAddress.getName() + ':' + curAddress.getDeviceId() + " - device id already in use.");
                    createSessionForNextDevice();
                }
                else if (OMEMO_HELPER.containsSession(curAddress))
                {
                    SessionCipher cipher = OMEMO_HELPER.loadCipher(curAddress);
                    SESSION.DEVICE_SESSIONS.Add(curAddress.getDeviceId(), cipher);
                    createSessionForNextDevice();
                }
                else
                {
                    requestBundleInformation();
                }
            }
        }

        private bool onRequestDeviceListMessage(IQMessage msg)
        {
            if (STATE != OmemoSessionBuildHelperState.REQUESTING_DEVICE_LIST)
            {
                return true;
            }

            if (msg is OmemoDeviceListResultMessage devMsg)
            {
                // Update devices in DB:
                OmemoDeviceDBManager.INSTANCE.setDevices(devMsg.DEVICES, CHAT_JID, BARE_ACCOUNT_JID);

                if (devMsg.DEVICES.DEVICES.Count > 0)
                {
                    OmemoDeviceListSubscriptionTable subscription = OmemoDeviceDBManager.INSTANCE.getDeviceListSubscription(CHAT_JID, BARE_ACCOUNT_JID);
                    if (subscription.state != OmemoDeviceListSubscriptionState.SUBSCRIBED)
                    {
                        subscribeToDeviceList();
                    }
                    createSessionsForDevices(devMsg.DEVICES.DEVICES);
                }
                else
                {
                    Logger.Error("[OmemoSessionBuildHelper] Failed to establish session - " + CHAT_JID + " doesn't support OMEMO: No devices");
                    setState(OmemoSessionBuildHelperState.ERROR);
                    ON_SESSION_RESULT(this, new OmemoSessionBuildResult(OmemoSessionBuildError.TARGET_DOES_NOT_SUPPORT_OMEMO));
                }
                return true;
            }
            else if (msg is IQErrorMessage errMsg)
            {
                if (errMsg.ERROR_OBJ.ERROR_NAME == ErrorName.ITEM_NOT_FOUND)
                {
                    Logger.Error("[OmemoSessionBuildHelper] Failed to establish session - " + CHAT_JID + " doesn't support OMEMO: " + errMsg.ERROR_OBJ.ToString());
                    setState(OmemoSessionBuildHelperState.ERROR);
                    ON_SESSION_RESULT(this, new OmemoSessionBuildResult(OmemoSessionBuildError.TARGET_DOES_NOT_SUPPORT_OMEMO));
                }
                else if (!deviceListRetried)
                {
                    Logger.Warn("[OmemoSessionBuildHelper] Request device list failed (" + errMsg.ERROR_OBJ.ToString() + ") - subscribing to the node and retrying.");
                    subscribeThenRetryDeviceList();
                }
                else
                {
                    Logger.Warn("[OmemoSessionBuildHelper] Request device list failed again - using the cached device list: " + errMsg.ERROR_OBJ.ToString());
                    useCachedDeviceList(OmemoSessionBuildError.REQUEST_DEVICE_LIST_IQ_ERROR);
                }
                return true;
            }
            return false;
        }

        private bool onSubscribeThenRetryMessage(IQMessage msg)
        {
            if (STATE != OmemoSessionBuildHelperState.SUBSCRIBING_TO_DEVICE_LIST)
            {
                return true;
            }

            deviceListRetried = true;
            if (msg is PubSubSubscriptionMessage subMsg)
            {
                if (subMsg.SUBSCRIPTION == PubSubSubscription.SUBSCRIBED)
                {
                    OmemoDeviceDBManager.INSTANCE.setDeviceListSubscription(new OmemoDeviceListSubscriptionTable(CHAT_JID, BARE_ACCOUNT_JID, OmemoDeviceListSubscriptionState.SUBSCRIBED, DateTime.Now));
                    Logger.Info("[OmemoSessionBuildHelper] Subscribed to " + CHAT_JID + " device list node - retrying the device list request.");
                    requestDeviceList();
                }
                else
                {
                    Logger.Warn("[OmemoSessionBuildHelper] Failed to subscribe to " + CHAT_JID + " device list node - returned: " + subMsg.SUBSCRIPTION + ". Using the cached device list.");
                    OmemoDeviceDBManager.INSTANCE.setDeviceListSubscription(new OmemoDeviceListSubscriptionTable(CHAT_JID, BARE_ACCOUNT_JID, OmemoDeviceListSubscriptionState.NONE, DateTime.Now));
                    useCachedDeviceList(OmemoSessionBuildError.SUBSCRIBE_TO_DEVICE_LIST_IQ_ERROR);
                }
                return true;
            }
            else if (msg is IQErrorMessage errMsg)
            {
                Logger.Warn("[OmemoSessionBuildHelper] Failed to subscribe to " + CHAT_JID + " device list node (" + errMsg.ERROR_OBJ.ToString() + "). Using the cached device list.");
                OmemoDeviceDBManager.INSTANCE.setDeviceListSubscription(new OmemoDeviceListSubscriptionTable(CHAT_JID, BARE_ACCOUNT_JID, OmemoDeviceListSubscriptionState.ERROR, DateTime.Now));
                useCachedDeviceList(OmemoSessionBuildError.SUBSCRIBE_TO_DEVICE_LIST_IQ_ERROR);
                return true;
            }
            return false;
        }

        private bool onSubscribeToDeviceListMessage(IQMessage msg)
        {
            if (msg is PubSubSubscriptionMessage subMsg)
            {
                if (subMsg.SUBSCRIPTION != PubSubSubscription.SUBSCRIBED)
                {
                    Logger.Warn("[OmemoSessionBuildHelper] Failed to subscribe to device list node - " + CHAT_JID + " returned: " + subMsg.SUBSCRIPTION);
                    OmemoDeviceDBManager.INSTANCE.setDeviceListSubscription(new OmemoDeviceListSubscriptionTable(CHAT_JID, BARE_ACCOUNT_JID, OmemoDeviceListSubscriptionState.NONE, DateTime.Now));
                }
                else
                {
                    OmemoDeviceDBManager.INSTANCE.setDeviceListSubscription(new OmemoDeviceListSubscriptionTable(CHAT_JID, BARE_ACCOUNT_JID, OmemoDeviceListSubscriptionState.SUBSCRIBED, DateTime.Now));
                }
                return true;
            }
            else if (msg is IQErrorMessage errMsg)
            {
                if (errMsg.ERROR_OBJ.ERROR_NAME == ErrorName.ITEM_NOT_FOUND)
                {
                    Logger.Error("[OmemoSessionBuildHelper] Failed to subscribe to device list node - " + CHAT_JID + " returned node does not exist: " + errMsg.ERROR_OBJ.ToString());
                }
                else
                {
                    Logger.Warn("[OmemoSessionBuildHelper] Failed to subscribe to device list node: " + errMsg.ERROR_OBJ.ToString());
                }
                OmemoDeviceDBManager.INSTANCE.setDeviceListSubscription(new OmemoDeviceListSubscriptionTable(CHAT_JID, BARE_ACCOUNT_JID, OmemoDeviceListSubscriptionState.ERROR, DateTime.Now));
                return true;
            }
            return false;
        }

        private bool onRequestBundleInformationMessage(IQMessage msg)
        {
            if (STATE != OmemoSessionBuildHelperState.REQUESTING_BUNDLE_INFORMATION)
            {
                return true;
            }

            if (msg is OmemoBundleInformationResultMessage bundleMsg)
            {
                if (bundleMsg.BUNDLE_INFO.isValid())
                {
                    try
                    {
                        // The session has to be stored for the JID the bundle belongs to (own JID for own devices):
                        SignalProtocolAddress address = OMEMO_HELPER.newSession(curAddress.getName(), curAddress.getDeviceId(), bundleMsg.BUNDLE_INFO.getRandomPreKey(curAddress.getDeviceId()));
                        SessionCipher cipher = OMEMO_HELPER.loadCipher(address);
                        SESSION.DEVICE_SESSIONS.Add(curAddress.getDeviceId(), cipher);
                        Logger.Info("[OmemoSessionBuildHelper] Session with " + curAddress.getName() + ':' + curAddress.getDeviceId() + " established.");
                    }
                    catch (Exception e)
                    {
                        Logger.Error("[OmemoSessionBuildHelper] Failed to establish session with " + curAddress.getName() + ':' + curAddress.getDeviceId() + " - invalid bundle: " + e.Message, e);
                    }
                }
                else
                {
                    Logger.Error("[OmemoSessionBuildHelper] Failed to establish session with " + curAddress.getName() + ':' + curAddress.getDeviceId() + " - bundle incomplete.");
                }
                createSessionForNextDevice();
                return true;
            }
            else if (msg is IQErrorMessage errMsg)
            {
                if (errMsg.ERROR_OBJ.ERROR_NAME == ErrorName.ITEM_NOT_FOUND)
                {
                    Logger.Error("[OmemoSessionBuildHelper] Failed to establish session - " + curAddress.getName() + ':' + curAddress.getDeviceId() + " doesn't support OMEMO: " + errMsg.ERROR_OBJ.ToString());
                }
                else
                {
                    Logger.Error("[OmemoSessionBuildHelper] Failed to establish session - request bundle info failed (" + curAddress.getName() + ':' + curAddress.getDeviceId() + "): " + errMsg.ERROR_OBJ.ToString());
                }
                createSessionForNextDevice();
                return true;
            }
            return false;
        }

        #endregion

        #region --Misc Methods (Protected)--


        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--


        #endregion
    }
}
