using System.Collections.Generic;
using System.Globalization;

namespace Tarinoi.Expressions
{
    enum TokenKind
    {
        Identifier,
        Int,
        Float,
        String,
        Operator,
    }

    readonly struct Token
    {
        public readonly TokenKind Kind;
        public readonly string Text;
        public readonly long IntValue;
        public readonly double FloatValue;

        Token(TokenKind kind, string text, long intValue = 0, double floatValue = 0)
        {
            Kind = kind;
            Text = text;
            IntValue = intValue;
            FloatValue = floatValue;
        }

        public static Token Identifier(string text) => new Token(TokenKind.Identifier, text);
        public static Token Operator(string text) => new Token(TokenKind.Operator, text);
        public static Token String(string text) => new Token(TokenKind.String, text);
        public static Token Int(long value) => new Token(TokenKind.Int, value.ToString(), value);
        public static Token Float(double value) =>
            new Token(TokenKind.Float, value.ToString(CultureInfo.InvariantCulture), 0, value);

        public bool IsOperator(string op) => Kind == TokenKind.Operator && Text == op;

        public override string ToString() => Text;
    }

    /// <summary>
    /// Turns an expression string into tokens.
    /// </summary>
    /// <remarks>
    /// The grammar is small and fixed: the operators <c>&amp;&amp; || ! ( ) , .</c>,
    /// double-quoted strings, unsigned numbers, and identifiers.
    /// <para>
    /// Numbers are deliberately unsigned — there is no negative literal. Authors express
    /// signed values through variable references instead, which is why <c>-</c> is not an
    /// operator here.
    /// </para>
    /// </remarks>
    static class ExpressionLexer
    {
        /// <summary>
        /// Tokenizes an expression. Returns false and logs on an unexpected character;
        /// callers treat that as a parse failure.
        /// </summary>
        public static bool TryTokenize(string expr, out List<Token> tokens)
        {
            tokens = new List<Token>();
            var i = 0;

            while (i < expr.Length)
            {
                var c = expr[i];

                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    i++;
                    continue;
                }

                if (i + 1 < expr.Length)
                {
                    var two = expr.Substring(i, 2);
                    if (two == "&&" || two == "||")
                    {
                        tokens.Add(Token.Operator(two));
                        i += 2;
                        continue;
                    }
                }

                if (c == '!' || c == '(' || c == ')' || c == ',' || c == '.')
                {
                    tokens.Add(Token.Operator(c.ToString()));
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    // No escape handling: authored strings are plain text, and adding
                    // escapes here would diverge from what the Tarinoi editor produces.
                    var end = i + 1;
                    while (end < expr.Length && expr[end] != '"')
                    {
                        end++;
                    }

                    if (end >= expr.Length)
                    {
                        TarinoiLog.Error($"ExpressionParser: unterminated string in: {expr}");
                        return false;
                    }

                    tokens.Add(Token.String(expr.Substring(i + 1, end - i - 1)));
                    i = end + 1;
                    continue;
                }

                if (c >= '0' && c <= '9')
                {
                    var end = i;
                    while (end < expr.Length && expr[end] >= '0' && expr[end] <= '9')
                    {
                        end++;
                    }

                    // A '.' only starts a fraction when a digit follows it; otherwise it
                    // is the member-access operator, as in "Ls.col.list.2".
                    if (end + 1 < expr.Length && expr[end] == '.'
                                              && expr[end + 1] >= '0' && expr[end + 1] <= '9')
                    {
                        end++;
                        while (end < expr.Length && expr[end] >= '0' && expr[end] <= '9')
                        {
                            end++;
                        }

                        tokens.Add(Token.Float(double.Parse(
                            expr.Substring(i, end - i), CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        tokens.Add(Token.Int(long.Parse(
                            expr.Substring(i, end - i), CultureInfo.InvariantCulture)));
                    }

                    i = end;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    var end = i;
                    while (end < expr.Length && IsIdentifierPart(expr[end]))
                    {
                        end++;
                    }

                    tokens.Add(Token.Identifier(expr.Substring(i, end - i)));
                    i = end;
                    continue;
                }

                TarinoiLog.Error(
                    $"ExpressionParser: unexpected character '{c}' at position {i} in: {expr}");
                return false;
            }

            return true;
        }

        static bool IsIdentifierStart(char c) =>
            c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        static bool IsIdentifierPart(char c) =>
            IsIdentifierStart(c) || (c >= '0' && c <= '9');
    }
}
