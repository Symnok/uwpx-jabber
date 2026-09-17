using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using XMPP_API.Classes;
using XMPP_API.Classes.Crypto;
using XMPP_API.Classes.Network.XML.Messages.XEP_0115;

namespace Component_Tests.Classes.XmppMessages
{
    [TestClass]
    public class Test_EntityCapabilities
    {
        [TestMethod]
        public void Test_VerificationHash_MatchesSpecExample()
        {
            // XEP-0115 section 5.2 canonical example. Verifies the SHA-1/base64 pipeline used for our own hash:
            string s = "client/pc//Exodus 0.9.1<" +
                       "http://jabber.org/protocol/caps<" +
                       "http://jabber.org/protocol/disco#info<" +
                       "http://jabber.org/protocol/disco#items<" +
                       "http://jabber.org/protocol/muc<";
            string ver = Convert.ToBase64String(CryptoUtils.SHA_1(Encoding.UTF8.GetBytes(s)));
            Assert.AreEqual("QgayPKawpkPSDYmwT/WM94uAlu0=", ver);
        }

        [TestMethod]
        public void Test_OwnCaps_AdvertiseOmemoNotify()
        {
            // The device list +notify feature must be advertised, otherwise the server never pushes contacts' device lists:
            Assert.IsTrue(EntityCapabilities.FEATURES.Contains(Consts.XML_XEP_0384_DEVICE_LIST_NODE_NOTIFY));

            XElement query = EntityCapabilities.getDiscoInfoQuery("node#ver");
            XNamespace ns = Consts.XML_XEP_0030_INFO_NAMESPACE;
            Assert.AreEqual("node#ver", query.Attribute("node")?.Value);
            Assert.AreEqual(1, query.Elements(ns + "identity").Count());

            var featureVars = query.Elements(ns + "feature").Select(f => f.Attribute("var")?.Value).ToList();
            Assert.IsTrue(featureVars.Contains(Consts.XML_XEP_0384_DEVICE_LIST_NODE_NOTIFY));
            // Every advertised feature is reflected in the hash input, so counts must match:
            Assert.AreEqual(EntityCapabilities.FEATURES.Count, featureVars.Count);
        }

        [TestMethod]
        public void Test_OwnHash_RecomputesConsistently()
        {
            // Recompute the ver hash independently from the served identity + features and compare:
            var sortedFeatures = EntityCapabilities.FEATURES.OrderBy(f => f, StringComparer.Ordinal).ToList();
            StringBuilder s = new StringBuilder("client/phone//UWPX Jabber<");
            foreach (string f in sortedFeatures)
            {
                s.Append(f).Append('<');
            }
            string expected = Convert.ToBase64String(CryptoUtils.SHA_1(Encoding.UTF8.GetBytes(s.ToString())));
            Assert.AreEqual(expected, EntityCapabilities.VERIFICATION_HASH);
        }

        [TestMethod]
        public void Test_CapsElement_HasHashAndNode()
        {
            XElement c = EntityCapabilities.getCapsElement();
            Assert.AreEqual("c", c.Name.LocalName);
            Assert.AreEqual(Consts.XML_XEP_0115_NAMESPACE, c.Name.NamespaceName);
            Assert.AreEqual("sha-1", c.Attribute("hash")?.Value);
            Assert.AreEqual(EntityCapabilities.NODE, c.Attribute("node")?.Value);
            Assert.AreEqual(EntityCapabilities.VERIFICATION_HASH, c.Attribute("ver")?.Value);
        }
    }
}
