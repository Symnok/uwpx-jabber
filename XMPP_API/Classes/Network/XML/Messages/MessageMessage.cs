using System;
using System.Xml;
using System.Xml.Linq;

namespace XMPP_API.Classes.Network.XML.Messages
{
    public class MessageMessage : AbstractAddressableMessage
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public string MESSAGE { get; protected set; }
        public readonly string TYPE;
        public readonly string FROM_NICK;
        public readonly bool RECIPT_REQUESTED;
        public readonly CarbonCopyType CC_TYPE;
        public DateTime delay { get; protected set; }
        // The unique DB id of the message. Only required for send messages:
        public string chatMessageId;
        protected bool includeBody;

        // XEP-0444 (Message Reactions) / XEP-0461 (Message Replies): set while parsing
        // an incoming stanza so a reaction can be shown in the chat but skipped for
        // notifications. See IsReaction().
        public bool HAS_REACTIONS_ELEMENT { get; private set; }
        public bool HAS_REPLY_ELEMENT { get; private set; }

        public const string TYPE_CHAT = "chat";
        public const string TYPE_GROUPCHAT = "groupchat";
        public const string TYPE_ERROR = "error";

        // XEP-0359 (Unique and Stable Stanza IDs):
        private const string XEP_0359_NAMESPACE = "urn:xmpp:sid:0";

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 17/08/2017 Created [Fabian Sauter]
        /// </history>
        public MessageMessage(string from, string to, string message, string type, bool reciptRequested) : this(from, to, message, type, null, reciptRequested)
        {
        }

        public MessageMessage(string from, string to, string message, string type, string from_nick, bool reciptRequested) : base(from, to)
        {
            this.MESSAGE = message;
            this.TYPE = type;
            this.cacheUntilSend = true;
            this.delay = DateTime.MinValue;
            this.FROM_NICK = from_nick;
            this.RECIPT_REQUESTED = reciptRequested;
            this.CC_TYPE = CarbonCopyType.NONE;
            this.includeBody = true;
        }

        public MessageMessage(XmlNode node, string type) : this(node, CarbonCopyType.NONE)
        {
            this.TYPE = type;
            this.chatMessageId = null;
        }

        public MessageMessage(XmlNode node, CarbonCopyType ccType) : base(node.Attributes["from"]?.Value, node.Attributes["to"]?.Value, getStableId(node))
        {
            this.CC_TYPE = ccType;
            if (!node.HasChildNodes)
            {
                MESSAGE = "invalid message: " + node.ToString();
                TYPE = "error";
                return;
            }

            XmlAttribute typeAttribute = XMLUtils.getAttribute(node, "type");
            if (typeAttribute != null)
            {
                TYPE = typeAttribute.Value;
                switch (TYPE)
                {
                    case TYPE_ERROR:
                        XmlNode error = XMLUtils.getChildNode(node, "error");
                        if (error != null)
                        {
                            XmlNode text = XMLUtils.getChildNode(error, "text");
                            if (text != null)
                            {
                                MESSAGE = text.InnerText;
                            }
                            else
                            {
                                MESSAGE = error.InnerXml;
                            }
                        }
                        else
                        {
                            MESSAGE = node.InnerXml;
                        }
                        return;

                    case TYPE_GROUPCHAT:
                        FROM_NICK = Utils.getResourceFromFullJid(FROM);
                        break;
                }
            }

            XmlNode body = XMLUtils.getChildNode(node, "body");
            if (body != null)
            {
                MESSAGE = body.InnerText;
            }

            // XEP-0444 (Message Reactions) and XEP-0461 (Message Replies): note whether
            // the stanza carries these so a reaction can be displayed but not notified.
            // A reaction reaches this client as a reply that quotes the original message
            // with the reaction emoji as the body text; modern senders also add a
            // <reactions/> element. Both markers live on the raw stanza node.
            HAS_REACTIONS_ELEMENT = XMLUtils.getChildNode(node, "reactions", Consts.XML_XMLNS, Consts.XML_XEP_0444_NAMESPACE) != null;
            HAS_REPLY_ELEMENT = XMLUtils.getChildNode(node, "reply", Consts.XML_XMLNS, Consts.XML_XEP_0461_NAMESPACE) != null;

            // XEP-0203 (Delayed Delivery):
            XmlNode delayNode = XMLUtils.getChildNode(node, "delay", Consts.XML_XMLNS, Consts.XML_XEP_0203_NAMESPACE);
            if (delayNode != null)
            {
                XmlAttribute stamp = XMLUtils.getAttribute(delayNode, "stamp");
                if (stamp != null)
                {
                    DateTimeParserHelper parserHelper = new DateTimeParserHelper();
                    delay = parserHelper.parse(stamp.Value);
                }
            }
            else
            {
                delay = DateTime.Now;
            }

            // XEP-0184 (Message Delivery Receipts):
            XmlNode requestNode = XMLUtils.getChildNode(node, "request", Consts.XML_XMLNS, Consts.XML_XEP_0184_NAMESPACE);
            RECIPT_REQUESTED = requestNode != null;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        public DateTime getDelay()
        {
            return delay;
        }

        /// <summary>
        /// The id used to recognise this message again (the DB key is built from it).
        /// Normally the stanza 'id' attribute. Some clients (e.g. Cheogram) send MUC
        /// messages without one - a random id would then store every replay of the
        /// message (MUC join history after each reconnect) as a new message. So fall
        /// back to ids that stay the same across replays:
        ///   1. XEP-0359 &lt;origin-id/&gt; set by the sender,
        ///   2. XEP-0359 &lt;stanza-id/&gt; set by the room (by = sender bare JID) or
        ///      by our own server (by = recipient bare JID).
        /// Only if none exists a random id gets used.
        /// </summary>
        private static string getStableId(XmlNode node)
        {
            string id = node.Attributes["id"]?.Value;
            if (!string.IsNullOrEmpty(id))
            {
                return id;
            }

            string fromBare = Utils.getBareJidFromFullJid(node.Attributes["from"]?.Value);
            string toBare = Utils.getBareJidFromFullJid(node.Attributes["to"]?.Value);
            string originId = null;
            string roomStanzaId = null;
            string ownStanzaId = null;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element || !Equals(child.NamespaceURI, XEP_0359_NAMESPACE))
                {
                    continue;
                }
                string childId = child.Attributes?["id"]?.Value;
                if (string.IsNullOrEmpty(childId))
                {
                    continue;
                }
                if (Equals(child.LocalName, "origin-id"))
                {
                    originId = childId;
                }
                else if (Equals(child.LocalName, "stanza-id"))
                {
                    string by = child.Attributes["by"]?.Value;
                    if (fromBare != null && string.Equals(by, fromBare, StringComparison.OrdinalIgnoreCase))
                    {
                        roomStanzaId = childId;
                    }
                    else if (toBare != null && string.Equals(by, toBare, StringComparison.OrdinalIgnoreCase))
                    {
                        ownStanzaId = childId;
                    }
                }
            }

            if (originId != null)
            {
                return "origin-" + originId;
            }
            if (roomStanzaId != null)
            {
                return "sid-" + roomStanzaId;
            }
            if (ownStanzaId != null)
            {
                return "sid-" + ownStanzaId;
            }
            return getRandomId();
        }

        /// <summary>
        /// True when this message is a reaction to another message (XEP-0444), so it
        /// should be shown in the chat but must NOT raise a notification.
        ///
        /// A reaction arrives as a reply that quotes the original message with the
        /// reaction emoji as its body text. It is treated as a reaction when either:
        ///   - the stanza carries a &lt;reactions/&gt; element (XEP-0444), or
        ///   - it is a reply / contains a quote and the remaining body (after removing
        ///     the quoted lines) is nothing but emoji.
        /// This also covers a reaction sent onto a message that was itself only a
        /// reaction, since each such message matches on its own.
        /// </summary>
        public bool IsReaction()
        {
            if (HAS_REACTIONS_ELEMENT)
            {
                return true;
            }

            string content = StripQuoteLines(MESSAGE);
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }
            return (HAS_REPLY_ELEMENT || HasQuoteLines(MESSAGE)) && IsEmojiOnly(content);
        }

        /// <summary>True when any line of the body is an XEP-0461 fallback quote ("&gt; ...").</summary>
        private static bool HasQuoteLines(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            string[] lines = message.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                if (line.TrimStart().StartsWith(">"))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The body with XEP-0461 quote lines ("&gt; ...") removed, trimmed.</summary>
        private static string StripQuoteLines(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "";
            }
            string[] lines = message.Replace("\r\n", "\n").Split('\n');
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string line in lines)
            {
                if (line.TrimStart().StartsWith(">"))
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(line);
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// True when the text has at least one visible character and none of them are
        /// letters or digits - i.e. it is only emoji / symbols, as a reaction is.
        /// </summary>
        private static bool IsEmojiOnly(string text)
        {
            bool hasVisible = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                hasVisible = true;
                if (char.IsLetterOrDigit(c))
                {
                    return false;
                }
            }
            return hasVisible;
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        public override XElement toXElement()
        {
            XElement msgNode = new XElement("message");

            if (FROM != null)
            {
                msgNode.Add(new XAttribute("from", FROM));
            }

            if (TO != null)
            {
                msgNode.Add(new XAttribute("to", TO));
            }

            if (ID != null)
            {
                msgNode.Add(new XAttribute("id", ID));
            }

            if (TYPE != null)
            {
                msgNode.Add(new XAttribute("type", TYPE));
            }

            if (includeBody && MESSAGE != null)
            {
                msgNode.Add(new XElement("body", MESSAGE));
            }

            // XEP-0203 (Delayed Delivery):
            if (delay != DateTime.MinValue)
            {
                XNamespace ns = Consts.XML_XEP_0203_NAMESPACE;
                XElement delayNode = new XElement(ns + "delay");
                DateTimeParserHelper parserHelper = new DateTimeParserHelper();
                delayNode.Add(new XAttribute("stamp", parserHelper.toString(DateTime.Now)));
                delayNode.Add(new XAttribute("from", FROM));
                delayNode.Add("Offline Storage");
                msgNode.Add(delay);
            }

            // XEP-0184 (Message Delivery Receipts):
            if (RECIPT_REQUESTED)
            {
                XNamespace ns = Consts.XML_XEP_0184_NAMESPACE;
                XElement requestNode = new XElement(ns + "request");
                msgNode.Add(requestNode);
            }

            return msgNode;
        }

        public void addDelay()
        {
            delay = DateTime.Now;
        }

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
