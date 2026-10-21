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
/// parser.ts. An instance holds the state of `namespace Parser` (TypeScript keeps it in module variables of the
/// singleton namespace; typescript-go in `type Parser struct`, internal/parser/parser.go); the functions of the
/// namespace are instance methods. The file-level functions of parser.ts are static members.
/// </summary>
internal sealed partial class Parser
{
    private enum SignatureFlags
    {
        None = 0,
        Yield = 1 << 0,
        Await = 1 << 1,
        Type = 1 << 2,
        IgnoreMissingOpenBrace = 1 << 4,
        JSDoc = 1 << 5,
    }

    private enum SpeculationKind
    {
        TryParse,
        Lookahead,
        Reparse,
    }

    private enum ParsingContext
    {
        SourceElements,            // Elements in source file
        BlockStatements,           // Statements in block
        SwitchClauses,             // Clauses in switch statement
        SwitchClauseStatements,    // Statements in switch clause
        TypeMembers,               // Members in interface or type literal
        ClassMembers,              // Members in class declaration
        EnumMembers,               // Members in enum declaration
        HeritageClauseElement,     // Elements in a heritage clause
        VariableDeclarations,      // Variable declarations in variable statement
        ObjectBindingElements,     // Binding elements in object binding list
        ArrayBindingElements,      // Binding elements in array binding list
        ArgumentExpressions,       // Expressions in argument list
        ObjectLiteralMembers,      // Members in object literal
        JsxAttributes,             // Attributes in jsx element
        JsxChildren,               // Things between opening and closing JSX tags
        ArrayLiteralMembers,       // Members in array literal
        Parameters,                // Parameters in parameter list
        JSDocParameters,           // JSDoc parameters in parameter list of JSDoc function type
        RestProperties,            // Property names in a rest type list
        TypeParameters,            // Type parameters in type parameter list
        TypeArguments,             // Type arguments in type argument list
        TupleElementTypes,         // Element types in tuple element type list
        HeritageClauses,           // Heritage clauses for a class or interface declaration.
        ImportOrExportSpecifiers,  // Named import clause's import specifier list,
        ImportAttributes,          // Import attributes
        JSDocComment,              // Parsing via JSDocParser
        Count,                     // Number of parsing contexts
    }

    private enum Tristate
    {
        False,
        True,
        Unknown,
    }

    // Parser pool: the counterpart of typescript-go's parserPool/getParser/putParser (internal/parser/parser.go,
    // sync.Pool). TypeScript shares one parser (and its scanner) across all calls; the pool keeps that sharing per
    // thread of use while allowing concurrent calls.
    private static readonly ConcurrentBag<Parser> ParserPool = [];

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
    private readonly Scanner scanner = Scanner.CreateScanner(ScriptTarget.Latest, /*skipTrivia*/ true);

    private const NodeFlags DisallowInAndDecoratorContext = NodeFlags.DisallowInContext | NodeFlags.DecoratorContext;

    // capture constructors in 'initializeState' to avoid null checks
    private NodeConstructor nodeConstructor = null!;
    private NodeConstructor tokenConstructor = null!;
    private NodeConstructor identifierConstructor = null!;
    private NodeConstructor privateIdentifierConstructor = null!;
    private NodeConstructor sourceFileConstructor = null!;

    private readonly NodeFactory factory;

    private string fileName = null!;
    private NodeFlags sourceFlags;
    private string sourceText = null!;
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

    // Flags that dictate what parsing context we're in.  For example:
    // Whether or not we are in strict parsing mode.  All that changes in strict parsing mode is
    // that some tokens that would be considered identifiers may be considered keywords.
    //
    // (See parser.ts for the full discussion of context flags.)
    private NodeFlags contextFlags;

    // Indicates whether we are currently parsing top-level statements.
    private bool topLevel = true;

    // Whether or not we've had a parse error since creating the last AST node.  If we have
    // encountered an error, it will be stored on the next AST node we create.
    // (See parser.ts for the full discussion.)
    private bool parseErrorBeforeNextFinishedNode;

    private bool hasDeprecatedTag;

    private Parser()
    {
        // var factory = createNodeFactory(NodeFactoryFlags.NoParenthesizerRules | NodeFactoryFlags.NoNodeConverters | NodeFactoryFlags.NoOriginalNode, baseNodeFactory);
        factory = NodeFactory.CreateNodeFactory(NodeFactoryFlags.NoParenthesizerRules | NodeFactoryFlags.NoNodeConverters | NodeFactoryFlags.NoOriginalNode, new ParserBaseNodeFactory(this));
    }

    /// <summary>
    /// Rather than using `createBaseNodeFactory` here, we establish a `BaseNodeFactory` that closes over the
    /// constructors above, which are reset each time `initializeState` is called.
    /// </summary>
    private sealed class ParserBaseNodeFactory(Parser parser) : BaseNodeFactory
    {
        public Node CreateBaseSourceFileNode(SyntaxKind kind) => parser.CountNode(parser.sourceFileConstructor(kind, /*pos*/ 0, /*end*/ 0));

        public Node CreateBaseIdentifierNode(SyntaxKind kind) => parser.CountNode(parser.identifierConstructor(kind, /*pos*/ 0, /*end*/ 0));

        public Node CreateBasePrivateIdentifierNode(SyntaxKind kind) => parser.CountNode(parser.privateIdentifierConstructor(kind, /*pos*/ 0, /*end*/ 0));

        public Node CreateBaseTokenNode(SyntaxKind kind) => parser.CountNode(parser.tokenConstructor(kind, /*pos*/ 0, /*end*/ 0));

        public Node CreateBaseNode(SyntaxKind kind) => parser.CountNode(parser.nodeConstructor(kind, /*pos*/ 0, /*end*/ 0));
    }

    private Node CountNode(Node node)
    {
        nodeCount++;
        return node;
    }

    //
    // File-level functions (parser.ts, outside namespace Parser)
    //

    private static void SetExternalModuleIndicator(SourceFile sourceFile)
    {
        sourceFile.ExternalModuleIndicator = IsFileProbablyExternalModule(sourceFile);
    }

    internal static SourceFile CreateSourceFile(string fileName, string sourceText, ScriptTarget languageVersionOrOptions, bool setParentNodes = false, ScriptKind? scriptKind = null)
    {
        // `typeof languageVersionOrOptions === "object" ? languageVersionOrOptions : ({ languageVersion: languageVersionOrOptions } as CreateSourceFileOptions)`
        return CreateSourceFile(fileName, sourceText, new CreateSourceFileOptions { LanguageVersion = languageVersionOrOptions }, setParentNodes, scriptKind);
    }

    internal static SourceFile CreateSourceFile(string fileName, string sourceText, CreateSourceFileOptions languageVersionOrOptions, bool setParentNodes = false, ScriptKind? scriptKind = null)
    {
        // tracing?.push(...) and performance.mark("beforeParse") are instrumentation (left out).
        SourceFile result;

        var languageVersion = languageVersionOrOptions.LanguageVersion;
        var overrideSetExternalModuleIndicator = languageVersionOrOptions.SetExternalModuleIndicator;
        var format = languageVersionOrOptions.ImpliedNodeFormat;
        var jsDocParsingMode = languageVersionOrOptions.JsDocParsingMode;
        var parser = GetParser();
        if (languageVersion == ScriptTarget.JSON)
        {
            result = parser.ParseSourceFile(fileName, sourceText, languageVersion, /*syntaxCursor*/ null, setParentNodes, ScriptKind.JSON, static _ => { }, jsDocParsingMode);
        }
        else
        {
            Action<SourceFile>? setIndicator = format == null ? overrideSetExternalModuleIndicator : file =>
            {
                file.ImpliedNodeFormat = format.Value;
                (overrideSetExternalModuleIndicator ?? SetExternalModuleIndicator)(file);
            };
            result = parser.ParseSourceFile(fileName, sourceText, languageVersion, /*syntaxCursor*/ null, setParentNodes, scriptKind, setIndicator, jsDocParsingMode);
        }
        PutParser(parser);

        // performance.mark("afterParse"), performance.measure(...) and tracing?.pop() are instrumentation (left out).
        return result;
    }

    /// <summary>
    /// Parse json text into SyntaxTree and return node and parse errors if any
    /// </summary>
    internal static SourceFile ParseJsonText(string fileName, string sourceText)
    {
        var parser = GetParser();
        var result = parser.ParseJsonText(fileName, sourceText, ScriptTarget.ES2015, null, false);
        PutParser(parser);
        return result;
    }

    // See also `isExternalOrCommonJsModule` in utilities.ts
    internal static bool IsExternalModule(SourceFile file)
    {
        return file.ExternalModuleIndicator != null;
    }

    //
    // namespace Parser
    //

    private SourceFile ParseSourceFile(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        SyntaxCursor? syntaxCursor,
        bool setParentNodes = false,
        ScriptKind? scriptKind = null,
        Action<SourceFile>? setExternalModuleIndicatorOverride = null,
        JSDocParsingMode? jsDocParsingMode = null)
    {
        var jsDocParsingModeValue = jsDocParsingMode ?? JSDocParsingMode.ParseAll;
        var scriptKindValue = Utilities.EnsureScriptKind(fileName, scriptKind);
        if (scriptKindValue == ScriptKind.JSON)
        {
            var result = ParseJsonText(fileName, sourceText, languageVersion, syntaxCursor, setParentNodes);
            CommandLineParser.ConvertToJson(result, result.Statements.Count > 0 ? ((ExpressionStatement)result.Statements[0]).Expression : null, result.ParseDiagnostics, /*returnValue*/ false, /*jsonConversionNotifier*/ null);
            result.ReferencedFiles = [];
            result.TypeReferenceDirectives = [];
            result.LibReferenceDirectives = [];
            result.AmdDependencies = [];
            result.HasNoDefaultLib = false;
            result.Pragmas = new OrderedDictionary<string, object>(StringComparer.Ordinal); // emptyMap as ReadonlyPragmaMap
            return result;
        }

        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, scriptKindValue, jsDocParsingModeValue);

        var sourceFile = ParseSourceFileWorker(languageVersion, setParentNodes, scriptKindValue, setExternalModuleIndicatorOverride ?? SetExternalModuleIndicator, jsDocParsingModeValue);

        ClearState();

        return sourceFile;
    }

    private SourceFile ParseJsonText(string fileName, string sourceText, ScriptTarget languageVersion = ScriptTarget.ES2015, SyntaxCursor? syntaxCursor = null, bool setParentNodes = false)
    {
        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, ScriptKind.JSON, JSDocParsingMode.ParseAll);
        sourceFlags = contextFlags;

        // Prime the scanner.
        NextToken();
        var pos = GetNodePos();
        NodeArray statements;
        EndOfFileToken endOfFileToken;
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            statements = CreateNodeArray([], pos, pos);
            endOfFileToken = ParseTokenNode<EndOfFileToken>();
        }
        else
        {
            // Loop and synthesize an ArrayLiteralExpression if there are more than
            // one top-level expressions to ensure all input text is consumed.
            // (TypeScript: `let expressions: Expression[] | Expression | undefined`.)
            List<Node>? expressionsArray = null;
            Node? expressions = null;
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
                        expression = ParseTokenNode();
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
                        goto default;
                    default:
                        expression = ParseObjectLiteralExpression();
                        break;
                }

                // Error recovery: collect multiple top-level expressions
                if (expressionsArray != null)
                {
                    expressionsArray.Add(expression);
                }
                else if (expressions != null)
                {
                    expressionsArray = [expressions, expression];
                }
                else
                {
                    expressions = expression;
                    if (Token() != SyntaxKind.EndOfFileToken)
                    {
                        ParseErrorAtCurrentToken(Diagnostics.Unexpected_token);
                    }
                }
            }

            var expressionNode = expressionsArray != null ? FinishNode(factory.CreateArrayLiteralExpression(expressionsArray), pos) : Debug.CheckDefined(expressions);
            var statement = factory.CreateExpressionStatement(expressionNode);
            FinishNode(statement, pos);
            statements = CreateNodeArray([statement], pos);
            endOfFileToken = ParseExpectedToken<EndOfFileToken>(SyntaxKind.EndOfFileToken, Diagnostics.Unexpected_token);
        }

        // Set source file so that errors will be reported with this file name
        var sourceFile = CreateSourceFile(fileName, ScriptTarget.ES2015, ScriptKind.JSON, /*isDeclarationFile*/ false, statements, endOfFileToken, sourceFlags, static _ => { });

        if (setParentNodes)
        {
            FixupParentReferences(sourceFile);
        }

        sourceFile.NodeCount = nodeCount;
        sourceFile.IdentifierCount = identifierCount;
        sourceFile.Identifiers = identifiers;
        sourceFile.ParseDiagnostics = Utilities.AttachFileToDiagnostics(parseDiagnostics, sourceFile);
        if (jsDocDiagnostics != null)
        {
            sourceFile.JsDocDiagnostics = Utilities.AttachFileToDiagnostics(jsDocDiagnostics, sourceFile);
        }

        var result = sourceFile;
        ClearState();
        return result;
    }

    private void InitializeState(string fileNameValue, string sourceTextValue, ScriptTarget languageVersionValue, SyntaxCursor? syntaxCursorValue, ScriptKind scriptKindValue, JSDocParsingMode jsDocParsingModeValue)
    {
        nodeConstructor = Utilities.ObjectAllocator.GetNodeConstructor();
        tokenConstructor = Utilities.ObjectAllocator.GetTokenConstructor();
        identifierConstructor = Utilities.ObjectAllocator.GetIdentifierConstructor();
        privateIdentifierConstructor = Utilities.ObjectAllocator.GetPrivateIdentifierConstructor();
        sourceFileConstructor = Utilities.ObjectAllocator.GetSourceFileConstructor();

        fileName = TsPath.NormalizePath(fileNameValue);
        sourceText = sourceTextValue;
        languageVersion = languageVersionValue;
        syntaxCursor = syntaxCursorValue;
        scriptKind = scriptKindValue;
        languageVariant = Utilities.GetLanguageVariant(scriptKindValue);

        parseDiagnostics = [];
        parsingContext = 0;
        identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
        identifierCount = 0;
        nodeCount = 0;
        sourceFlags = 0;
        topLevel = true;

        switch (scriptKind)
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
        scanner.SetOnError(ScanError);
        scanner.SetScriptTarget(languageVersion);
        scanner.SetLanguageVariant(languageVariant);
        scanner.SetScriptKind(scriptKind);
        scanner.SetJSDocParsingMode(jsDocParsingModeValue);
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
        sourceText = null!;
        languageVersion = default;
        syntaxCursor = null;
        scriptKind = default;
        languageVariant = default;
        sourceFlags = 0;
        parseDiagnostics = null!;
        jsDocDiagnostics = null;
        parsingContext = 0;
        identifiers = null!;
        notParenthesizedArrow = null;
        topLevel = true;

        // (The port keeps intersectingChangeSet per parser; see Parser.FileFunctions.cs.)
        intersectingChangeSet.Clear();
    }

    private SourceFile ParseSourceFileWorker(ScriptTarget languageVersionValue, bool setParentNodes, ScriptKind scriptKindValue, Action<SourceFile> setExternalModuleIndicator, JSDocParsingMode jsDocParsingMode)
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

        var sourceFile = CreateSourceFile(fileName, languageVersionValue, scriptKindValue, isDeclarationFile, statements, endOfFileToken, sourceFlags, setExternalModuleIndicator);

        // A member of ReadonlyArray<T> isn't assignable to a member of T[] (and prevents a direct cast) - but this is where we set up those members so they can be readonly in the future
        ProcessCommentPragmas(sourceFile, sourceText);
        ProcessPragmasIntoFields(sourceFile, ReportPragmaDiagnostic);

        sourceFile.CommentDirectives = scanner.GetCommentDirectives();
        sourceFile.NodeCount = nodeCount;
        sourceFile.IdentifierCount = identifierCount;
        sourceFile.Identifiers = identifiers;
        sourceFile.ParseDiagnostics = Utilities.AttachFileToDiagnostics(parseDiagnostics, sourceFile);
        sourceFile.JsDocParsingMode = jsDocParsingMode;
        if (jsDocDiagnostics != null)
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

    private T WithJSDoc<T>(T node, bool hasJSDoc)
        where T : Node
    {
        if (!hasJSDoc)
        {
            return node;
        }

        Debug.Assert(JSDocContainers.GetJsDoc(node) == null); // Should only be called once per node
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
        syntaxCursor = new ReparseSyntaxCursor(this, baseSyntaxCursor);

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
            SpeculationHelper(
                () =>
                {
                    var savedContextFlags = contextFlags;
                    contextFlags |= NodeFlags.AwaitContext;
                    scanner.ResetTokenState(nextStatement.Pos);
                    NextToken();

                    while (Token() != SyntaxKind.EndOfFileToken)
                    {
                        var startPos = scanner.GetTokenFullStart();
                        var statement = ParseListElement(ParsingContext.SourceElements, ParseStatement)!;
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
                    return (object?)null;
                },
                SpeculationKind.Reparse);

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
    }

    private static bool ContainsPossibleTopLevelAwait(Node node)
    {
        return (node.Flags & NodeFlags.AwaitContext) == 0
            && (node.TransformFlags & TransformFlags.ContainsPossibleTopLevelAwait) != 0;
    }

    /// <summary>reparseTopLevelAwait's `syntaxCursor = { currentNode }` (its inner function currentNode).</summary>
    private sealed class ReparseSyntaxCursor(Parser parser, SyntaxCursor baseSyntaxCursor) : SyntaxCursor
    {
        public Node? CurrentNode(int position)
        {
            var node = baseSyntaxCursor.CurrentNode(position);
            if (parser.topLevel && node != null && ContainsPossibleTopLevelAwait(node))
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
        Utilities.SetParentRecursive(rootNode, /*incremental*/ true);
    }

    private SourceFile CreateSourceFile(
        string fileNameValue,
        ScriptTarget languageVersionValue,
        ScriptKind scriptKindValue,
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
            sourceFile.LanguageVersion = languageVersionValue;
            sourceFile.FileName = fileNameValue;
            sourceFile.LanguageVariant = Utilities.GetLanguageVariant(scriptKindValue);
            sourceFile.IsDeclarationFile = isDeclarationFile;
            sourceFile.ScriptKind = scriptKindValue;

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

    private T DoOutsideOfContext<T>(NodeFlags context, Func<T> func)
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
            SetContextFlag(/*val*/ false, contextFlagsToClear);
            var result = func();
            // restore the context flags we just cleared
            SetContextFlag(/*val*/ true, contextFlagsToClear);
            return result;
        }

        // no need to do anything special as we are not in any of the requested contexts
        return func();
    }

    private T DoInsideOfContext<T>(NodeFlags context, Func<T> func)
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
            SetContextFlag(/*val*/ true, contextFlagsToSet);
            var result = func();
            // reset the context flags we just set
            SetContextFlag(/*val*/ false, contextFlagsToSet);
            return result;
        }

        // no need to do anything special as we are already in all of the requested contexts
        return func();
    }

    private T AllowInAnd<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.DisallowInContext, func);
    }

    private T DisallowInAnd<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DisallowInContext, func);
    }

    private T AllowConditionalTypesAnd<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);
    }

    private T DisallowConditionalTypesAnd<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);
    }

    private T DoInYieldContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.YieldContext, func);
    }

    private T DoInDecoratorContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DecoratorContext, func);
    }

    private T DoInAwaitContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.AwaitContext, func);
    }

    private T DoOutsideOfAwaitContext<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.AwaitContext, func);
    }

    private T DoInYieldAndAwaitContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    private T DoOutsideOfYieldAndAwaitContext<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    private bool InContext(NodeFlags flags)
    {
        return (contextFlags & flags) != 0;
    }

    private bool InYieldContext()
    {
        return InContext(NodeFlags.YieldContext);
    }

    private bool InDisallowInContext()
    {
        return InContext(NodeFlags.DisallowInContext);
    }

    private bool InDisallowConditionalTypesContext()
    {
        return InContext(NodeFlags.DisallowConditionalTypesContext);
    }

    private bool InDecoratorContext()
    {
        return InContext(NodeFlags.DecoratorContext);
    }

    private bool InAwaitContext()
    {
        return InContext(NodeFlags.AwaitContext);
    }

    private DiagnosticWithDetachedLocation? ParseErrorAtCurrentToken(DiagnosticMessage message, params object?[] args)
    {
        return ParseErrorAt(scanner.GetTokenStart(), scanner.GetTokenEnd(), message, args);
    }

    private DiagnosticWithDetachedLocation? ParseErrorAtPosition(int start, int length, DiagnosticMessage message, params object?[] args)
    {
        // Don't report another error if it would just be at the same position as the last error.
        var lastError = Core.LastOrUndefined(parseDiagnostics);
        DiagnosticWithDetachedLocation? result = null;
        if (lastError == null || start != lastError.Start)
        {
            result = Utilities.CreateDetachedDiagnostic(fileName, sourceText, start, length, message, args);
            parseDiagnostics.Add(result);
        }

        // Mark that we've encountered an error.  We'll set an appropriate bit on the next
        // node we finish so that it can't be reused incrementally.
        parseErrorBeforeNextFinishedNode = true;
        return result;
    }

    private DiagnosticWithDetachedLocation? ParseErrorAt(int start, int end, DiagnosticMessage message, params object?[] args)
    {
        return ParseErrorAtPosition(start, end - start, message, args);
    }

    private void ParseErrorAtRange(TextRange range, DiagnosticMessage message, params object?[] args)
    {
        ParseErrorAt(range.Pos, range.End, message, args);
    }

    private void ScanError(DiagnosticMessage message, int length, object? arg0)
    {
        ParseErrorAtPosition(scanner.GetTokenEnd(), length, message, arg0);
    }

    private int GetNodePos()
    {
        return scanner.GetTokenFullStart();
    }

    private bool HasPrecedingJSDocComment()
    {
        return scanner.HasPrecedingJSDocComment();
    }

    // Use this function to access the current token instead of reading the currentToken
    // variable. Since function results aren't narrowed in control flow analysis, this ensures
    // that the type checker doesn't make wrong assumptions about the type of the current
    // token (e.g. a call to nextToken() changes the current token but the checker doesn't
    // reason about this side effect).  Mainstream VMs inline simple functions like this, so
    // there is no performance penalty.
    private SyntaxKind Token()
    {
        return currentToken;
    }

    private SyntaxKind NextTokenWithoutCheck()
    {
        return currentToken = scanner.Scan();
    }

    private T NextTokenAnd<T>(Func<T> func)
    {
        NextToken();
        return func();
    }

    private SyntaxKind NextToken()
    {
        // if the keyword had an escape
        if (Utilities.IsKeyword(currentToken) && (scanner.HasUnicodeEscape() || scanner.HasExtendedUnicodeEscape()))
        {
            // issue a parse error for the escape
            ParseErrorAt(scanner.GetTokenStart(), scanner.GetTokenEnd(), Diagnostics.Keywords_cannot_contain_escape_characters);
        }
        return NextTokenWithoutCheck();
    }

    private SyntaxKind NextTokenJSDoc()
    {
        return currentToken = scanner.ScanJsDocToken();
    }

    private SyntaxKind NextJSDocCommentTextToken(bool inBackticks)
    {
        return currentToken = scanner.ScanJSDocCommentTextToken(inBackticks);
    }

    private SyntaxKind ReScanGreaterToken()
    {
        return currentToken = scanner.ReScanGreaterToken();
    }

    private SyntaxKind ReScanSlashToken()
    {
        return currentToken = scanner.ReScanSlashToken();
    }

    private SyntaxKind ReScanTemplateToken(bool isTaggedTemplate)
    {
        return currentToken = scanner.ReScanTemplateToken(isTaggedTemplate);
    }

    private SyntaxKind ReScanLessThanToken()
    {
        return currentToken = scanner.ReScanLessThanToken();
    }

    private SyntaxKind ReScanHashToken()
    {
        return currentToken = scanner.ReScanHashToken();
    }

    private SyntaxKind ScanJsxIdentifier()
    {
        return currentToken = scanner.ScanJsxIdentifier();
    }

    private SyntaxKind ScanJsxText()
    {
        return currentToken = scanner.ScanJsxToken();
    }

    private SyntaxKind ScanJsxAttributeValue()
    {
        return currentToken = scanner.ScanJsxAttributeValue();
    }

    private T SpeculationHelper<T>(Func<T> callback, SpeculationKind speculationKind)
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
        if (!IsTruthy(result) || speculationKind != SpeculationKind.TryParse)
        {
            currentToken = saveToken;
            if (speculationKind != SpeculationKind.Reparse)
            {
                // parseDiagnostics.length = saveParseDiagnosticsLength
                parseDiagnostics.RemoveRange(saveParseDiagnosticsLength, parseDiagnostics.Count - saveParseDiagnosticsLength);
            }
            parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
        }

        return result;
    }

    /// <summary>
    /// JavaScript truthiness of a speculation result (`!result`): undefined, false and the enum member 0
    /// (Tristate.False) are falsy; nodes and node arrays are truthy.
    /// </summary>
    private static bool IsTruthy<T>(T value)
    {
        return !EqualityComparer<T>.Default.Equals(value, default!);
    }

    /// <summary>
    /// Invokes the provided callback then unconditionally restores the parser to the state it
    /// was in immediately prior to invoking the callback.  The result of invoking the callback
    /// is returned from this function.
    /// </summary>
    private T LookAhead<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, SpeculationKind.Lookahead);
    }

    /// <summary>
    /// Invokes the provided callback.  If the callback returns something falsy, then it restores
    /// the parser to the state it was in immediately prior to invoking the callback.  If the
    /// callback returns something truthy, then the parser state is not rolled back.  The result
    /// of invoking the callback is returned from this function.
    /// </summary>
    private T TryParse<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, SpeculationKind.TryParse);
    }

    private bool IsBindingIdentifier()
    {
        if (Token() == SyntaxKind.Identifier)
        {
            return true;
        }

        // `let await`/`let yield` in [Yield] or [Await] are allowed here and disallowed in the binder.
        return Token() > SyntaxKind.LastReservedWord;
    }

    // Ignore strict mode flag because we will report an error in type checker instead.
    private bool IsIdentifier()
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

    private bool ParseExpected(SyntaxKind kind, DiagnosticMessage? diagnosticMessage = null, bool shouldAdvance = true)
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
        if (diagnosticMessage != null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage);
        }
        else
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(kind));
        }
        return false;
    }

    private static readonly List<string> ViableKeywordSuggestions = CreateViableKeywordSuggestions();

    private static List<string> CreateViableKeywordSuggestions()
    {
        // Object.keys(textToKeywordObj).filter(keyword => keyword.length > 2)
        var result = new List<string>();
        foreach (var keyword in Scanner.TextToKeywordObj.Keys)
        {
            if (keyword.Length > 2)
            {
                result.Add(keyword);
            }
        }
        return result;
    }

    /// <summary>
    /// Provides a better error message than the generic "';' expected" if possible for
    /// known common variants of a missing semicolon, such as from a mispelled names.
    /// </summary>
    /// <param name="node">Node preceding the expected semicolon location.</param>
    private void ParseErrorForMissingSemicolonAfter(Node node)
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
        var suggestion = Core.GetSpellingSuggestion(expressionText, ViableKeywordSuggestions, static n => n) ?? GetSpaceSuggestion(expressionText);
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

    /// <summary>
    /// Reports a diagnostic error for the current token being an invalid name.
    /// </summary>
    /// <param name="nameDiagnostic">Diagnostic to report for all other cases.</param>
    /// <param name="blankDiagnostic">Diagnostic to report for the case of the name being blank (matched tokenIfBlankName).</param>
    /// <param name="tokenIfBlankName">Current token if the name was invalid for being blank (not provided / skipped).</param>
    private void ParseErrorForInvalidName(DiagnosticMessage nameDiagnostic, DiagnosticMessage blankDiagnostic, SyntaxKind tokenIfBlankName)
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

    private static string? GetSpaceSuggestion(string expressionText)
    {
        foreach (var keyword in ViableKeywordSuggestions)
        {
            if (expressionText.Length > keyword.Length + 2 && Core.StartsWith(expressionText, keyword))
            {
                return $"{keyword} {expressionText[keyword.Length..]}";
            }
        }

        return null;
    }

    private void ParseSemicolonAfterPropertyName(Node name, Node? type, Node? initializer)
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

        if (type != null && !CanParseSemicolon())
        {
            if (initializer != null)
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

        if (initializer != null)
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.SemicolonToken));
            return;
        }

        ParseErrorForMissingSemicolonAfter(name);
    }

    private bool ParseExpectedJSDoc(SyntaxKind kind)
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

    private void ParseExpectedMatchingBrackets(SyntaxKind openKind, SyntaxKind closeKind, bool openParsed, int openPosition)
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
        if (lastError != null)
        {
            Utilities.AddRelatedInfo(
                lastError,
                Utilities.CreateDetachedDiagnostic(fileName, sourceText, openPosition, 1, Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here, Scanner.TokenToString(openKind), Scanner.TokenToString(closeKind)));
        }
    }

    private bool ParseOptional(SyntaxKind t)
    {
        if (Token() == t)
        {
            NextToken();
            return true;
        }
        return false;
    }

    private Node? ParseOptionalToken(SyntaxKind t)
    {
        if (Token() == t)
        {
            return ParseTokenNode();
        }
        return null;
    }

    /// <summary>parseOptionalToken&lt;TKind&gt;: the token node typed as its class.</summary>
    private T? ParseOptionalToken<T>(SyntaxKind t)
        where T : Node
    {
        return (T?)ParseOptionalToken(t);
    }

    private Node? ParseOptionalTokenJSDoc(SyntaxKind t)
    {
        if (Token() == t)
        {
            return ParseTokenNodeJSDoc();
        }
        return null;
    }

    private Node ParseExpectedToken(SyntaxKind t, DiagnosticMessage? diagnosticMessage = null, string? arg0 = null)
    {
        return ParseOptionalToken(t) ??
            CreateMissingNode<Node>(t, /*reportAtCurrentPosition*/ false, diagnosticMessage ?? Diagnostics._0_expected, !string.IsNullOrEmpty(arg0) ? arg0 : Scanner.TokenToString(t)!);
    }

    /// <summary>parseExpectedToken&lt;TKind&gt;: the token node typed as its class.</summary>
    private T ParseExpectedToken<T>(SyntaxKind t, DiagnosticMessage? diagnosticMessage = null, string? arg0 = null)
        where T : Node
    {
        return (T)ParseExpectedToken(t, diagnosticMessage, arg0);
    }

    private Node ParseExpectedTokenJSDoc(SyntaxKind t)
    {
        var optional = ParseOptionalTokenJSDoc(t);
        if (optional != null)
        {
            return optional;
        }
        Debug.Assert(Utilities.IsKeywordOrPunctuation(t));
        return CreateMissingNode<Node>(t, /*reportAtCurrentPosition*/ false, Diagnostics._0_expected, Scanner.TokenToString(t));
    }

    private Node ParseTokenNode()
    {
        var pos = GetNodePos();
        var kind = Token();
        NextToken();
        return FinishNode(factory.CreateToken(kind), pos);
    }

    /// <summary>parseTokenNode&lt;T&gt;: the token node typed as its class.</summary>
    private T ParseTokenNode<T>()
        where T : Node
    {
        return (T)ParseTokenNode();
    }

    private Node ParseTokenNodeJSDoc()
    {
        var pos = GetNodePos();
        var kind = Token();
        NextTokenJSDoc();
        return FinishNode(factory.CreateToken(kind), pos);
    }

    private bool CanParseSemicolon()
    {
        // If there's a real semicolon, then we can always parse it out.
        if (Token() == SyntaxKind.SemicolonToken)
        {
            return true;
        }

        // We can parse out an optional semicolon in ASI cases in the following cases.
        return Token() == SyntaxKind.CloseBraceToken || Token() == SyntaxKind.EndOfFileToken || scanner.HasPrecedingLineBreak();
    }

    private bool TryParseSemicolon()
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

    private bool ParseSemicolon()
    {
        return TryParseSemicolon() || ParseExpected(SyntaxKind.SemicolonToken);
    }

    private NodeArray CreateNodeArray(IReadOnlyList<Node> elements, int pos, int? end = null, bool? hasTrailingComma = null)
    {
        var array = factory.CreateNodeArray(elements, hasTrailingComma);
        Utilities.SetTextRangePosEnd(array, pos, end ?? scanner.GetTokenFullStart());
        return array;
    }

    private T FinishNode<T>(T node, int pos, int? end = null)
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

    private T CreateMissingNode<T>(SyntaxKind kind, bool reportAtCurrentPosition, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
        where T : Node
    {
        if (reportAtCurrentPosition)
        {
            ParseErrorAtPosition(scanner.GetTokenFullStart(), 0, diagnosticMessage!, args);
        }
        else if (diagnosticMessage != null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage, args);
        }

        var pos = GetNodePos();
        Node result = kind == SyntaxKind.Identifier ? factory.CreateIdentifier("", /*originalKeywordKind*/ null) :
            UtilitiesPublic.IsTemplateLiteralKind(kind) ? factory.CreateTemplateLiteralLikeNode(kind, "", "", /*templateFlags*/ null) :
            kind == SyntaxKind.NumericLiteral ? factory.CreateNumericLiteral("", /*numericLiteralFlags*/ default) :
            kind == SyntaxKind.StringLiteral ? factory.CreateStringLiteral("", /*isSingleQuote*/ null) :
            kind == SyntaxKind.MissingDeclaration ? factory.CreateMissingDeclaration() :
            factory.CreateToken(kind);
        return (T)FinishNode(result, pos);
    }

    private string InternIdentifier(string text)
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
    private Identifier CreateIdentifier(bool isIdentifier, DiagnosticMessage? diagnosticMessage = null, DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
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
            return CreateIdentifier(/*isIdentifier*/ true);
        }

        if (Token() == SyntaxKind.Unknown && scanner.TryScan(() => scanner.ReScanInvalidIdentifier() == SyntaxKind.Identifier))
        {
            // Scanner has already recorded an 'Invalid character' error, so no need to add another from the parser.
            return CreateIdentifier(/*isIdentifier*/ true);
        }

        identifierCount++;
        // Only for end of file because the error gets reported incorrectly on embedded script tags.
        var reportAtCurrentPosition = Token() == SyntaxKind.EndOfFileToken;

        var isReservedWord = scanner.IsReservedWord();
        var msgArg = scanner.GetTokenText();

        var defaultMessage = isReservedWord ?
            Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here :
            Diagnostics.Identifier_expected;

        return CreateMissingNode<Identifier>(SyntaxKind.Identifier, reportAtCurrentPosition, diagnosticMessage ?? defaultMessage, msgArg);
    }

    private Identifier ParseBindingIdentifier(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        return CreateIdentifier(IsBindingIdentifier(), /*diagnosticMessage*/ null, privateIdentifierDiagnosticMessage);
    }

    private Identifier ParseIdentifier(DiagnosticMessage? diagnosticMessage = null, DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        return CreateIdentifier(IsIdentifier(), diagnosticMessage, privateIdentifierDiagnosticMessage);
    }

    private Identifier ParseIdentifierName(DiagnosticMessage? diagnosticMessage = null)
    {
        return CreateIdentifier(Scanner.TokenIsIdentifierOrKeyword(Token()), diagnosticMessage);
    }

    private Identifier ParseIdentifierNameErrorOnUnicodeEscapeSequence()
    {
        if (scanner.HasUnicodeEscape() || scanner.HasExtendedUnicodeEscape())
        {
            ParseErrorAtCurrentToken(Diagnostics.Unicode_escape_sequence_cannot_appear_here);
        }
        return CreateIdentifier(Scanner.TokenIsIdentifierOrKeyword(Token()));
    }

    private bool IsLiteralPropertyName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) ||
            Token() == SyntaxKind.StringLiteral ||
            Token() == SyntaxKind.NumericLiteral ||
            Token() == SyntaxKind.BigIntLiteral;
    }

    private bool IsImportAttributeName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;
    }

    private Node ParsePropertyNameWorker(bool allowComputedPropertyNames)
    {
        if (Token() == SyntaxKind.StringLiteral || Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral)
        {
            var node = ParseLiteralNode();
            NodeAccessors.SetText(node, InternIdentifier(NodeAccessors.GetText(node)!));
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

    private Node ParsePropertyName()
    {
        return ParsePropertyNameWorker(/*allowComputedPropertyNames*/ true);
    }

    private ComputedPropertyName ParseComputedPropertyName()
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

    private PrivateIdentifier ParsePrivateIdentifier()
    {
        var pos = GetNodePos();
        var node = factory.CreatePrivateIdentifier(InternIdentifier(scanner.GetTokenValue()));
        NextToken();
        return FinishNode(node, pos);
    }

    private bool ParseContextualModifier(SyntaxKind t)
    {
        return Token() == t && TryParse(NextTokenCanFollowModifier);
    }

    private bool NextTokenIsOnSameLineAndCanFollowModifier()
    {
        NextToken();
        if (scanner.HasPrecedingLineBreak())
        {
            return false;
        }
        return CanFollowModifier();
    }

    private bool NextTokenCanFollowModifier()
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

    private bool CanFollowExportModifier()
    {
        return Token() == SyntaxKind.AtToken
            || Token() != SyntaxKind.AsteriskToken
                && Token() != SyntaxKind.AsKeyword
                && Token() != SyntaxKind.OpenBraceToken
                && CanFollowModifier();
    }

    private bool NextTokenCanFollowExportModifier()
    {
        NextToken();
        return CanFollowExportModifier();
    }

    private bool ParseAnyContextualModifier()
    {
        return UtilitiesPublic.IsModifierKind(Token()) && TryParse(NextTokenCanFollowModifier);
    }

    private bool CanFollowModifier()
    {
        return Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.AsteriskToken
            || Token() == SyntaxKind.DotDotDotToken
            || IsLiteralPropertyName();
    }

    private bool CanFollowGetOrSetKeyword()
    {
        return Token() == SyntaxKind.OpenBracketToken
            || IsLiteralPropertyName();
    }

    private bool NextTokenCanFollowDefaultKeyword()
    {
        NextToken();
        return Token() == SyntaxKind.ClassKeyword
            || Token() == SyntaxKind.FunctionKeyword
            || Token() == SyntaxKind.InterfaceKeyword
            || Token() == SyntaxKind.AtToken
            || (Token() == SyntaxKind.AbstractKeyword && LookAhead(NextTokenIsClassKeywordOnSameLine))
            || (Token() == SyntaxKind.AsyncKeyword && LookAhead(NextTokenIsFunctionKeywordOnSameLine));
    }

    // True if positioned at the start of a list element
    private bool IsListElement(ParsingContext parsingContext, bool inErrorRecovery)
    {
        var node = CurrentNode(parsingContext);
        if (node != null)
        {
            return true;
        }

        switch (parsingContext)
        {
            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
                // If we're in error recovery, then we don't want to treat ';' as an empty statement.
                // The problem is that ';' can show up in far too many contexts, and if we see one
                // and assume it's a statement, then we may bail out inappropriately from whatever
                // we're parsing.  For example, if we have a semicolon in the middle of a class, then
                // we really don't want to assume the class is over and we're on a statement in the
                // outer module.  We just want to consume and move on.
                return !(Token() == SyntaxKind.SemicolonToken && inErrorRecovery) && IsStartOfStatement();
            case ParsingContext.SwitchClauses:
                return Token() == SyntaxKind.CaseKeyword || Token() == SyntaxKind.DefaultKeyword;
            case ParsingContext.TypeMembers:
                return LookAhead(IsTypeMemberStart);
            case ParsingContext.ClassMembers:
                // We allow semicolons as class elements (as specified by ES6) as long as we're
                // not in error recovery.  If we're in error recovery, we don't want an errant
                // semicolon to be treated as a class member (since they're almost always used
                // for statements.
                return LookAhead(IsClassMemberStart) || (Token() == SyntaxKind.SemicolonToken && !inErrorRecovery);
            case ParsingContext.EnumMembers:
                // Include open bracket computed properties. This technically also lets in indexers,
                // which would be a candidate for improved error reporting.
                return Token() == SyntaxKind.OpenBracketToken || IsLiteralPropertyName();
            case ParsingContext.ObjectLiteralMembers:
                switch (Token())
                {
                    case SyntaxKind.OpenBracketToken:
                    case SyntaxKind.AsteriskToken:
                    case SyntaxKind.DotDotDotToken:
                    case SyntaxKind.DotToken: // Not an object literal member, but don't want to close the object (see `tests/cases/fourslash/completionsDotInObjectLiteral.ts`)
                        return true;
                    default:
                        return IsLiteralPropertyName();
                }
            case ParsingContext.RestProperties:
                return IsLiteralPropertyName();
            case ParsingContext.ObjectBindingElements:
                return Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.DotDotDotToken || IsLiteralPropertyName();
            case ParsingContext.ImportAttributes:
                return IsImportAttributeName();
            case ParsingContext.HeritageClauseElement:
                // If we see `{ ... }` then only consume it as an expression if it is followed by `,` or `{`
                // That way we won't consume the body of a class in its heritage clause.
                if (Token() == SyntaxKind.OpenBraceToken)
                {
                    return LookAhead(IsValidHeritageClauseObjectLiteral);
                }

                if (!inErrorRecovery)
                {
                    return IsStartOfLeftHandSideExpression() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }
                else
                {
                    // If we're in error recovery we tighten up what we're willing to match.
                    // That way we don't treat something like "this" as a valid heritage clause
                    // element during recovery.
                    return IsIdentifier() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }
            case ParsingContext.VariableDeclarations:
                return IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.ArrayBindingElements:
                return Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.DotDotDotToken || IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.TypeParameters:
                return Token() == SyntaxKind.InKeyword || Token() == SyntaxKind.ConstKeyword || IsIdentifier();
            case ParsingContext.ArrayLiteralMembers:
                switch (Token())
                {
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.DotToken: // Not an array literal member, but don't want to close the array (see `tests/cases/fourslash/completionsDotInArrayLiteralInObjectLiteral.ts`)
                        return true;
                }
                goto case ParsingContext.ArgumentExpressions;
            case ParsingContext.ArgumentExpressions:
                return Token() == SyntaxKind.DotDotDotToken || IsStartOfExpression();
            case ParsingContext.Parameters:
                return IsStartOfParameter(/*isJSDocParameter*/ false);
            case ParsingContext.JSDocParameters:
                return IsStartOfParameter(/*isJSDocParameter*/ true);
            case ParsingContext.TypeArguments:
            case ParsingContext.TupleElementTypes:
                return Token() == SyntaxKind.CommaToken || IsStartOfType();
            case ParsingContext.HeritageClauses:
                return IsHeritageClause();
            case ParsingContext.ImportOrExportSpecifiers:
                // bail out if the next token is [FromKeyword StringLiteral].
                // That means we're in something like `import { from "mod"`. Stop here can give better error message.
                if (Token() == SyntaxKind.FromKeyword && LookAhead(NextTokenIsStringLiteral))
                {
                    return false;
                }
                if (Token() == SyntaxKind.StringLiteral)
                {
                    return true; // For "arbitrary module namespace identifiers"
                }
                return Scanner.TokenIsIdentifierOrKeyword(Token());
            case ParsingContext.JsxAttributes:
                return Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.OpenBraceToken;
            case ParsingContext.JsxChildren:
                return true;
            case ParsingContext.JSDocComment:
                return true;
            case ParsingContext.Count:
                Debug.Fail("ParsingContext.Count used as a context"); // Not a real context, only a marker.
                return false;
            default:
                Debug.AssertNever(parsingContext, "Non-exhaustive case in 'isListElement'.");
                return false;
        }
    }

    private bool IsValidHeritageClauseObjectLiteral()
    {
        Debug.Assert(Token() == SyntaxKind.OpenBraceToken);
        if (NextToken() == SyntaxKind.CloseBraceToken)
        {
            // if we see "extends {}" then only treat the {} as what we're extending (and not
            // the class body) if we have:
            //
            //      extends {} {
            //      extends {},
            //      extends {} extends
            //      extends {} implements

            var next = NextToken();
            return next == SyntaxKind.CommaToken || next == SyntaxKind.OpenBraceToken || next == SyntaxKind.ExtendsKeyword || next == SyntaxKind.ImplementsKeyword;
        }

        return true;
    }

    private bool NextTokenIsIdentifier()
    {
        NextToken();
        return IsIdentifier();
    }

    private bool NextTokenIsIdentifierOrKeyword()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeyword(Token());
    }

    private bool NextTokenIsIdentifierOrKeywordOrGreaterThan()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeywordOrGreaterThan(Token());
    }

    private bool IsHeritageClauseExtendsOrImplementsKeyword()
    {
        if (
            Token() == SyntaxKind.ImplementsKeyword ||
            Token() == SyntaxKind.ExtendsKeyword)
        {
            return LookAhead(NextTokenIsStartOfExpression);
        }

        return false;
    }

    private bool NextTokenIsStartOfExpression()
    {
        NextToken();
        return IsStartOfExpression();
    }

    private bool NextTokenIsStartOfType()
    {
        NextToken();
        return IsStartOfType();
    }

    // True if positioned at a list terminator
    private bool IsListTerminator(ParsingContext kind)
    {
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            // Being at the end of the file ends all lists.
            return true;
        }

        switch (kind)
        {
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauses:
            case ParsingContext.TypeMembers:
            case ParsingContext.ClassMembers:
            case ParsingContext.EnumMembers:
            case ParsingContext.ObjectLiteralMembers:
            case ParsingContext.ObjectBindingElements:
            case ParsingContext.ImportOrExportSpecifiers:
            case ParsingContext.ImportAttributes:
                return Token() == SyntaxKind.CloseBraceToken;
            case ParsingContext.SwitchClauseStatements:
                return Token() == SyntaxKind.CloseBraceToken || Token() == SyntaxKind.CaseKeyword || Token() == SyntaxKind.DefaultKeyword;
            case ParsingContext.HeritageClauseElement:
                return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
            case ParsingContext.VariableDeclarations:
                return IsVariableDeclaratorListTerminator();
            case ParsingContext.TypeParameters:
                // Tokens other than '>' are here for better error recovery
                return Token() == SyntaxKind.GreaterThanToken || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
            case ParsingContext.ArgumentExpressions:
                // Tokens other than ')' are here for better error recovery
                return Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.SemicolonToken;
            case ParsingContext.ArrayLiteralMembers:
            case ParsingContext.TupleElementTypes:
            case ParsingContext.ArrayBindingElements:
                return Token() == SyntaxKind.CloseBracketToken;
            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
            case ParsingContext.RestProperties:
                // Tokens other than ')' and ']' (the latter for index signatures) are here for better error recovery
                return Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.CloseBracketToken /*|| token === SyntaxKind.OpenBraceToken*/;
            case ParsingContext.TypeArguments:
                // All other tokens should cause the type-argument to terminate except comma token
                return Token() != SyntaxKind.CommaToken;
            case ParsingContext.HeritageClauses:
                return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.CloseBraceToken;
            case ParsingContext.JsxAttributes:
                return Token() == SyntaxKind.GreaterThanToken || Token() == SyntaxKind.SlashToken;
            case ParsingContext.JsxChildren:
                return Token() == SyntaxKind.LessThanToken && LookAhead(NextTokenIsSlash);
            default:
                return false;
        }
    }

    private bool IsVariableDeclaratorListTerminator()
    {
        // If we can consume a semicolon (either explicitly, or with ASI), then consider us done
        // with parsing the list of variable declarators.
        if (CanParseSemicolon())
        {
            return true;
        }

        // in the case where we're parsing the variable declarator of a 'for-in' statement, we
        // are done if we see an 'in' keyword in front of us. Same with for-of
        if (IsInOrOfKeyword(Token()))
        {
            return true;
        }

        // ERROR RECOVERY TWEAK:
        // For better error recovery, if we see an '=>' then we just stop immediately.  We've got an
        // arrow function here and it's going to be very unlikely that we'll resynchronize and get
        // another variable declaration.
        if (Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            return true;
        }

        // Keep trying to parse out variable declarators.
        return false;
    }

    // True if positioned at element or terminator of the current list or any enclosing list
    private bool IsInSomeParsingContext()
    {
        // We should be in at least one parsing context, be it SourceElements while parsing
        // a SourceFile, or JSDocComment when lazily parsing JSDoc.
        Debug.Assert(parsingContext != 0, "Missing parsing context");
        for (var kind = 0; kind < (int)ParsingContext.Count; kind++)
        {
            if ((parsingContext & (1 << kind)) != 0)
            {
                if (IsListElement((ParsingContext)kind, /*inErrorRecovery*/ true) || IsListTerminator((ParsingContext)kind))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Parses a list of elements
    private NodeArray ParseList(ParsingContext kind, Func<Node> parseElement)
    {
        var saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)kind;
        var list = new List<Node>();
        var listPos = GetNodePos();

        while (!IsListTerminator(kind))
        {
            if (IsListElement(kind, /*inErrorRecovery*/ false))
            {
                list.Add(ParseListElement(kind, parseElement)!);

                continue;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        parsingContext = saveParsingContext;
        return CreateNodeArray(list, listPos);
    }

    private Node? ParseListElement(ParsingContext parsingContext, Func<Node?> parseElement)
    {
        var node = CurrentNode(parsingContext);
        if (node != null)
        {
            return ConsumeNode(node);
        }

        return parseElement();
    }

    private Node? CurrentNode(ParsingContext parsingContext, int? pos = null)
    {
        // If we don't have a cursor or the parsing context isn't reusable, there's nothing to reuse.
        //
        // If there is an outstanding parse error that we've encountered, but not attached to
        // some node, then we cannot get a node from the old source tree.  This is because we
        // want to mark the next node we encounter as being unusable.
        //
        // Note: This may be too conservative.  Perhaps we could reuse the node and set the bit
        // on it (or its leftmost child) as having the error.  For now though, being conservative
        // is nice and likely won't ever affect perf.
        if (syntaxCursor == null || !IsReusableParsingContext(parsingContext) || parseErrorBeforeNextFinishedNode)
        {
            return null;
        }

        var node = syntaxCursor.CurrentNode(pos ?? scanner.GetTokenFullStart());

        // Can't reuse a missing node.
        // Can't reuse a node that intersected the change range.
        // Can't reuse a node that contains a parse error.  This is necessary so that we
        // produce the same set of errors again.
        if (Utilities.NodeIsMissing(node) || IntersectsIncrementalChange(node!) || Utilities.ContainsParseError(node!))
        {
            return null;
        }

        // We can only reuse a node if it was parsed under the same strict mode that we're
        // currently in.  i.e. if we originally parsed a node in non-strict mode, but then
        // the user added 'using strict' at the top of the file, then we can't use that node
        // again as the presence of strict mode may cause us to parse the tokens in the file
        // differently.
        //
        // Note: we *can* reuse tokens when the strict mode changes.  That's because tokens
        // are unaffected by strict mode.  It's just the parser will decide what to do with it
        // differently depending on what mode it is in.
        //
        // This also applies to all our other context flags as well.
        var nodeContextFlags = node!.Flags & NodeFlags.ContextFlags;
        if (nodeContextFlags != contextFlags)
        {
            return null;
        }

        // Ok, we have a node that looks like it could be reused.  Now verify that it is valid
        // in the current list parsing context that we're currently at.
        if (!CanReuseNode(node, parsingContext))
        {
            return null;
        }

        // if (canHaveJSDoc(node) && node.jsDoc?.jsDocCache) node.jsDoc.jsDocCache = undefined;
        // (jsDocCache is computed by getJSDocCommentsAndTags outside the parser; the parser's nodes never carry it.)
        return node;
    }

    private Node ConsumeNode(Node node)
    {
        // Move the scanner so it is after the node we just consumed.
        scanner.ResetTokenState(node.End);
        NextToken();
        return node;
    }

    private static bool IsReusableParsingContext(ParsingContext parsingContext)
    {
        switch (parsingContext)
        {
            case ParsingContext.ClassMembers:
            case ParsingContext.SwitchClauses:
            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
            case ParsingContext.EnumMembers:
            case ParsingContext.TypeMembers:
            case ParsingContext.VariableDeclarations:
            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
                return true;
        }
        return false;
    }

    private static bool CanReuseNode(Node node, ParsingContext parsingContext)
    {
        switch (parsingContext)
        {
            case ParsingContext.ClassMembers:
                return IsReusableClassMember(node);

            case ParsingContext.SwitchClauses:
                return IsReusableSwitchClause(node);

            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
                return IsReusableStatement(node);

            case ParsingContext.EnumMembers:
                return IsReusableEnumMember(node);

            case ParsingContext.TypeMembers:
                return IsReusableTypeMember(node);

            case ParsingContext.VariableDeclarations:
                return IsReusableVariableDeclaration(node);

            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
                return IsReusableParameter(node);

                // Any other lists we do not care about reusing nodes in.  But feel free to add if
                // you can do so safely.  Danger areas involve nodes that may involve speculative
                // parsing.  If speculative parsing is involved with the node, then the range the
                // parser reached while looking ahead might be in the edited range (see the example
                // in canReuseVariableDeclaratorNode for a good case of this).
                // (See parser.ts for the discussion of the other lists.)
        }

        return false;
    }

    private static bool IsReusableClassMember(Node node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.Constructor:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.SemicolonClassElement:
                    return true;
                case SyntaxKind.MethodDeclaration:
                    // Method declarations are not necessarily reusable.  An object-literal
                    // may have a method calls "constructor(...)" and we must reparse that
                    // into an actual .ConstructorDeclaration.
                    var methodDeclaration = (MethodDeclaration)node;
                    var nameIsConstructor = methodDeclaration.Name.Kind == SyntaxKind.Identifier &&
                        ((Identifier)methodDeclaration.Name).EscapedText is "constructor";

                    return !nameIsConstructor;
            }
        }

        return false;
    }

    private static bool IsReusableSwitchClause(Node node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                    return true;
            }
        }

        return false;
    }

    private static bool IsReusableStatement(Node node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.VariableStatement:
                case SyntaxKind.Block:
                case SyntaxKind.IfStatement:
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.ThrowStatement:
                case SyntaxKind.ReturnStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.BreakStatement:
                case SyntaxKind.ContinueStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.EmptyStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.DebuggerStatement:
                case SyntaxKind.ImportDeclaration:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ExportDeclaration:
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                    return true;
            }
        }

        return false;
    }

    private static bool IsReusableEnumMember(Node node)
    {
        return node.Kind == SyntaxKind.EnumMember;
    }

    private static bool IsReusableTypeMember(Node node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.CallSignature:
                    return true;
            }
        }

        return false;
    }

    private static bool IsReusableVariableDeclaration(Node node)
    {
        if (node.Kind != SyntaxKind.VariableDeclaration)
        {
            return false;
        }

        // Very subtle incremental parsing bug.  Consider the following code:
        //
        //      let v = new List < A, B
        //
        // This is actually legal code.  It's a list of variable declarators "v = new List<A"
        // on one side and "B" on the other. If you then change that to:
        //
        //      let v = new List < A, B >()
        //
        // then we have a problem.  "v = new List<A" doesn't intersect the change range, so we
        // start reparsing at "B" and we completely fail to handle this properly.
        //
        // In order to prevent this, we do not allow a variable declarator to be reused if it
        // has an initializer.
        var variableDeclarator = (VariableDeclaration)node;
        return variableDeclarator.Initializer == null;
    }

    private static bool IsReusableParameter(Node node)
    {
        if (node.Kind != SyntaxKind.Parameter)
        {
            return false;
        }

        // See the comment in isReusableVariableDeclaration for why we do this.
        var parameter = (ParameterDeclaration)node;
        return parameter.Initializer == null;
    }

    // Returns true if we should abort parsing.
    private bool AbortParsingListOrMoveToNextToken(ParsingContext kind)
    {
        ParsingContextErrors(kind);
        if (IsInSomeParsingContext())
        {
            return true;
        }

        NextToken();
        return false;
    }

    private DiagnosticWithDetachedLocation? ParsingContextErrors(ParsingContext context)
    {
        switch (context)
        {
            case ParsingContext.SourceElements:
                return Token() == SyntaxKind.DefaultKeyword
                    ? ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.ExportKeyword))
                    : ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected);
            case ParsingContext.BlockStatements:
                return ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected);
            case ParsingContext.SwitchClauses:
                return ParseErrorAtCurrentToken(Diagnostics.case_or_default_expected);
            case ParsingContext.SwitchClauseStatements:
                return ParseErrorAtCurrentToken(Diagnostics.Statement_expected);
            case ParsingContext.RestProperties: // fallthrough
            case ParsingContext.TypeMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Property_or_signature_expected);
            case ParsingContext.ClassMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Unexpected_token_A_constructor_method_accessor_or_property_was_expected);
            case ParsingContext.EnumMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Enum_member_expected);
            case ParsingContext.HeritageClauseElement:
                return ParseErrorAtCurrentToken(Diagnostics.Expression_expected);
            case ParsingContext.VariableDeclarations:
                return Utilities.IsKeyword(Token())
                    ? ParseErrorAtCurrentToken(Diagnostics._0_is_not_allowed_as_a_variable_declaration_name, Scanner.TokenToString(Token())!)
                    : ParseErrorAtCurrentToken(Diagnostics.Variable_declaration_expected);
            case ParsingContext.ObjectBindingElements:
                return ParseErrorAtCurrentToken(Diagnostics.Property_destructuring_pattern_expected);
            case ParsingContext.ArrayBindingElements:
                return ParseErrorAtCurrentToken(Diagnostics.Array_element_destructuring_pattern_expected);
            case ParsingContext.ArgumentExpressions:
                return ParseErrorAtCurrentToken(Diagnostics.Argument_expression_expected);
            case ParsingContext.ObjectLiteralMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Property_assignment_expected);
            case ParsingContext.ArrayLiteralMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Expression_or_comma_expected);
            case ParsingContext.JSDocParameters:
                return ParseErrorAtCurrentToken(Diagnostics.Parameter_declaration_expected);
            case ParsingContext.Parameters:
                return Utilities.IsKeyword(Token())
                    ? ParseErrorAtCurrentToken(Diagnostics._0_is_not_allowed_as_a_parameter_name, Scanner.TokenToString(Token())!)
                    : ParseErrorAtCurrentToken(Diagnostics.Parameter_declaration_expected);
            case ParsingContext.TypeParameters:
                return ParseErrorAtCurrentToken(Diagnostics.Type_parameter_declaration_expected);
            case ParsingContext.TypeArguments:
                return ParseErrorAtCurrentToken(Diagnostics.Type_argument_expected);
            case ParsingContext.TupleElementTypes:
                return ParseErrorAtCurrentToken(Diagnostics.Type_expected);
            case ParsingContext.HeritageClauses:
                return ParseErrorAtCurrentToken(Diagnostics.Unexpected_token_expected);
            case ParsingContext.ImportOrExportSpecifiers:
                if (Token() == SyntaxKind.FromKeyword)
                {
                    return ParseErrorAtCurrentToken(Diagnostics._0_expected, "}");
                }
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
            case ParsingContext.JsxAttributes:
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
            case ParsingContext.JsxChildren:
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
            case ParsingContext.ImportAttributes:
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_or_string_literal_expected);
            case ParsingContext.JSDocComment:
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
            case ParsingContext.Count:
                Debug.Fail("ParsingContext.Count used as a context"); // Not a real context, only a marker.
                return null;
            default:
                Debug.AssertNever(context);
                return null;
        }
    }

    // Parses a comma-delimited list of elements
    private NodeArray ParseDelimitedList(ParsingContext kind, Func<Node> parseElement, bool considerSemicolonAsDelimiter = false)
    {
        return ParseDelimitedListAllowingUndefined(kind, parseElement, considerSemicolonAsDelimiter)!;
    }

    /// <summary>
    /// The overload of parseDelimitedList whose element parser may return undefined (then the whole list is undefined).
    /// </summary>
    private NodeArray? ParseDelimitedListAllowingUndefined(ParsingContext kind, Func<Node?> parseElement, bool considerSemicolonAsDelimiter = false)
    {
        var saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)kind;
        var list = new List<Node>();
        var listPos = GetNodePos();

        var commaStart = -1; // Meaning the previous token was not a comma
        while (true)
        {
            if (IsListElement(kind, /*inErrorRecovery*/ false))
            {
                var startPos = scanner.GetTokenFullStart();
                var result = ParseListElement(kind, parseElement);
                if (result == null)
                {
                    parsingContext = saveParsingContext;
                    return null;
                }
                list.Add(result);
                commaStart = scanner.GetTokenStart();

                if (ParseOptional(SyntaxKind.CommaToken))
                {
                    // No need to check for a zero length node since we know we parsed a comma
                    continue;
                }

                commaStart = -1; // Back to the state where the last token was not a comma
                if (IsListTerminator(kind))
                {
                    break;
                }

                // We didn't get a comma, and the list wasn't terminated, explicitly parse
                // out a comma so we give a good error message.
                ParseExpected(SyntaxKind.CommaToken, GetExpectedCommaDiagnostic(kind));

                // If the token was a semicolon, and the caller allows that, then skip it and
                // continue.  This ensures we get back on track and don't result in tons of
                // parse errors.  For example, this can happen when people do things like use
                // a semicolon to delimit object literal members.   Note: we'll have already
                // reported an error when we called parseExpected above.
                if (considerSemicolonAsDelimiter && Token() == SyntaxKind.SemicolonToken && !scanner.HasPrecedingLineBreak())
                {
                    NextToken();
                }
                if (startPos == scanner.GetTokenFullStart())
                {
                    // What we're parsing isn't actually remotely recognizable as a element and we've consumed no tokens whatsoever
                    // Consume a token to advance the parser in some way and avoid an infinite loop
                    // This can happen when we're speculatively parsing parenthesized expressions which we think may be arrow functions,
                    // or when a modifier keyword which is disallowed as a parameter name (ie, `static` in strict mode) is supplied
                    NextToken();
                }
                continue;
            }

            if (IsListTerminator(kind))
            {
                break;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        parsingContext = saveParsingContext;
        // Recording the trailing comma is deliberately done after the previous
        // loop, and not just if we see a list terminator. This is because the list
        // may have ended incorrectly, but it is still important to know if there
        // was a trailing comma.
        // Check if the last token was a comma.
        // Always preserve a trailing comma by marking it on the NodeArray
        return CreateNodeArray(list, listPos, /*end*/ null, commaStart >= 0);
    }

    private static DiagnosticMessage? GetExpectedCommaDiagnostic(ParsingContext kind)
    {
        return kind == ParsingContext.EnumMembers ? Diagnostics.An_enum_member_name_must_be_followed_by_a_or : null;
    }

    private NodeArray CreateMissingList()
    {
        var list = CreateNodeArray([], GetNodePos());
        list.IsMissingList = true;
        return list;
    }

    private static bool IsMissingList(NodeArray arr)
    {
        return arr.IsMissingList;
    }

    private NodeArray ParseBracketedList(ParsingContext kind, Func<Node> parseElement, SyntaxKind open, SyntaxKind close)
    {
        if (ParseExpected(open))
        {
            var result = ParseDelimitedList(kind, parseElement);
            ParseExpected(close);
            return result;
        }

        return CreateMissingList();
    }

    private Node ParseEntityName(bool allowReservedWords, DiagnosticMessage? diagnosticMessage = null)
    {
        var pos = GetNodePos();
        Node entity = allowReservedWords ? ParseIdentifierName(diagnosticMessage) : ParseIdentifier(diagnosticMessage);
        while (ParseOptional(SyntaxKind.DotToken))
        {
            if (Token() == SyntaxKind.LessThanToken)
            {
                // The entity is part of a JSDoc-style generic. We will use the gap between `typeName` and
                // `typeArguments` to report it as a grammar error in the checker.
                break;
            }
            entity = FinishNode(
                factory.CreateQualifiedName(
                    entity,
                    (Identifier)ParseRightSideOfDot(allowReservedWords, /*allowPrivateIdentifiers*/ false, /*allowUnicodeEscapeSequenceInIdentifierName*/ true)),
                pos);
        }
        return entity;
    }

    private QualifiedName CreateQualifiedName(Node entity, Identifier name)
    {
        return FinishNode(factory.CreateQualifiedName(entity, name), entity.Pos);
    }

    private Node ParseRightSideOfDot(bool allowIdentifierNames, bool allowPrivateIdentifiers, bool allowUnicodeEscapeSequenceInIdentifierName)
    {
        // Technically a keyword is valid here as all identifiers and keywords are identifier names.
        // However, often we'll encounter this in error situations when the identifier or keyword
        // is actually starting another valid construct.
        //
        // So, we check for the following specific case:
        //
        //      name.
        //      identifierOrKeyword identifierNameOrKeyword
        //
        // Note: the newlines are important here.  For example, if that above code
        // were rewritten into:
        //
        //      name.identifierOrKeyword
        //      identifierNameOrKeyword
        //
        // Then we would consider it valid.  That's because ASI would take effect and
        // the code would be implicitly: "name.identifierOrKeyword; identifierNameOrKeyword".
        // In the first case though, ASI will not take effect because there is not a
        // line terminator after the identifier or keyword.
        if (scanner.HasPrecedingLineBreak() && Scanner.TokenIsIdentifierOrKeyword(Token()))
        {
            var matchesPattern = LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine);

            if (matchesPattern)
            {
                // Report that we need an identifier.  However, report it right after the dot,
                // and not on the next token.  This is because the next token might actually
                // be an identifier and the error would be quite confusing.
                return CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Identifier_expected);
            }
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            var node = ParsePrivateIdentifier();
            return allowPrivateIdentifiers ? node : CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Identifier_expected);
        }

        if (allowIdentifierNames)
        {
            return allowUnicodeEscapeSequenceInIdentifierName ? ParseIdentifierName() : ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        }

        return ParseIdentifier();
    }

    private NodeArray ParseTemplateSpans(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        var list = new List<Node>();
        TemplateSpan node;
        do
        {
            node = ParseTemplateSpan(isTaggedTemplate);
            list.Add(node);
        }
        while (node.Literal.Kind == SyntaxKind.TemplateMiddle);
        return CreateNodeArray(list, pos);
    }

    private TemplateExpression ParseTemplateExpression(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateExpression(
                ParseTemplateHead(isTaggedTemplate),
                ParseTemplateSpans(isTaggedTemplate)),
            pos);
    }

    private TemplateLiteralTypeNode ParseTemplateType()
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateLiteralType(
                ParseTemplateHead(/*isTaggedTemplate*/ false),
                ParseTemplateTypeSpans()),
            pos);
    }

    private NodeArray ParseTemplateTypeSpans()
    {
        var pos = GetNodePos();
        var list = new List<Node>();
        TemplateLiteralTypeSpan node;
        do
        {
            node = ParseTemplateTypeSpan();
            list.Add(node);
        }
        while (node.Literal.Kind == SyntaxKind.TemplateMiddle);
        return CreateNodeArray(list, pos);
    }

    private TemplateLiteralTypeSpan ParseTemplateTypeSpan()
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateLiteralTypeSpan(
                ParseType(),
                ParseLiteralOfTemplateSpan(/*isTaggedTemplate*/ false)),
            pos);
    }

    private Node ParseLiteralOfTemplateSpan(bool isTaggedTemplate)
    {
        if (Token() == SyntaxKind.CloseBraceToken)
        {
            ReScanTemplateToken(isTaggedTemplate);
            return ParseTemplateMiddleOrTemplateTail();
        }
        else
        {
            // TODO(rbuckton): Do we need to call `parseExpectedToken` or can we just call `createMissingNode` directly?
            return ParseExpectedToken(SyntaxKind.TemplateTail, Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.CloseBraceToken));
        }
    }

    private TemplateSpan ParseTemplateSpan(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateSpan(
                AllowInAnd(ParseExpression),
                ParseLiteralOfTemplateSpan(isTaggedTemplate)),
            pos);
    }

    private Node ParseLiteralNode()
    {
        return ParseLiteralLikeNode(Token());
    }

    private TemplateHead ParseTemplateHead(bool isTaggedTemplate)
    {
        if (!isTaggedTemplate && (scanner.GetTokenFlags() & TokenFlags.IsInvalid) != 0)
        {
            ReScanTemplateToken(/*isTaggedTemplate*/ false);
        }
        var fragment = ParseLiteralLikeNode(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateHead, "Template head has wrong token kind");
        return (TemplateHead)fragment;
    }

    private Node ParseTemplateMiddleOrTemplateTail()
    {
        var fragment = ParseLiteralLikeNode(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateMiddle || fragment.Kind == SyntaxKind.TemplateTail, "Template fragment has wrong token kind");
        return fragment;
    }

    private string GetTemplateLiteralRawText(SyntaxKind kind)
    {
        var isLast = kind == SyntaxKind.NoSubstitutionTemplateLiteral || kind == SyntaxKind.TemplateTail;
        var tokenText = scanner.GetTokenText();
        return JsSubstring(tokenText, 1, tokenText.Length - (scanner.IsUnterminated() ? 0 : isLast ? 1 : 2));
    }

    private Node ParseLiteralLikeNode(SyntaxKind kind)
    {
        var pos = GetNodePos();
        Node node;
        if (UtilitiesPublic.IsTemplateLiteralKind(kind))
        {
            node = factory.CreateTemplateLiteralLikeNode(kind, scanner.GetTokenValue(), GetTemplateLiteralRawText(kind), scanner.GetTokenFlags() & TokenFlags.TemplateLiteralLikeFlags);
        }
        else if (kind == SyntaxKind.NumericLiteral)
        {
            // Note that theoretically the following condition would hold true literals like 009,
            // which is not octal. But because of how the scanner separates the tokens, we would
            // never get a token like this. Instead, we would get 00 and 9 as two separate tokens.
            // We also do not need to check for negatives because any prefix operator would be part of a
            // parent unary expression.
            node = factory.CreateNumericLiteral(scanner.GetTokenValue(), scanner.GetNumericLiteralFlags());
        }
        else if (kind == SyntaxKind.StringLiteral)
        {
            node = factory.CreateStringLiteral(scanner.GetTokenValue(), /*isSingleQuote*/ null, scanner.HasExtendedUnicodeEscape());
        }
        else if (UtilitiesPublic.IsLiteralKind(kind))
        {
            node = factory.CreateLiteralLikeNode(kind, scanner.GetTokenValue());
        }
        else
        {
            Debug.Fail();
            return null!;
        }

        if (scanner.HasExtendedUnicodeEscape())
        {
            NodeAccessors.SetHasExtendedUnicodeEscape(node, true);
        }

        if (scanner.IsUnterminated())
        {
            NodeAccessors.SetIsUnterminated(node, true);
        }

        NextToken();
        return FinishNode(node, pos);
    }

    /// <summary>
    /// JavaScript's String.prototype.substring(start, end): arguments are clamped to [0, length] and swapped if
    /// start &gt; end.
    /// </summary>
    private static string JsSubstring(string s, int start, int end)
    {
        start = Math.Clamp(start, 0, s.Length);
        end = Math.Clamp(end, 0, s.Length);
        if (start > end)
        {
            (start, end) = (end, start);
        }
        return s[start..end];
    }
}
