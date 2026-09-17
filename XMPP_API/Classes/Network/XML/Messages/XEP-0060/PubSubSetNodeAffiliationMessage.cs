using System.Xml.Linq;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0060
{
    /// <summary>
    /// XEP-0060 section 8.9.2: sets the affiliation of a JID on one of our own PEP nodes.
    /// Used to grant contacts 'member' access to our OMEMO nodes on servers that force an
    /// access model of 'whitelist' (e.g. xabber.org / ejabberd), where an 'open' access model
    /// cannot be set and contacts can otherwise not read the device list or bundles.
    /// </summary>
    public class PubSubSetNodeAffiliationMessage : AbstractPubSubMessage
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public readonly string NODE_NAME;
        public readonly string AFFILIATED_JID;
        public readonly string AFFILIATION;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        public PubSubSetNodeAffiliationMessage(string from, string to, string nodeName, string affiliatedJid, string affiliation) : base(from, to)
        {
            this.NODE_NAME = nodeName;
            this.AFFILIATED_JID = affiliatedJid;
            this.AFFILIATION = affiliation;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        protected override XNamespace getPubSubNamespace()
        {
            return Consts.XML_XEP_0060_NAMESPACE_OWNER;
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Protected)--
        protected override void addContent(XElement node, XNamespace ns)
        {
            XElement affiliations = new XElement(ns + "affiliations");
            affiliations.Add(new XAttribute("node", NODE_NAME));
            XElement affiliation = new XElement(ns + "affiliation");
            affiliation.Add(new XAttribute("jid", AFFILIATED_JID));
            affiliation.Add(new XAttribute("affiliation", AFFILIATION));
            affiliations.Add(affiliation);
            node.Add(affiliations);
        }

        #endregion
    }
}
