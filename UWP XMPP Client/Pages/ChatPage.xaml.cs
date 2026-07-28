using Data_Manager2.Classes;
using Data_Manager2.Classes.DBManager;
using Data_Manager2.Classes.DBTables;
using Data_Manager2.Classes.Events;
using Data_Manager2.Classes.ToastActivation;
using Logging;
using Microsoft.Toolkit.Uwp.UI;
using Microsoft.Toolkit.Uwp.UI.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UWP_XMPP_Client.Classes;
using UWP_XMPP_Client.Classes.Collections;
using UWP_XMPP_Client.DataTemplates;
using UWP_XMPP_Client.Dialogs;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using XMPP_API.Classes;

namespace UWP_XMPP_Client.Pages
{
    public sealed partial class ChatPage : Page
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        private readonly MyAdvancedCollectionView CHATS_ACV;
        private readonly ObservableChatDictionaryList CHATS;
        private readonly ChatFilter CHAT_FILTER;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 26/11/2017 Created [Fabian Sauter]
        /// </history>
        public ChatPage()
        {
            this.CHATS = new ObservableChatDictionaryList();
            this.CHATS_ACV = new MyAdvancedCollectionView(CHATS, true)
            {
                Filter = aCVFilter
            };

            this.CHATS_ACV.ObserveFilterProperty(nameof(ChatTemplate.chat));
            this.CHATS_ACV.SortDescriptions.Add(new SortDescription(nameof(ChatTemplate.chat), SortDirection.Descending));
            this.CHAT_FILTER = new ChatFilter(this.CHATS_ACV);
            this.InitializeComponent();
            // BackRequested is hooked in OnNavigatedTo and released in
            // OnNavigatedFrom - subscribing from the constructor left every
            // instance we ever navigated away from attached to the
            // SystemNavigationManager for the life of the process.
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        /// <summary>
        /// Returns the current MasterDetailsView control.
        /// </summary>
        public MasterDetailsView getMasterDetailsView()
        {
            return masterDetail_pnl;
        }

        /// <summary>
        /// Returns true if the chat type of the given chat is CHAT and chat state messages aren't disabled.
        /// </summary>
        /// <param name="chat">The chat which </param>
        /// <returns></returns>
        private bool shouldSendChatState(ChatTable chat)
        {
            return !Settings.getSettingBoolean(SettingsConsts.DONT_SEND_CHAT_STATE) && chat != null && chat.chatType == ChatType.CHAT;
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--


        #endregion

        #region --Misc Methods (Private)--
        private bool aCVFilter(object o)
        {
            return CHAT_FILTER.filter(o);
        }

        /// <summary>
        /// Returns a list of ChatTemplates loaded from the DB, bases on the XMPPClients from the ConnectionHandler.
        /// </summary>
        private List<ChatTemplate> getChatsFromDB()
        {
            List<ChatTemplate> list = new List<ChatTemplate>();
            foreach (XMPPClient c in ConnectionHandler.INSTANCE.getClients())
            {
                foreach (ChatTable chat in ChatDBManager.INSTANCE.getAllChatsForClient(c.getXMPPAccount().getIdAndDomain()))
                {
                    if (chat.chatType == ChatType.MUC)
                    {
                        list.Add(new ChatTemplate(c, chat, MUCDBManager.INSTANCE.getMUCInfo(chat.id), null));
                    }
                    else
                    {
                        list.Add(new ChatTemplate(c, chat, null));
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Selects the given chat on the ALREADY loaded page.
        /// Used when the app gets activated by a toast while this page is
        /// already the current one - navigating to ChatPage again would build a
        /// second instance whose predecessor stays alive and subscribed to all
        /// the DB events (Page_Loaded only detaches its own handler).
        /// </summary>
        /// <param name="chatId">The id of the chat which should get selected.</param>
        public void showChatFromToast(string chatId)
        {
            if (string.IsNullOrEmpty(chatId))
            {
                return;
            }

            // Called on the UI thread from App.OnActivated, so select the chat
            // exactly the way tapping it in the list does: just assign the item
            // that is already loaded.
            //
            // Do NOT reload the list here. loadChats() clears CHATS and refills
            // it with freshly built ChatTemplates from a thread pool thread and
            // then selects one of those new instances, all from a dispatcher
            // callback - swapping out every bound item (including the selected
            // one) underneath the MasterDetailsView that way threw
            // RPC_E_WRONG_THREAD (0x8001010E).
            ChatTemplate chat = CHATS.GetById(chatId);
            if (chat != null)
            {
                masterDetail_pnl.SelectedItem = chat;
                return;
            }

            // Chat is not in the list yet (e.g. the message created it). Fall
            // back to the normal load, which is the same path used on launch.
            loadChats(chatId, true);
        }

        /// <summary>
        /// Loads all chats and inserts them into the chatsList.
        /// </summary>
        /// <param name="selectedChatId">The id of the chat which should get selected.</param>
        private void loadChats(string selectedChatId)
        {
            loadChats(selectedChatId, false);
        }

        /// <summary>
        /// Loads all chats and inserts them into the chatsList.
        /// </summary>
        /// <param name="selectedChatId">The id of the chat which should get selected.</param>
        /// <param name="forceSelect">Select the chat even if another one is already selected.</param>
        private void loadChats(string selectedChatId, bool forceSelect)
        {
            // Load all chats:
            Task.Run(() =>
            {
                ChatTemplate selectedChat = null;
                List<ChatTemplate> chats = getChatsFromDB();
                for (int i = 0; i < chats.Count; i++)
                {
                    //if (string.Equals(selectedChatId, chats[i].chat.id))
                    if (selectedChatId != null && chats[i] != null && chats[i].chat != null && string.Equals(selectedChatId, chats[i].chat.id))
                    {
                        selectedChat = chats[i];
                    }
                }

                // Show selected chat:
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Clear list:
                    CHATS.Clear();

                    // Add chats:
                    using (CHATS_ACV.DeferRefresh())
                    {
                        CHATS.AddRange(chats, false);
                    }
                    if ((forceSelect || masterDetail_pnl.SelectedItem == null) && selectedChat != null)
                    {
                        masterDetail_pnl.SelectedItem = selectedChat;
                    }
                }).AsTask();
            });
        }

        /// <summary>
        /// Adds a new chat to the chatsList and the DB.
        /// </summary>
        /// <param name="client">Which account/client owns this chat?</param>
        /// <param name="jID">The JID if the new chat.</param>
        /// <param name="addToRoster">Should the chat get added to the users roster?</param>
        /// <param name="requestSubscription">Request a presence subscription?</param>
        private async Task addChatAsync(XMPPClient client, string jID, bool addToRoster, bool requestSubscription)
        {
            if (client == null || jID == null)
            {
                string errorMessage = "Unable to add chat! client ?= " + (client == null) + " jabberId ?=" + (jID == null);
                Logger.Error(errorMessage);
                MessageDialog messageDialog = new MessageDialog("Error")
                {
                    Content = errorMessage
                };
                await messageDialog.ShowAsync();
            }
            else
            {
                if (addToRoster)
                {
                    await client.addToRosterAsync(jID).ConfigureAwait(false);
                }
                if (requestSubscription)
                {
                    await client.requestPresenceSubscriptionAsync(jID).ConfigureAwait(false);
                }
                ChatDBManager.INSTANCE.setChat(new ChatTable
                {
                    id = ChatTable.generateId(jID, client.getXMPPAccount().getIdAndDomain()),
                    chatJabberId = jID,
                    userAccountId = client.getXMPPAccount().getIdAndDomain(),
                    ask = null,
                    inRoster = addToRoster,
                    lastActive = DateTime.Now,
                    muted = false,
                    presence = Presence.Unavailable,
                    status = null,
                    subscription = requestSubscription ? "pending" : null
                }, false, true);
            }
        }

        /// <summary>
        /// Filters all chats and only shows those that contain the given string.
        /// </summary>
        /// <param name="s">The string for filtering chats.</param>
        /// <param name="force">Force filtering.</param>
        private void filterChats(string s, bool force)
        {
            if (!CHAT_FILTER.setChatQuery(s) && force)
            {
                CHATS_ACV.RefreshFilter();
            }
        }

        private void updateFilterUi()
        {
            filterPresenceNotUnavailable_tmfo.IsChecked = CHAT_FILTER.notUnavailable;
            filterPresenceNotOnline_tmfo.IsChecked = CHAT_FILTER.notOnline;

            filterPresenceOnline_tmfo.IsChecked = CHAT_FILTER.hasPresenceFilter(Presence.Online);
            filterPresenceChat_tmfo.IsChecked = CHAT_FILTER.hasPresenceFilter(Presence.Chat);
            filterPresenceAway_tmfo.IsChecked = CHAT_FILTER.hasPresenceFilter(Presence.Away);
            filterPresenceXa_tmfo.IsChecked = CHAT_FILTER.hasPresenceFilter(Presence.Xa);
            filterPresenceDnd_tmfo.IsChecked = CHAT_FILTER.hasPresenceFilter(Presence.Dnd);
            filterPresenceUnavailable_tmfo.IsChecked = CHAT_FILTER.hasPresenceFilter(Presence.Unavailable);

            filterChat_tmfo.IsChecked = CHAT_FILTER.chat;
            filterMUC_tmfo.IsChecked = CHAT_FILTER.muc;
        }
        #endregion

        #region --Misc Methods (Protected)--


        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            loading_grid.Visibility = Visibility.Visible;
            main_grid.Visibility = Visibility.Collapsed;

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            SystemNavigationManager.GetForCurrentView().BackRequested -= ChatPage2_BackRequested;
            SystemNavigationManager.GetForCurrentView().BackRequested += ChatPage2_BackRequested;

            string toastActivationString = null;
            if (e.NavigationMode == NavigationMode.New && e.Parameter is string && ((e.Parameter as string).Equals("App.xaml.cs") || (e.Parameter as string).Equals("AddAccountPage.xaml.cs")))
            {
                await UiUtils.showInitialStartDialogAsync();
                await UiUtils.showWhatsNewDialog();
            }
            else if (e.Parameter is ChatToastActivation chatToastActivation)
            {
                toastActivationString = chatToastActivation.CHAT_ID;
            }
            loadChats(toastActivationString);

            loading_grid.Visibility = Visibility.Collapsed;
            main_grid.Visibility = Visibility.Visible;

            if (e.Parameter is ShowAddMUCNavigationParameter)
            {
                ShowAddMUCNavigationParameter parameter = e.Parameter as ShowAddMUCNavigationParameter;
                AddMUCDialog dialog = new AddMUCDialog(parameter.ROOM_JID);
                await dialog.ShowAsync();
            }
        }

        private void ChatPage2_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }
            if (!(Window.Current.Content is Frame rootFrame))
            {
                return;
            }

            // Somewhere to go back to - ordinary back navigation.
            if (rootFrame.CanGoBack)
            {
                e.Handled = true;
                rootFrame.GoBack();
                return;
            }

            // A chat is open on a narrow screen: back closes the chat first.
            // Handled here rather than left to the MasterDetailsView, because
            // this handler is registered before the control's own and would
            // otherwise quit the app out from under an open chat.
            if (masterDetail_pnl.ViewState == MasterDetailsViewState.Details)
            {
                e.Handled = true;
                masterDetail_pnl.SelectedItem = null;
                return;
            }

            // Root of the back stack, no chat open: BACK means quit for good -
            // background tasks unregistered, location released, accounts
            // disconnected, process terminated. HOME is unaffected and still
            // just suspends the app.
            e.Handled = true;
            Task ignored = AppExitHelper.exitAppAsync("back button on the chat list");
        }

        private async void INSTANCE_ChatChanged(ChatDBManager handler, ChatChangedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Backup selected chat:
                ChatTemplate selectedChat = null;
                if (masterDetail_pnl.SelectedItem != null && masterDetail_pnl.SelectedItem is ChatTemplate)
                {
                    selectedChat = masterDetail_pnl.SelectedItem as ChatTemplate;
                }

                if (args.REMOVED)
                {
                    CHATS.RemoveId(args.CHAT.id);
                    args.Cancel = true;

                    // Restore selected chat:
                    if (selectedChat != null && !string.Equals(args.CHAT.id, selectedChat.chat.id))
                    {
                        masterDetail_pnl.SelectedItem = selectedChat;
                    }
                    return;
                }
                else
                {
                    if (CHATS.UpdateChat(args.CHAT))
                    {
                        args.Cancel = true;
                        // Restore selected chat:
                        if (selectedChat != null)
                        {
                            masterDetail_pnl.SelectedItem = selectedChat;
                        }
                        return;
                    }
                }

                Task.Run(async () =>
                {
                    // Add the new chat to the list of chats:
                    foreach (XMPPClient c in ConnectionHandler.INSTANCE.getClients())
                    {
                        if (Equals(args.CHAT.userAccountId, c.getXMPPAccount().getIdAndDomain()))
                        {
                            ChatTemplate chat;
                            if (args.CHAT.chatType == ChatType.MUC)
                            {
                                chat = new ChatTemplate(c, args.CHAT, MUCDBManager.INSTANCE.getMUCInfo(args.CHAT.id), null);
                            }
                            else
                            {
                                chat = new ChatTemplate(c, args.CHAT, null);
                            }

                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                CHATS.Add(chat);
                                // Restore selected chat:
                                if (selectedChat != null)
                                {
                                    masterDetail_pnl.SelectedItem = selectedChat;
                                }
                            });
                        }
                    }
                });
            });
        }

        private async void INSTANCE_MUCInfoChanged(MUCDBManager handler, MUCInfoChangedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => CHATS.UpdateMUCInfo(args.MUC_INFO));
        }

        private async void addChat_mfoi_Click(object sender, RoutedEventArgs e)
        {
            AddChatDialog dialog = new AddChatDialog();
            await UiUtils.showDialogAsyncQueue(dialog);
            if (!dialog.cancled)
            {
                await addChatAsync(dialog.client, dialog.jabberId, dialog.addToRoster, dialog.requestSubscription).ConfigureAwait(false);
            }
        }

        private async void addMUC_mfoi_Click(object sender, RoutedEventArgs e)
        {
            AddMUCDialog dialog = new AddMUCDialog();
            await UiUtils.showDialogAsyncQueue(dialog);
        }

        private void addMIX_mfoi_Click(object sender, RoutedEventArgs e)
        {
            // ToDo Add MIX support.
        }

        private void settings_abb_Click(object sender, RoutedEventArgs e)
        {
            (Window.Current.Content as Frame).Navigate(typeof(SettingsPage));
        }

        private async void masterDetail_pnl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Read BOTH collections out of the event args before the first
            // await.
            //
            // SelectionChangedEventArgs is a thread-affine XAML object, and the
            // sends below use ConfigureAwait(false), so everything after the
            // first one continues on a thread pool thread. Reaching for
            // e.RemovedItems from there threw
            // "The application called an interface that was marshalled for a
            // different thread" (RPC_E_WRONG_THREAD, 0x8001010E) - and since
            // this is an async void event handler, nothing could catch it and
            // it took the app down.
            List<ChatTemplate> added = new List<ChatTemplate>();
            List<ChatTemplate> removed = new List<ChatTemplate>();
            foreach (object o in e.AddedItems)
            {
                if (o is ChatTemplate c)
                {
                    added.Add(c);
                }
            }
            foreach (object o in e.RemovedItems)
            {
                if (o is ChatTemplate c)
                {
                    removed.Add(c);
                }
            }

            try
            {
                // Send active chat state:
                foreach (ChatTemplate c in added)
                {
                    if (c.client != null && shouldSendChatState(c.chat))
                    {
                        await c.client.sendChatStateAsync(c.chat.chatJabberId, XMPP_API.Classes.Network.XML.Messages.XEP_0085.ChatState.ACTIVE).ConfigureAwait(false);
                    }
                }
                // Send inactive chat state:
                foreach (ChatTemplate c in removed)
                {
                    if (c.client != null && shouldSendChatState(c.chat))
                    {
                        await c.client.sendChatStateAsync(c.chat.chatJabberId, XMPP_API.Classes.Network.XML.Messages.XEP_0085.ChatState.INACTIVE).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                // Same reason: an exception escaping an async void handler is
                // unhandled by definition. Sending a chat state is never worth
                // the process.
                Logger.Error("Failed to send a chat state after the selected chat changed.", ex);
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UiUtils.setBackgroundImage(backgroundImage_img);

            // Subscribe to chat and MUC info changed events.
            // Released again in OnNavigatedFrom - see the note there.
            ChatDBManager.INSTANCE.ChatChanged -= INSTANCE_ChatChanged;
            ChatDBManager.INSTANCE.ChatChanged += INSTANCE_ChatChanged;
            MUCDBManager.INSTANCE.MUCInfoChanged -= INSTANCE_MUCInfoChanged;
            MUCDBManager.INSTANCE.MUCInfoChanged += INSTANCE_MUCInfoChanged;

            // Load chat filter:
            filterChats_asb.Text = CHAT_FILTER.chatQuery;
            filterQuery_abb.IsChecked = CHAT_FILTER.chatQueryEnabled;
        }

        private void filterChats_asb_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            filterChats(filterChats_asb.Text, false);
        }

        private void filterChats_asb_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            filterChats((args.QueryText ?? filterChats_asb.Text), true);
        }

        private void master_cmdb_Opening(object sender, object e)
        {
            changePresence_abb.IsEnabled = ConnectionHandler.INSTANCE.getClients().Count > 0;
        }

        private async void changePresence_abb_Click(object sender, RoutedEventArgs e)
        {
            ChangeAccountPresenceDialog dialog = new ChangeAccountPresenceDialog();
            await UiUtils.showDialogAsyncQueue(dialog);
        }

        private void manageBookmarks_abb_Click(object sender, RoutedEventArgs e)
        {
            (Window.Current.Content as Frame).Navigate(typeof(ManageBookmarksPage));
        }

        private async void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            await UiUtils.onPageSizeChangedAsync(e);
        }

        protected async override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // Detach EVERYTHING this page subscribed to, before the await.
            //
            // These are all long-lived publishers (the DB manager singletons and
            // the view's SystemNavigationManager), so a page that stays
            // subscribed after being navigated away from is never collected and
            // keeps handling events forever. "-=" only removes THIS instance's
            // delegate, so the next page's "-=; +=" in Page_Loaded could not
            // clean up after us. Every abandoned instance then posted its own
            // dispatcher callback - and a DB query - for every chat row change,
            // which is a lot: presence updates alone rewrite chat rows in bursts
            // on each reconnect. That is the slowdown/freeze that got worse the
            // longer the app ran.
            //
            // Pages here have NavigationCacheMode.Disabled (the default), so a
            // navigated-from page is never shown again - going back builds a new
            // instance whose Page_Loaded subscribes afresh. If anyone ever turns
            // caching on, move these subscriptions to OnNavigatedTo, or the page
            // will come back deaf to chat updates.
            try
            {
                ChatDBManager.INSTANCE.ChatChanged -= INSTANCE_ChatChanged;
                MUCDBManager.INSTANCE.MUCInfoChanged -= INSTANCE_MUCInfoChanged;
                SystemNavigationManager.GetForCurrentView().BackRequested -= ChatPage2_BackRequested;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to unsubscribe the ChatPage events.", ex);
            }

            await UiUtils.onPageNavigatedFromAsync();
        }

        private void filterChat_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setChatOnly(filterChat_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterMUC_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setMUCOnly(filterMUC_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterPresenceNotUnavailable_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setNotUnavailable(filterPresenceNotUnavailable_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterPresenceNotOnline_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setNotOnline(filterPresenceNotOnline_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterPresenceOnline_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setPresenceFilter(Presence.Online, filterPresenceOnline_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterPresenceChat_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setPresenceFilter(Presence.Chat, filterPresenceChat_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterPresenceAway_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setPresenceFilter(Presence.Away, filterPresenceAway_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterPresenceXa_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setPresenceFilter(Presence.Xa, filterPresenceXa_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterPresenceDnd_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setPresenceFilter(Presence.Dnd, filterPresenceDnd_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterPresenceUnavailable_tmfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setPresenceFilter(Presence.Unavailable, filterPresenceUnavailable_tmfo.IsChecked);
            updateFilterUi();
        }

        private void filterClear_mfo_Click(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.clearPresenceFilter();
            updateFilterUi();
        }

        private void filterQuery_abb_Checked(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setChatQueryEnabled(true);
            filter_query_stckp.Visibility = Visibility.Visible;
            filterChats(filterChats_asb.Text, false);
        }

        private void filterQuery_abb_Unchecked(object sender, RoutedEventArgs e)
        {
            CHAT_FILTER.setChatQueryEnabled(false);
            filter_query_stckp.Visibility = Visibility.Collapsed;
            filterChats(string.Empty, false);
        }
        #endregion
    }
}
