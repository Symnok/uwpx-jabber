using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using XMPP_API.Classes.Crypto;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0115
{
    /// <summary>
    /// XEP-0115 (Entity Capabilities): the set of features this client advertises and the
    /// verification hash for them. Advertising 'eu.siacs.conversations.axolotl.devicelist+notify'
    /// is what makes the server push a contact's OMEMO device list to us on presence (XEP-0163),
    /// which is how every interoperable OMEMO client discovers device lists - an explicit
    /// items request is refused by servers like ejabberd.
    /// </summary>
    public static class EntityCapabilities
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        // The node URL that identifies this client implementation:
        public const string NODE = "https://github.com/Symnok/uwpx-jabber";

        private const string IDENTITY_CATEGORY = "client";
        private const string IDENTITY_TYPE = "phone";
        private const string IDENTITY_NAME = "UWPX Jabber";

        /// <summary>
        /// The disco#info features this client supports. The OMEMO device list '+notify' entry
        /// makes the server deliver contacts' device lists automatically.
        /// </summary>
        public static readonly IReadOnlyList<string> FEATURES = new List<string>
        {
            Consts.XML_XEP_0030_INFO_NAMESPACE,
            Consts.XML_XEP_0115_NAMESPACE,
            Consts.XML_XEP_0085_NAMESPACE,
            Consts.XML_XEP_0184_NAMESPACE,
            Consts.XML_XEP_0384_DEVICE_LIST_NODE_NOTIFY
        };

        /// <summary>
        /// The XEP-0115 verification string hash (base64 of SHA-1) computed over the identity and features.
        /// </summary>
        public static readonly string VERIFICATION_HASH = computeVerificationHash();

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        /// <summary>
        /// Builds the &lt;c/&gt; entity capabilities element to attach to a presence stanza.
        /// </summary>
        public static XElement getCapsElement()
        {
            XNamespace ns = Consts.XML_XEP_0115_NAMESPACE;
            XElement c = new XElement(ns + "c");
            c.Add(new XAttribute("hash", "sha-1"));
            c.Add(new XAttribute("node", NODE));
            c.Add(new XAttribute("ver", VERIFICATION_HASH));
            return c;
        }

        /// <summary>
        /// Builds the disco#info &lt;query/&gt; describing this client, echoing the requested node.
        /// </summary>
        public static XElement getDiscoInfoQuery(string requestedNode)
        {
            XNamespace ns = Consts.XML_XEP_0030_INFO_NAMESPACE;
            XElement query = new XElement(ns + "query");
            if (!string.IsNullOrEmpty(requestedNode))
            {
                query.Add(new XAttribute("node", requestedNode));
            }

            XElement identity = new XElement(ns + "identity");
            identity.Add(new XAttribute("category", IDENTITY_CATEGORY));
            identity.Add(new XAttribute("type", IDENTITY_TYPE));
            identity.Add(new XAttribute("name", IDENTITY_NAME));
            query.Add(identity);

            foreach (string feature in FEATURES)
            {
                XElement f = new XElement(ns + "feature");
                f.Add(new XAttribute("var", feature));
                query.Add(f);
            }
            return query;
        }

        #endregion

        #region --Misc Methods (Private)--
        /// <summary>
        /// XEP-0115 section 5.1: S = identities (sorted) then features (sorted), each terminated by '&lt;'.
        /// ver = base64(SHA-1(S)). Computed from the same identity/feature set served via disco#info,
        /// so the advertised hash always matches the disco response.
        /// </summary>
        private static string computeVerificationHash()
        {
            StringBuilder s = new StringBuilder();
            // Single identity: category/type/lang/name, lang is empty:
            s.Append(IDENTITY_CATEGORY).Append('/').Append(IDENTITY_TYPE).Append('/').Append('/').Append(IDENTITY_NAME).Append('<');

            List<string> sortedFeatures = FEATURES.ToList();
            sortedFeatures.Sort(StringComparer.Ordinal);
            foreach (string feature in sortedFeatures)
            {
                s.Append(feature).Append('<');
            }

            byte[] hash = CryptoUtils.SHA_1(Encoding.UTF8.GetBytes(s.ToString()));
            return Convert.ToBase64String(hash);
        }

        #endregion
    }
}
