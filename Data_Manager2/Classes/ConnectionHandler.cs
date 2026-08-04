using Data_Manager2.Classes.Events;
using Data_Manager2.Classes.DBManager;
using Data_Manager2.Classes.DBTables;
using Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XMPP_API.Classes;
using XMPP_API.Classes.Network;
using XMPP_API.Classes.Network.XML;
using XMPP_API.Classes.Network.XML.Messages;
using XMPP_API.Classes.Network.XML.Messages.XEP_0045;
using XMPP_API.Classes.Network.XML.Messages.XEP_0249;
using System.Threading;
using XMPP_API.Classes.Network.XML.Messages.XEP_0048;
using XMPP_API.Classes.Network.XML.Messages.XEP_0184;
using XMPP_API.Classes.Network.Events;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384;
using Data_Manager2.Classes.ToastActivation;

namespace Data_Manager2.Classes
{
    public class ConnectionHandler
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        private static readonly SemaphoreSlim CLIENT_SEMA = new SemaphoreSlim(1);
        public static readonly ConnectionHandler INSTANCE = new ConnectionHandler();
        private readonly List<XMPPClient> CLIENTS;

        public delegate void ClientConnectedHandler(ConnectionHandler handler, ClientConnectedEventArgs args);

        public event ClientConnectedHandler ClientConnected;
        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 18/11/2017 Created [Fabian Sauter]
        /// </history>
        public ConnectionHandler()
        {
            this.CLIENTS = new List<XMPPClient>();
            loadClients();
            AccountDBManager.INSTANCE.AccountChanged += INSTANCE_AccountChanged;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        /// <summary>
        /// Returns the XMPPClient that matches the given id and domain.
        /// </summary>
        /// <param name="iDAndDomain">The id and domain of the requested XMPPClient. e.g. 'alice@jabber.org'</param>
        public XMPPClient getClient(string iDAndDomain)
        {
            foreach (XMPPClient c in CLIENTS)
            {
                if (c.getXMPPAccount().getIdAndDomain().Equals(iDAndDomain))
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns all available XMPPClients.
        /// </summary>
        public List<XMPPClient> getClients()
        {
            return CLIENTS;
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        /// <summary>
        /// Starts connecting all XMPPClients.
        /// </summary>
        public void connectAll()
        {
            Parallel.ForEach(CLIENTS, (c) =>
            {
                if (!c.getXMPPAccount().disabled)
                {
                    switch (c.getConnetionState())
                    {
                        case ConnectionState.DISCONNECTED:
                        case ConnectionState.DISCONNECTING:
                        case ConnectionState.ERROR:
                            Task.Run(() => c.connect()).ContinueWith((Task prev) =>
                            {
                                if (prev.Exception != null)
                                {
                                    Logger.Error("Error during connectAll() - ConnectionHandler!", prev.Exception);
                                }
                            });
                            break;

                        default:
                            break;
                    }
                }
            });
        }

        /// <summary>
        /// Disconnects all XMPPClients in parallel.
        /// </summary>
        public void disconnectAll()
        {
            Parallel.ForEach(CLIENTS, async (c) => await c.disconnectAsync());
        }

        /// <summary>
        /// Disconnects all XMPPClients.
        /// </summary>
        public async Task disconnectAllAsync()
        {
            Task[] tasks = new Task[CLIENTS.Count];
            CLIENT_SEMA.Wait();
            for (int i = 0; i < CLIENTS.Count; i++)
            {
                tasks[i] = CLIENTS[i].disconnectAsync();
            }
            CLIENT_SEMA.Release();

            for (int i = 0; i < tasks.Length; i++)
            {
                await tasks[i].ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Disconnects and removes the given account from the client list.
        /// </summary>
        /// <param name="accountId">The account id of the client you would like to remove.</param>
        public async Task removeAccountAsync(string accountId)
        {
            CLIENT_SEMA.Wait();
            for (int i = 0; i < CLIENTS.Count; i++)
            {
                if (Equals(CLIENTS[i].getXMPPAccount().getIdAndDomain(), accountId))
                {
                    await CLIENTS[i].disconnectAsync();
                    CLIENTS.RemoveAt(i);
                }
            }
            CLIENT_SEMA.Release();
        }

        /// <summary>
        /// Reconnects all XMPPClients.
        /// </summary>
        public void reconnectAll()
        {
            Parallel.ForEach(CLIENTS, async (c) => await reconnectClientAsync(c));
        }

        #endregion

        #region --Misc Methods (Private)--
        /// <summary>
        /// Loads all XMPPClients from the DB and inserts them into the clients list.
        /// </summary>
        private void loadClients()
        {
            CLIENT_SEMA.Wait();
            CLIENTS.Clear();
            foreach (XMPPAccount acc in AccountDBManager.INSTANCE.loadAllAccounts())
            {
                CLIENTS.Add(loadAccount(acc));
            }
            CLIENT_SEMA.Release();
        }

        /// <summary>
        /// Reloads all clients from the DB.
        /// Disconnects all existing clients first.
        /// </summary>
        public void reloadClients()
        {
            disconnectAll();
            loadClients();
            connectAll();
        }

        /// <summary>
        /// Loads one specific XMPPAccount and subscribes to all its events.
        /// </summary>
        /// <param name="acc">The account which should get loaded.</param>
        /// <returns></returns>
        private XMPPClient loadAccount(XMPPAccount acc)
        {
            XMPPClient c = new XMPPClient(acc);

            // Ensure no event gets bound multiple times:
            unsubscribeFromEvents(c);

            c.NewChatMessage += C_NewChatMessage;
            c.NewRoosterMessage += C_NewRoosterMessage;
            c.NewPresence += C_NewPresence;
            // Requesting roster if connected
            c.ConnectionStateChanged += C_ConnectionStateChanged;
            c.MessageSend += C_MessageSend;
            c.NewBookmarksResultMessage += C_NewBookmarksResultMessage;
            c.NewDeliveryReceipt += C_NewDeliveryReceipt;
            c.getXMPPAccount().PropertyChanged += ConnectionHandler_PropertyChanged;
            return c;
        }

        /// <summary>
        /// Unsubscribes from all events of the given XMPPClient.
        /// </summary>
        private void unsubscribeFromEvents(XMPPClient c)
        {
            c.NewChatMessage -= C_NewChatMessage;
            c.NewRoosterMessage -= C_NewRoosterMessage;
            c.NewPresence -= C_NewPresence;
            c.ConnectionStateChanged -= C_ConnectionStateChanged;
            c.MessageSend -= C_MessageSend;
            c.NewBookmarksResultMessage -= C_NewBookmarksResultMessage;
            c.NewDeliveryReceipt -= C_NewDeliveryReceipt;
            c.getXMPPAccount().PropertyChanged -= ConnectionHandler_PropertyChanged;
        }

        /// <summary>
        /// Called once a client enters the 'Disconnected' or 'Error' state.
        /// </summary>
        /// <param name="client">The Client which entered the state.</param>
        private void onClientDisconnectedOrError(XMPPClient client)
        {
            ChatDBManager.INSTANCE.resetPresence(client.getXMPPAccount().getIdAndDomain());
            clearResourcePresence(client.getXMPPAccount().getIdAndDomain());
            MUCHandler.INSTANCE.onClientDisconnected(client);
        }

        /// <summary>
        /// Called once a client enters the 'Disconnecting' state.
        /// </summary>
        /// <param name="client">The Client which entered the state.</param>
        private void onClientDisconneting(XMPPClient client)
        {
            MUCHandler.INSTANCE.onClientDisconnecting(client);
        }

        /// <summary>
        /// Called once a client enters the 'Connected' state.
        /// </summary>
        /// <param name="client">The Client which entered the state.</param>
        private void onClientConnected(XMPPClient client)
        {
            Task.Run(async () =>
            {
                await client.requestRoosterAsync();
                await client.requestBookmarksAsync();
                MUCHandler.INSTANCE.onClientConnected(client);
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(client));
            });
        }

        /// <summary>
        /// Reconnects a given client.
        /// </summary>
        /// <param name="client">The client, for which a reconnect should get performed.</param>
        /// <returns></returns>
        private async Task reconnectClientAsync(XMPPClient client)
        {
            if (client.getXMPPAccount().disabled)
            {
                // Only disconnect if the client is disabled:
                await client.disconnectAsync();
            }
            else
            {
                await client.reconnectAsync();
            }
        }

        #endregion

        #region --Misc Methods (Protected)--


        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--
        private void C_ConnectionStateChanged(XMPPClient client, XMPP_API.Classes.Network.Events.ConnectionStateChangedEventArgs args)
        {
            switch (args.newState)
            {
                case ConnectionState.CONNECTED:
                    onClientConnected(client);
                    break;

                case ConnectionState.DISCONNECTING:
                    onClientDisconneting(client);
                    break;

                case ConnectionState.ERROR:
                case ConnectionState.DISCONNECTED:
                    onClientDisconnectedOrError(client);
                    break;
                
                default:
                    break;
            }
        }

        /// <summary>
        /// Which resources of a contact are currently available, keyed by chat id and
        /// then by full JID. In memory only - it is rebuilt from the presence the
        /// server sends after every connect.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, Presence>> RESOURCE_PRESENCE =
            new Dictionary<string, Dictionary<string, Presence>>();

        /// <summary>
        /// Records one resource's presence and returns the contact's presence overall:
        /// the most available of its resources, or Unavailable once none are left.
        /// </summary>
        private Presence updateResourcePresence(string chatId, string fullJid, string type,
            Presence presence)
        {
            if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(fullJid))
            {
                return presence;
            }

            lock (RESOURCE_PRESENCE)
            {
                Dictionary<string, Presence> resources;
                if (!RESOURCE_PRESENCE.TryGetValue(chatId, out resources))
                {
                    resources = new Dictionary<string, Presence>();
                    RESOURCE_PRESENCE[chatId] = resources;
                }

                if (string.Equals(type, "unavailable"))
                {
                    resources.Remove(fullJid);
                }
                else
                {
                    resources[fullJid] = presence;
                }

                Presence best = Presence.Unavailable;
                foreach (Presence current in resources.Values)
                {
                    if (availability(current) > availability(best))
                    {
                        best = current;
                    }
                }
                return best;
            }
        }

        /// <summary>
        /// How available a presence is. Written out rather than comparing the enum
        /// directly, because NotDefined sorts highest in the enum but means "unknown".
        /// </summary>
        private static int availability(Presence presence)
        {
            switch (presence)
            {
                case Presence.Chat: return 5;
                case Presence.Online: return 4;
                case Presence.Away: return 3;
                case Presence.Xa: return 2;
                case Presence.Dnd: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// Forgets every tracked resource for an account. Called when its client goes
        /// away, so resources that were available at disconnect are not still counted
        /// as available after reconnecting.
        /// </summary>
        private void clearResourcePresence(string userAccountId)
        {
            if (string.IsNullOrEmpty(userAccountId))
            {
                return;
            }

            lock (RESOURCE_PRESENCE)
            {
                List<string> stale = new List<string>();
                foreach (string chatId in RESOURCE_PRESENCE.Keys)
                {
                    // Chat ids are built as 'contact_account', so this is the account's
                    // own set of chats.
                    if (chatId != null && chatId.EndsWith(userAccountId))
                    {
                        stale.Add(chatId);
                    }
                }
                foreach (string chatId in stale)
                {
                    RESOURCE_PRESENCE.Remove(chatId);
                }
            }
        }

        private void C_NewPresence(XMPPClient client, XMPP_API.Classes.Events.NewPresenceMessageEventArgs args)
        {
            string from = Utils.getBareJidFromFullJid(args.PRESENCE_MESSAGE.getFrom());

            // If received a presence message from your own account, ignore it:
            if (string.Equals(from, client.getXMPPAccount().getIdAndDomain()))
            {
                return;
            }

            string to = client.getXMPPAccount().getIdAndDomain();
            string id = ChatTable.generateId(from, to);
            ChatTable chat = ChatDBManager.INSTANCE.getChat(id);
            switch (args.PRESENCE_MESSAGE.TYPE)
            {
                case "subscribe":
                case "unsubscribed":
                    if (chat == null)
                    {
                        chat = new ChatTable
                        {
                            id = id,
                            chatJabberId = from,
                            userAccountId = to,
                            inRoster = false,
                            muted = false,
                            lastActive = DateTime.Now,
                            ask = null,
                            status = null,
                            chatState = null
                        };
                    }
                    chat.subscription = args.PRESENCE_MESSAGE.TYPE;
                    break;

                default:
                    break;
            }

            if (chat != null)
            {
                // Presence is per RESOURCE, but a ChatTable holds one value per
                // contact. Taking whatever arrived last means a contact signed in on
                // two devices shows the presence of whichever one spoke most recently,
                // and - worse - one of them signing out marks the whole contact
                // offline while the other is still connected. A client with a
                // background agent (a second, short-lived resource) therefore appeared
                // to go offline every time its agent finished a run.
                Presence best = updateResourcePresence(id,
                    args.PRESENCE_MESSAGE.getFrom(),
                    args.PRESENCE_MESSAGE.TYPE,
                    args.PRESENCE_MESSAGE.PRESENCE);

                // A departing resource's status text must not overwrite the one from
                // the resource that is still here.
                if (!string.Equals(args.PRESENCE_MESSAGE.TYPE, "unavailable"))
                {
                    chat.status = args.PRESENCE_MESSAGE.STATUS;
                }
                else if (best == Presence.Unavailable)
                {
                    chat.status = null;
                }

                chat.presence = best;
                ChatDBManager.INSTANCE.setChat(chat, false, true);
            }
            else
            {
                Logger.Warn("Received a presence message for an unknown chat from: " + from + ", to: " + to);
            }
        }

        private void C_NewRoosterMessage(IMessageSender sender, NewValidMessageEventArgs args)
        {
            XMPPClient client = sender as XMPPClient;
            if (args.MESSAGE is RosterMessage)
            {
                RosterMessage msg = args.MESSAGE as RosterMessage;
                string to = client.getXMPPAccount().getIdAndDomain();
                string type = msg.TYPE;

                if (string.Equals(type, IQMessage.RESULT))
                {
                    ChatDBManager.INSTANCE.setAllNotInRoster(client.getXMPPAccount().getIdAndDomain());
                }
                else if (type == null || !string.Equals(type, IQMessage.SET))
                {
                    // No roster result or set => return
                    return;
                }

                foreach (RosterItem item in msg.ITEMS)
                {
                    string from = item.JID;
                    string id = ChatTable.generateId(from, to);
                    ChatTable chat = ChatDBManager.INSTANCE.getChat(id);
                    if (chat != null)
                    {
                        chat.subscription = item.SUBSCRIPTION;
                        chat.inRoster = !item.SUBSCRIPTION.Equals("remove");
                        chat.ask = item.ASK;
                    }
                    else if (!Equals(item.SUBSCRIPTION, "remove"))
                    {
                        chat = new ChatTable()
                        {
                            id = id,
                            chatJabberId = from,
                            userAccountId = to,
                            subscription = item.SUBSCRIPTION,
                            lastActive = DateTime.Now,
                            muted = false,
                            inRoster = true,
                            ask = item.ASK,
                            chatType = ChatType.CHAT
                        };
                    }
                    else
                    {
                        continue;
                    }

                    switch (chat.subscription)
                    {
                        case "unsubscribe":
                        case "from":
                        case "none":
                        case "pending":
                        case null:
                            chat.presence = Presence.Unavailable;
                            break;

                        default:
                            break;
                    }

                    ChatDBManager.INSTANCE.setChat(chat, false, true);
                }
            }
        }

        private void C_NewChatMessage(XMPPClient client, XMPP_API.Classes.Network.Events.NewChatMessageEventArgs args)
        {
            MessageMessage msg = args.getMessage();

            // Handel MUC room subject messages:
            if (msg is MUCRoomSubjectMessage)
            {
                MUCHandler.INSTANCE.onMUCRoomSubjectMessage(msg as MUCRoomSubjectMessage);
                return;
            }

            string from = Utils.getBareJidFromFullJid(msg.getFrom());

            // Check if device id is valid and if, decrypt the OMEMO messages:
            if (msg is OmemoMessageMessage omemoMessage)
            {
                // Decryption failed:
                if (!omemoMessage.decrypt(client.getOmemoHelper(), client.getXMPPAccount().omemoDeviceId))
                {
                    return;
                }
            }

            string to = Utils.getBareJidFromFullJid(msg.getTo());
            string id;
            if (msg.CC_TYPE == CarbonCopyType.SENT)
            {
                id = ChatTable.generateId(to, from);
            }
            else
            {
                id = ChatTable.generateId(from, to);
            }


            ChatTable chat = ChatDBManager.INSTANCE.getChat(id);
            bool chatChanged = false;
            if (chat == null)
            {
                chatChanged = true;
                chat = new ChatTable()
                {
                    id = id,
                    chatJabberId = from,
                    userAccountId = to,
                    ask = null,
                    inRoster = false,
                    lastActive = msg.getDelay(),
                    muted = false,
                    presence = Presence.Unavailable,
                    status = null,
                    subscription = null,
                    chatType = Equals(msg.TYPE, MessageMessage.TYPE_GROUPCHAT) ? ChatType.MUC : ChatType.CHAT,
                };
            }

            ChatMessageTable message = new ChatMessageTable(msg, chat);

            // Handle MUC invite messages:
            if (msg is DirectMUCInvitationMessage)
            {
                DirectMUCInvitationMessage inviteMessage = msg as DirectMUCInvitationMessage;
                bool doesRoomExist = ChatDBManager.INSTANCE.doesMUCExist(ChatTable.generateId(inviteMessage.ROOM_JID, msg.getTo()));
                bool doesOutstandingInviteExist = ChatDBManager.INSTANCE.doesOutstandingMUCInviteExist(id, inviteMessage.ROOM_JID);

                if (doesRoomExist && doesOutstandingInviteExist)
                {
                    return;
                }

                MUCDirectInvitationTable inviteTable = new MUCDirectInvitationTable(inviteMessage, message.id);
                ChatDBManager.INSTANCE.setMUCDirectInvitation(inviteTable);
            }

            bool isMUCMessage = string.Equals(MessageMessage.TYPE_GROUPCHAT, message.type);
            ChatMessageTable existingMessage = ChatDBManager.INSTANCE.getChatMessageById(message.id);
            bool doesMessageExist = existingMessage != null;

            if (isMUCMessage)
            {
                MUCChatInfoTable mucInfo = MUCDBManager.INSTANCE.getMUCInfo(chat.id);
                if (mucInfo != null)
                {
                    if (Equals(message.fromUser, mucInfo.nickname))
                    {
                        // Filter MUC messages that already exist:
                        // ToDo: Allow MUC messages being edited and detect it
                        if (doesMessageExist)
                        {
                            return;
                        }
                        else
                        {
                            message.state = MessageState.SEND;
                        }
                    }
                    else
                    {
                        if (doesMessageExist)
                        {
                            message.state = existingMessage.state;
                        }
                    }
                }
            }

            if (chat.lastActive.CompareTo(msg.getDelay()) < 0)
            {
                chatChanged = true;
                chat.lastActive = msg.getDelay();
            }

            if (chatChanged)
            {
                ChatDBManager.INSTANCE.setChat(chat, false, true);
            }

            // Send XEP-0184 (Message Delivery Receipts) reply:
            if (msg.RECIPT_REQUESTED && id != null && !Settings.getSettingBoolean(SettingsConsts.DONT_SEND_CHAT_MESSAGE_RECEIVED_MARKERS))
            {
                Task.Run(async () =>
                {
                    DeliveryReceiptMessage receiptMessage = new DeliveryReceiptMessage(client.getXMPPAccount().getIdDomainAndResource(), from, msg.ID);
                    await client.sendAsync(receiptMessage, true);
                });
            }

            ChatDBManager.INSTANCE.setChatMessage(message, !doesMessageExist, doesMessageExist && !isMUCMessage);

            // Show toast:
            if (!doesMessageExist && !chat.muted)
            {
                Task.Run(() =>
                {
                    try
                    {
                        switch (msg.TYPE)
                        {
                            case MessageMessage.TYPE_GROUPCHAT:
                            case MessageMessage.TYPE_CHAT:
                                if (!message.isCC)
                                {
                                    if (message.isEncrypted)
                                    {
                                        ToastHelper.showChatTextEncryptedToast(message, chat);
                                    }
                                    else if (message.isImage)
                                    {
                                        ToastHelper.showChatTextImageToast(message, chat);
                                    }
                                    else
                                    {
                                        ToastHelper.showChatTextToast(message, chat);
                                    }
                                }
                                break;

                            default:
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Failed to send toast notification!", e);
                    }
                });
            }
        }

        private async void INSTANCE_AccountChanged(AccountDBManager handler, AccountChangedEventArgs args)
        {
            CLIENT_SEMA.Wait();
            for (int i = 0; i < CLIENTS.Count; i++)
            {
                if (Equals(CLIENTS[i].getXMPPAccount().getIdAndDomain(), args.ACCOUNT.getIdAndDomain()))
                {
                    // Disconnect first:
                    await CLIENTS[i].disconnectAsync();

                    if (args.REMOVED)
                    {
                        unsubscribeFromEvents(CLIENTS[i]);
                        CLIENTS.RemoveAt(i);
                    }
                    else
                    {
                        CLIENTS[i].setAccount(args.ACCOUNT);
                        if (!CLIENTS[i].getXMPPAccount().disabled)
                        {
                            CLIENTS[i].connect();
                        }
                    }
                    CLIENT_SEMA.Release();
                    return;
                }
            }

            // Account got added:
            if (!args.REMOVED)
            {
                XMPPClient client = loadAccount(args.ACCOUNT);
                if (!client.getXMPPAccount().disabled)
                {
                    client.connect();
                }
                CLIENTS.Add(client);
            }
            CLIENT_SEMA.Release();
        }

        private void C_MessageSend(XMPPClient client, MessageSendEventArgs args)
        {
            ChatDBManager.INSTANCE.updateChatMessageState(args.CHAT_MESSAGE_ID, MessageState.SEND);
        }

        private void C_NewBookmarksResultMessage(XMPPClient client, NewBookmarksResultMessageEventArgs args)
        {
            foreach (ConferenceItem c in args.BOOKMARKS_MESSAGE.STORAGE.CONFERENCES)
            {
                bool newMUC = false;
                string to = client.getXMPPAccount().getIdAndDomain();
                string from = c.jid;
                string id = ChatTable.generateId(from, to);

                // Create / update chat:
                ChatTable chat = ChatDBManager.INSTANCE.getChat(id);
                if (chat == null)
                {
                    chat = new ChatTable()
                    {
                        id = id,
                        chatJabberId = from,
                        userAccountId = to,
                        ask = null,
                        lastActive = DateTime.Now,
                        muted = false,
                        status = null,
                        subscription = null
                    };
                    newMUC = true;
                }
                chat.chatType = ChatType.MUC;
                chat.inRoster = true;
                chat.presence = Presence.Unavailable;

                ChatDBManager.INSTANCE.setChat(chat, false, true);

                // Create / update MUC info:
                MUCChatInfoTable info = MUCDBManager.INSTANCE.getMUCInfo(chat.id);
                if (info == null)
                {
                    info = new MUCChatInfoTable
                    {
                        chatId = chat.id,
                        subject = null
                    };
                    newMUC = true;
                }

                info.autoEnterRoom = c.autoJoin;
                info.name = c.name;
                info.nickname = c.nick;
                info.password = c.password;

                MUCDBManager.INSTANCE.setMUCChatInfo(info, false, true);


                // Enter MUC manually if the MUC is new for this client:
                if (newMUC && info.autoEnterRoom && !Settings.getSettingBoolean(SettingsConsts.DISABLE_AUTO_JOIN_MUC))
                {
                    Task.Run(() => MUCHandler.INSTANCE.enterMUCAsync(client, chat, info));
                }
            }
        }

        private void C_NewDeliveryReceipt(XMPPClient client, NewDeliveryReceiptEventArgs args)
        {
            Task.Run(() =>
            {
                string to = Utils.getBareJidFromFullJid(args.MSG.getTo());
                string from = Utils.getBareJidFromFullJid(args.MSG.getFrom());
                string chatId = ChatTable.generateId(from, to);
                string msgId = ChatMessageTable.generateId(args.MSG.RECEIPT_ID, chatId);
                ChatDBManager.INSTANCE.setMessageAsDeliverd(msgId, true);
            });
        }

        private void ConnectionHandler_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is XMPPAccount account)
            {
                AccountDBManager.INSTANCE.setAccount(account, false);
            }
        }

        #endregion
    }
}
