using System.Collections.Generic;
using System.Xml;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0363
{
    public class HTTPUploadSlot
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public readonly string URL_PUT;
        public readonly string URL_GET;
        public readonly Dictionary<string, string> HEADERS;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 17/03/2018 Created [Fabian Sauter]
        /// </history>
        public HTTPUploadSlot(XmlNode node)
        {
            HEADERS = new Dictionary<string, string>();

            XmlNode putNode = XMLUtils.getChildNode(node, "put");
            if (putNode != null)
            {
                URL_PUT = putNode.Attributes["url"]?.Value;

                // XEP-0363 headers are <header name='...'>value</header> CHILDREN of
                // <put>, not attributes of it. Copying the attributes in put 'url'
                // into the header list, which servers reject when it is sent back as
                // a request header, and missed the Authorization header that many
                // upload components require.
                foreach (XmlNode child in putNode.ChildNodes)
                {
                    if (!string.Equals(child.Name, "header"))
                    {
                        continue;
                    }
                    string name = child.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(name))
                    {
                        HEADERS[name] = child.InnerText;
                    }
                }
            }

            XmlNode getNode = XMLUtils.getChildNode(node, "get");
            if (getNode != null)
            {
                URL_GET = getNode.Attributes["url"]?.Value;
            }
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
