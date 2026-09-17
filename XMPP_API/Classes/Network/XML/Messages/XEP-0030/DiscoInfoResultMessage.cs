using System.Xml.Linq;
using XMPP_API.Classes.Network.XML.Messages.XEP_0115;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0030
{
    /// <summary>
    /// The disco#info result (IQ result) describing this client, sent in reply to a
    /// <see cref="DiscoInfoRequestMessage"/>.
    /// </summary>
    public class DiscoInfoResultMessage : IQMessage
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        private readonly string NODE;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        public DiscoInfoResultMessage(string from, string to, string id, string node) : base(from, to, RESULT, id)
        {
            this.NODE = node;
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        protected override XElement getQuery()
        {
            return EntityCapabilities.getDiscoInfoQuery(NODE);
        }

        #endregion
    }
}
