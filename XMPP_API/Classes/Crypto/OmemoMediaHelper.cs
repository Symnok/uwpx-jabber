using System;
using System.Text.RegularExpressions;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;

namespace XMPP_API.Classes.Crypto
{
    /// <summary>
    /// XEP-0454 (OMEMO Media Sharing): a shared file is uploaded AES-256-GCM encrypted and referenced
    /// with an 'aesgcm://' URL whose fragment carries the IV and key. This helper recognizes such URLs,
    /// extracts the download URL + key material and decrypts the downloaded ciphertext.
    ///
    /// URL shape: aesgcm://&lt;https host/path&gt;#&lt;hex(IV(12) || key(32))&gt;
    /// Downloaded file: ciphertext with the 16 byte GCM authentication tag appended.
    /// </summary>
    public static class OmemoMediaHelper
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        // aesgcm URL with a hex fragment of 88 (12+32 byte) or 96 (16+32 byte) chars:
        private static readonly Regex AESGCM_URL_REGEX = new Regex(@"^aesgcm:\/\/\S+#[0-9a-fA-F]{88}$|^aesgcm:\/\/\S+#[0-9a-fA-F]{96}$", RegexOptions.IgnoreCase);
        private const int GCM_TAG_SIZE_BYTES = 16;
        private const int AES_256_KEY_SIZE_BYTES = 32;

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        public static bool isAesGcmUrl(string msg)
        {
            return msg != null && AESGCM_URL_REGEX.IsMatch(msg.Trim());
        }

        /// <summary>
        /// Parses an 'aesgcm://' URL into its https download URL and the AES-256-GCM key/IV.
        /// </summary>
        /// <returns>True on success.</returns>
        public static bool tryParse(string aesGcmUrl, out string downloadUrl, out byte[] key, out byte[] iv)
        {
            downloadUrl = null;
            key = null;
            iv = null;
            if (!isAesGcmUrl(aesGcmUrl))
            {
                return false;
            }
            aesGcmUrl = aesGcmUrl.Trim();
            int hashIndex = aesGcmUrl.LastIndexOf('#');
            if (hashIndex < 0)
            {
                return false;
            }
            string fragment = aesGcmUrl.Substring(hashIndex + 1);
            // aesgcm:// -> https://, drop the fragment:
            downloadUrl = "https://" + aesGcmUrl.Substring("aesgcm://".Length, hashIndex - "aesgcm://".Length);

            byte[] ivAndKey = CryptoUtils.hexStringToByteArray(fragment);
            int ivLength = ivAndKey.Length - AES_256_KEY_SIZE_BYTES;
            if (ivLength <= 0)
            {
                return false;
            }
            iv = new byte[ivLength];
            key = new byte[AES_256_KEY_SIZE_BYTES];
            System.Buffer.BlockCopy(ivAndKey, 0, iv, 0, ivLength);
            System.Buffer.BlockCopy(ivAndKey, ivLength, key, 0, AES_256_KEY_SIZE_BYTES);
            return true;
        }

        /// <summary>
        /// Decrypts a downloaded XEP-0454 file (ciphertext with the GCM tag appended) using AES-256-GCM.
        /// </summary>
        public static byte[] decrypt(byte[] cipherTextWithTag, byte[] key, byte[] iv)
        {
            if (cipherTextWithTag == null || cipherTextWithTag.Length < GCM_TAG_SIZE_BYTES)
            {
                throw new ArgumentException("Encrypted media too short to contain a GCM tag.");
            }

            int cipherLength = cipherTextWithTag.Length - GCM_TAG_SIZE_BYTES;
            byte[] cipherText = new byte[cipherLength];
            byte[] tag = new byte[GCM_TAG_SIZE_BYTES];
            System.Buffer.BlockCopy(cipherTextWithTag, 0, cipherText, 0, cipherLength);
            System.Buffer.BlockCopy(cipherTextWithTag, cipherLength, tag, 0, GCM_TAG_SIZE_BYTES);

            SymmetricKeyAlgorithmProvider provider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesGcm);
            CryptographicKey cryptoKey = provider.CreateSymmetricKey(CryptographicBuffer.CreateFromByteArray(key));
            IBuffer decrypted = CryptographicEngine.DecryptAndAuthenticate(
                cryptoKey,
                CryptographicBuffer.CreateFromByteArray(cipherText),
                CryptographicBuffer.CreateFromByteArray(iv),
                CryptographicBuffer.CreateFromByteArray(tag),
                null);
            CryptographicBuffer.CopyToByteArray(decrypted, out byte[] result);
            return result;
        }


        /// <summary>
        /// Extracts the file name (last path segment, without the key fragment) from an aesgcm:// or http(s) URL.
        /// </summary>
        public static string getFileName(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return "file";
            }
            string u = url.Trim();
            int hash = u.IndexOf('#');
            if (hash >= 0)
            {
                u = u.Substring(0, hash);
            }
            int slash = u.LastIndexOf('/');
            string name = slash >= 0 ? u.Substring(slash + 1) : u;
            return string.IsNullOrEmpty(name) ? "file" : name;
        }

        public static bool isAesGcmImageUrl(string msg)
        {
            if (!isAesGcmUrl(msg))
            {
                return false;
            }
            string u = msg.Trim();
            int hash = u.LastIndexOf('#');
            string path = (hash >= 0 ? u.Substring(0, hash) : u).ToLowerInvariant();
            return path.EndsWith(".jpg") || path.EndsWith(".jpeg") || path.EndsWith(".png") || path.EndsWith(".gif") || path.EndsWith(".webp");
        }

        /// <summary>
        /// AES-256-GCM encrypts the given data for XEP-0454 sharing. Returns the ciphertext with the
        /// 16 byte GCM tag appended, and outputs a freshly generated key and IV.
        /// </summary>
        public static byte[] encrypt(byte[] data, out byte[] key, out byte[] iv)
        {
            CryptographicBuffer.CopyToByteArray(CryptographicBuffer.GenerateRandom(AES_256_KEY_SIZE_BYTES), out key);
            CryptographicBuffer.CopyToByteArray(CryptographicBuffer.GenerateRandom(12), out iv);

            SymmetricKeyAlgorithmProvider provider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesGcm);
            CryptographicKey cryptoKey = provider.CreateSymmetricKey(CryptographicBuffer.CreateFromByteArray(key));
            EncryptedAndAuthenticatedData ead = CryptographicEngine.EncryptAndAuthenticate(
                cryptoKey,
                CryptographicBuffer.CreateFromByteArray(data),
                CryptographicBuffer.CreateFromByteArray(iv),
                null);

            CryptographicBuffer.CopyToByteArray(ead.EncryptedData, out byte[] cipherText);
            CryptographicBuffer.CopyToByteArray(ead.AuthenticationTag, out byte[] tag);
            byte[] result = new byte[cipherText.Length + tag.Length];
            System.Buffer.BlockCopy(cipherText, 0, result, 0, cipherText.Length);
            System.Buffer.BlockCopy(tag, 0, result, cipherText.Length, tag.Length);
            return result;
        }

        /// <summary>
        /// Builds an 'aesgcm://' URL from an https GET URL and the IV/key used to encrypt the file.
        /// </summary>
        public static string buildAesGcmUrl(string httpsUrl, byte[] iv, byte[] key)
        {
            string body = httpsUrl;
            if (body.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                body = body.Substring("https://".Length);
            }
            else if (body.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                body = body.Substring("http://".Length);
            }
            byte[] ivKey = new byte[iv.Length + key.Length];
            System.Buffer.BlockCopy(iv, 0, ivKey, 0, iv.Length);
            System.Buffer.BlockCopy(key, 0, ivKey, iv.Length, key.Length);
            return "aesgcm://" + body + "#" + CryptoUtils.byteArrayToHexString(ivKey);
        }

        #endregion
    }
}
