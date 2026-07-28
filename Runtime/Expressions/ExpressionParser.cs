using System.Collections.Generic;

namespace Tarinoi.Expressions
{
    /// <summary>
    /// Parses Tarinoi expression strings into an <see cref="ExprNode"/> tree.
    /// </summary>
    /// <remarks>
    /// The grammar, loosest binding first:
    /// <code>
    /// condition   := or
    /// or          := and ("||" and)*
    /// and         := not ("&amp;&amp;" not)*
    /// not         := "!" not | atom
    /// atom        := "true" | "false" | "(" condition ")" | refOrCall
    /// refOrCall   := "Fn"   "." IDENT "." IDENT "(" args ")"
    ///              | "Ls"   "." IDENT "." IDENT "." IDENT
    ///              | "Var"  "." IDENT "." IDENT
    ///              | "Ent"  "." IDENT "." IDENT
    ///              | "Card" "." IDENT
    /// args        := ε | arg ("," arg)*
    /// arg         := literal | refOrCall
    /// </code>
    /// Parse failures are logged and reported as null rather than thrown. Expressions
    /// come from authored content, so a malformed one is an authoring mistake that
    /// should degrade gracefully — never take down a running game.
    /// </remarks>
    public static class ExpressionParser
    {
        static readonly HashSet<string> Prefixes =
            new HashSet<string> { "Fn", "Var", "Ent", "Ls", "Card" };

        /// <summary>
        /// Parses a boolean condition. Returns null for an empty expression or a parse
        /// failure — callers treat null as "no condition", which passes.
        /// </summary>
        public static ExprNode ParseCondition(string expr)
        {
            var text = expr?.Trim() ?? "";
            if (text.Length == 0)
            {
                return null;
            }

            if (!ExpressionLexer.TryTokenize(text, out var tokens))
            {
                return null;
            }

            var parser = new Parser(tokens);
            var node = parser.ParseCondition();

            if (parser.HasError)
            {
                return null;
            }

            if (!parser.AtEnd)
            {
                TarinoiLog.Error($"ExpressionParser: trailing tokens after condition in: {text}");
                return null;
            }

            return node;
        }

        /// <summary>
        /// Parses a single function call, as used by a card's output selector. Returns
        /// null when the expression is empty or is not a call.
        /// </summary>
        public static CallNode ParseCall(string expr)
        {
            var text = expr?.Trim() ?? "";
            if (text.Length == 0)
            {
                return null;
            }

            if (!ExpressionLexer.TryTokenize(text, out var tokens))
            {
                return null;
            }

            var parser = new Parser(tokens);
            var node = parser.ParseRefOrCall();

            if (parser.HasError)
            {
                return null;
            }

            if (!(node is CallNode call))
            {
                TarinoiLog.Error($"ExpressionParser: expected a function call, got: {text}");
                return null;
            }

            return call;
        }

        /// <summary>Recursive-descent parser over a token list. One instance per parse.</summary>
        sealed class Parser
        {
            readonly List<Token> _tokens;
            int _pos;

            public bool HasError { get; private set; }

            public Parser(List<Token> tokens) => _tokens = tokens;

            public bool AtEnd => _pos >= _tokens.Count;

            Token Peek => AtEnd ? Token.Operator("<end>") : _tokens[_pos];

            Token Consume() => _tokens[_pos++];

            bool Expect(string op)
            {
                if (!AtEnd && Peek.IsOperator(op))
                {
                    _pos++;
                    return true;
                }

                Fail($"expected '{op}', got '{(AtEnd ? "end of expression" : Peek.Text)}'");
                return false;
            }

            void Fail(string message)
            {
                if (!HasError)
                {
                    // Only the first failure is reported: once the cursor is off the
                    // rails, later errors are noise that hides the real cause.
                    TarinoiLog.Error($"ExpressionParser: {message}");
                }

                HasError = true;
            }

            public ExprNode ParseCondition() => ParseOr();

            ExprNode ParseOr()
            {
                var left = ParseAnd();
                while (!HasError && !AtEnd && Peek.IsOperator("||"))
                {
                    _pos++;
                    left = new OrNode(left, ParseAnd());
                }

                return left;
            }

            ExprNode ParseAnd()
            {
                var left = ParseNot();
                while (!HasError && !AtEnd && Peek.IsOperator("&&"))
                {
                    _pos++;
                    left = new AndNode(left, ParseNot());
                }

                return left;
            }

            ExprNode ParseNot()
            {
                if (!AtEnd && Peek.IsOperator("!"))
                {
                    _pos++;
                    return new NotNode(ParseNot());
                }

                return ParseAtom();
            }

            ExprNode ParseAtom()
            {
                if (!AtEnd && Peek.Kind == TokenKind.Identifier)
                {
                    if (Peek.Text == "true")
                    {
                        _pos++;
                        return new BoolLiteral(true);
                    }

                    if (Peek.Text == "false")
                    {
                        _pos++;
                        return new BoolLiteral(false);
                    }
                }

                if (!AtEnd && Peek.IsOperator("("))
                {
                    _pos++;
                    var inner = ParseCondition();
                    Expect(")");
                    return inner;
                }

                return ParseRefOrCall();
            }

            public ExprNode ParseRefOrCall()
            {
                if (AtEnd || Peek.Kind != TokenKind.Identifier)
                {
                    Fail($"expected Fn/Var/Ent/Ls/Card, got '{(AtEnd ? "end of expression" : Peek.Text)}'");
                    return new BoolLiteral(false);
                }

                var prefix = Consume().Text;
                if (!Prefixes.Contains(prefix))
                {
                    Fail($"expected Fn/Var/Ent/Ls/Card, got '{prefix}'");
                    return new BoolLiteral(false);
                }

                Expect(".");

                if (prefix == "Card")
                {
                    return new CardRefNode(ReadIdentifier());
                }

                var collection = ReadIdentifier();
                Expect(".");
                var name = ReadIdentifier();

                switch (prefix)
                {
                    case "Fn":
                        Expect("(");
                        var args = ParseArgList();
                        Expect(")");
                        return new CallNode(collection, name, args);

                    case "Ls":
                        Expect(".");
                        return new RefNode(RefKind.List, collection, name, ReadIdentifier());

                    case "Var":
                        return new RefNode(RefKind.Variable, collection, name);

                    default:
                        return new RefNode(RefKind.Entity, collection, name);
                }
            }

            string ReadIdentifier()
            {
                if (!AtEnd && Peek.Kind == TokenKind.Identifier)
                {
                    return Consume().Text;
                }

                // Deliberately lenient, matching the Godot parser: a missing name yields
                // an empty one, which fails later as an unbound lookup with a message
                // naming the collection — more useful than a bare syntax error.
                return "";
            }

            List<ExprNode> ParseArgList()
            {
                var args = new List<ExprNode>();
                if (AtEnd || Peek.IsOperator(")"))
                {
                    return args;
                }

                args.Add(ParseArg());
                while (!HasError && !AtEnd && Peek.IsOperator(","))
                {
                    _pos++;
                    args.Add(ParseArg());
                }

                return args;
            }

            ExprNode ParseArg()
            {
                if (AtEnd)
                {
                    Fail("unexpected end of expression in argument list");
                    return new BoolLiteral(false);
                }

                switch (Peek.Kind)
                {
                    case TokenKind.Identifier:
                        if (Peek.Text == "true")
                        {
                            _pos++;
                            return new BoolLiteral(true);
                        }

                        if (Peek.Text == "false")
                        {
                            _pos++;
                            return new BoolLiteral(false);
                        }

                        return ParseRefOrCall();

                    case TokenKind.Int:
                        return new IntLiteral(Consume().IntValue);

                    case TokenKind.Float:
                        return new FloatLiteral(Consume().FloatValue);

                    case TokenKind.String:
                        return new StringLiteral(Consume().Text);

                    default:
                        Fail($"unexpected token in argument: '{Peek.Text}'");
                        return new BoolLiteral(false);
                }
            }
        }
    }
}
