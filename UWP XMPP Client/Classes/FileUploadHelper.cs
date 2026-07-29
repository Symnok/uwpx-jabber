using Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using XMPP_API.Classes;
using XMPP_API.Classes.Network;
using XMPP_API.Classes.Network.XML.Messages;
using XMPP_API.Classes.Network.XML.Messages.XEP_0030;
using XMPP_API.Classes.Network.XML.Messages.XEP_0363;

namespace UWP_XMPP_Client.Classes
{
    public class FileUploadResult
    {
        /// <summary>The GET url to send in the message. Null when UPLOAD failed.</summary>
        public string url;
        /// <summary>Null on success, otherwise something to show the user.</summary>
        public string error;
    }

    /// <summary>
    /// Sharing a file over XMPP (XEP-0363): ask the server's upload component for a
    /// slot, PUT the bytes over HTTPS, then send the resulting URL as an ordinary
    /// message. There is no in-band file transfer worth having, and this is the same
    /// shape as the links other clients send us - ChatMessageTable already recognises
    /// an image URL and the existing download pipeline renders it.
    /// </summary>
    public static class FileUploadHelper
    {
        private const int REQUEST_TIMEOUT_SEC = 15;

        /// <summary>
        /// Upload component per account. Discovery is several round trips and the
        /// component does not move while we are connected.
        /// </summary>
        private static readonly Dictionary<string, string> UPLOAD_SERVICES =
            new Dictionary<string, string>();

        #region --Misc Methods (Public)--
        /// <summary>
        /// Uploads the file and returns the URL to send. Never throws - failures come
        /// back in <see cref="FileUploadResult.error"/>.
        /// </summary>
        public static async Task<FileUploadResult> uploadAsync(XMPPClient client, StorageFile file)
        {
            FileUploadResult result = new FileUploadResult();
            if (client == null || file == null)
            {
                result.error = "Nothing to upload.";
                return result;
            }

            try
            {
                string service = await findUploadServiceAsync(client);
                if (string.IsNullOrEmpty(service))
                {
                    result.error = "This server does not offer file upload (XEP-0363).";
                    return result;
                }

                BasicProperties properties = await file.GetBasicPropertiesAsync();
                HTTPUploadSlot slot = await requestSlotAsync(client, service, file, properties.Size);
                if (slot == null)
                {
                    result.error = "The server refused the upload. The file may be too large.";
                    return result;
                }

                string uploadError = await putAsync(slot, file);
                if (uploadError != null)
                {
                    result.error = uploadError;
                    return result;
                }

                result.url = slot.URL_GET;
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("File upload failed.", ex);
                result.error = "Upload failed: " + ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Forgets the cached upload component. Call when an account reconnects to a
        /// different server.
        /// </summary>
        public static void clearCache()
        {
            lock (UPLOAD_SERVICES)
            {
                UPLOAD_SERVICES.Clear();
            }
        }

        #endregion

        #region --Misc Methods (Private)--
        private static async Task<string> findUploadServiceAsync(XMPPClient client)
        {
            XMPPAccount account = client.getXMPPAccount();
            string accountId = account.getIdAndDomain();

            lock (UPLOAD_SERVICES)
            {
                string cached;
                if (UPLOAD_SERVICES.TryGetValue(accountId, out cached))
                {
                    return cached;
                }
            }

            string from = account.getIdDomainAndResource();
            string domain = account.user.domain;

            // Some servers advertise the feature on the domain itself rather than on
            // a separate component, so check there before enumerating items.
            string service = null;
            if (await supportsUploadAsync(client, from, domain))
            {
                service = domain;
            }
            else
            {
                IQMessage response = await requestAsync(client,
                    new DiscoRequestMessage(from, domain, DiscoType.ITEMS));

                DiscoResponseMessage disco = response as DiscoResponseMessage;
                if (disco != null && disco.ITEMS != null)
                {
                    foreach (DiscoItem item in disco.ITEMS)
                    {
                        if (string.IsNullOrEmpty(item.JID))
                        {
                            continue;
                        }
                        if (await supportsUploadAsync(client, from, item.JID))
                        {
                            service = item.JID;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(service))
            {
                Logger.Info("Found HTTP upload component: " + service);
                lock (UPLOAD_SERVICES)
                {
                    UPLOAD_SERVICES[accountId] = service;
                }
            }
            else
            {
                Logger.Warn("No HTTP upload component found for " + accountId + '.');
            }
            return service;
        }

        private static async Task<bool> supportsUploadAsync(XMPPClient client, string from,
            string target)
        {
            IQMessage response = await requestAsync(client,
                new DiscoRequestMessage(from, target, DiscoType.INFO));

            DiscoResponseMessage disco = response as DiscoResponseMessage;
            if (disco == null || disco.FEATURES == null)
            {
                return false;
            }

            foreach (DiscoFeature feature in disco.FEATURES)
            {
                if (string.Equals(feature.VAR, Consts.XML_XEP_0363_NAMESPACE))
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task<HTTPUploadSlot> requestSlotAsync(XMPPClient client,
            string service, StorageFile file, ulong size)
        {
            string from = client.getXMPPAccount().getIdDomainAndResource();
            string contentType = string.IsNullOrEmpty(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

            HTTPUploadRequestSlotMessage request = new HTTPUploadRequestSlotMessage(
                from, service, file.Name, contentType, (uint)size);

            IQMessage response = await requestAsync(client, request);

            HTTPUploadErrorMessage error = response as HTTPUploadErrorMessage;
            if (error != null)
            {
                Logger.Warn("Upload slot refused: " + error.ToString());
                return null;
            }

            HTTPUploadResponseSlotMessage slotResponse = response as HTTPUploadResponseSlotMessage;
            if (slotResponse == null || slotResponse.SLOT == null ||
                string.IsNullOrEmpty(slotResponse.SLOT.URL_PUT) ||
                string.IsNullOrEmpty(slotResponse.SLOT.URL_GET))
            {
                return null;
            }
            return slotResponse.SLOT;
        }

        /// <summary>
        /// Wraps the callback-based MessageResponseHelper in a Task. Returns null on
        /// timeout.
        /// </summary>
        private static async Task<IQMessage> requestAsync(XMPPClient client, IQMessage message)
        {
            TaskCompletionSource<IQMessage> completion = new TaskCompletionSource<IQMessage>();

            MessageResponseHelper<IQMessage> helper = new MessageResponseHelper<IQMessage>(
                client,
                (IQMessage response) =>
                {
                    completion.TrySetResult(response);
                    return true;
                },
                () =>
                {
                    completion.TrySetResult(null);
                });
            helper.timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SEC);

            try
            {
                helper.start(message);
                return await completion.Task;
            }
            finally
            {
                helper.Dispose();
            }
        }

        private static async Task<string> putAsync(HTTPUploadSlot slot, StorageFile file)
        {
            Uri target;
            if (!Uri.TryCreate(slot.URL_PUT, UriKind.Absolute, out target))
            {
                return "The server returned an unusable upload address.";
            }

            using (HttpClient httpClient = new HttpClient())
            using (IRandomAccessStreamWithContentType stream = await file.OpenReadAsync())
            {
                foreach (KeyValuePair<string, string> header in slot.HEADERS)
                {
                    try
                    {
                        httpClient.DefaultRequestHeaders.Append(header.Key, header.Value);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Skipping upload header " + header.Key + ": " + ex.Message);
                    }
                }

                HttpStreamContent content = new HttpStreamContent(stream);
                if (!string.IsNullOrEmpty(file.ContentType))
                {
                    try
                    {
                        content.Headers.ContentType = new HttpMediaTypeHeaderValue(file.ContentType);
                    }
                    catch (Exception)
                    {
                        // An odd content type is not worth failing the upload over.
                    }
                }

                HttpResponseMessage response = await httpClient.PutAsync(target, content);
                if (!response.IsSuccessStatusCode)
                {
                    return "Upload rejected by the server: " + (int)response.StatusCode + ' ' +
                           response.ReasonPhrase;
                }
            }
            return null;
        }

        #endregion
    }
}
