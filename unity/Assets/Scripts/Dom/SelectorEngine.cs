using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// CSS selector engine for querySelector, querySelectorAll, matches, closest

internal static class SelectorEngine
{
    private enum Combinator
    {
        None,
        Descendant, // ' '
        Child       // '>'
    }

    private sealed class AttrFilter
    {
        public string name;
        public string op; // null (presence), "=", "^=", "$=", "*="
        public string value;
    }

    private sealed class PseudoFilter
    {
        public string name; // "not", "disabled", "enabled", "checked", "focus", "first-child", "last-child", "nth-child"
        public string arg;
        public List<SelectorChain> notBranches;
    }

    private sealed class CompoundSelector
    {
        public string tag; // uppercase or "*" or null
        public string id;
        public List<string> classes;
        public List<AttrFilter> attrs;
        public List<PseudoFilter> pseudos;
    }

    private struct ChainStep
    {
        public CompoundSelector compound;
        public Combinator combinatorToRight;
    }

    private sealed class SelectorChain
    {
        public readonly List<ChainStep> steps = new List<ChainStep>();
    }

    public static T QuerySelector<T>(HTMLElement root, string selector) where T : HTMLElement
    {
        if (root == null || string.IsNullOrWhiteSpace(selector)) return null;
        var branches = ParseSelectorList(selector);
        if (branches.Count == 0) return null;
        return FindFirstDescendant<T>(root, branches);
    }

    public static List<T> QuerySelectorAll<T>(HTMLElement root, string selector) where T : HTMLElement
    {
        var results = new List<T>();
        if (root == null || string.IsNullOrWhiteSpace(selector)) return results;
        var branches = ParseSelectorList(selector);
        if (branches.Count == 0) return results;
        CollectDescendants(root, branches, results);
        return results;
    }

    public static bool Matches(HTMLElement el, string selector)
    {
        if (el == null || string.IsNullOrWhiteSpace(selector)) return false;
        var branches = ParseSelectorList(selector);
        return MatchesAny(el, branches);
    }

    public static HTMLElement Closest(HTMLElement el, string selector)
    {
        if (el == null || string.IsNullOrWhiteSpace(selector)) return null;
        var branches = ParseSelectorList(selector);
        if (branches.Count == 0) return null;
        var cur = el;
        while (cur != null)
        {
            if (MatchesAny(cur, branches)) return cur;
            cur = cur.parentElement;
        }
        return null;
    }

    private static T FindFirstDescendant<T>(HTMLElement parent, List<SelectorChain> branches) where T : HTMLElement
    {
        var children = parent.children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child is T typed && MatchesAny(child, branches))
            {
                return typed;
            }
            var sub = FindFirstDescendant<T>(child, branches);
            if (sub != null) return sub;
        }
        return null;
    }

    private static void CollectDescendants<T>(HTMLElement parent, List<SelectorChain> branches, List<T> results) where T : HTMLElement
    {
        var children = parent.children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child is T typed && MatchesAny(child, branches))
            {
                results.Add(typed);
            }
            CollectDescendants(child, branches, results);
        }
    }

    private static bool MatchesAny(HTMLElement el, List<SelectorChain> branches)
    {
        for (int i = 0; i < branches.Count; i++)
        {
            if (MatchesChain(el, branches[i])) return true;
        }
        return false;
    }

    private static bool MatchesChain(HTMLElement el, SelectorChain chain)
    {
        int n = chain.steps.Count;
        if (n == 0) return false;
        if (!MatchesCompound(el, chain.steps[n - 1].compound)) return false;
        if (n == 1) return true;
        return MatchesStepBackwards(el, chain, n - 2);
    }

    private static bool MatchesStepBackwards(HTMLElement currentEl, SelectorChain chain, int stepIdx)
    {
        var step = chain.steps[stepIdx];
        if (step.combinatorToRight == Combinator.Child)
        {
            var p = currentEl.parentElement;
            if (p == null || !MatchesCompound(p, step.compound)) return false;
            return stepIdx == 0 || MatchesStepBackwards(p, chain, stepIdx - 1);
        }
        else // Descendant
        {
            var p = currentEl.parentElement;
            while (p != null)
            {
                if (MatchesCompound(p, step.compound))
                {
                    if (stepIdx == 0 || MatchesStepBackwards(p, chain, stepIdx - 1))
                        return true;
                }
                p = p.parentElement;
            }
            return false;
        }
    }

    private static bool MatchesCompound(HTMLElement el, CompoundSelector c)
    {
        if (el == null) return false;

        if (!string.IsNullOrEmpty(c.tag) && c.tag != "*")
        {
            if (!string.Equals(el.tagName, c.tag, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(c.id))
        {
            if (!string.Equals(el.id, c.id, StringComparison.Ordinal))
                return false;
        }

        if (c.classes != null)
        {
            for (int i = 0; i < c.classes.Count; i++)
            {
                if (!el.classList.contains(c.classes[i]))
                    return false;
            }
        }

        if (c.attrs != null)
        {
            for (int i = 0; i < c.attrs.Count; i++)
            {
                var a = c.attrs[i];
                string actual = el.getAttribute(a.name);
                if (actual == null) return false;
                if (a.op == "=")
                {
                    if (!string.Equals(actual, a.value, StringComparison.Ordinal))
                        return false;
                }
                else if (a.op == "^=")
                {
                    if (!actual.StartsWith(a.value ?? "", StringComparison.Ordinal))
                        return false;
                }
                else if (a.op == "$=")
                {
                    if (!actual.EndsWith(a.value ?? "", StringComparison.Ordinal))
                        return false;
                }
                else if (a.op == "*=")
                {
                    if (actual.IndexOf(a.value ?? "", StringComparison.Ordinal) < 0)
                        return false;
                }
            }
        }

        if (c.pseudos != null)
        {
            for (int i = 0; i < c.pseudos.Count; i++)
            {
                if (!MatchesPseudo(el, c.pseudos[i]))
                    return false;
            }
        }

        return true;
    }

    private static bool MatchesPseudo(HTMLElement el, PseudoFilter p)
    {
        switch (p.name)
        {
            case "not":
                if (p.notBranches != null && p.notBranches.Count > 0)
                    return !MatchesAny(el, p.notBranches);
                return true;

            case "disabled":
                return el.disabled;

            case "enabled":
                return !el.disabled;

            case "checked":
                if (el is HTMLInputElement inp) return inp.@checked;
                if (el is HTMLOptionElement opt) return opt.selected;
                return el.classList.contains("checked");

            case "focus":
                return document.activeElement == el;

            case "first-child":
            {
                var parent = el.parentElement;
                return parent != null && parent.children.Count > 0 && parent.children[0] == el;
            }

            case "last-child":
            {
                var parent = el.parentElement;
                return parent != null && parent.children.Count > 0 && parent.children[parent.children.Count - 1] == el;
            }

            case "nth-child":
            {
                var parent = el.parentElement;
                if (parent == null) return false;
                int idx1 = -1;
                for (int i = 0; i < parent.children.Count; i++)
                {
                    if (parent.children[i] == el) { idx1 = i + 1; break; }
                }
                if (idx1 < 1) return false;
                string arg = (p.arg ?? "").Trim().ToLowerInvariant();
                if (arg == "even" || arg == "2n") return (idx1 % 2) == 0;
                if (arg == "odd" || arg == "2n+1") return (idx1 % 2) == 1;
                if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    return idx1 == n;
                return false;
            }

            default:
                return false;
        }
    }

    private static List<SelectorChain> ParseSelectorList(string selector)
    {
        var result = new List<SelectorChain>();
        var parts = SplitTopLevel(selector, ',');
        for (int i = 0; i < parts.Count; i++)
        {
            string branchStr = parts[i].Trim();
            if (string.IsNullOrEmpty(branchStr)) continue;
            var chain = ParseChain(branchStr);
            if (chain != null && chain.steps.Count > 0)
            {
                result.Add(chain);
            }
        }
        return result;
    }

    private static SelectorChain ParseChain(string s)
    {
        var chain = new SelectorChain();
        int i = 0;
        int len = s.Length;

        while (i < len)
        {
            while (i < len && char.IsWhiteSpace(s[i])) i++;
            if (i >= len) break;

            int start = i;
            int parenDepth = 0;
            int bracketDepth = 0;
            char quote = '\0';

            while (i < len)
            {
                char c = s[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    i++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    i++;
                    continue;
                }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }
                if (c == '[') { bracketDepth++; i++; continue; }
                if (c == ']') { if (bracketDepth > 0) bracketDepth--; i++; continue; }

                if (parenDepth == 0 && bracketDepth == 0 && (char.IsWhiteSpace(c) || c == '>'))
                {
                    break;
                }
                i++;
            }

            string compoundStr = s.Substring(start, i - start).Trim();
            if (string.IsNullOrEmpty(compoundStr)) break;

            var compound = ParseCompound(compoundStr);
            Combinator comb = Combinator.None;

            while (i < len && char.IsWhiteSpace(s[i]))
            {
                comb = Combinator.Descendant;
                i++;
            }
            if (i < len && s[i] == '>')
            {
                comb = Combinator.Child;
                i++;
                while (i < len && char.IsWhiteSpace(s[i])) i++;
            }

            chain.steps.Add(new ChainStep { compound = compound, combinatorToRight = comb });
        }

        return chain;
    }

    private static CompoundSelector ParseCompound(string s)
    {
        var comp = new CompoundSelector();
        int i = 0;
        int n = s.Length;

        // Optional leading tag or '*'
        if (i < n && s[i] != '.' && s[i] != '#' && s[i] != '[' && s[i] != ':')
        {
            int start = i;
            while (i < n && s[i] != '.' && s[i] != '#' && s[i] != '[' && s[i] != ':')
                i++;
            comp.tag = s.Substring(start, i - start).Trim().ToUpperInvariant();
        }

        while (i < n)
        {
            char c = s[i];
            if (c == '.')
            {
                i++;
                int start = i;
                while (i < n && IsIdentChar(s[i])) i++;
                string cls = s.Substring(start, i - start);
                if (!string.IsNullOrEmpty(cls))
                {
                    comp.classes ??= new List<string>();
                    comp.classes.Add(cls);
                }
            }
            else if (c == '#')
            {
                i++;
                int start = i;
                while (i < n && IsIdentChar(s[i])) i++;
                comp.id = s.Substring(start, i - start);
            }
            else if (c == '[')
            {
                i++;
                int start = i;
                char quote = '\0';
                while (i < n)
                {
                    if (quote != '\0')
                    {
                        if (s[i] == quote) quote = '\0';
                    }
                    else
                    {
                        if (s[i] == '"' || s[i] == '\'') quote = s[i];
                        else if (s[i] == ']') break;
                    }
                    i++;
                }
                string attrContent = s.Substring(start, i - start).Trim();
                if (i < n && s[i] == ']') i++;
                var af = ParseAttrFilter(attrContent);
                if (af != null)
                {
                    comp.attrs ??= new List<AttrFilter>();
                    comp.attrs.Add(af);
                }
            }
            else if (c == ':')
            {
                i++;
                if (i < n && s[i] == ':') i++; // skip double colon if any
                int start = i;
                while (i < n && IsIdentChar(s[i])) i++;
                string pName = s.Substring(start, i - start).ToLowerInvariant();
                string pArg = null;
                if (i < n && s[i] == '(')
                {
                    i++;
                    int argStart = i;
                    int depth = 1;
                    while (i < n && depth > 0)
                    {
                        if (s[i] == '(') depth++;
                        else if (s[i] == ')') depth--;
                        if (depth > 0) i++;
                    }
                    pArg = s.Substring(argStart, i - argStart).Trim();
                    if (i < n && s[i] == ')') i++;
                }
                var pf = new PseudoFilter
                {
                    name = pName,
                    arg = pArg,
                    notBranches = pName == "not" && !string.IsNullOrEmpty(pArg) ? ParseSelectorList(pArg) : null
                };
                comp.pseudos ??= new List<PseudoFilter>();
                comp.pseudos.Add(pf);
            }
            else
            {
                i++;
            }
        }

        return comp;
    }

    private static AttrFilter ParseAttrFilter(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        int eq = raw.IndexOf('=');
        if (eq < 0)
        {
            return new AttrFilter { name = raw.Trim(), op = null, value = null };
        }
        string op = "=";
        int nameEnd = eq;
        if (eq > 0 && (raw[eq - 1] == '^' || raw[eq - 1] == '$' || raw[eq - 1] == '*' || raw[eq - 1] == '~' || raw[eq - 1] == '|'))
        {
            op = raw.Substring(eq - 1, 2);
            nameEnd = eq - 1;
        }
        string name = raw.Substring(0, nameEnd).Trim();
        string val = raw.Substring(eq + 1).Trim();
        if (val.Length >= 2 && ((val[0] == '"' && val[val.Length - 1] == '"') || (val[0] == '\'' && val[val.Length - 1] == '\'')))
        {
            val = val.Substring(1, val.Length - 2);
        }
        return new AttrFilter { name = name, op = op, value = val };
    }

    private static bool IsIdentChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '-' || c == '_';
    }

    private static List<string> SplitTopLevel(string s, char sep)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(s)) return list;
        int paren = 0, bracket = 0;
        char quote = '\0';
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'') { quote = c; continue; }
            if (c == '(') { paren++; continue; }
            if (c == ')') { if (paren > 0) paren--; continue; }
            if (c == '[') { bracket++; continue; }
            if (c == ']') { if (bracket > 0) bracket--; continue; }
            if (paren == 0 && bracket == 0 && c == sep)
            {
                list.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }
        list.Add(s.Substring(start));
        return list;
    }
}
