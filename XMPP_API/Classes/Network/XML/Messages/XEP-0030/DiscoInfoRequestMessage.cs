using System.Xml;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0030
{
    /// <summary>
    /// An incoming disco#info request (IQ get) from the server or a contact, e.g. resolving our
    /// XEP-0115 entity capabilities. Gets answered with our identity and feature list.
    /// </summary>
    public class DiscoInfoRequestMessage : IQMessage
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        /// <summary>
        /// The optionally requested node (e.g. '&lt;caps node&gt;#&lt;ver&gt;'), echoed back in the response.
        /// </summary>
        public readonly string NODE;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        public DiscoInfoRequestMessage(XmlNode node) : base(node)
        {
            XmlNode queryNode = XMLUtils.getChildNode(node, "query", Consts.XML_XMLNS, Consts.XML_XEP_0030_INFO_NAMESPACE);
            NODE = queryNode?.Attributes["node"]?.Value;
        }

        #endregion
    }
}
