using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using XMPP_API.Classes.Crypto;

namespace Component_Tests.Classes.Crypto
{
    [TestClass]
    public class Test_OmemoMediaHelper
    {
        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_AesGcm_EncryptDecrypt_RoundTrip()
        {
            Random r = new Random(42);
            for (int i = 0; i < 20; i++)
            {
                byte[] data = new byte[r.Next(1, 5000)];
                r.NextBytes(data);
                byte[] cipher = OmemoMediaHelper.encrypt(data, out byte[] key, out byte[] iv);
                Assert.AreEqual(32, key.Length);
                Assert.AreEqual(12, iv.Length);
                Assert.AreEqual(data.Length + 16, cipher.Length, "cipher must be plaintext + 16 byte GCM tag");
                byte[] plain = OmemoMediaHelper.decrypt(cipher, key, iv);
                Assert.IsTrue(data.SequenceEqual(plain));
            }
        }

        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_AesGcm_TamperedRejected()
        {
            byte[] data = new byte[100];
            new Random(1).NextBytes(data);
            byte[] cipher = OmemoMediaHelper.encrypt(data, out byte[] key, out byte[] iv);
            cipher[0] ^= 0xFF; // flip a ciphertext byte -> GCM tag check must fail
            Assert.ThrowsException<Exception>(() => OmemoMediaHelper.decrypt(cipher, key, iv), "tampered data must not decrypt");
        }

        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_AesGcmUrl_BuildParse_RoundTrip()
        {
            byte[] key = new byte[32];
            byte[] iv = new byte[12];
            new Random(7).NextBytes(key);
            new Random(8).NextBytes(iv);
            string url = OmemoMediaHelper.buildAesGcmUrl("https://upload.example.org/abc/def/pic.jpg", iv, key);
            Assert.IsTrue(url.StartsWith("aesgcm://upload.example.org/abc/def/pic.jpg#"));
            Assert.IsTrue(OmemoMediaHelper.isAesGcmUrl(url));
            Assert.IsTrue(OmemoMediaHelper.isAesGcmImageUrl(url));

            Assert.IsTrue(OmemoMediaHelper.tryParse(url, out string dl, out byte[] key2, out byte[] iv2));
            Assert.AreEqual("https://upload.example.org/abc/def/pic.jpg", dl);
            Assert.IsTrue(key.SequenceEqual(key2));
            Assert.IsTrue(iv.SequenceEqual(iv2));
        }

        [TestCategory("Crypto")]
        [TestMethod]
        public void Test_AesGcmUrl_Detection()
        {
            // real-shaped url from Conversations (88 hex fragment = 12 iv + 32 key):
            string real = "aesgcm://upload02.xabber.org/x/y/z.jpg#7ea33f13dc4172de3d09a62e6b3fb78ab190c569b856c556a21a66b84eb5c1a68a01e691c0d3c752efe5266f";
            Assert.IsTrue(OmemoMediaHelper.isAesGcmUrl(real));
            Assert.IsTrue(OmemoMediaHelper.isAesGcmImageUrl(real));
            Assert.IsFalse(OmemoMediaHelper.isAesGcmImageUrl("https://example.org/a.jpg"));
            Assert.IsFalse(OmemoMediaHelper.isAesGcmUrl("aesgcm://example.org/a.jpg"), "missing key fragment");
            // non-image file over aesgcm is a valid aesgcm url but not an image:
            string pdf = "aesgcm://example.org/a.pdf#7ea33f13dc4172de3d09a62e6b3fb78ab190c569b856c556a21a66b84eb5c1a68a01e691c0d3c752efe5266f";
            Assert.IsTrue(OmemoMediaHelper.isAesGcmUrl(pdf));
            Assert.IsFalse(OmemoMediaHelper.isAesGcmImageUrl(pdf));
        }
    }
}
