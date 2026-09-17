using System.Xml.Linq;
using XMPP_API.Classes.Network.XML.Messages.XEP_0004;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0060
{
    /// <summary>
    /// XEP-0060 section 8.2.4: Submits a node configuration form (pubsub#owner).
    /// </summary>
    public class PubSubConfigureNodeMessage : AbstractPubSubMessage
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public readonly string NODE_NAME;
        public readonly DataForm NODE_CONFIG;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        public PubSubConfigureNodeMessage(string from, string to, string nodeName, DataForm nodeConfig) : base(from, to)
        {
            this.NODE_NAME = nodeName;
            this.NODE_CONFIG = nodeConfig;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        protected override XNamespace getPubSubNamespace()
        {
            return Consts.XML_XEP_0060_NAMESPACE_OWNER;
        }

        /// <summary>
        /// Returns a node configuration form that only sets the access model to open (XEP-0060 section 4.5).
        /// Servers merge submitted fields with the current node configuration.
        /// </summary>
        public static DataForm getOpenAccessModelConfig()
        {
            DataForm config = new DataForm(DataFormType.SUBMIT);
            config.FIELDS.Add(new Field()
            {
                var = "FORM_TYPE",
                type = FieldType.HIDDEN,
                value = "http://jabber.org/protocol/pubsub#node_config"
            });
            config.FIELDS.Add(new Field()
            {
                var = "pubsub#access_model",
                type = FieldType.NONE,
                value = "open"
            });
            return config;
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--


        #endregion

        #region --Misc Methods (Private)--


        #endregion

        #region --Misc Methods (Protected)--
        protected override void addContent(XElement node, XNamespace ns)
        {
            XElement configureNode = new XElement(ns + "configure");
            configureNode.Add(new XAttribute("node", NODE_NAME));
            NODE_CONFIG.addToXElement(configureNode);
            node.Add(configureNode);
        }

        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--


        #endregion
    }
}
