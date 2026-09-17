using System;
using System.Collections.Generic;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384.Signal.Session;

namespace XMPP_API.Classes.Network.Events
{
    public class OmemoSessionBuildErrorEventArgs : EventArgs
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public readonly string CHAT_JID;
        public readonly OmemoSessionBuildError ERROR;
        /// <summary>
        /// The chatMessageIds of all messages that could not be encrypted and got dropped.
        /// </summary>
        public readonly IList<string> CHAT_MESSAGE_IDS;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        public OmemoSessionBuildErrorEventArgs(string chatJid, OmemoSessionBuildError error, IList<string> chatMessageIds)
        {
            this.CHAT_JID = chatJid;
            this.ERROR = error;
            this.CHAT_MESSAGE_IDS = chatMessageIds;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--


        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--


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
