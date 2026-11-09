using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

// HTML subset parser & entity decoder for HTMLElement.innerHTML

internal static class HtmlParser
{
    private static readonly HashSet<string> VoidTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "br", "hr", "img", "input", "meta", "link", "source", "col", "area", "base", "embed", "param", "track", "wbr"
    };

    private static readonly HashSet<string> InlineRichTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "b", "strong", "i", "em", "u", "small", "br"
    };

    private sealed class HtmlNode
    {
        public bool isText;
        public string text;
        public string tag;
        public string rawSvg;
        public Dictionary<string, string> attrs;
        public List<HtmlNode> children;
    }

    public static void ApplyInnerHtml(HTMLElement target, string html)
    {
        target.ClearAllContent();
        if (string.IsNullOrEmpty(html)) return;

        if (html.IndexOf('<') < 0)
        {
            target.SetDirectText(DecodeEntities(html), isRich: false);
            return;
        }

        var rootNodes = ParseFragment(html);
        PopulateElementFromNodes(target, rootNodes);
    }

    private static void PopulateElementFromNodes(HTMLElement target, List<HtmlNode> nodes)
    {
        if (nodes == null || nodes.Count == 0) return;

        // Single text node inside an HTML element -> decode & collapse HTML whitespace
        if (nodes.Count == 1 && nodes[0].isText)
        {
            target.SetDirectText(DecodeEntities(CollapseWhitespace(nodes[0].text).Trim()), isRich: false);
            return;
        }

        // Check if this is an inline-only run (plain text mixed with attribute-less b, i, em, strong, br, small, u)
        if (CanCollapseToRichText(nodes))
        {
            var sb = new StringBuilder();
            BuildRichText(nodes, sb);
            target.SetDirectText(sb.ToString(), isRich: true);
            return;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.isText)
            {
                if (string.IsNullOrWhiteSpace(n.text)) continue;
                string decoded = DecodeEntities(CollapseWhitespace(n.text));
                if (string.IsNullOrEmpty(decoded)) continue;
                var textNode = new HTMLElement("#text");
                if (textNode.ve != null) textNode.ve.style.flexShrink = 1;
                textNode.SetDirectText(decoded, isRich: false);
                target.appendChild(textNode);
                continue;
            }

            if (string.Equals(n.tag, "svg", StringComparison.OrdinalIgnoreCase))
            {
                var svgEl = document.createElement("svg");
                ApplyAttributes(svgEl, n.attrs);
                float fontSize = target.GetEffectiveFontSize();
                Texture2D tex = SvgRasterizer.Rasterize(n.rawSvg, fontSize, out float w, out float h);
                if (tex != null && svgEl.ve != null)
                {
                    svgEl.ve.style.backgroundImage = new StyleBackground(tex);
                    svgEl.ve.style.width = w;
                    svgEl.ve.style.height = h;
                    svgEl.ve.style.flexShrink = 0;
                    svgEl.ve.style.alignSelf = Align.Center;
                }
                target.appendChild(svgEl);
                continue;
            }

            var el = document.createElement(n.tag);
            ApplyAttributes(el, n.attrs);
            if (n.children != null && n.children.Count > 0)
            {
                PopulateElementFromNodes(el, n.children);
            }
            target.appendChild(el);
        }
    }

    private static bool CanCollapseToRichText(List<HtmlNode> nodes)
    {
        bool hasDirectTextOrBr = false;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.isText)
            {
                if (!string.IsNullOrWhiteSpace(n.text))
                    hasDirectTextOrBr = true;
                continue;
            }
            if (string.Equals(n.tag, "br", StringComparison.OrdinalIgnoreCase))
            {
                hasDirectTextOrBr = true;
                continue;
            }
            if (!InlineRichTags.Contains(n.tag))
                return false;
            if (n.attrs != null && n.attrs.Count > 0)
                return false;
            if (n.children == null || n.children.Count == 0)
                return false;
            if (!CanCollapseSubtree(n.children))
                return false;
        }
        return hasDirectTextOrBr;
    }

    private static bool CanCollapseSubtree(List<HtmlNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.isText) continue;
            if (!InlineRichTags.Contains(n.tag)) return false;
            if (n.attrs != null && n.attrs.Count > 0) return false;
            if (n.children != null && !CanCollapseSubtree(n.children)) return false;
        }
        return true;
    }

    private static void BuildRichText(List<HtmlNode> nodes, StringBuilder sb)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.isText)
            {
                sb.Append(DecodeEntities(CollapseWhitespace(n.text)));
                continue;
            }
            string t = n.tag.ToLowerInvariant();
            if (t == "br")
            {
                sb.Append('\n');
                continue;
            }
            string openTag = t switch
            {
                "b" or "strong" => "<b>",
                "i" or "em" => "<i>",
                "u" => "<u>",
                "small" => "<size=80%>",
                _ => ""
            };
            string closeTag = t switch
            {
                "b" or "strong" => "</b>",
                "i" or "em" => "</i>",
                "u" => "</u>",
                "small" => "</size>",
                _ => ""
            };
            sb.Append(openTag);
            if (n.children != null) BuildRichText(n.children, sb);
            sb.Append(closeTag);
        }
    }

    private static void ApplyAttributes(HTMLElement el, Dictionary<string, string> attrs)
    {
        if (attrs == null || attrs.Count == 0) return;
        foreach (var kv in attrs)
        {
            el.setAttribute(kv.Key, DecodeEntities(kv.Value));
        }
    }

    private static List<HtmlNode> ParseFragment(string html)
    {
        var root = new HtmlNode { children = new List<HtmlNode>() };
        var stack = new Stack<HtmlNode>();
        stack.Push(root);

        int pos = 0;
        int len = html.Length;

        while (pos < len)
        {
            int lt = html.IndexOf('<', pos);
            if (lt < 0)
            {
                string tail = html.Substring(pos);
                if (tail.Length > 0)
                {
                    stack.Peek().children.Add(new HtmlNode { isText = true, text = tail });
                }
                break;
            }

            if (lt > pos)
            {
                string txt = html.Substring(pos, lt - pos);
                stack.Peek().children.Add(new HtmlNode { isText = true, text = txt });
            }

            // Comment <!-- ... -->
            if (lt + 3 < len && html[lt + 1] == '!' && html[lt + 2] == '-' && html[lt + 3] == '-')
            {
                int endComment = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                pos = endComment >= 0 ? endComment + 3 : len;
                continue;
            }

            // Check for <svg ...>...</svg>
            if (lt + 4 < len &&
                (html[lt + 1] == 's' || html[lt + 1] == 'S') &&
                (html[lt + 2] == 'v' || html[lt + 2] == 'V') &&
                (html[lt + 3] == 'g' || html[lt + 3] == 'G') &&
                (char.IsWhiteSpace(html[lt + 4]) || html[lt + 4] == '>' || html[lt + 4] == '/'))
            {
                int openEnd = html.IndexOf('>', lt + 4);
                if (openEnd >= 0)
                {
                    string openAttrText = html.Substring(lt + 4, openEnd - (lt + 4));
                    bool selfClose = openAttrText.EndsWith("/", StringComparison.Ordinal);
                    if (selfClose) openAttrText = openAttrText.Substring(0, openAttrText.Length - 1);

                    int svgEndPos;
                    string rawSvg;
                    if (selfClose)
                    {
                        svgEndPos = openEnd + 1;
                        rawSvg = html.Substring(lt, svgEndPos - lt);
                    }
                    else
                    {
                        int closeSvg = html.IndexOf("</svg>", openEnd + 1, StringComparison.OrdinalIgnoreCase);
                        if (closeSvg >= 0)
                        {
                            svgEndPos = closeSvg + 6;
                            rawSvg = html.Substring(lt, svgEndPos - lt);
                        }
                        else
                        {
                            svgEndPos = openEnd + 1;
                            rawSvg = html.Substring(lt, svgEndPos - lt);
                        }
                    }

                    stack.Peek().children.Add(new HtmlNode
                    {
                        tag = "svg",
                        rawSvg = rawSvg,
                        attrs = ParseTagAttributes(openAttrText),
                    });
                    pos = svgEndPos;
                    continue;
                }
            }

            int gt = FindTagEnd(html, lt + 1);
            if (gt < 0)
            {
                stack.Peek().children.Add(new HtmlNode { isText = true, text = html.Substring(lt) });
                break;
            }

            string tagContent = html.Substring(lt + 1, gt - (lt + 1)).Trim();
            pos = gt + 1;

            if (string.IsNullOrEmpty(tagContent)) continue;

            if (tagContent[0] == '/')
            {
                string closeTag = tagContent.Substring(1).Trim().ToLowerInvariant();
                if (stack.Count > 1)
                {
                    // Pop up to matching tag
                    var temp = new List<HtmlNode>();
                    bool found = false;
                    while (stack.Count > 1)
                    {
                        var top = stack.Pop();
                        if (string.Equals(top.tag, closeTag, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                        temp.Add(top);
                    }
                    if (!found)
                    {
                        for (int i = temp.Count - 1; i >= 0; i--)
                            stack.Push(temp[i]);
                    }
                }
                continue;
            }

            bool isSelfClosing = tagContent.EndsWith("/", StringComparison.Ordinal);
            if (isSelfClosing)
            {
                tagContent = tagContent.Substring(0, tagContent.Length - 1).Trim();
            }

            int spaceIdx = -1;
            for (int i = 0; i < tagContent.Length; i++)
            {
                if (char.IsWhiteSpace(tagContent[i]))
                {
                    spaceIdx = i;
                    break;
                }
            }

            string tagName = (spaceIdx >= 0 ? tagContent.Substring(0, spaceIdx) : tagContent).ToLowerInvariant();
            string attrPart = spaceIdx >= 0 ? tagContent.Substring(spaceIdx + 1) : "";

            var node = new HtmlNode
            {
                tag = tagName,
                attrs = ParseTagAttributes(attrPart),
                children = new List<HtmlNode>(),
            };

            stack.Peek().children.Add(node);

            if (!isSelfClosing && !VoidTags.Contains(tagName))
            {
                stack.Push(node);
            }
        }

        return root.children;
    }

    private static int FindTagEnd(string html, int start)
    {
        char quote = '\0';
        for (int i = start; i < html.Length; i++)
        {
            char c = html[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else
            {
                if (c == '"' || c == '\'') quote = c;
                else if (c == '>') return i;
            }
        }
        return -1;
    }

    private static Dictionary<string, string> ParseTagAttributes(string attrPart)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(attrPart)) return attrs;

        int i = 0;
        int n = attrPart.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(attrPart[i])) i++;
            if (i >= n) break;

            int nameStart = i;
            while (i < n && !char.IsWhiteSpace(attrPart[i]) && attrPart[i] != '=' && attrPart[i] != '/')
                i++;

            string name = attrPart.Substring(nameStart, i - nameStart);
            if (string.IsNullOrEmpty(name)) { i++; continue; }

            while (i < n && char.IsWhiteSpace(attrPart[i])) i++;

            if (i < n && attrPart[i] == '=')
            {
                i++; // skip '='
                while (i < n && char.IsWhiteSpace(attrPart[i])) i++;
                if (i < n && (attrPart[i] == '"' || attrPart[i] == '\''))
                {
                    char q = attrPart[i++];
                    int valStart = i;
                    while (i < n && attrPart[i] != q) i++;
                    string val = attrPart.Substring(valStart, i - valStart);
                    if (i < n && attrPart[i] == q) i++;
                    attrs[name] = val;
                }
                else
                {
                    int valStart = i;
                    while (i < n && !char.IsWhiteSpace(attrPart[i])) i++;
                    attrs[name] = attrPart.Substring(valStart, i - valStart);
                }
            }
            else
            {
                // Boolean attribute like checked, disabled
                attrs[name] = "";
            }
        }

        return attrs;
    }

    private static string CollapseWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool inWs = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                if (!inWs)
                {
                    sb.Append(' ');
                    inWs = true;
                }
            }
            else
            {
                sb.Append(c);
                inWs = false;
            }
        }
        return sb.ToString();
    }

    public static string DecodeEntities(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s ?? "";
        return Regex.Replace(s, @"&(#x[0-9a-fA-F]+|#[0-9]+|[a-zA-Z0-9]+);", m =>
        {
            string ent = m.Groups[1].Value;
            if (ent.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(ent.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code) && code > 0)
                    return char.ConvertFromUtf32(code);
                return m.Value;
            }
            if (ent.StartsWith("#", StringComparison.Ordinal))
            {
                if (int.TryParse(ent.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) && code > 0)
                    return char.ConvertFromUtf32(code);
                return m.Value;
            }
            return ent switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" => "'",
                "nbsp" => "\u00A0",
                "times" => "×",
                "ndash" => "–",
                "mdash" => "—",
                "lsquo" => "‘",
                "rsquo" => "’",
                "ldquo" => "“",
                "rdquo" => "”",
                "lsaquo" => "‹",
                "rsaquo" => "›",
                "hellip" => "…",
                "middot" => "·",
                "bull" => "•",
                "check" => "✓",
                _ => m.Value
            };
        });
    }
}
