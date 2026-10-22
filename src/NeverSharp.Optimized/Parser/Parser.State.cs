// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// parser.ts: `namespace Parser`, its state and the infrastructure of the grammar (contexts, errors, tokens,
/// speculation, node completion, identifiers and property names).
/// </summary>
internal sealed partial class Parser : BaseNodeFactory
{
    /// <summary>parser.ts: SignatureFlags (the parser's const enum; types.ts has an unrelated one).</summary>
    [Flags]
    internal enum SignatureFlags
    {
        None = 0,
        Yield = 1 << 0,
        Await = 1 << 1,
        Type = 1 << 2,
        IgnoreMissingOpenBrace = 1 << 4,
        JSDoc = 1 << 5,
    }

    /// <summary>parser.ts: SpeculationKind.</summary>
    internal enum SpeculationKind
    {
        TryParse,
        Lookahead,
        Reparse,
    }

    /// <summary>parser.ts: ParsingContext.</summary>
    internal enum ParsingContext
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

    /// <summary>parser.ts: Tristate.</summary>
    internal enum Tristate
    {
        False,
        True,
        Unknown,
    }

    // Share a single scanner across all calls to parse a source file.  This helps speed things
    // up by avoiding the cost of creating/compiling scanners over and over again.
    private readonly Scanner scanner = Scanner.CreateScanner(ScriptTarget.Latest, /*skipTrivia*/ true);

    private const NodeFlags DisallowInAndDecoratorContext = NodeFlags.DisallowInContext | NodeFlags.DecoratorContext;

    private readonly NodeFactory factory;

    // The function scanError, passed to the scanner (TypeScript passes the same function object each time).
    private readonly ErrorCallback scanErrorCallback;

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
    // When adding more parser context flags, consider which is the more common case that the
    // flag will be in.  This should be the 'false' state for that flag.  The reason for this is
    // that we don't store data in our nodes unless the value is in the *non-default* state.  So,
    // for example, more often than code 'allows-in' (or doesn't 'disallow-in').  We opt for
    // 'disallow-in' set to 'false'.  Otherwise, if we had 'allowsIn' set to 'true', then almost
    // all nodes would need extra state on them to store this info.
    //
    // Note: 'allowIn' and 'allowYield' track 1:1 with the [in] and [yield] concepts in the ES6
    // grammar specification.
    //
    // An important thing about these context concepts.  By default they are effectively inherited
    // while parsing through every grammar production.  i.e. if you don't change them, then when
    // you parse a sub-production, it will have the same context values as the parent production.
    // This is great most of the time.  After all, consider all the 'expression' grammar productions
    // and how nearly all of them pass along the 'in' and 'yield' context values:
    //
    // EqualityExpression[In, Yield] :
    //      RelationalExpression[?In, ?Yield]
    //      EqualityExpression[?In, ?Yield] == RelationalExpression[?In, ?Yield]
    //      EqualityExpression[?In, ?Yield] != RelationalExpression[?In, ?Yield]
    //      EqualityExpression[?In, ?Yield] === RelationalExpression[?In, ?Yield]
    //      EqualityExpression[?In, ?Yield] !== RelationalExpression[?In, ?Yield]
    //
    // Where you have to be careful is then understanding what the points are in the grammar
    // where the values are *not* passed along.  For example:
    //
    // SingleNameBinding[Yield,GeneratorParameter]
    //      [+GeneratorParameter]BindingIdentifier[Yield] Initializer[In]opt
    //      [~GeneratorParameter]BindingIdentifier[?Yield]Initializer[In, ?Yield]opt
    //
    // Here this is saying that if the GeneratorParameter context flag is set, that we should
    // explicitly set the 'yield' context flag to false before calling into the BindingIdentifier
    // and we should explicitly unset the 'yield' context flag before calling into the Initializer.
    // production.  Conversely, if the GeneratorParameter context flag is not set, then we
    // should leave the 'yield' context flag alone.
    //
    // Getting this all correct is tricky and requires careful reading of the grammar to
    // understand when these values should be changed versus when they should be inherited.
    //
    // Note: it should not be necessary to save/restore these flags during speculative/lookahead
    // parsing.  These context flags are naturally stored and restored through normal recursive
    // descent parsing and unwinding.
    private NodeFlags contextFlags;

    // Indicates whether we are currently parsing top-level statements.
    private bool topLevel = true;

    // Whether or not we've had a parse error since creating the last AST node.  If we have
    // encountered an error, it will be stored on the next AST node we create.  Parse errors
    // can be broken down into three categories:
    //
    // 1) An error that occurred during scanning.  For example, an unterminated literal, or a
    //    character that was completely not understood.
    //
    // 2) A token was expected, but was not present.  This type of error is commonly produced
    //    by the 'parseExpected' function.
    //
    // 3) A token was present that no parsing function was able to consume.  This type of error
    //    only occurs in the 'abortParsingListOrMoveToNextToken' function when the parser
    //    decides to skip the token.
    //
    // In all of these cases, we want to mark the next node as having had an error before it.
    // With this mark, we can know in incremental settings if this node can be reused, or if
    // we have to reparse it.  If we don't keep this information around, we may just reuse the
    // node.  in that event we would then not produce the same errors as we did before, causing
    // significant confusion problems.
    //
    // Note: it is necessary that this value be saved/restored during speculative/lookahead
    // parsing.  During lookahead parsing, we will often create a node.  That node will have
    // this value attached, and then this value will be set back to 'false'.  If we decide to
    // rewind, we must get back to the same value we had prior to the lookahead.
    //
    // Note: any errors at the end of the file that do not precede a regular node, should get
    // attached to the EOF token.
    private bool parseErrorBeforeNextFinishedNode;

    private bool hasDeprecatedTag;

    private Parser()
    {
        factory = NodeFactory.CreateNodeFactory(NodeFactoryFlags.NoParenthesizerRules | NodeFactoryFlags.NoNodeConverters | NodeFactoryFlags.NoOriginalNode, this);
        scanErrorCallback = ScanError;
    }

    private Node CountNode(Node node)
    {
        nodeCount++;
        return node;
    }

    // Rather than using `createBaseNodeFactory` here, we establish a `BaseNodeFactory` that closes over the
    // constructors above, which are reset each time `initializeState` is called.
    // (The constructors of objectAllocator create the node with the given kind, pos 0 and end 0.)
    Node BaseNodeFactory.CreateBaseSourceFileNode(SyntaxKind kind) => CountNode(NewNode(kind));

    Node BaseNodeFactory.CreateBaseIdentifierNode(SyntaxKind kind) => CountNode(NewNode(kind));

    Node BaseNodeFactory.CreateBasePrivateIdentifierNode(SyntaxKind kind) => CountNode(NewNode(kind));

    Node BaseNodeFactory.CreateBaseTokenNode(SyntaxKind kind) => CountNode(NewNode(kind));

    Node BaseNodeFactory.CreateBaseNode(SyntaxKind kind) => CountNode(NewNode(kind));

    /// <summary>`new XConstructor(kind, /*pos*/ 0, /*end*/ 0)` (utilities.ts: Node, Token, Identifier constructors).</summary>
    private static Node NewNode(SyntaxKind kind)
    {
        var node = NodeAllocator.Create(kind);
        node.Pos = 0;
        node.End = 0;
        return node;
    }

    internal SourceFile ParseSourceFile(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        SyntaxCursor? syntaxCursor,
        bool setParentNodes = false,
        ScriptKind? scriptKind = null,
        Action<SourceFile>? setExternalModuleIndicatorOverride = null,
        JSDocParsingMode jsDocParsingMode = JSDocParsingMode.ParseAll)
    {
        var ensuredScriptKind = Utilities.EnsureScriptKind(fileName, scriptKind);
        if (ensuredScriptKind == ScriptKind.JSON)
        {
            var jsonResult = ParseJsonText(fileName, sourceText, languageVersion, syntaxCursor, setParentNodes);
            CommandLineParser.ConvertToJson(jsonResult, jsonResult.Statements.Count > 0 ? ((ExpressionStatement)jsonResult.Statements[0]).Expression : null, jsonResult.ParseDiagnostics, /*returnValue*/ false, /*jsonConversionNotifier*/ null);
            jsonResult.ReferencedFiles = Core.EmptyArray<FileReference>();
            jsonResult.TypeReferenceDirectives = Core.EmptyArray<FileReference>();
            jsonResult.LibReferenceDirectives = Core.EmptyArray<FileReference>();
            jsonResult.AmdDependencies = Core.EmptyArray<AmdDependency>();
            jsonResult.HasNoDefaultLib = false;
            jsonResult.Pragmas = PragmaMap.Empty;
            return jsonResult;
        }

        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, ensuredScriptKind, jsDocParsingMode);

        var result = ParseSourceFileWorker(languageVersion, setParentNodes, ensuredScriptKind, setExternalModuleIndicatorOverride ?? SetExternalModuleIndicator, jsDocParsingMode);

        ClearState();

        return result;
    }

    private void InitializeState(string fileName, string sourceText, ScriptTarget languageVersion, SyntaxCursor? syntaxCursor, ScriptKind scriptKind, JSDocParsingMode jsDocParsingMode)
    {
        // (TypeScript first captures the node constructors of objectAllocator; NodeAllocator creates the nodes here.)
        this.fileName = TsPath.NormalizePath(fileName);
        this.sourceText = sourceText;
        this.languageVersion = languageVersion;
        this.syntaxCursor = syntaxCursor;
        this.scriptKind = scriptKind;
        languageVariant = Utilities.GetLanguageVariant(scriptKind);

        parseDiagnostics = [];
        parsingContext = 0;
        identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
        identifierCount = 0;
        nodeCount = 0;
        sourceFlags = 0;
        topLevel = true;

        switch (this.scriptKind)
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
        scanner.SetText(this.sourceText);
        scanner.SetOnError(scanErrorCallback);
        scanner.SetScriptTarget(this.languageVersion);
        scanner.SetLanguageVariant(languageVariant);
        scanner.SetScriptKind(this.scriptKind);
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

        var statements = ParseList(ParsingContext.SourceElements, static p => p.ParseStatement());
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
        var jsDoc = Core.MapDefined(Utilities.GetJSDocCommentRanges(node, sourceText), (comment, _) => JSDocParserParseJSDocComment(node, comment.Pos, comment.End - comment.Pos));
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

    internal static void FixupParentReferences(Node rootNode)
    {
        // normally parent references are set during binding. However, for clients that only need
        // a syntax tree, and no semantic features, then the binding process is an unnecessary
        // overhead.  This functions allows us to set all the parents, without all the expense of
        // binding.
        Utilities.SetParentRecursive(rootNode, /*incremental*/ true);
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

    private T DoOutsideOfContext<T>(NodeFlags context, Func<Parser, T> func)
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
            var result = func(this);
            // restore the context flags we just cleared
            SetContextFlag(/*val*/ true, contextFlagsToClear);
            return result;
        }

        // no need to do anything special as we are not in any of the requested contexts
        return func(this);
    }

    private T DoInsideOfContext<T>(NodeFlags context, Func<Parser, T> func)
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
            var result = func(this);
            // reset the context flags we just set
            SetContextFlag(/*val*/ false, contextFlagsToSet);
            return result;
        }

        // no need to do anything special as we are already in all of the requested contexts
        return func(this);
    }

    private T AllowInAnd<T>(Func<Parser, T> func)
    {
        return DoOutsideOfContext(NodeFlags.DisallowInContext, func);
    }

    private T DisallowInAnd<T>(Func<Parser, T> func)
    {
        return DoInsideOfContext(NodeFlags.DisallowInContext, func);
    }

    private T AllowConditionalTypesAnd<T>(Func<Parser, T> func)
    {
        return DoOutsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);
    }

    private T DisallowConditionalTypesAnd<T>(Func<Parser, T> func)
    {
        return DoInsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);
    }

    private T DoInYieldContext<T>(Func<Parser, T> func)
    {
        return DoInsideOfContext(NodeFlags.YieldContext, func);
    }

    private T DoInDecoratorContext<T>(Func<Parser, T> func)
    {
        return DoInsideOfContext(NodeFlags.DecoratorContext, func);
    }

    private T DoInAwaitContext<T>(Func<Parser, T> func)
    {
        return DoInsideOfContext(NodeFlags.AwaitContext, func);
    }

    private T DoOutsideOfAwaitContext<T>(Func<Parser, T> func)
    {
        return DoOutsideOfContext(NodeFlags.AwaitContext, func);
    }

    private T DoInYieldAndAwaitContext<T>(Func<Parser, T> func)
    {
        return DoInsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    private T DoOutsideOfYieldAndAwaitContext<T>(Func<Parser, T> func)
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
        // TypeScript: `parseErrorAtPosition(scanner.getTokenEnd(), length, message, arg0)`; an undefined arg0 is
        // an argument too (`some(args)` is true, and formatting fails only if the message uses it).
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

    private T NextTokenAnd<T>(Func<Parser, T> func)
    {
        NextToken();
        return func(this);
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

    /// <summary>
    /// The scanner's lookAhead and tryScan take a callback without arguments, the parser's callbacks take the parser
    /// (as typescript-go's `lookAhead(callback func(p *Parser) bool)`, internal/parser/parser.go, which passes method
    /// expressions such as `(*Parser).nextTokenIsSlash` instead of closures). The adapter hands the parser's callback
    /// to the scanner through one delegate per adapter instead of a closure per call.
    /// </summary>
    private sealed class SpeculationAdapter<T>
    {
        private readonly Parser parser;
        private readonly Func<T> invoke;
        private Func<Parser, T>? callback;

        internal SpeculationAdapter(Parser parser)
        {
            this.parser = parser;
            invoke = Run;
        }

        internal Func<T> Prepare(Func<Parser, T> callback)
        {
            this.callback = callback;
            return invoke;
        }

        private T Run()
        {
            // Read before running: the callback may speculate again through this adapter.
            var cb = callback!;
            callback = null;
            return cb(parser);
        }
    }

    private SpeculationAdapter<bool>? boolSpeculationAdapter;
    private SpeculationAdapter<Tristate>? tristateSpeculationAdapter;
    private SpeculationAdapter<object?>? objectSpeculationAdapter;

    /// <summary>`scanner.lookAhead(callback)` or `scanner.tryScan(callback)` with the parser's callback.</summary>
    private T ScannerSpeculate<T>(Func<Parser, T> callback, bool isLookahead)
    {
        if (typeof(T) == typeof(bool))
        {
            var adapter = boolSpeculationAdapter ??= new SpeculationAdapter<bool>(this);
            var invoke = adapter.Prepare((Func<Parser, bool>)(object)callback);
            return (T)(object)(isLookahead ? scanner.LookAhead(invoke) : scanner.TryScan(invoke));
        }
        if (typeof(T) == typeof(Tristate))
        {
            var adapter = tristateSpeculationAdapter ??= new SpeculationAdapter<Tristate>(this);
            var invoke = adapter.Prepare((Func<Parser, Tristate>)(object)callback);
            return (T)(object)(isLookahead ? scanner.LookAhead(invoke) : scanner.TryScan(invoke));
        }
        if (!typeof(T).IsValueType)
        {
            // Func<Parser, TNode> converts to Func<Parser, object?> by delegate variance (no allocation).
            var adapter = objectSpeculationAdapter ??= new SpeculationAdapter<object?>(this);
            var invoke = adapter.Prepare((Func<Parser, object?>)(object)callback);
            return (T)(isLookahead ? scanner.LookAhead(invoke) : scanner.TryScan(invoke))!;
        }
        return ScannerSpeculateWithClosure(callback, isLookahead);
    }

    // Other value types (none at present): a closure per call. (A separate method, so that the closure of the
    // fallback is not allocated on the paths above.)
    private T ScannerSpeculateWithClosure<T>(Func<Parser, T> callback, bool isLookahead)
    {
        return isLookahead ? scanner.LookAhead(() => callback(this)) : scanner.TryScan(() => callback(this));
    }

    private T SpeculationHelper<T>(Func<Parser, T> callback, SpeculationKind speculationKind)
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
        var result = ScannerSpeculate(callback, /*isLookahead*/ speculationKind != SpeculationKind.TryParse);

        Debug.Assert(saveContextFlags == contextFlags);

        // If our callback returned something 'falsy' or we're just looking ahead,
        // then unconditionally restore us to where we were.
        if (!IsTruthy(result) || speculationKind != SpeculationKind.TryParse)
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
    private T LookAhead<T>(Func<Parser, T> callback)
    {
        return SpeculationHelper(callback, SpeculationKind.Lookahead);
    }

    /// <summary>
    /// Invokes the provided callback.  If the callback returns something falsy, then it restores
    /// the parser to the state it was in immediately prior to invoking the callback.  If the
    /// callback returns something truthy, then the parser state is not rolled back.  The result
    /// of invoking the callback is returned from this function.
    /// </summary>
    private T TryParse<T>(Func<Parser, T> callback)
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

    // `Object.keys(textToKeywordObj).filter(keyword => keyword.length > 2)` (in the object's key order).
    private static readonly List<string> ViableKeywordSuggestions = CreateViableKeywordSuggestions();

    private static List<string> CreateViableKeywordSuggestions()
    {
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
        if (FactoryNodeTests.IsTaggedTemplateExpression(node))
        {
            var template = ((TaggedTemplateExpression)node).Template;
            ParseErrorAt(Scanner.SkipTrivia(sourceText, template.Pos), template.End, Diagnostics.Module_declaration_names_may_only_use_or_quoted_strings);
            return;
        }

        // Otherwise, if this isn't a well-known keyword-like identifier, give the generic fallback message.
        var expressionText = FactoryNodeTests.IsIdentifier(node) ? UtilitiesPublic.IdText((Identifier)node) : null;
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
        var suggestion = Core.GetSpellingSuggestion(expressionText, ViableKeywordSuggestions, n => n) ?? GetSpaceSuggestion(expressionText);
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
            CreateMissingNode(t, /*reportAtCurrentPosition*/ false, diagnosticMessage ?? Diagnostics._0_expected, !string.IsNullOrEmpty(arg0) ? arg0 : Scanner.TokenToString(t)!);
    }

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
        return CreateMissingNode(t, /*reportAtCurrentPosition*/ false, Diagnostics._0_expected, Scanner.TokenToString(t));
    }

    private Node ParseTokenNode()
    {
        var pos = GetNodePos();
        var kind = Token();
        NextToken();
        return FinishNode(factory.CreateToken(kind), pos);
    }

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

    /// <summary>
    /// TypeScript: `createNodeArray(elements: T[], pos, end?, hasTrailingComma?)`. The elements are a plain array
    /// (a NodeArray object without the NodeArray properties, see NodeArray.HasNodeArrayProperties).
    /// </summary>
    private NodeArray CreateNodeArray(NodeArray elements, int pos, int? end = null, bool? hasTrailingComma = null)
    {
        var array = NodeFactory.CreateNodeArray(elements, hasTrailingComma);
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

    private Node CreateMissingNode(SyntaxKind kind, bool reportAtCurrentPosition, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
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
        var result = kind == SyntaxKind.Identifier ? factory.CreateIdentifier("", /*originalKeywordKind*/ null) :
            UtilitiesPublic.IsTemplateLiteralKind(kind) ? factory.CreateTemplateLiteralLikeNode(kind, "", "", /*templateFlags*/ null) :
            kind == SyntaxKind.NumericLiteral ? factory.CreateNumericLiteral("", /*numericLiteralFlags*/ null) :
            kind == SyntaxKind.StringLiteral ? factory.CreateStringLiteral("", /*isSingleQuote*/ null) :
            kind == SyntaxKind.MissingDeclaration ? factory.CreateMissingDeclaration() :
            factory.CreateToken(kind);
        return FinishNode(result, pos);
    }

    private T CreateMissingNode<T>(SyntaxKind kind, bool reportAtCurrentPosition, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
        where T : Node
    {
        return (T)CreateMissingNode(kind, reportAtCurrentPosition, diagnosticMessage, args);
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
        var expression = AllowInAnd(static p => p.ParseExpression());
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
        return Token() == t && TryParse(static p => p.NextTokenCanFollowModifier());
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
                    return LookAhead(static p => p.NextTokenCanFollowDefaultKeyword());
                }
                if (Token() == SyntaxKind.TypeKeyword)
                {
                    return LookAhead(static p => p.NextTokenCanFollowExportModifier());
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
        return UtilitiesPublic.IsModifierKind(Token()) && TryParse(static p => p.NextTokenCanFollowModifier());
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
            || (Token() == SyntaxKind.AbstractKeyword && LookAhead(static p => p.NextTokenIsClassKeywordOnSameLine()))
            || (Token() == SyntaxKind.AsyncKeyword && LookAhead(static p => p.NextTokenIsFunctionKeywordOnSameLine()));
    }
}
