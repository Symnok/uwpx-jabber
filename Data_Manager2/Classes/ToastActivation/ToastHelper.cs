using Data_Manager2.Classes.DBTables;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace Data_Manager2.Classes.ToastActivation
{
    public static class ToastHelper
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        private const string DEFAULT_MUC_IMAGE_PATH = "Assets/Images/default_muc_image.png";
        private const string DEFAULT_USER_IMAGE_PATH = "Assets/Images/default_user_image.png";
        private const string SEND_BUTTON_IMAGE_PATH = "Assets/Images/send.png";
        public const string TEXT_BOX_ID = "msg_tbx";

        /// <summary>
        /// The app window is on screen. Published by the app (see
        /// BackgroundService.IsInForeground), because toasts are raised from
        /// ConnectionHandler on a thread pool thread where Window.Current is
        /// always null and cannot answer this.
        /// </summary>
        public static volatile bool IsAppInForeground = true;

        /// <summary>
        /// Id of the chat currently open on screen, or null. Published by
        /// ChatPage. A message arriving in this chat while the app is on
        /// screen needs no notification at all - the user is reading it.
        /// </summary>
        public static volatile string OpenChatId = null;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--


        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--


        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        public static void removeToastGroup(string group)
        {
            ToastNotificationManager.History.RemoveGroup(group);
        }

        /// <summary>
        /// Puts the number of chats holding unread messages on the Start tile,
        /// the way the tile badge works for mail and messaging apps.
        ///
        /// Clearing at zero is not optional: without it Windows keeps showing
        /// the last number it was given, forever.
        /// </summary>
        public static void updateUnreadBadge()
        {
            try
            {
                int count = DBManager.ChatDBManager.INSTANCE.getUnreadChatCount();
                BadgeUpdater updater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
                if (count <= 0)
                {
                    updater.Clear();
                    return;
                }

                Windows.Data.Xml.Dom.XmlDocument xml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);
                Windows.Data.Xml.Dom.XmlElement badge = xml.SelectSingleNode("/badge") as Windows.Data.Xml.Dom.XmlElement;
                if (badge is null)
                {
                    return;
                }
                badge.SetAttribute("value", count.ToString());
                updater.Update(new BadgeNotification(xml));
            }
            catch (System.Exception ex)
            {
                Logging.Logger.Error("Failed to update the unread tile badge.", ex);
            }
        }

        public static void showChatTextToast(ChatMessageTable msg, ChatTable chat)
        {
            var toastContent = new ToastContent()
            {
                Visual = new ToastVisual()
                {
                    BindingGeneric = new ToastBindingGeneric()
                    {
                        Children =
                        {
                            new AdaptiveText()
                            {
                                Text = chat.chatJabberId,
                                HintMaxLines = 1
                            },
                            new AdaptiveText()
                            {
                                Text = msg.message
                            }
                        },
                        AppLogoOverride = new ToastGenericAppLogo()
                        {
                            Source = chat.chatType == ChatType.CHAT ? DEFAULT_USER_IMAGE_PATH : DEFAULT_MUC_IMAGE_PATH,
                            HintCrop = ToastGenericAppLogoCrop.Default
                        }
                    }
                },
                Actions = getActions(msg, chat),
                DisplayTimestamp = msg.date,
                Launch = new ChatToastActivation(chat.id, false).generate()
            };

            popToast(toastContent, chat);
        }

        public static void showChatTextEncryptedToast(ChatMessageTable msg, ChatTable chat)
        {
            var toastContent = new ToastContent()
            {
                Visual = new ToastVisual()
                {
                    BindingGeneric = new ToastBindingGeneric()
                    {
                        Children =
                        {
                            new AdaptiveText()
                            {
                                Text = chat.chatJabberId,
                                HintMaxLines = 1
                            },
                            new AdaptiveText()
                            {
                                Text = "You received an encrypted message!"
                            }
                        },
                        AppLogoOverride = new ToastGenericAppLogo()
                        {
                            Source = chat.chatType == ChatType.CHAT ? DEFAULT_USER_IMAGE_PATH : DEFAULT_MUC_IMAGE_PATH,
                            HintCrop = ToastGenericAppLogoCrop.Default
                        }
                    }
                },
                Actions = getActions(msg, chat),
                DisplayTimestamp = msg.date,
                Launch = new ChatToastActivation(chat.id, false).generate()
            };

            popToast(toastContent, chat);
        }

        public static void showChatTextImageToast(ChatMessageTable msg, ChatTable chat)
        {
            var toastContent = new ToastContent()
            {
                Visual = new ToastVisual()
                {
                    BindingGeneric = new ToastBindingGeneric()
                    {
                        Children =
                        {
                            new AdaptiveText()
                            {
                                Text = chat.chatJabberId,
                                HintMaxLines = 1
                            },
                            new AdaptiveText()
                            {
                                Text = "You received an image!"
                            }
                        },
                        HeroImage = new ToastGenericHeroImage()
                        {
                            Source = msg.message
                        },
                        AppLogoOverride = new ToastGenericAppLogo()
                        {
                            Source = chat.chatType == ChatType.CHAT ? DEFAULT_USER_IMAGE_PATH : DEFAULT_MUC_IMAGE_PATH,
                            HintCrop = ToastGenericAppLogoCrop.Default
                        }
                    }
                },
                Actions = getActions(msg, chat),
                DisplayTimestamp = msg.date,
                Launch = new ChatToastActivation(chat.id, false).generate()
            };

            popToast(toastContent, chat);
        }

        #endregion

        #region --Misc Methods (Private)--
        private static void popToast(ToastContent content, ChatTable chat)
        {
            if (IsAppInForeground)
            {
                // The message belongs to the chat being read right now: there is
                // nothing to announce, so no toast at all - no sound, no popup
                // and no entry in the notification centre.
                if (chat.id != null && Equals(chat.id, OpenChatId))
                {
                    return;
                }

                // On screen, but a different chat. The popup still has to appear
                // so the user learns about it, only the sound is dropped.
                content.Audio = new ToastAudio()
                {
                    Silent = true
                };
            }

            var toastNotif = new ToastNotification(content.GetXml())
            {
                Group = chat.id
            };

            // And send the notification
            ToastNotificationManager.CreateToastNotifier().Show(toastNotif);
        }

        private static ToastActionsCustom getActions(ChatMessageTable msg, ChatTable chat)
        {
            return new ToastActionsCustom()
            {
                /*Inputs =
                {
                    new ToastTextBox(TEXT_BOX_ID)
                    {
                        PlaceholderContent = "Reply"
                    }
                },*/
                Buttons =
                {
                    /*new ToastButton("Send", new SendReplyToastActivation(chat.id, false).generate())
                    {
                        ActivationType = ToastActivationType.Background,
                        ImageUri = SEND_BUTTON_IMAGE_PATH,
                        TextBoxId = TEXT_BOX_ID,
                    },*/
                    new ToastButton("Mark chat as read", new MarkChatAsReadToastActivation(chat.id, false).generate())
                    {
                        ActivationType = ToastActivationType.Background
                    },
                    new ToastButton("Mark as read", new MarkMessageAsReadToastActivation(msg.id, false).generate())
                    {
                        ActivationType = ToastActivationType.Background
                    }
                }
            };
        }

        #endregion

        #region --Misc Methods (Protected)--


        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--


        #endregion
    }
}
