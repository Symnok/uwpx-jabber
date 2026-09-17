using libsignal;
using libsignal.protocol;
using Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using XMPP_API.Classes.Crypto;
using XMPP_API.Classes.Network.XML.Messages.XEP_0334;
using XMPP_API.Classes.Network.XML.Messages.XEP_0384.Signal.Session;

namespace XMPP_API.Classes.Network.XML.Messages.XEP_0384
{
    public class OmemoMessageMessage : MessageMessage
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        public uint SOURCE_DEVICE_ID { get; private set; }

        public IList<OmemoKey> KEYS { get; private set; }

        public string BASE_64_IV { get; private set; }
        public string BASE_64_PAYLOAD { get; private set; }
        public bool ENCRYPTED { get; private set; }
        /// <summary>
        /// True if the message got decrypted from a PreKeySignalMessage, i.e. the sender used one of our published pre keys
        /// to establish a new session. The used pre key has to be replaced and the bundle republished.
        /// </summary>
        public bool USED_PRE_KEY { get; private set; }

        /// <summary>
        /// Fallback body for clients that don't support OMEMO (XEP-0384 section 4.6).
        /// </summary>
        public const string FALLBACK_BODY = "I sent you an OMEMO encrypted message but your client doesn't seem to support that. Find more information on https://conversations.im/omemo";

        private const int AES_KEY_SIZE_BYTES = 16;
        private const int AES_AUTH_TAG_SIZE_BYTES = 16;

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        /// <summary>
        /// Basic Constructor
        /// </summary>
        /// <history>
        /// 08/08/2018 Created [Fabian Sauter]
        /// </history>
        public OmemoMessageMessage(string from, string to, string message, string type, bool reciptRequested) : base(from, to, message, type, reciptRequested)
        {
            this.includeBody = false;
            this.KEYS = new List<OmemoKey>();
        }

        public OmemoMessageMessage(XmlNode node, CarbonCopyType ccType) : base(node, ccType)
        {
            this.KEYS = new List<OmemoKey>();
            // The parsed <body/> (if any) is only the fallback text for clients without OMEMO support.
            // It gets replaced with the decrypted text once decrypt(...) succeeded:
            this.ENCRYPTED = true;
            XmlNode encryptedNode = XMLUtils.getChildNode(node, "encrypted", Consts.XML_XMLNS, Consts.XML_XEP_0384_NAMESPACE);
            if (encryptedNode != null)
            {
                XmlNode headerNode = XMLUtils.getChildNode(encryptedNode, "header");
                if (headerNode != null)
                {
                    if (uint.TryParse(headerNode.Attributes["sid"]?.Value, out uint sid))
                    {
                        SOURCE_DEVICE_ID = sid;
                    }

                    foreach (XmlNode n in headerNode.ChildNodes)
                    {
                        switch (n.Name)
                        {
                            case "key":
                                KEYS.Add(new OmemoKey(n));
                                break;

                            case "iv":
                                this.BASE_64_IV = n.InnerText?.Trim();
                                break;

                            default:
                                break;
                        }
                    }
                }

                XmlNode payloadNode = XMLUtils.getChildNode(encryptedNode, "payload");
                if (payloadNode != null)
                {
                    this.BASE_64_PAYLOAD = payloadNode.InnerText?.Trim();
                }
            }
        }

        #endregion
        //--------------------------------------------------------Set-, Get- Methods:---------------------------------------------------------\\
        #region --Set-, Get- Methods--
        public OmemoKey getOmemoKey(uint deviceId)
        {
            foreach (OmemoKey key in KEYS)
            {
                if (key.REMOTE_DEVICE_ID == deviceId)
                {
                    return key;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns true if the message contains an encrypted payload.
        /// Messages without a payload are key transport messages (XEP-0384 section 4.7) which are used to build/heal sessions only.
        /// </summary>
        public bool hasPayload()
        {
            return !string.IsNullOrEmpty(BASE_64_PAYLOAD);
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        /// <summary>
        /// Encrypts the content of MESSAGE with the given SessionCipher and saves the result in BASE_64_PAYLOAD.
        /// </summary>
        /// <param name="omemoSession">A storage object containing all SessionCipher for the target OMEMO devices.</param>
        /// <param name="sourceDeviceId">The sender OMEMO device id.</param>
        public void encrypt(OmemoSession omemoSession, uint sourceDeviceId)
        {
            SOURCE_DEVICE_ID = sourceDeviceId;

            // 1. Generate a new AES-128 GCM key/iv:
            Aes128GcmCpp aes128Gcm = new Aes128GcmCpp();
            aes128Gcm.generateKey();
            aes128Gcm.generateIv();

            // 2. Encrypt the message using the Aes128Gcm instance.
            // XEP-0384 requires the plaintext to be UTF-8 encoded:
            byte[] encryptedData = aes128Gcm.encrypt(Encoding.UTF8.GetBytes(MESSAGE ?? ""));
            BASE_64_PAYLOAD = Convert.ToBase64String(encryptedData);
            BASE_64_IV = Convert.ToBase64String(aes128Gcm.iv);

            // 3. Concatenate key and authentication tag (XEP-0384 v0.3: key || tag gets encrypted for each device):
            byte[] keyAuthTag = new byte[aes128Gcm.authTag.Length + aes128Gcm.key.Length];
            Buffer.BlockCopy(aes128Gcm.key, 0, keyAuthTag, 0, aes128Gcm.key.Length);
            Buffer.BlockCopy(aes128Gcm.authTag, 0, keyAuthTag, aes128Gcm.key.Length, aes128Gcm.authTag.Length);

            // 4. Encrypt the key/authTag pair with libsignal for each deviceId:
            KEYS = new List<OmemoKey>();
            CiphertextMessage ciphertextMessage;
            foreach (KeyValuePair<uint, SessionCipher> pair in omemoSession.DEVICE_SESSIONS)
            {
                ciphertextMessage = pair.Value.encrypt(keyAuthTag);
                // Create a new OmemoKey object with the target device id, whether it's the first time the session got established and the encrypted key:
                OmemoKey key = new OmemoKey(pair.Key, ciphertextMessage is PreKeySignalMessage, Convert.ToBase64String(ciphertextMessage.serialize()));
                KEYS.Add(key);
            }
            ENCRYPTED = true;
        }

        /// <summary>
        /// Decrypts the content of BASE_64_PAYLOAD with the given SessionCipher and saves the result in MESSAGE.
        /// Sets ENCRYPTED to false.
        /// </summary>
        /// <param name="omemoHelper">The OmemoHelper of the account that received the message.</param>
        /// <param name="localOmemoDeviceId">The OMEMO device id of the local account.</param>
        /// <returns>True if the message contains a payload and it got decrypted successfully.</returns>
        public bool decrypt(OmemoHelper omemoHelper, uint localOmemoDeviceId)
        {
            try
            {
                // 1. Check if the message contains a key for the local device:
                OmemoKey key = getOmemoKey(localOmemoDeviceId);
                if (key == null)
                {
                    Logger.Info("Discarded received OMEMO message - doesn't contain device id!");
                    return false;
                }

                // 2. Load the cipher and decrypt key || auth tag:
                SignalProtocolAddress address = new SignalProtocolAddress(Utils.getBareJidFromFullJid(FROM), SOURCE_DEVICE_ID);
                SessionCipher cipher = omemoHelper.loadCipher(address);
                byte[] encryptedKeyAuthTag = Convert.FromBase64String(key.BASE_64_KEY);
                byte[] decryptedKeyAuthTag = null;
                if (key.IS_PRE_KEY)
                {
                    decryptedKeyAuthTag = cipher.decrypt(new PreKeySignalMessage(encryptedKeyAuthTag));
                    // The sender consumed one of our pre keys. Replace it and republish the bundle:
                    USED_PRE_KEY = true;
                    omemoHelper.onPreKeyMessageReceived();
                }
                else
                {
                    decryptedKeyAuthTag = cipher.decrypt(new SignalMessage(encryptedKeyAuthTag));
                }

                // 3. Check if the cipher got loaded successfully:
                if (decryptedKeyAuthTag == null)
                {
                    Logger.Info("Discarded received OMEMO message - failed to decrypt keyAuthTag is null!");
                    return false;
                }

                // 4. Key transport message (no payload) - the session got established/healed, nothing to show:
                if (!hasPayload())
                {
                    Logger.Info("Received OMEMO key transport message from: " + address.getName() + ':' + address.getDeviceId());
                    ENCRYPTED = false;
                    return false;
                }

                // 5. Split key and auth tag. Legacy (XEP-0384 < v0.3) clients did not append the auth tag to the key,
                // but appended it to the payload instead:
                if (decryptedKeyAuthTag.Length < AES_KEY_SIZE_BYTES)
                {
                    Logger.Info("Discarded received OMEMO message - invalid key length: " + decryptedKeyAuthTag.Length);
                    return false;
                }
                byte[] encryptedData = Convert.FromBase64String(BASE_64_PAYLOAD);
                byte[] aesKey = new byte[AES_KEY_SIZE_BYTES];
                Buffer.BlockCopy(decryptedKeyAuthTag, 0, aesKey, 0, aesKey.Length);
                byte[] aesAuthTag;
                if (decryptedKeyAuthTag.Length > AES_KEY_SIZE_BYTES)
                {
                    aesAuthTag = new byte[decryptedKeyAuthTag.Length - AES_KEY_SIZE_BYTES];
                    Buffer.BlockCopy(decryptedKeyAuthTag, AES_KEY_SIZE_BYTES, aesAuthTag, 0, aesAuthTag.Length);
                }
                else
                {
                    if (encryptedData.Length < AES_AUTH_TAG_SIZE_BYTES)
                    {
                        Logger.Info("Discarded received OMEMO message - payload too short for an auth tag: " + encryptedData.Length);
                        return false;
                    }
                    aesAuthTag = new byte[AES_AUTH_TAG_SIZE_BYTES];
                    byte[] payloadOnly = new byte[encryptedData.Length - AES_AUTH_TAG_SIZE_BYTES];
                    Buffer.BlockCopy(encryptedData, payloadOnly.Length, aesAuthTag, 0, aesAuthTag.Length);
                    Buffer.BlockCopy(encryptedData, 0, payloadOnly, 0, payloadOnly.Length);
                    encryptedData = payloadOnly;
                }

                // 6. Decrypt the payload:
                byte[] aesIv = Convert.FromBase64String(BASE_64_IV);
                Aes128GcmCpp aes128Gcm = new Aes128GcmCpp()
                {
                    key = aesKey,
                    authTag = aesAuthTag,
                    iv = aesIv
                };
                byte[] decryptedData = aes128Gcm.decrypt(encryptedData);

                // 7. Convert decrypted data to an UTF-8 string:
                MESSAGE = Encoding.UTF8.GetString(decryptedData);

                ENCRYPTED = false;
                return true;
            }
            catch (Exception e)
            {
                Logger.Error("Discarded received OMEMO message from " + FROM + ':' + SOURCE_DEVICE_ID + " - failed to decrypt with: " + e.Message, e);
            }
            return false;
        }

        public override XElement toXElement()
        {
            if (!ENCRYPTED)
            {
                throw new InvalidOperationException("Message not encrypted! Call encrypt(...) first.");
            }

            XElement msgNode = base.toXElement();

            XNamespace ns = Consts.XML_XEP_0384_NAMESPACE;
            XElement encNode = new XElement(ns + "encrypted");

            XElement headerNode = new XElement(ns + "header");
            headerNode.Add(new XAttribute("sid", SOURCE_DEVICE_ID));

            foreach (OmemoKey key in KEYS)
            {
                headerNode.Add(key.toXElement(ns));
            }

            headerNode.Add(new XElement(ns + "iv")
            {
                Value = BASE_64_IV
            });
            encNode.Add(headerNode);

            if (hasPayload())
            {
                encNode.Add(new XElement(ns + "payload")
                {
                    Value = BASE_64_PAYLOAD
                });
            }
            msgNode.Add(encNode);

            // XEP-0380 (Explicit Message Encryption):
            XNamespace emeNs = Consts.XML_XEP_0380_NAMESPACE;
            XElement emeNode = new XElement(emeNs + "encryption");
            emeNode.Add(new XAttribute("namespace", Consts.XML_XEP_0384_NAMESPACE));
            emeNode.Add(new XAttribute("name", Consts.XML_XEP_0380_OMEMO_NAME));
            msgNode.Add(emeNode);

            // Fallback body for clients without OMEMO support:
            if (hasPayload())
            {
                msgNode.Add(new XElement("body", FALLBACK_BODY));
            }

            addMPHints(msgNode, new List<MessageProcessingHint>() { MessageProcessingHint.STORE });

            return msgNode;
        }

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
