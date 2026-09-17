using libsignal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using XMPP_API.Classes;
using XMPP_API.Classes.Network;
using XMPP_API.Classes.Network.XML;
using XMPP_API.Classes.Network.XML.Messages;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384.Signal.Session;

namespace Component_Tests.Classes.Crypto
{
    /// <summary>
    /// End-to-end XEP-0384 (OMEMO) tests: session building via bundle, encrypt -> XML -> parse -> decrypt
    /// between two accounts sharing the local Signal key store.
    /// </summary>
    [TestClass]
    public class Test_OmemoMessage
    {
        private static XMPPAccount createAccount(string userId, uint deviceId)
        {
            // Unique JIDs per run, since the key stores are persisted in the app's local DB:
            string domain = "test-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".example.org";
            XMPPAccount account = new XMPPAccount(new XMPPUser(userId, "password", domain, "phone"), domain, 5222);
            account.generateOmemoKeys();
            account.omemoDeviceId = deviceId;
            account.savePreKeys();
            account.saveSignedPreKey();
            return account;
        }

        private static OmemoMessageMessage sendAndParse(OmemoMessageMessage msg)
        {
            string xml = msg.toXmlString();
            Assert.IsTrue(xml.Contains("xmlns=\"eu.siacs.conversations.axolotl\""), xml);
            Assert.IsTrue(xml.Contains("xmlns=\"urn:xmpp:eme:0\""), xml);
            Assert.IsTrue(xml.Contains("xmlns=\"urn:xmpp:hints\""), xml);
            Assert.IsTrue(xml.Contains("<body>" + OmemoMessageMessage.FALLBACK_BODY + "</body>"), xml);

            List<AbstractMessage> parsed = new MessageParser2().parseMessages(ref xml);
            Assert.IsFalse(parsed.Any((x) => x.GetType() == typeof(MessageMessage)), "Fallback body must not get parsed as plain message");
            OmemoMessageMessage received = parsed.OfType<OmemoMessageMessage>().FirstOrDefault();
            Assert.IsNotNull(received);
            Assert.AreEqual(OmemoMessageMessage.FALLBACK_BODY, received.MESSAGE);
            return received;
        }

        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_OmemoMessage_Enc_Dec_1()
        {
            XMPPAccount alice = createAccount("alice", 1234);
            XMPPAccount bob = createAccount("bob", 4321);
            OmemoHelper aliceHelper = new XMPPConnection2(alice).OMEMO_HELPER;
            OmemoHelper bobHelper = new XMPPConnection2(bob).OMEMO_HELPER;

            // 1. Alice builds a session using Bob's published bundle:
            OmemoBundleInformation bobBundle = bob.getOmemoBundleInformation();
            Assert.IsTrue(bobBundle.isValid());
            Assert.AreEqual(100, bobBundle.PUBLIC_PRE_KEYS.Count);
            SignalProtocolAddress bobAddress = aliceHelper.newSession(bob.getIdAndDomain(), bob.omemoDeviceId, bobBundle.getRandomPreKey(bob.omemoDeviceId));
            Assert.IsTrue(aliceHelper.containsSession(bobAddress));
            OmemoSession aliceSession = new OmemoSession(bob.getIdAndDomain());
            aliceSession.DEVICE_SESSIONS.Add(bob.omemoDeviceId, aliceHelper.loadCipher(bobAddress));

            // 2. Alice -> Bob (PreKeySignalMessage):
            const string TEXT_1 = "Hello Bob! Unicode: äöü ✓ \U0001F600";
            OmemoMessageMessage msg1 = new OmemoMessageMessage(alice.getIdDomainAndResource(), bob.getIdAndDomain(), TEXT_1, MessageMessage.TYPE_CHAT, true);
            msg1.encrypt(aliceSession, alice.omemoDeviceId);
            Assert.AreEqual(1, msg1.KEYS.Count);
            Assert.IsTrue(msg1.getOmemoKey(bob.omemoDeviceId).IS_PRE_KEY);

            OmemoMessageMessage recv1 = sendAndParse(msg1);
            Assert.AreEqual(alice.omemoDeviceId, recv1.SOURCE_DEVICE_ID);
            Assert.IsNotNull(recv1.getOmemoKey(bob.omemoDeviceId));
            Assert.IsTrue(recv1.decrypt(bobHelper, bob.omemoDeviceId));
            Assert.AreEqual(TEXT_1, recv1.MESSAGE);
            Assert.IsTrue(recv1.USED_PRE_KEY);
            Assert.IsFalse(recv1.ENCRYPTED);

            // The used pre key got consumed and removed from the store (new ones only get generated below the replenish threshold):
            Assert.AreEqual(99, bob.omemoPreKeys.Count);

            // 3. Bob -> Alice (SignalMessage over the now established session):
            SignalProtocolAddress aliceAddress = new SignalProtocolAddress(alice.getIdAndDomain(), alice.omemoDeviceId);
            Assert.IsTrue(bobHelper.containsSession(aliceAddress));
            OmemoSession bobSession = new OmemoSession(alice.getIdAndDomain());
            bobSession.DEVICE_SESSIONS.Add(alice.omemoDeviceId, bobHelper.loadCipher(aliceAddress));

            const string TEXT_2 = "Hi Alice!";
            OmemoMessageMessage msg2 = new OmemoMessageMessage(bob.getIdDomainAndResource(), alice.getIdAndDomain(), TEXT_2, MessageMessage.TYPE_CHAT, true);
            msg2.encrypt(bobSession, bob.omemoDeviceId);
            Assert.IsFalse(msg2.getOmemoKey(alice.omemoDeviceId).IS_PRE_KEY);

            OmemoMessageMessage recv2 = sendAndParse(msg2);
            Assert.IsTrue(recv2.decrypt(aliceHelper, alice.omemoDeviceId));
            Assert.AreEqual(TEXT_2, recv2.MESSAGE);
            Assert.IsFalse(recv2.USED_PRE_KEY);

            // 4. A few more messages in both directions to make sure the ratchet keeps working:
            for (int i = 0; i < 5; i++)
            {
                OmemoMessageMessage m = new OmemoMessageMessage(alice.getIdDomainAndResource(), bob.getIdAndDomain(), "ping " + i, MessageMessage.TYPE_CHAT, true);
                m.encrypt(aliceSession, alice.omemoDeviceId);
                OmemoMessageMessage r = sendAndParse(m);
                Assert.IsTrue(r.decrypt(bobHelper, bob.omemoDeviceId));
                Assert.AreEqual("ping " + i, r.MESSAGE);

                m = new OmemoMessageMessage(bob.getIdDomainAndResource(), alice.getIdAndDomain(), "pong " + i, MessageMessage.TYPE_CHAT, true);
                m.encrypt(bobSession, bob.omemoDeviceId);
                r = sendAndParse(m);
                Assert.IsTrue(r.decrypt(aliceHelper, alice.omemoDeviceId));
                Assert.AreEqual("pong " + i, r.MESSAGE);
            }

            // 5. A message for a different device must get discarded:
            OmemoMessageMessage msg3 = new OmemoMessageMessage(alice.getIdDomainAndResource(), bob.getIdAndDomain(), "secret", MessageMessage.TYPE_CHAT, true);
            msg3.encrypt(aliceSession, alice.omemoDeviceId);
            OmemoMessageMessage recv3 = sendAndParse(msg3);
            Assert.IsFalse(recv3.decrypt(bobHelper, 99));
            Assert.AreEqual(OmemoMessageMessage.FALLBACK_BODY, recv3.MESSAGE);

            // Cleanup:
            alice.deleteOmemoKeysAndDevices();
            bob.deleteOmemoKeysAndDevices();
        }

        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_OmemoMessage_Tampered_1()
        {
            XMPPAccount alice = createAccount("alice", 1);
            XMPPAccount bob = createAccount("bob", 2);
            OmemoHelper aliceHelper = new XMPPConnection2(alice).OMEMO_HELPER;
            OmemoHelper bobHelper = new XMPPConnection2(bob).OMEMO_HELPER;

            SignalProtocolAddress bobAddress = aliceHelper.newSession(bob.getIdAndDomain(), bob.omemoDeviceId, bob.getOmemoBundleInformation().getRandomPreKey(bob.omemoDeviceId));
            OmemoSession aliceSession = new OmemoSession(bob.getIdAndDomain());
            aliceSession.DEVICE_SESSIONS.Add(bob.omemoDeviceId, aliceHelper.loadCipher(bobAddress));

            OmemoMessageMessage msg = new OmemoMessageMessage(alice.getIdDomainAndResource(), bob.getIdAndDomain(), "do not touch", MessageMessage.TYPE_CHAT, true);
            msg.encrypt(aliceSession, alice.omemoDeviceId);

            // Flip a byte of the payload - the GCM auth tag check has to reject it:
            string xml = msg.toXmlString();
            byte[] payload = Convert.FromBase64String(msg.BASE_64_PAYLOAD);
            payload[0] ^= 0xFF;
            xml = xml.Replace(msg.BASE_64_PAYLOAD, Convert.ToBase64String(payload));
            OmemoMessageMessage recv = new MessageParser2().parseMessages(ref xml).OfType<OmemoMessageMessage>().First();
            Assert.IsFalse(recv.decrypt(bobHelper, bob.omemoDeviceId));
            Assert.AreEqual(OmemoMessageMessage.FALLBACK_BODY, recv.MESSAGE);

            alice.deleteOmemoKeysAndDevices();
            bob.deleteOmemoKeysAndDevices();
        }
    }
}
