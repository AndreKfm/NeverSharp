// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Collections.Concurrent;
using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// parser.ts: `namespace Parser`. TypeScript keeps its state in module variables (a singleton parser); here the
/// instance holds that state (as typescript-go does in `type Parser struct`, internal/parser/parser.go) and
/// instances are pooled (`getParser`/`putParser`/`parserPool`, internal/parser/parser.go) so that
/// <see cref="CreateSourceFile(string, string, CreateSourceFileOptions, bool, ScriptKind?)"/> is safe to call
/// from many threads. The file-level functions of parser.ts are static members.
/// </summary>
internal sealed partial class Parser
{
    // The pool of parser instances: counterpart of parserPool (sync.Pool) in internal/parser/parser.go.
    private static readonly ConcurrentBag<Parser> ParserPool = new();

    private static Parser GetParser()
    {
        return ParserPool.TryTake(out var parser) ? parser : new Parser();
    }

    private static void PutParser(Parser parser)
    {
        ParserPool.Add(parser);
    }

    // Share a single scanner across all calls to parse a source file.  This helps speed things
    // up by avoiding the cost of creating/compiling scanners over and over again.
    private readonly Scanner scanner = Scanner.CreateScanner(ScriptTarget.Latest, skipTrivia: true);

    private const NodeFlags disallowInAndDecoratorContext = NodeFlags.DisallowInContext | NodeFlags.DecoratorContext;

    // Rather than using `createBaseNodeFactory` here, we establish a `BaseNodeFactory` that counts the nodes
    // it creates (`countNode`).
    private readonly ParserBaseNodeFactory baseNodeFactory;

    private readonly NodeFactory factory;

    private readonly ErrorCallback scanErrorCallback;

    private string fileName = "";
    private NodeFlags sourceFlags;
    private string sourceText = "";
    private ScriptTarget languageVersion;
    private ScriptKind scriptKind;
    private LanguageVariant languageVariant;
    private List<DiagnosticWithDetachedLocation> parseDiagnostics = null!;
    private List<DiagnosticWithDetachedLocation>? jsDocDiagnostics;
    private SyntaxCursor? syntaxCursor;

    private SyntaxKind currentToken;
    private int nodeCount;
    private Dictionary<string, string> identifiers = null!;
    private int identifierCount;

    // TODO(jakebailey): This type is a lie; this value actually contains the result
    // of ORing a bunch of `1 << ParsingContext.XYZ`.
    private int parsingContext;

    private HashSet<int>? notParenthesizedArrow;

    // Flags that dictate what parsing context we're in (see the long comment in parser.ts).
    private NodeFlags contextFlags;

    // Indicates whether we are currently parsing top-level statements.
    private bool topLevel = true;

    // Whether or not we've had a parse error since creating the last AST node (see parser.ts).
    private bool parseErrorBeforeNextFinishedNode;

    private bool hasDeprecatedTag;

    private Parser()
    {
        baseNodeFactory = new ParserBaseNodeFactory(this);
        factory = NodeFactory.CreateNodeFactory(NodeFactoryFlags.NoParenthesizerRules | NodeFactoryFlags.NoNodeConverters | NodeFactoryFlags.NoOriginalNode, baseNodeFactory);
        scanErrorCallback = ScanError;
    }

    /// <summary>The base node factory of the parser: creates the node of a kind and counts it (`countNode`).</summary>
    private sealed class ParserBaseNodeFactory(Parser parser) : BaseNodeFactory
    {
        private Node CountNode(Node node)
        {
            node.Pos = 0;
            node.End = 0;
            parser.nodeCount++;
            return node;
        }

        public Node CreateBaseSourceFileNode(SyntaxKind kind)
        {
            return CountNode(NodeAllocator.Create(kind));
        }

        public Node CreateBaseIdentifierNode(SyntaxKind kind)
        {
            return CountNode(NodeAllocator.Create(kind));
        }

        public Node CreateBasePrivateIdentifierNode(SyntaxKind kind)
        {
            return CountNode(NodeAllocator.Create(kind));
        }

        public Node CreateBaseTokenNode(SyntaxKind kind)
        {
            return CountNode(NodeAllocator.Create(kind));
        }

        public Node CreateBaseNode(SyntaxKind kind)
        {
            return CountNode(NodeAllocator.Create(kind));
        }
    }

    //
    // Entry points (file-level functions of parser.ts)
    //

    internal static SourceFile CreateSourceFile(string fileName, string sourceText, ScriptTarget languageVersion, bool setParentNodes = false, ScriptKind? scriptKind = null)
    {
        return CreateSourceFile(fileName, sourceText, new CreateSourceFileOptions { LanguageVersion = languageVersion }, setParentNodes, scriptKind);
    }

    internal static SourceFile CreateSourceFile(string fileName, string sourceText, CreateSourceFileOptions languageVersionOrOptions, bool setParentNodes = false, ScriptKind? scriptKind = null)
    {
        var languageVersion = languageVersionOrOptions.LanguageVersion;
        var overrideSetExternalModuleIndicator = languageVersionOrOptions.SetExternalModuleIndicator;
        var format = languageVersionOrOptions.ImpliedNodeFormat;
        var jsDocParsingMode = languageVersionOrOptions.JsDocParsingMode;
        var parser = GetParser();
        SourceFile result;
        if (languageVersion == ScriptTarget.JSON)
        {
            result = parser.ParseSourceFile(fileName, sourceText, languageVersion, null, setParentNodes, ScriptKind.JSON, static _ => { }, jsDocParsingMode ?? JSDocParsingMode.ParseAll);
        }
        else
        {
            Action<SourceFile>? setIndicator;
            if (format is null)
            {
                setIndicator = overrideSetExternalModuleIndicator;
            }
            else
            {
                setIndicator = file =>
                {
                    file.ImpliedNodeFormat = format.Value;
                    (overrideSetExternalModuleIndicator ?? SetExternalModuleIndicator)(file);
                };
            }
            result = parser.ParseSourceFile(fileName, sourceText, languageVersion, null, setParentNodes, scriptKind, setIndicator, jsDocParsingMode ?? JSDocParsingMode.ParseAll);
        }
        PutParser(parser);
        return result;
    }

    /// <summary>Parse json text into SyntaxTree and return node and parse errors if any.</summary>
    internal static SourceFile ParseJsonText(string fileName, string sourceText)
    {
        var parser = GetParser();
        var result = parser.ParseJsonText(fileName, sourceText, ScriptTarget.ES2015, null, false);
        PutParser(parser);
        return result;
    }

    //
    // namespace Parser
    //

    internal SourceFile ParseSourceFile(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        SyntaxCursor? syntaxCursor,
        bool setParentNodes,
        ScriptKind? scriptKindIn,
        Action<SourceFile>? setExternalModuleIndicatorOverride,
        JSDocParsingMode jsDocParsingMode)
    {
        var scriptKind = Utilities.EnsureScriptKind(fileName, scriptKindIn);
        if (scriptKind == ScriptKind.JSON)
        {
            var result = ParseJsonText(fileName, sourceText, languageVersion, syntaxCursor, setParentNodes);
            Node? rootExpression = result.Statements.Count > 0 ? ((ExpressionStatement)result.Statements[0]).Expression : null;
            CommandLineParser.ConvertToJson(result, rootExpression, result.ParseDiagnostics, false, null);
            result.ReferencedFiles = Core.EmptyArray<FileReference>();
            result.TypeReferenceDirectives = Core.EmptyArray<FileReference>();
            result.LibReferenceDirectives = Core.EmptyArray<FileReference>();
            result.AmdDependencies = Core.EmptyArray<AmdDependency>();
            result.HasNoDefaultLib = false;
            result.Pragmas = new PragmaMap();
            return result;
        }

        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, scriptKind, jsDocParsingMode);

        var sourceFile = ParseSourceFileWorker(languageVersion, setParentNodes, scriptKind, setExternalModuleIndicatorOverride ?? SetExternalModuleIndicator, jsDocParsingMode);

        ClearState();

        return sourceFile;
    }

    internal SourceFile ParseJsonText(string fileName, string sourceText, ScriptTarget languageVersion, SyntaxCursor? syntaxCursor, bool setParentNodes)
    {
        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, ScriptKind.JSON, JSDocParsingMode.ParseAll);
        sourceFlags = contextFlags;

        // Prime the scanner.
        NextToken();
        var pos = GetNodePos();
        NodeArray statements;
        Node endOfFileToken;
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            statements = CreateNodeArray([], pos, pos);
            endOfFileToken = ParseTokenNode<EndOfFileToken>();
        }
        else
        {
            // Loop and synthesize an ArrayLiteralExpression if there are more than
            // one top-level expressions to ensure all input text is consumed.
            Node? singleExpression = null;
            List<Node>? expressionList = null;
            while (Token() != SyntaxKind.EndOfFileToken)
            {
                Node expression;
                switch (Token())
                {
                    case SyntaxKind.OpenBracketToken:
                        expression = ParseArrayLiteralExpression();
                        break;
                    case SyntaxKind.TrueKeyword:
                    case SyntaxKind.FalseKeyword:
                    case SyntaxKind.NullKeyword:
                        expression = ParseTokenNode<Node>();
                        break;
                    case SyntaxKind.MinusToken:
                        if (LookAhead(() => NextToken() == SyntaxKind.NumericLiteral && NextToken() != SyntaxKind.ColonToken))
                        {
                            expression = ParsePrefixUnaryExpression();
                        }
                        else
                        {
                            expression = ParseObjectLiteralExpression();
                        }
                        break;
                    case SyntaxKind.NumericLiteral:
                    case SyntaxKind.StringLiteral:
                        if (LookAhead(() => NextToken() != SyntaxKind.ColonToken))
                        {
                            expression = ParseLiteralNode();
                            break;
                        }
                        // falls through
                        expression = ParseObjectLiteralExpression();
                        break;
                    default:
                        expression = ParseObjectLiteralExpression();
                        break;
                }

                // Error recovery: collect multiple top-level expressions
                if (expressionList is not null)
                {
                    expressionList.Add(expression);
                }
                else if (singleExpression is not null)
                {
                    expressionList = [singleExpression, expression];
                }
                else
                {
                    singleExpression = expression;
                    if (Token() != SyntaxKind.EndOfFileToken)
                    {
                        ParseErrorAtCurrentToken(Diagnostics.Unexpected_token);
                    }
                }
            }

            var finalExpression = expressionList is not null ? FinishNode(factory.CreateArrayLiteralExpression(expressionList), pos) : Debug.CheckDefined(singleExpression);
            var statement = factory.CreateExpressionStatement(finalExpression);
            FinishNode(statement, pos);
            statements = CreateNodeArray([statement], pos);
            endOfFileToken = ParseExpectedToken<EndOfFileToken>(SyntaxKind.EndOfFileToken, Diagnostics.Unexpected_token);
        }

        // Set source file so that errors will be reported with this file name
        var sourceFile = CreateSourceFile(this.fileName, ScriptTarget.ES2015, ScriptKind.JSON, false, statements, (EndOfFileToken)endOfFileToken, sourceFlags, static _ => { });

        if (setParentNodes)
        {
            FixupParentReferences(sourceFile);
        }

        sourceFile.NodeCount = nodeCount;
        sourceFile.IdentifierCount = identifierCount;
        sourceFile.Identifiers = identifiers;
        sourceFile.ParseDiagnostics = Utilities.AttachFileToDiagnostics(parseDiagnostics, sourceFile);
        if (jsDocDiagnostics is not null)
        {
            sourceFile.JsDocDiagnostics = Utilities.AttachFileToDiagnostics(jsDocDiagnostics, sourceFile);
        }

        ClearState();
        return sourceFile;
    }

    private void InitializeState(string fileNameIn, string sourceTextIn, ScriptTarget languageVersionIn, SyntaxCursor? syntaxCursorIn, ScriptKind scriptKindIn, JSDocParsingMode jsDocParsingMode)
    {
        fileName = TsPath.NormalizePath(fileNameIn);
        sourceText = sourceTextIn;
        languageVersion = languageVersionIn;
        syntaxCursor = syntaxCursorIn;
        scriptKind = scriptKindIn;
        languageVariant = Utilities.GetLanguageVariant(scriptKindIn);

        parseDiagnostics = [];
        parsingContext = 0;
        identifiers = new Dictionary<string, string>();
        identifierCount = 0;
        nodeCount = 0;
        sourceFlags = 0;
        topLevel = true;

        switch (scriptKindIn)
        {
            case ScriptKind.JS:
            case ScriptKind.JSX:
                contextFlags = NodeFlags.JavaScriptFile;
                break;
            case ScriptKind.JSON:
                contextFlags = NodeFlags.JavaScriptFile | NodeFlags.JsonFile;
                break;
            default:
                contextFlags = NodeFlags.None;
                break;
        }
        parseErrorBeforeNextFinishedNode = false;

        // Initialize and prime the scanner before parsing the source elements.
        scanner.SetText(sourceText);
        scanner.SetOnError(scanErrorCallback);
        scanner.SetScriptTarget(languageVersion);
        scanner.SetLanguageVariant(languageVariant);
        scanner.SetScriptKind(scriptKindIn);
        scanner.SetJSDocParsingMode(jsDocParsingMode);
    }

    private void ClearState()
    {
        // Clear out the text the scanner is pointing at, so it doesn't keep anything alive unnecessarily.
        scanner.ClearCommentDirectives();
        scanner.SetText("");
        scanner.SetOnError(null);
        scanner.SetScriptKind(ScriptKind.Unknown);
        scanner.SetJSDocParsingMode(JSDocParsingMode.ParseAll);

        // Clear any data.  We don't want to accidentally hold onto it for too long.
        sourceText = "";
        syntaxCursor = null;
        sourceFlags = 0;
        parseDiagnostics = null!;
        jsDocDiagnostics = null;
        parsingContext = 0;
        identifiers = null!;
        notParenthesizedArrow = null;
        topLevel = true;
        intersectingChangeSet.Clear();
    }

    private SourceFile ParseSourceFileWorker(ScriptTarget languageVersion, bool setParentNodes, ScriptKind scriptKind, Action<SourceFile> setExternalModuleIndicator, JSDocParsingMode jsDocParsingMode)
    {
        var isDeclarationFile = IsDeclarationFileName(fileName);
        if (isDeclarationFile)
        {
            contextFlags |= NodeFlags.Ambient;
        }

        sourceFlags = contextFlags;

        // Prime the scanner.
        NextToken();

        var statements = ParseList(ParsingContext.SourceElements, ParseStatement);
        Debug.Assert(Token() == SyntaxKind.EndOfFileToken);
        var endHasJSDoc = HasPrecedingJSDocComment();
        var endOfFileToken = WithJSDoc(ParseTokenNode<EndOfFileToken>(), endHasJSDoc);

        var sourceFile = CreateSourceFile(fileName, languageVersion, scriptKind, isDeclarationFile, statements, endOfFileToken, sourceFlags, setExternalModuleIndicator);

        // A member of ReadonlyArray<T> isn't assignable to a member of T[] (and prevents a direct cast) - but this is where we set up those members so they can be readonly in the future
        ProcessCommentPragmas(sourceFile, sourceText);
        ProcessPragmasIntoFields(sourceFile, ReportPragmaDiagnostic);

        sourceFile.CommentDirectives = scanner.GetCommentDirectives();
        sourceFile.NodeCount = nodeCount;
        sourceFile.IdentifierCount = identifierCount;
        sourceFile.Identifiers = identifiers;
        sourceFile.ParseDiagnostics = Utilities.AttachFileToDiagnostics(parseDiagnostics, sourceFile);
        sourceFile.JsDocParsingMode = jsDocParsingMode;
        if (jsDocDiagnostics is not null)
        {
            sourceFile.JsDocDiagnostics = Utilities.AttachFileToDiagnostics(jsDocDiagnostics, sourceFile);
        }

        if (setParentNodes)
        {
            FixupParentReferences(sourceFile);
        }

        return sourceFile;

        void ReportPragmaDiagnostic(int pos, int end, DiagnosticMessage diagnostic)
        {
            parseDiagnostics.Add(Utilities.CreateDetachedDiagnostic(fileName, sourceText, pos, end, diagnostic));
        }
    }

    internal T WithJSDoc<T>(T node, bool hasJSDoc)
        where T : Node
    {
        if (!hasJSDoc)
        {
            return node;
        }

        Debug.Assert(JSDocContainers.GetJsDoc(node) is null); // Should only be called once per node
        var jsDoc = Core.MapDefined(Utilities.GetJSDocCommentRanges(node, sourceText), (comment, _) => ParseJSDocComment(node, comment.Pos, comment.End - comment.Pos));
        if (jsDoc.Count != 0)
        {
            JSDocContainers.SetJsDoc(node, jsDoc);
        }
        if (hasDeprecatedTag)
        {
            hasDeprecatedTag = false;
            node.Flags |= NodeFlags.Deprecated;
        }
        return node;
    }

    private SourceFile ReparseTopLevelAwait(SourceFile sourceFile)
    {
        var savedSyntaxCursor = syntaxCursor;
        var baseSyntaxCursor = IncrementalParser.CreateSyntaxCursor(sourceFile);
        syntaxCursor = new SyntaxCursor(ReparseCurrentNode);

        var statements = new List<Node>();
        var savedParseDiagnostics = parseDiagnostics;

        parseDiagnostics = [];

        var pos = 0;
        var start = FindNextStatementWithAwait(sourceFile.Statements, 0);
        while (start != -1)
        {
            // append all statements between pos and start
            var prevStatement = sourceFile.Statements[pos];
            var nextStatement = sourceFile.Statements[start];
            Core.AddRange(statements, sourceFile.Statements, pos, start);
            pos = FindNextStatementWithoutAwait(sourceFile.Statements, start);

            // append all diagnostics associated with the copied range
            var diagnosticStart = Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= prevStatement.Pos);
            var diagnosticEnd = diagnosticStart >= 0 ? Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= nextStatement.Pos, diagnosticStart) : -1;
            if (diagnosticStart >= 0)
            {
                Core.AddRange(parseDiagnostics, savedParseDiagnostics, diagnosticStart, diagnosticEnd >= 0 ? diagnosticEnd : null);
            }

            // reparse all statements between start and pos. We skip existing diagnostics for the same range and allow the parser to generate new ones.
            var nextStatementPos = nextStatement.Pos;
            SpeculationHelper(() =>
            {
                var savedContextFlags = contextFlags;
                contextFlags |= NodeFlags.AwaitContext;
                scanner.ResetTokenState(nextStatementPos);
                NextToken();

                while (Token() != SyntaxKind.EndOfFileToken)
                {
                    var startPos = scanner.GetTokenFullStart();
                    var statement = ParseListElement(ParsingContext.SourceElements, ParseStatement);
                    statements.Add(statement);
                    if (startPos == scanner.GetTokenFullStart())
                    {
                        NextToken();
                    }

                    if (pos >= 0)
                    {
                        var nonAwaitStatement = sourceFile.Statements[pos];
                        if (statement.End == nonAwaitStatement.Pos)
                        {
                            // done reparsing this section
                            break;
                        }
                        if (statement.End > nonAwaitStatement.Pos)
                        {
                            // we ate into the next statement, so we must reparse it.
                            pos = FindNextStatementWithoutAwait(sourceFile.Statements, pos + 1);
                        }
                    }
                }

                contextFlags = savedContextFlags;
                return true;
            }, SpeculationKind.Reparse);

            // find the next statement containing an `await`
            start = pos >= 0 ? FindNextStatementWithAwait(sourceFile.Statements, pos) : -1;
        }

        // append all statements between pos and the end of the list
        if (pos >= 0)
        {
            var prevStatement = sourceFile.Statements[pos];
            Core.AddRange(statements, sourceFile.Statements, pos);

            // append all diagnostics associated with the copied range
            var diagnosticStart = Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= prevStatement.Pos);
            if (diagnosticStart >= 0)
            {
                Core.AddRange(parseDiagnostics, savedParseDiagnostics, diagnosticStart);
            }
        }

        syntaxCursor = savedSyntaxCursor;
        return factory.UpdateSourceFile(sourceFile, FactoryUtilitiesPublic.SetTextRange(factory.CreateNodeArray(statements), sourceFile.Statements));

        static bool ContainsPossibleTopLevelAwait(Node node)
        {
            return (node.Flags & NodeFlags.AwaitContext) == 0
                && (node.TransformFlags & TransformFlags.ContainsPossibleTopLevelAwait) != 0;
        }

        static int FindNextStatementWithAwait(NodeArray statements, int start)
        {
            for (var i = start; i < statements.Count; i++)
            {
                if (ContainsPossibleTopLevelAwait(statements[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        static int FindNextStatementWithoutAwait(NodeArray statements, int start)
        {
            for (var i = start; i < statements.Count; i++)
            {
                if (!ContainsPossibleTopLevelAwait(statements[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        Node? ReparseCurrentNode(int position)
        {
            var node = baseSyntaxCursor.CurrentNode(position);
            if (topLevel && node is not null && ContainsPossibleTopLevelAwait(node))
            {
                MarkAsIntersectingIncrementalChange(node);
            }
            return node;
        }
    }

    internal static void FixupParentReferences(Node rootNode)
    {
        // normally parent references are set during binding. However, for clients that only need
        // a syntax tree, and no semantic features, then the binding process is an unnecessary
        // overhead.  This functions allows us to set all the parents, without all the expense of
        // binding.
        Utilities.SetParentRecursive(rootNode, true);
    }

    private SourceFile CreateSourceFile(
        string fileName,
        ScriptTarget languageVersion,
        ScriptKind scriptKind,
        bool isDeclarationFile,
        IReadOnlyList<Node> statements,
        EndOfFileToken endOfFileToken,
        NodeFlags flags,
        Action<SourceFile> setExternalModuleIndicator)
    {
        // code from createNode is inlined here so createNode won't have to deal with special case of creating source files
        // this is quite rare comparing to other nodes and createNode should be as fast as possible
        var sourceFile = factory.CreateSourceFile(statements, endOfFileToken, flags);
        Utilities.SetTextRangePosWidth(sourceFile, 0, sourceText.Length);
        SetFields(sourceFile);

        // If we parsed this as an external module, it may contain top-level await
        if (!isDeclarationFile && IsExternalModule(sourceFile) && (sourceFile.TransformFlags & TransformFlags.ContainsPossibleTopLevelAwait) != 0)
        {
            var oldSourceFile = sourceFile;
            sourceFile = ReparseTopLevelAwait(sourceFile);
            if (oldSourceFile != sourceFile)
            {
                SetFields(sourceFile);
            }
        }

        return sourceFile;

        void SetFields(SourceFile sourceFile)
        {
            sourceFile.Text = sourceText;
            sourceFile.BindDiagnostics = [];
            sourceFile.BindSuggestionDiagnostics = null;
            sourceFile.LanguageVersion = languageVersion;
            sourceFile.FileName = fileName;
            sourceFile.LanguageVariant = Utilities.GetLanguageVariant(scriptKind);
            sourceFile.IsDeclarationFile = isDeclarationFile;
            sourceFile.ScriptKind = scriptKind;

            setExternalModuleIndicator(sourceFile);
            sourceFile.SetExternalModuleIndicator = setExternalModuleIndicator;
        }
    }

    private void SetContextFlag(bool val, NodeFlags flag)
    {
        if (val)
        {
            contextFlags |= flag;
        }
        else
        {
            contextFlags &= ~flag;
        }
    }

    private void SetDisallowInContext(bool val)
    {
        SetContextFlag(val, NodeFlags.DisallowInContext);
    }

    private void SetYieldContext(bool val)
    {
        SetContextFlag(val, NodeFlags.YieldContext);
    }

    private void SetDecoratorContext(bool val)
    {
        SetContextFlag(val, NodeFlags.DecoratorContext);
    }

    private void SetAwaitContext(bool val)
    {
        SetContextFlag(val, NodeFlags.AwaitContext);
    }

    internal T DoOutsideOfContext<T>(NodeFlags context, Func<T> func)
    {
        // contextFlagsToClear will contain only the context flags that are
        // currently set that we need to temporarily clear
        // We don't just blindly reset to the previous flags to ensure
        // that we do not mutate cached flags for the incremental
        // parser (ThisNodeHasError, ThisNodeOrAnySubNodesHasError, and
        // HasAggregatedChildData).
        var contextFlagsToClear = context & contextFlags;
        if (contextFlagsToClear != 0)
        {
            // clear the requested context flags
            SetContextFlag(false, contextFlagsToClear);
            var result = func();
            // restore the context flags we just cleared
            SetContextFlag(true, contextFlagsToClear);
            return result;
        }

        // no need to do anything special as we are not in any of the requested contexts
        return func();
    }

    internal T DoInsideOfContext<T>(NodeFlags context, Func<T> func)
    {
        // contextFlagsToSet will contain only the context flags that
        // are not currently set that we need to temporarily enable.
        // We don't just blindly reset to the previous flags to ensure
        // that we do not mutate cached flags for the incremental
        // parser (ThisNodeHasError, ThisNodeOrAnySubNodesHasError, and
        // HasAggregatedChildData).
        var contextFlagsToSet = context & ~contextFlags;
        if (contextFlagsToSet != 0)
        {
            // set the requested context flags
            SetContextFlag(true, contextFlagsToSet);
            var result = func();
            // reset the context flags we just set
            SetContextFlag(false, contextFlagsToSet);
            return result;
        }

        // no need to do anything special as we are already in all of the requested contexts
        return func();
    }

    internal T AllowInAnd<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.DisallowInContext, func);
    }

    internal T DisallowInAnd<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DisallowInContext, func);
    }

    internal T AllowConditionalTypesAnd<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);
    }

    internal T DisallowConditionalTypesAnd<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);
    }

    internal T DoInYieldContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.YieldContext, func);
    }

    internal T DoInDecoratorContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DecoratorContext, func);
    }

    internal T DoInAwaitContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.AwaitContext, func);
    }

    internal T DoOutsideOfAwaitContext<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.AwaitContext, func);
    }

    internal T DoInYieldAndAwaitContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    internal T DoOutsideOfYieldAndAwaitContext<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    internal bool InContext(NodeFlags flags)
    {
        return (contextFlags & flags) != 0;
    }

    internal bool InYieldContext()
    {
        return InContext(NodeFlags.YieldContext);
    }

    internal bool InDisallowInContext()
    {
        return InContext(NodeFlags.DisallowInContext);
    }

    internal bool InDisallowConditionalTypesContext()
    {
        return InContext(NodeFlags.DisallowConditionalTypesContext);
    }

    internal bool InDecoratorContext()
    {
        return InContext(NodeFlags.DecoratorContext);
    }

    internal bool InAwaitContext()
    {
        return InContext(NodeFlags.AwaitContext);
    }

    internal DiagnosticWithDetachedLocation? ParseErrorAtCurrentToken(DiagnosticMessage message, params object?[] args)
    {
        return ParseErrorAt(scanner.GetTokenStart(), scanner.GetTokenEnd(), message, args);
    }

    internal DiagnosticWithDetachedLocation? ParseErrorAtPosition(int start, int length, DiagnosticMessage message, params object?[] args)
    {
        // Don't report another error if it would just be at the same position as the last error.
        var lastError = parseDiagnostics.Count > 0 ? parseDiagnostics[^1] : null;
        DiagnosticWithDetachedLocation? result = null;
        if (lastError is null || start != lastError.Start)
        {
            result = Utilities.CreateDetachedDiagnostic(fileName, sourceText, start, length, message, args);
            parseDiagnostics.Add(result);
        }

        // Mark that we've encountered an error.  We'll set an appropriate bit on the next
        // node we finish so that it can't be reused incrementally.
        parseErrorBeforeNextFinishedNode = true;
        return result;
    }

    internal DiagnosticWithDetachedLocation? ParseErrorAt(int start, int end, DiagnosticMessage message, params object?[] args)
    {
        return ParseErrorAtPosition(start, end - start, message, args);
    }

    internal void ParseErrorAtRange(TextRange range, DiagnosticMessage message, params object?[] args)
    {
        ParseErrorAt(range.Pos, range.End, message, args);
    }

    private void ScanError(DiagnosticMessage message, int length, object? arg0)
    {
        ParseErrorAtPosition(scanner.GetTokenEnd(), length, message, arg0);
    }

    internal int GetNodePos()
    {
        return scanner.GetTokenFullStart();
    }

    internal bool HasPrecedingJSDocComment()
    {
        return scanner.HasPrecedingJSDocComment();
    }

    // Use this function to access the current token instead of reading the currentToken
    // variable. Since function results aren't narrowed in control flow analysis, this ensures
    // that the type checker doesn't make wrong assumptions about the type of the current
    // token (e.g. a call to nextToken() changes the current token but the checker doesn't
    // reason about this side effect).  Mainstream VMs inline simple functions like this, so
    // there is no performance penalty.
    internal SyntaxKind Token()
    {
        return currentToken;
    }

    internal SyntaxKind NextTokenWithoutCheck()
    {
        return currentToken = scanner.Scan();
    }

    internal T NextTokenAnd<T>(Func<T> func)
    {
        NextToken();
        return func();
    }

    internal SyntaxKind NextToken()
    {
        // if the keyword had an escape
        if (Utilities.IsKeyword(currentToken) && (scanner.HasUnicodeEscape() || scanner.HasExtendedUnicodeEscape()))
        {
            // issue a parse error for the escape
            ParseErrorAt(scanner.GetTokenStart(), scanner.GetTokenEnd(), Diagnostics.Keywords_cannot_contain_escape_characters);
        }
        return NextTokenWithoutCheck();
    }

    internal SyntaxKind NextTokenJSDoc()
    {
        return currentToken = scanner.ScanJsDocToken();
    }

    internal SyntaxKind NextJSDocCommentTextToken(bool inBackticks)
    {
        return currentToken = scanner.ScanJSDocCommentTextToken(inBackticks);
    }

    internal SyntaxKind ReScanGreaterToken()
    {
        return currentToken = scanner.ReScanGreaterToken();
    }

    internal SyntaxKind ReScanSlashToken()
    {
        return currentToken = scanner.ReScanSlashToken();
    }

    internal SyntaxKind ReScanTemplateToken(bool isTaggedTemplate)
    {
        return currentToken = scanner.ReScanTemplateToken(isTaggedTemplate);
    }

    internal SyntaxKind ReScanLessThanToken()
    {
        return currentToken = scanner.ReScanLessThanToken();
    }

    internal SyntaxKind ReScanHashToken()
    {
        return currentToken = scanner.ReScanHashToken();
    }

    internal SyntaxKind ScanJsxIdentifier()
    {
        return currentToken = scanner.ScanJsxIdentifier();
    }

    internal SyntaxKind ScanJsxText()
    {
        return currentToken = scanner.ScanJsxToken();
    }

    internal SyntaxKind ScanJsxAttributeValue()
    {
        return currentToken = scanner.ScanJsxAttributeValue();
    }

    internal T SpeculationHelper<T>(Func<T> callback, SpeculationKind speculationKind)
    {
        // Keep track of the state we'll need to rollback to if lookahead fails (or if the
        // caller asked us to always reset our state).
        var saveToken = currentToken;
        var saveParseDiagnosticsLength = parseDiagnostics.Count;
        var saveParseErrorBeforeNextFinishedNode = parseErrorBeforeNextFinishedNode;

        // Note: it is not actually necessary to save/restore the context flags here.  That's
        // because the saving/restoring of these flags happens naturally through the recursive
        // descent nature of our parser.  However, we still store this here just so we can
        // assert that invariant holds.
        var saveContextFlags = contextFlags;

        // If we're only looking ahead, then tell the scanner to only lookahead as well.
        // Otherwise, if we're actually speculatively parsing, then tell the scanner to do the
        // same.
        var result = speculationKind != SpeculationKind.TryParse
            ? scanner.LookAhead(callback)
            : scanner.TryScan(callback);

        Debug.Assert(saveContextFlags == contextFlags);

        // If our callback returned something 'falsy' or we're just looking ahead,
        // then unconditionally restore us to where we were.
        if (EqualityComparer<T>.Default.Equals(result, default!) || speculationKind != SpeculationKind.TryParse)
        {
            currentToken = saveToken;
            if (speculationKind != SpeculationKind.Reparse)
            {
                parseDiagnostics.RemoveRange(saveParseDiagnosticsLength, parseDiagnostics.Count - saveParseDiagnosticsLength);
            }
            parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
        }

        return result;
    }

    /// <summary>
    /// Invokes the provided callback then unconditionally restores the parser to the state it
    /// was in immediately prior to invoking the callback.  The result of invoking the callback
    /// is returned from this function.
    /// </summary>
    internal T LookAhead<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, SpeculationKind.Lookahead);
    }

    /// <summary>
    /// Invokes the provided callback.  If the callback returns something falsy, then it restores
    /// the parser to the state it was in immediately prior to invoking the callback.  If the
    /// callback returns something truthy, then the parser state is not rolled back.  The result
    /// of invoking the callback is returned from this function.
    /// </summary>
    internal T TryParse<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, SpeculationKind.TryParse);
    }

    internal bool IsBindingIdentifier()
    {
        if (Token() == SyntaxKind.Identifier)
        {
            return true;
        }

        // `let await`/`let yield` in [Yield] or [Await] are allowed here and disallowed in the binder.
        return Token() > SyntaxKind.LastReservedWord;
    }

    // Ignore strict mode flag because we will report an error in type checker instead.
    internal bool IsIdentifier()
    {
        if (Token() == SyntaxKind.Identifier)
        {
            return true;
        }

        // If we have a 'yield' keyword, and we're in the [yield] context, then 'yield' is
        // considered a keyword and is not an identifier.
        if (Token() == SyntaxKind.YieldKeyword && InYieldContext())
        {
            return false;
        }

        // If we have a 'await' keyword, and we're in the [Await] context, then 'await' is
        // considered a keyword and is not an identifier.
        if (Token() == SyntaxKind.AwaitKeyword && InAwaitContext())
        {
            return false;
        }

        return Token() > SyntaxKind.LastReservedWord;
    }

    internal bool ParseExpected(SyntaxKind kind, DiagnosticMessage? diagnosticMessage = null, bool shouldAdvance = true)
    {
        if (Token() == kind)
        {
            if (shouldAdvance)
            {
                NextToken();
            }
            return true;
        }

        // Report specific message if provided with one.  Otherwise, report generic fallback message.
        if (diagnosticMessage is not null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage);
        }
        else
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(kind));
        }
        return false;
    }

    private static readonly string[] viableKeywordSuggestions = ComputeViableKeywordSuggestions();

    private static string[] ComputeViableKeywordSuggestions()
    {
        var result = new List<string>();
        foreach (var keyword in Scanner.TextToKeywordObj.Keys)
        {
            if (keyword.Length > 2)
            {
                result.Add(keyword);
            }
        }
        return [.. result];
    }

    /// <summary>
    /// Provides a better error message than the generic "';' expected" if possible for
    /// known common variants of a missing semicolon, such as from a mispelled names.
    /// </summary>
    /// <param name="node">Node preceding the expected semicolon location.</param>
    internal void ParseErrorForMissingSemicolonAfter(Node node)
    {
        // Tagged template literals are sometimes used in places where only simple strings are allowed, i.e.:
        //   module `M1` {
        //   ^^^^^^^^^^^ This block is parsed as a template literal like module`M1`.
        if (NodeTests.IsTaggedTemplateExpression(node))
        {
            var template = ((TaggedTemplateExpression)node).Template;
            ParseErrorAt(Scanner.SkipTrivia(sourceText, template.Pos), template.End, Diagnostics.Module_declaration_names_may_only_use_or_quoted_strings);
            return;
        }

        // Otherwise, if this isn't a well-known keyword-like identifier, give the generic fallback message.
        var expressionText = NodeTests.IsIdentifier(node) ? UtilitiesPublic.IdText(node) : null;
        if (string.IsNullOrEmpty(expressionText) || !Scanner.IsIdentifierText(expressionText, languageVersion))
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.SemicolonToken));
            return;
        }

        var pos = Scanner.SkipTrivia(sourceText, node.Pos);

        // Some known keywords are likely signs of syntax being used improperly.
        switch (expressionText)
        {
            case "const":
            case "let":
            case "var":
                ParseErrorAt(pos, node.End, Diagnostics.Variable_declaration_not_allowed_at_this_location);
                return;

            case "declare":
                // If a declared node failed to parse, it would have emitted a diagnostic already.
                return;

            case "interface":
                ParseErrorForInvalidName(Diagnostics.Interface_name_cannot_be_0, Diagnostics.Interface_must_be_given_a_name, SyntaxKind.OpenBraceToken);
                return;

            case "is":
                ParseErrorAt(pos, scanner.GetTokenStart(), Diagnostics.A_type_predicate_is_only_allowed_in_return_type_position_for_functions_and_methods);
                return;

            case "module":
            case "namespace":
                ParseErrorForInvalidName(Diagnostics.Namespace_name_cannot_be_0, Diagnostics.Namespace_must_be_given_a_name, SyntaxKind.OpenBraceToken);
                return;

            case "type":
                ParseErrorForInvalidName(Diagnostics.Type_alias_name_cannot_be_0, Diagnostics.Type_alias_must_be_given_a_name, SyntaxKind.EqualsToken);
                return;
        }

        // The user alternatively might have misspelled or forgotten to add a space after a common keyword.
        var suggestion = Core.GetSpellingSuggestion(expressionText, viableKeywordSuggestions, static s => s) ?? GetSpaceSuggestion(expressionText);
        if (!string.IsNullOrEmpty(suggestion))
        {
            ParseErrorAt(pos, node.End, Diagnostics.Unknown_keyword_or_identifier_Did_you_mean_0, suggestion);
            return;
        }

        // Unknown tokens are handled with their own errors in the scanner
        if (Token() == SyntaxKind.Unknown)
        {
            return;
        }

        // Otherwise, we know this some kind of unknown word, not just a missing expected semicolon.
        ParseErrorAt(pos, node.End, Diagnostics.Unexpected_keyword_or_identifier);
    }

    /// <summary>Reports a diagnostic error for the current token being an invalid name.</summary>
    /// <param name="nameDiagnostic">Diagnostic to report for all other cases.</param>
    /// <param name="blankDiagnostic">Diagnostic to report for the case of the name being blank (matched tokenIfBlankName).</param>
    /// <param name="tokenIfBlankName">Current token if the name was invalid for being blank (not provided / skipped).</param>
    internal void ParseErrorForInvalidName(DiagnosticMessage nameDiagnostic, DiagnosticMessage blankDiagnostic, SyntaxKind tokenIfBlankName)
    {
        if (Token() == tokenIfBlankName)
        {
            ParseErrorAtCurrentToken(blankDiagnostic);
        }
        else
        {
            ParseErrorAtCurrentToken(nameDiagnostic, scanner.GetTokenValue());
        }
    }

    internal static string? GetSpaceSuggestion(string expressionText)
    {
        foreach (var keyword in viableKeywordSuggestions)
        {
            if (expressionText.Length > keyword.Length + 2 && Core.StartsWith(expressionText, keyword))
            {
                return $"{keyword} {expressionText.Substring(keyword.Length)}";
            }
        }

        return null;
    }

    internal void ParseSemicolonAfterPropertyName(Node name, Node? type, Node? initializer)
    {
        if (Token() == SyntaxKind.AtToken && !scanner.HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Diagnostics.Decorators_must_precede_the_name_and_all_keywords_of_property_declarations);
            return;
        }

        if (Token() == SyntaxKind.OpenParenToken)
        {
            ParseErrorAtCurrentToken(Diagnostics.Cannot_start_a_function_call_in_a_type_annotation);
            NextToken();
            return;
        }

        if (type is not null && !CanParseSemicolon())
        {
            if (initializer is not null)
            {
                ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.SemicolonToken));
            }
            else
            {
                ParseErrorAtCurrentToken(Diagnostics.Expected_for_property_initializer);
            }
            return;
        }

        if (TryParseSemicolon())
        {
            return;
        }

        if (initializer is not null)
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.SemicolonToken));
            return;
        }

        ParseErrorForMissingSemicolonAfter(name);
    }

    internal bool ParseExpectedJSDoc(SyntaxKind kind)
    {
        if (Token() == kind)
        {
            NextTokenJSDoc();
            return true;
        }
        Debug.Assert(Utilities.IsKeywordOrPunctuation(kind));
        ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(kind));
        return false;
    }

    internal void ParseExpectedMatchingBrackets(SyntaxKind openKind, SyntaxKind closeKind, bool openParsed, int openPosition)
    {
        if (Token() == closeKind)
        {
            NextToken();
            return;
        }
        var lastError = ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(closeKind));
        if (!openParsed)
        {
            return;
        }
        if (lastError is not null)
        {
            Utilities.AddRelatedInfo(
                lastError,
                Utilities.CreateDetachedDiagnostic(fileName, sourceText, openPosition, 1, Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here, Scanner.TokenToString(openKind), Scanner.TokenToString(closeKind)));
        }
    }

    internal bool ParseOptional(SyntaxKind t)
    {
        if (Token() == t)
        {
            NextToken();
            return true;
        }
        return false;
    }

    internal T? ParseOptionalToken<T>(SyntaxKind t)
        where T : Node
    {
        if (Token() == t)
        {
            return ParseTokenNode<T>();
        }
        return null;
    }

    internal T? ParseOptionalTokenJSDoc<T>(SyntaxKind t)
        where T : Node
    {
        if (Token() == t)
        {
            return ParseTokenNodeJSDoc<T>();
        }
        return null;
    }

    internal T ParseExpectedToken<T>(SyntaxKind t, DiagnosticMessage? diagnosticMessage = null, string? arg0 = null)
        where T : Node
    {
        return ParseOptionalToken<T>(t) ??
            CreateMissingNode<T>(t, false, diagnosticMessage ?? Diagnostics._0_expected, !string.IsNullOrEmpty(arg0) ? arg0 : Scanner.TokenToString(t)!);
    }

    internal T ParseExpectedTokenJSDoc<T>(SyntaxKind t)
        where T : Node
    {
        var optional = ParseOptionalTokenJSDoc<T>(t);
        if (optional is not null)
        {
            return optional;
        }
        Debug.Assert(Utilities.IsKeywordOrPunctuation(t));
        return CreateMissingNode<T>(t, false, Diagnostics._0_expected, Scanner.TokenToString(t));
    }

    internal T ParseTokenNode<T>()
        where T : Node
    {
        var pos = GetNodePos();
        var kind = Token();
        NextToken();
        return (T)FinishNode(factory.CreateToken(kind), pos);
    }

    internal T ParseTokenNodeJSDoc<T>()
        where T : Node
    {
        var pos = GetNodePos();
        var kind = Token();
        NextTokenJSDoc();
        return (T)FinishNode(factory.CreateToken(kind), pos);
    }

    internal bool CanParseSemicolon()
    {
        // If there's a real semicolon, then we can always parse it out.
        if (Token() == SyntaxKind.SemicolonToken)
        {
            return true;
        }

        // We can parse out an optional semicolon in ASI cases in the following cases.
        return Token() == SyntaxKind.CloseBraceToken || Token() == SyntaxKind.EndOfFileToken || scanner.HasPrecedingLineBreak();
    }

    internal bool TryParseSemicolon()
    {
        if (!CanParseSemicolon())
        {
            return false;
        }

        if (Token() == SyntaxKind.SemicolonToken)
        {
            // consume the semicolon if it was explicitly provided.
            NextToken();
        }

        return true;
    }

    internal bool ParseSemicolon()
    {
        return TryParseSemicolon() || ParseExpected(SyntaxKind.SemicolonToken);
    }

    internal NodeArray CreateNodeArray(IReadOnlyList<Node> elements, int pos, int? end = null, bool? hasTrailingComma = null)
    {
        var array = factory.CreateNodeArray(elements, hasTrailingComma);
        Utilities.SetTextRangePosEnd(array, pos, end ?? scanner.GetTokenFullStart());
        return array;
    }

    internal T FinishNode<T>(T node, int pos, int? end = null)
        where T : Node
    {
        Utilities.SetTextRangePosEnd(node, pos, end ?? scanner.GetTokenFullStart());
        if (contextFlags != 0)
        {
            node.Flags |= contextFlags;
        }

        // Keep track on the node if we encountered an error while parsing it.  If we did, then
        // we cannot reuse the node incrementally.  Once we've marked this node, clear out the
        // flag so that we don't mark any subsequent nodes.
        if (parseErrorBeforeNextFinishedNode)
        {
            parseErrorBeforeNextFinishedNode = false;
            node.Flags |= NodeFlags.ThisNodeHasError;
        }

        return node;
    }

    internal T CreateMissingNode<T>(SyntaxKind kind, bool reportAtCurrentPosition, DiagnosticMessage? diagnosticMessage, params object?[] args)
        where T : Node
    {
        if (reportAtCurrentPosition)
        {
            ParseErrorAtPosition(scanner.GetTokenFullStart(), 0, diagnosticMessage!, args);
        }
        else if (diagnosticMessage is not null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage, args);
        }

        var pos = GetNodePos();
        Node result = kind == SyntaxKind.Identifier ? factory.CreateIdentifier("", null)
            : UtilitiesPublic.IsTemplateLiteralKind(kind) ? factory.CreateTemplateLiteralLikeNode(kind, "", "", null)
            : kind == SyntaxKind.NumericLiteral ? factory.CreateNumericLiteral("")
            : kind == SyntaxKind.StringLiteral ? factory.CreateStringLiteral("", null)
            : kind == SyntaxKind.MissingDeclaration ? factory.CreateMissingDeclaration()
            : factory.CreateToken(kind);
        return (T)FinishNode(result, pos);
    }

    internal string InternIdentifier(string text)
    {
        if (!identifiers.TryGetValue(text, out var identifier))
        {
            identifiers[text] = identifier = text;
        }
        return identifier;
    }

    // An identifier that starts with two underscores has an extra underscore character prepended to it to avoid issues
    // with magic property names like '__proto__'. The 'identifiers' object is used to share a single string instance for
    // each identifier in order to reduce memory consumption.
    internal Identifier CreateIdentifier(bool isIdentifier, DiagnosticMessage? diagnosticMessage = null, DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        if (isIdentifier)
        {
            identifierCount++;
            var pos = scanner.HasPrecedingJSDocLeadingAsterisks() ? scanner.GetTokenStart() : GetNodePos();
            // Store original token kind if it is not just an Identifier so we can report appropriate error later in type checker
            var originalKeywordKind = Token();
            var text = InternIdentifier(scanner.GetTokenValue());
            var hasExtendedUnicodeEscape = scanner.HasExtendedUnicodeEscape();
            NextTokenWithoutCheck();
            return FinishNode(factory.CreateIdentifier(text, originalKeywordKind, hasExtendedUnicodeEscape), pos);
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            ParseErrorAtCurrentToken(privateIdentifierDiagnosticMessage ?? Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
            return CreateIdentifier(true);
        }

        if (Token() == SyntaxKind.Unknown && scanner.TryScan(() => scanner.ReScanInvalidIdentifier() == SyntaxKind.Identifier))
        {
            // Scanner has already recorded an 'Invalid character' error, so no need to add another from the parser.
            return CreateIdentifier(true);
        }

        identifierCount++;
        // Only for end of file because the error gets reported incorrectly on embedded script tags.
        var reportAtCurrentPosition = Token() == SyntaxKind.EndOfFileToken;

        var isReservedWord = scanner.IsReservedWord();
        var msgArg = scanner.GetTokenText();

        var defaultMessage = isReservedWord
            ? Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here
            : Diagnostics.Identifier_expected;

        return CreateMissingNode<Identifier>(SyntaxKind.Identifier, reportAtCurrentPosition, diagnosticMessage ?? defaultMessage, msgArg);
    }

    internal Identifier ParseBindingIdentifier(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        return CreateIdentifier(IsBindingIdentifier(), null, privateIdentifierDiagnosticMessage);
    }

    internal Identifier ParseIdentifier(DiagnosticMessage? diagnosticMessage = null, DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        return CreateIdentifier(IsIdentifier(), diagnosticMessage, privateIdentifierDiagnosticMessage);
    }

    internal Identifier ParseIdentifierName(DiagnosticMessage? diagnosticMessage = null)
    {
        return CreateIdentifier(Scanner.TokenIsIdentifierOrKeyword(Token()), diagnosticMessage);
    }

    internal Identifier ParseIdentifierNameErrorOnUnicodeEscapeSequence()
    {
        if (scanner.HasUnicodeEscape() || scanner.HasExtendedUnicodeEscape())
        {
            ParseErrorAtCurrentToken(Diagnostics.Unicode_escape_sequence_cannot_appear_here);
        }
        return CreateIdentifier(Scanner.TokenIsIdentifierOrKeyword(Token()));
    }

    internal bool IsLiteralPropertyName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) ||
            Token() == SyntaxKind.StringLiteral ||
            Token() == SyntaxKind.NumericLiteral ||
            Token() == SyntaxKind.BigIntLiteral;
    }

    internal bool IsImportAttributeName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;
    }

    internal Node ParsePropertyNameWorker(bool allowComputedPropertyNames)
    {
        if (Token() == SyntaxKind.StringLiteral || Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral)
        {
            var node = ParseLiteralNode();
            switch (node)
            {
                case StringLiteral s:
                    s.Text = InternIdentifier(s.Text);
                    break;
                case NumericLiteral n:
                    n.Text = InternIdentifier(n.Text);
                    break;
                case BigIntLiteral b:
                    b.Text = InternIdentifier(b.Text);
                    break;
            }
            return node;
        }
        if (allowComputedPropertyNames && Token() == SyntaxKind.OpenBracketToken)
        {
            return ParseComputedPropertyName();
        }
        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            return ParsePrivateIdentifier();
        }
        return ParseIdentifierName();
    }

    internal Node ParsePropertyName()
    {
        return ParsePropertyNameWorker(true);
    }

    internal ComputedPropertyName ParseComputedPropertyName()
    {
        // PropertyName [Yield]:
        //      LiteralPropertyName
        //      ComputedPropertyName[?Yield]
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        // We parse any expression (including a comma expression). But the grammar
        // says that only an assignment expression is allowed, so the grammar checker
        // will error if it sees a comma expression.
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.CloseBracketToken);
        return FinishNode(factory.CreateComputedPropertyName(expression), pos);
    }

    internal PrivateIdentifier ParsePrivateIdentifier()
    {
        var pos = GetNodePos();
        var node = factory.CreatePrivateIdentifier(InternIdentifier(scanner.GetTokenValue()));
        NextToken();
        return FinishNode(node, pos);
    }

    internal bool ParseContextualModifier(SyntaxKind t)
    {
        return Token() == t && TryParse(NextTokenCanFollowModifier);
    }

    internal bool NextTokenIsOnSameLineAndCanFollowModifier()
    {
        NextToken();
        if (scanner.HasPrecedingLineBreak())
        {
            return false;
        }
        return CanFollowModifier();
    }

    internal bool NextTokenCanFollowModifier()
    {
        switch (Token())
        {
            case SyntaxKind.ConstKeyword:
                // 'const' is only a modifier if followed by 'enum'.
                return NextToken() == SyntaxKind.EnumKeyword;
            case SyntaxKind.ExportKeyword:
                NextToken();
                if (Token() == SyntaxKind.DefaultKeyword)
                {
                    return LookAhead(NextTokenCanFollowDefaultKeyword);
                }
                if (Token() == SyntaxKind.TypeKeyword)
                {
                    return LookAhead(NextTokenCanFollowExportModifier);
                }
                return CanFollowExportModifier();
            case SyntaxKind.DefaultKeyword:
                return NextTokenCanFollowDefaultKeyword();
            case SyntaxKind.StaticKeyword:
                NextToken();
                return CanFollowModifier();
            case SyntaxKind.GetKeyword:
            case SyntaxKind.SetKeyword:
                NextToken();
                return CanFollowGetOrSetKeyword();
            default:
                return NextTokenIsOnSameLineAndCanFollowModifier();
        }
    }

    internal bool CanFollowExportModifier()
    {
        return Token() == SyntaxKind.AtToken
            || (Token() != SyntaxKind.AsteriskToken
                && Token() != SyntaxKind.AsKeyword
                && Token() != SyntaxKind.OpenBraceToken
                && CanFollowModifier());
    }

    internal bool NextTokenCanFollowExportModifier()
    {
        NextToken();
        return CanFollowExportModifier();
    }

    internal bool ParseAnyContextualModifier()
    {
        return UtilitiesPublic.IsModifierKind(Token()) && TryParse(NextTokenCanFollowModifier);
    }

    internal bool CanFollowModifier()
    {
        return Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.AsteriskToken
            || Token() == SyntaxKind.DotDotDotToken
            || IsLiteralPropertyName();
    }

    internal bool CanFollowGetOrSetKeyword()
    {
        return Token() == SyntaxKind.OpenBracketToken
            || IsLiteralPropertyName();
    }

    internal bool NextTokenCanFollowDefaultKeyword()
    {
        NextToken();
        return Token() == SyntaxKind.ClassKeyword
            || Token() == SyntaxKind.FunctionKeyword
            || Token() == SyntaxKind.InterfaceKeyword
            || Token() == SyntaxKind.AtToken
            || (Token() == SyntaxKind.AbstractKeyword && LookAhead(NextTokenIsClassKeywordOnSameLine))
            || (Token() == SyntaxKind.AsyncKeyword && LookAhead(NextTokenIsFunctionKeywordOnSameLine));
    }
}
