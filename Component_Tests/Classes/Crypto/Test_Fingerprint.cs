using libsignal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using XMPP_API.Classes.Crypto;

namespace Component_Tests.Classes.Crypto
{
    [TestClass]
    public class Test_Fingerprint
    {
        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_FingerprintHex_KnownKey()
        {
            // Real OMEMO identity key (ogilvy@xabber.org, device 4844) from a bundle:
            byte[] serialized = Convert.FromBase64String("BSvPkdrCD1aigUzs/AqZo1LVKu1MytCW6fMyaRyT5IIR");
            IdentityKey key = new IdentityKey(serialized, 0);
            string hex = CryptoUtils.getFingerprintHex(key);
            // 32 byte curve key, DJB 0x05 prefix dropped, lowercase hex:
            Assert.AreEqual("2bcf91dac20f56a2814cecfc0a99a352d52aed4ccad096e9f332691c93e48211", hex);
            Assert.AreEqual(64, hex.Length);
        }

        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_FingerprintHex_Generated()
        {
            for (int i = 0; i < 5; i++)
            {
                IdentityKeyPair pair = CryptoUtils.generateOmemoIdentityKeyPair();
                string hex = CryptoUtils.getFingerprintHex(pair.getPublicKey());
                Assert.AreEqual(64, hex.Length, "fingerprint must be the 32 byte key as hex");
                foreach (char c in hex)
                {
                    Assert.IsTrue((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), "lowercase hex only");
                }
            }
        }

        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_FingerprintHex_Null()
        {
            Assert.IsNull(CryptoUtils.getFingerprintHex(null));
        }
    }
}
