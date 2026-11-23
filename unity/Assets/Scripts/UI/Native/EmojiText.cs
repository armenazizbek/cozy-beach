using System.Collections.Generic;
using System.Text;
using UnityEngine.TextCore.Text;

/// <summary>
/// Puts the colour emoji from Resources/UI/Emoji (Microsoft's Fluent Emoji) into text that reaches the screen.
///
/// Each emoji the atlas has becomes an explicit &lt;sprite&gt; tag, so it is always drawn in colour. Left to Unity's own emoji
/// detection, emoji that default to text style (⚙ ☀ ♻ ✉) could still come from the plain symbol font as black outlines.
/// The emoji-style and text-style selectors (U+FE0F, U+FE0E) are dropped: browsers draw nothing for them, but Unity's text
/// engine gave them width, which pushed emoji such as 🏝️ off-centre.
/// </summary>
internal static class EmojiText
{
    private static HashSet<uint> known;

    internal static void Use(SpriteAsset emoji)
    {
        known = new HashSet<uint>();
        if (emoji == null) return;
        foreach (var c in emoji.spriteCharacterTable) known.Add(c.unicode);
    }

    internal static string Apply(string s)
    {
        if (known == null || known.Count == 0 || string.IsNullOrEmpty(s) || !MayHaveEmoji(s)) return s;
        var sb = new StringBuilder(s.Length + 32);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            uint cp = c;
            int len = 1;
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = (uint)char.ConvertToUtf32(c, s[i + 1]);
                len = 2;
            }
            if (cp == 0xFE0F || cp == 0xFE0E) continue;
            if (known.Contains(cp)) sb.Append("<sprite name=\"u").Append(cp.ToString("x")).Append("\">");
            else sb.Append(s, i, len);
            i += len - 1;
        }
        return sb.ToString();
    }

    /// <summary>Most text has no emoji at all; skip it without building a new string.</summary>
    private static bool MayHaveEmoji(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (s[i] >= 0x2190) return true;
        return false;
    }
}
