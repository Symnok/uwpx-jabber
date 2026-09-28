using System.Text;

namespace XMPP_API.Classes
{
    /// <summary>
    /// Emoji detection / removal. Used to keep emoji out of MUC (conference) chats:
    /// emoji are stripped from text that is typed, sent or displayed there, and
    /// messages that are nothing but emoji (e.g. reactions) are hidden entirely.
    /// </summary>
    public static class EmojiUtils
    {
        //--------------------------------------------------------Attributes:-----------------------------------------------------------------\\
        #region --Attributes--
        private const int ZWJ = 0x200D;

        #endregion
        //--------------------------------------------------------Misc Methods:---------------------------------------------------------------\\
        #region --Misc Methods (Public)--
        /// <summary>
        /// True if the given Unicode code point is an emoji or an emoji building block
        /// (variation selector, keycap, skin tone modifier, tag character, ...).
        /// </summary>
        public static bool isEmojiCodePoint(int cp)
        {
            return (cp >= 0x1F000 && cp <= 0x1FAFF)     // Mahjong, cards, enclosed, pictographs, emoticons, transport, supplemental symbols, flags, skin tones
                || (cp >= 0x2600 && cp <= 0x27BF)       // Misc symbols + dingbats
                || (cp >= 0xE0020 && cp <= 0xE007F)     // Tag characters (subdivision flags)
                || (cp >= 0xFE00 && cp <= 0xFE0F)       // Variation selectors
                || cp == 0x20E3                         // Combining enclosing keycap
                || cp == 0x203C || cp == 0x2049         // ‼ ⁉
                || cp == 0x2139                         // ℹ
                || (cp >= 0x2194 && cp <= 0x2199) || cp == 0x21A9 || cp == 0x21AA
                || cp == 0x231A || cp == 0x231B || cp == 0x2328 || cp == 0x23CF
                || (cp >= 0x23E9 && cp <= 0x23F3) || (cp >= 0x23F8 && cp <= 0x23FA)
                || cp == 0x24C2
                || cp == 0x25AA || cp == 0x25AB || cp == 0x25B6 || cp == 0x25C0 || (cp >= 0x25FB && cp <= 0x25FE)
                || cp == 0x2934 || cp == 0x2935
                || (cp >= 0x2B05 && cp <= 0x2B07) || cp == 0x2B1B || cp == 0x2B1C || cp == 0x2B50 || cp == 0x2B55
                || cp == 0x3030 || cp == 0x303D || cp == 0x3297 || cp == 0x3299;
        }

        /// <summary>True if the text contains at least one emoji.</summary>
        public static bool containsEmoji(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                int cp = readCodePoint(text, i, out int len);
                if (isEmojiCodePoint(cp))
                {
                    return true;
                }
                i += len - 1;
            }
            return false;
        }

        /// <summary>
        /// Returns the text with all emoji removed. Zero width joiners that glue emoji
        /// sequences together are removed as well; ZWJs between normal letters stay.
        /// </summary>
        public static string removeEmoji(string text)
        {
            if (string.IsNullOrEmpty(text) || !containsEmoji(text))
            {
                return text;
            }

            StringBuilder sb = new StringBuilder(text.Length);
            bool prevWasEmoji = false;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = readCodePoint(text, i, out int len);
                if (isEmojiCodePoint(cp))
                {
                    prevWasEmoji = true;
                }
                else if (cp == ZWJ && (prevWasEmoji || nextIsEmoji(text, i + len)))
                {
                    prevWasEmoji = true;
                }
                else
                {
                    sb.Append(text, i, len);
                    prevWasEmoji = false;
                }
                i += len - 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// True if the text has at least one emoji and nothing else but whitespace.
        /// </summary>
        public static bool isEmojiOnly(string text)
        {
            return containsEmoji(text) && string.IsNullOrWhiteSpace(removeEmoji(text));
        }

        /// <summary>
        /// True if a MUC message body should not be shown at all: the message itself
        /// (ignoring any XEP-0461 "&gt; ..." quote lines of a reply) consists only of emoji.
        /// </summary>
        public static bool isEmojiOnlyMessage(string body)
        {
            return isEmojiOnly(stripQuoteLines(body));
        }

        #endregion

        #region --Misc Methods (Private)--
        private static int readCodePoint(string text, int index, out int length)
        {
            char c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                length = 2;
                return char.ConvertToUtf32(c, text[index + 1]);
            }
            length = 1;
            return c;
        }

        private static bool nextIsEmoji(string text, int index)
        {
            return index < text.Length && isEmojiCodePoint(readCodePoint(text, index, out int _));
        }

        /// <summary>The body with XEP-0461 quote lines ("&gt; ...") removed.</summary>
        private static string stripQuoteLines(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }
            string[] lines = message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder sb = new StringBuilder();
            foreach (string line in lines)
            {
                if (line.TrimStart().StartsWith(">"))
                {
                    continue;
                }
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        #endregion
    }
}
