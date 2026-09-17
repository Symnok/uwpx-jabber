using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using XMPP_API.Classes.Network.XML;
using XMPP_API.Classes.Network.XML.Messages;
using XMPP_API.Classes.Network.XML.Messages.XEP_0085;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384;

namespace Component_Tests.Classes.XmppMessages
{
    [TestClass]
    public class Test_MessageParser2
    {
        [TestMethod]
        public void Test_MessageParser2_1()
        {
            string msg = "<iq xml:lang='en' to='uwptest@404.city/FABIAN-TOWER-PC' from='fabi@xmpp.uwpx.org' type='error' id='134077900-349929748-1523671119-224987985-1457976454'><pubsub xmlns='http://jabber.org/protocol/pubsub'><items node='eu.siacs.conversations.axolotl.devicelist'/></pubsub><error code='405' type='cancel'><closed-node xmlns='http://jabber.org/protocol/pubsub#errors'/><not-allowed xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error></iq>";
            MessageParser2 parser = new MessageParser2();
            List<AbstractMessage> messages = parser.parseMessages(ref msg);
            Assert.IsTrue(messages.Any((x) => x is IQErrorMessage));
        }

        [TestMethod]
        public void Test_MessageParser2_Omemo_1()
        {
            // OMEMO message as send by Conversations: chat state + fallback body + encrypted element:
            string msg = "<message xmlns='jabber:client' from='alice@example.org/phone' to='bob@example.org' type='chat' id='c1'><active xmlns='http://jabber.org/protocol/chatstates'/><body>I sent you an OMEMO encrypted message but your client doesn't seem to support that. Find more information on https://conversations.im/omemo</body><encrypted xmlns='eu.siacs.conversations.axolotl'><header sid='2005576180'><key prekey='true' rid='1234567890'>MwohBZ==</key><key rid='42'>MwohBQ==</key><iv>AAECAwQFBgcICQoL</iv></header><payload>AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=</payload></encrypted><encryption xmlns='urn:xmpp:eme:0' name='OMEMO' namespace='eu.siacs.conversations.axolotl'/><store xmlns='urn:xmpp:hints'/><request xmlns='urn:xmpp:receipts'/></message>";
            MessageParser2 parser = new MessageParser2();
            List<AbstractMessage> messages = parser.parseMessages(ref msg);

            Assert.IsTrue(messages.Any((x) => x is ChatStateMessage));
            // The fallback body must not show up as a plain text message:
            Assert.IsFalse(messages.Any((x) => x.GetType() == typeof(MessageMessage)));
            OmemoMessageMessage omemoMsg = messages.FirstOrDefault((x) => x is OmemoMessageMessage) as OmemoMessageMessage;
            Assert.IsNotNull(omemoMsg);
            Assert.AreEqual(2005576180u, omemoMsg.SOURCE_DEVICE_ID);
            Assert.AreEqual("AAECAwQFBgcICQoL", omemoMsg.BASE_64_IV);
            Assert.AreEqual("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=", omemoMsg.BASE_64_PAYLOAD);
            Assert.IsTrue(omemoMsg.hasPayload());
            Assert.AreEqual(2, omemoMsg.KEYS.Count);
            Assert.IsNotNull(omemoMsg.getOmemoKey(42));
            Assert.IsFalse(omemoMsg.getOmemoKey(42).IS_PRE_KEY);
            Assert.IsNotNull(omemoMsg.getOmemoKey(1234567890));
            Assert.IsTrue(omemoMsg.getOmemoKey(1234567890).IS_PRE_KEY);
            Assert.AreEqual("MwohBZ==", omemoMsg.getOmemoKey(1234567890).BASE_64_KEY);
            Assert.IsTrue(omemoMsg.RECIPT_REQUESTED);
        }

        [TestMethod]
        public void Test_MessageParser2_Omemo_2()
        {
            // Key transport message (no payload) without a chat state:
            string msg = "<message xmlns='jabber:client' from='alice@example.org/phone' to='bob@example.org' type='chat' id='c2'><encrypted xmlns='eu.siacs.conversations.axolotl'><header sid='1'><key prekey='true' rid='2'>MwohBZ==</key><iv>AAECAwQFBgcICQoL</iv></header></encrypted><store xmlns='urn:xmpp:hints'/></message>";
            MessageParser2 parser = new MessageParser2();
            List<AbstractMessage> messages = parser.parseMessages(ref msg);

            OmemoMessageMessage omemoMsg = messages.FirstOrDefault((x) => x is OmemoMessageMessage) as OmemoMessageMessage;
            Assert.IsNotNull(omemoMsg);
            Assert.IsFalse(omemoMsg.hasPayload());
            Assert.AreEqual(1u, omemoMsg.SOURCE_DEVICE_ID);
            Assert.AreEqual(1, omemoMsg.KEYS.Count);
        }

        [TestMethod]
        public void Test_MessageParser2_Performance_1()
        {
            string msg = "<iq xml:lang='en' to='uwptest@404.city/FABIAN-TOWER-PC' from='fabi@xmpp.uwpx.org' type='error' id='134077900-349929748-1523671119-224987985-1457976454'><pubsub xmlns='http://jabber.org/protocol/pubsub'><items node='eu.siacs.conversations.axolotl.devicelist'/></pubsub><error code='405' type='cancel'><closed-node xmlns='http://jabber.org/protocol/pubsub#errors'/><not-allowed xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error></iq>";
            MessageParser2 parser = new MessageParser2();
            Stopwatch watch = new Stopwatch();
            long sum = 0;
            for (int e = 0; e < 100; e++)
            {
                watch.Start();
                for (int i = 0; i < 1000; i++)
                {
                    parser.parseMessages(ref msg);
                }
                watch.Stop();
                sum += watch.ElapsedTicks;
            }
            sum /= 100;
            Logging.Logger.Info("[UNIT_TEST] Message Parser average parse time: " + sum);
        }
    }
}
