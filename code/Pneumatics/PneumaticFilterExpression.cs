using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Gearwright.Pneumatics;

/// <summary>Bounded value lists: AND binds before OR; parentheses override it.
/// Quoted literals preserve codes or attributes containing whitespace/operators.</summary>
internal static class PneumaticFilterExpression
{
    internal const int MaximumLength = 1024, MaximumTerms = 64, MaximumDepth = 16;
    internal abstract record Node
    {
        internal abstract bool Match(System.Func<string, bool> predicate);
    }
    private sealed record Literal(string Value) : Node
    {
        internal override bool Match(System.Func<string, bool> predicate) => predicate(Value);
    }
    private sealed record Binary(Node Left, Node Right, bool And) : Node
    {
        internal override bool Match(System.Func<string, bool> predicate) => And ?
            Left.Match(predicate) && Right.Match(predicate) : Left.Match(predicate) || Right.Match(predicate);
    }
    internal static string Atom(string value) => value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '(' or ')' or '"' or '\\') &&
        !value.Equals("AND", StringComparison.OrdinalIgnoreCase) && !value.Equals("OR", StringComparison.OrdinalIgnoreCase)
        ? value : JsonConvert.SerializeObject(value);
    internal static string Group(IEnumerable<string> values) => "(" + string.Join(" OR ", values.Distinct().Select(Atom)) + ")";

    internal static Node? Parse(string text)
    {
        if (text.Length == 0 || text.Length > MaximumLength) return null;
        var parser = new Parser(text);
        var node = parser.Or(0);
        return !parser.Failed && parser.End ? node : null;
    }
    private sealed class Parser
    {
        private readonly string text;
        private int position, terms;
        internal bool Failed;
        internal string Token = "";
        private bool quoted;
        internal bool End => Token == "" && !quoted;
        internal Parser(string text) { this.text = text; Next(); }
        private bool Is(string op) => !quoted && Token.Equals(op, StringComparison.OrdinalIgnoreCase);
        private void Next()
        {
            quoted = false;
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position == text.Length) { Token = ""; return; }
            int start = position;
            char c = text[position++];
            if (c is '(' or ')') { Token = c.ToString(); return; }
            if (c == '"')
            {
                quoted = true;
                while (position < text.Length)
                {
                    char value = text[position++];
                    if (value == '\\') { if (position < text.Length) position++; else break; }
                    else if (value == '"')
                    {
                        try { Token = JsonConvert.DeserializeObject<string>(text[start..position])!; return; }
                        catch (JsonException) { break; }
                    }
                }
                Failed = true; Token = ""; return;
            }
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not '(' and not ')') position++;
            Token = text[start..position];
            if (Token.Contains('"') || Token.Contains('\\')) Failed = true;
        }
        internal Node? Or(int depth)
        {
            var left = And(depth);
            while (!Failed && Is("OR"))
            { Next(); var right = And(depth); if (left == null || right == null) return null; left = new Binary(left, right, false); }
            return left;
        }
        private Node? And(int depth)
        {
            var left = Primary(depth);
            while (!Failed && Is("AND"))
            { Next(); var right = Primary(depth); if (left == null || right == null) return null; left = new Binary(left, right, true); }
            return left;
        }
        private Node? Primary(int depth)
        {
            if (Failed || depth > MaximumDepth || !quoted && (Token == "" || Is("AND") || Is("OR") || Is(")")))
            { Failed = true; return null; }
            if (Is("("))
            {
                Next(); var node = Or(depth + 1);
                if (!Is(")")) { Failed = true; return null; }
                Next(); return node;
            }
            if (++terms > MaximumTerms) { Failed = true; return null; }
            var literal = new Literal(Token); Next(); return literal;
        }
    }
}
