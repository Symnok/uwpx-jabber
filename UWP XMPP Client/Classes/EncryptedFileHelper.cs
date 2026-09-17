using Logging;
using System;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.Web.Http;
using UWP_XMPP_Client.Dialogs;
using XMPP_API.Classes.Crypto;

namespace UWP_XMPP_Client.Classes
{
    /// <summary>
    /// Handles XEP-0454 (OMEMO Media Sharing) files that are not displayed inline (anything but images):
    /// downloads the ciphertext, AES-256-GCM decrypts it and hands the plaintext to the OS to open with
    /// the default app for that file type. Used for received encrypted videos, documents, audio, ...
    /// </summary>
    public static class EncryptedFileHelper
    {
        #region --Misc Methods (Public)--
        /// <summary>
        /// Downloads, decrypts and opens the given aesgcm:// file. Shows a dialog on failure.
        /// </summary>
        public static async Task openAsync(string aesGcmUrl)
        {
            if (!OmemoMediaHelper.tryParse(aesGcmUrl, out string downloadUrl, out byte[] key, out byte[] iv))
            {
                await showErrorAsync("This encrypted file link is invalid.");
                return;
            }

            try
            {
                byte[] plain = await downloadAndDecryptAsync(downloadUrl, key, iv);
                string name = OmemoMediaHelper.getFileName(aesGcmUrl);
                StorageFile file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(file, plain);

                bool launched = await Launcher.LaunchFileAsync(file);
                if (!launched)
                {
                    await showErrorAsync("No installed app can open this file type.");
                }
            }
            catch (Exception e)
            {
                Logger.Error("Failed to open encrypted file from: " + downloadUrl, e);
                await showErrorAsync("Failed to open the encrypted file: " + e.Message);
            }
        }

        #endregion

        #region --Misc Methods (Private)--
        private static async Task<byte[]> downloadAndDecryptAsync(string url, byte[] key, byte[] iv)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                IBuffer buffer = await httpClient.GetBufferAsync(new Uri(url));
                CryptographicBuffer.CopyToByteArray(buffer, out byte[] cipher);
                return OmemoMediaHelper.decrypt(cipher, key, iv);
            }
        }

        private static async Task showErrorAsync(string message)
        {
            await UiUtils.showDialogAsyncQueue(new TextDialog(message, "Encrypted file"));
        }

        #endregion
    }
}
