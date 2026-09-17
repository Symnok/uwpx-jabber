using XMPP_API.Classes.Network.XML.Messages.XEP_0060;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0384
{
    public class OmemoSetDeviceListMessage : AbstractPubSubPublishMessage
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public readonly OmemoDevices DEVICES;
        /// <summary>
        /// Whether the node should get published with an open access model (XEP-0384 section 4.1).
        /// Servers that do not support publish options or already have the node configured differently
        /// reply with an error - retry with this flag set to false in that case.
        /// </summary>
        public readonly bool OPEN_ACCESS_MODEL;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 04/08/2018 Created [Fabian Sauter]
        /// </history>
        public OmemoSetDeviceListMessage(string from, OmemoDevices devices) : this(from, devices, true)
        {
        }

        public OmemoSetDeviceListMessage(string from, OmemoDevices devices, bool openAccessModel) : base(from, null, Consts.XML_XEP_0384_DEVICE_LIST_NODE)
        {
            this.DEVICES = devices;
            this.OPEN_ACCESS_MODEL = openAccessModel;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        protected override PubSubPublishOptions getPublishOptions()
        {
            return OPEN_ACCESS_MODEL ? PubSubPublishOptions.getOpenAccessModelPublishOptions() : null;
        }

        protected override AbstractPubSubItem getPubSubItem()
        {
            return DEVICES;
        }

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
