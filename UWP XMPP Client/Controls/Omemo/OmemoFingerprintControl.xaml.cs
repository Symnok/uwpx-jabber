using System.Text.RegularExpressions;
using UWP_XMPP_Client.Classes;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UWP_XMPP_Client.Controls.Omemo
{
    public sealed partial class OmemoFingerprintControl : UserControl
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        /// <summary>
        /// The OMEMO fingerprint as lowercase hex (the 32 byte identity key, 64 hex chars), matching the
        /// format shown by Conversations, Dino, monocles, ... - not the Signal numeric safety number.
        /// </summary>
        public string FingerprintHex
        {
            get { return (string)GetValue(FingerprintHexProperty); }
            set
            {
                SetValue(FingerprintHexProperty, value);
                showFingerprint();
            }
        }
        public static readonly DependencyProperty FingerprintHexProperty = DependencyProperty.Register(nameof(FingerprintHex), typeof(string), typeof(OmemoFingerprintControl), new PropertyMetadata(null));

        #endregion
        //--------------------------------------------------------Constructor:----------------------------------------------------------------\\
        #region --Constructors--
        public OmemoFingerprintControl()
        {
            this.InitializeComponent();
        }

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Private)--
        private void showFingerprint()
        {
            if (!string.IsNullOrEmpty(FingerprintHex))
            {
                // Group into blocks of 8 like other OMEMO clients:
                fingerprint_tbx.Text = Regex.Replace(FingerprintHex, ".{8}", "$0 ").Trim();
                fingerprintQRCode_qrcc.QRCodeText = FingerprintHex;
                cpyFingerprint_btn.IsEnabled = true;
            }
            else
            {
                fingerprint_tbx.Text = "Error! No fingerprint available.";
                fingerprintQRCode_qrcc.QRCodeText = null;
                cpyFingerprint_btn.IsEnabled = false;
            }
        }

        #endregion
        //--------------------------------------------------------Events:---------------------------------------------------------------------\\
        #region --Events--
        private void cpyFingerprint_btn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(FingerprintHex))
            {
                UiUtils.addTextToClipboard(FingerprintHex);
            }
        }

        #endregion
    }
}
