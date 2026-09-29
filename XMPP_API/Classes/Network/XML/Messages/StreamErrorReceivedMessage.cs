using System.Xml;
using System.Xml.Linq;

namespace XMPP_API.Classes.Network.XML.Messages
{
    /// <summary>
    /// A top level &lt;stream:error/&gt; received from the server (RFC 6120, 4.9).
    /// The server closes the stream right after sending it.
    /// </summary>
    public class StreamErrorReceivedMessage : AbstractMessage
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public const string CONDITION_CONFLICT = "conflict";

        /// <summary>The defined condition, e.g. "conflict". Null if none was found.</summary>
        public readonly string CONDITION;
        /// <summary>The optional human readable &lt;text/&gt;.</summary>
        public readonly string TEXT;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        public StreamErrorReceivedMessage(XmlNode node)
        {
            if (node == null)
            {
                return;
            }
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                if (Equals(child.LocalName, "text"))
                {
                    TEXT = child.InnerText;
                }
                else if (CONDITION == null)
                {
                    CONDITION = child.LocalName;
                }
            }
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        /// <summary>
        /// True if another session with the same full JID replaced this one. Reconnecting
        /// right away would just kick that other session in turn.
        /// </summary>
        public bool isConflict()
        {
            return Equals(CONDITION, CONDITION_CONFLICT);
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        /// <summary>Only received, never sent - a readable representation for logging.</summary>
        public override XElement toXElement()
        {
            XNamespace ns = "urn:ietf:params:xml:ns:xmpp-streams";
            XElement error = new XElement("error");
            if (CONDITION != null)
            {
                error.Add(new XElement(ns + CONDITION));
            }
            if (TEXT != null)
            {
                error.Add(new XElement(ns + "text", TEXT));
            }
            return error;
        }

        #endregion
    }
}
