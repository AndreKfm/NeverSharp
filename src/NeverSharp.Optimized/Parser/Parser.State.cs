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
    private readonly Action<int, int, DiagnosticMessage> reportPragmaDiagnosticCallback;

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
    // Track O (O5a): the texts of the parse (the store's): character arena and interned identifiers (TypeScript's
    // `identifiers` map).
    private TextTable texts = null!;
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

    // Track O: the node store of the current parse (created by the caller, kept by the result).
    private NodeStore store = null!;

    private Parser()
    {
        factory = NodeFactory.CreateNodeFactory(NodeFactoryFlags.NoParenthesizerRules | NodeFactoryFlags.NoNodeConverters | NodeFactoryFlags.NoOriginalNode, this);
        scanErrorCallback = ScanError;
        reportPragmaDiagnosticCallback = ReportPragmaDiagnostic;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CountNode(Node node)
    {
        nodeCount++;
        return node;
    }

    // Rather than using `createBaseNodeFactory` here, we establish a `BaseNodeFactory` that closes over the
    // constructors above, which are reset each time `initializeState` is called.
    // (The constructors of objectAllocator create the node with the given kind, pos 0 and end 0.)
    Node BaseNodeFactory.CreateBaseSourceFileNode(SyntaxKind kind) => CountNode(NewNode(store, kind));

    Node BaseNodeFactory.CreateBaseIdentifierNode(SyntaxKind kind) => CountNode(NewNode(store, kind));

    Node BaseNodeFactory.CreateBasePrivateIdentifierNode(SyntaxKind kind) => CountNode(NewNode(store, kind));

    Node BaseNodeFactory.CreateBaseTokenNode(SyntaxKind kind) => CountNode(NewNode(store, kind));

    Node BaseNodeFactory.CreateBaseNode(SyntaxKind kind) => CountNode(NewNode(store, kind));

    /// <summary>`new XConstructor(kind, /*pos*/ 0, /*end*/ 0)` (utilities.ts: Node, Token, Identifier constructors).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node NewNode(NodeStore store, SyntaxKind kind)
    {
        var node = store.Create(kind);
        store.SetPos(node, 0);
        store.SetEnd(node, 0);
        return node;
    }

    /// <summary>Makes <paramref name="nodeStore"/> the store the parser and its factory create nodes in.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void UseStore(NodeStore nodeStore)
    {
        store = nodeStore;
        factory.store = nodeStore;
        texts = nodeStore?.Texts!;
    }

    internal Node ParseSourceFile(
        NodeStore nodeStore,
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        SyntaxCursor? syntaxCursor,
        bool setParentNodes = false,
        ScriptKind? scriptKind = null,
        Action<Node>? setExternalModuleIndicatorOverride = null,
        JSDocParsingMode jsDocParsingMode = JSDocParsingMode.ParseAll)
    {
        UseStore(nodeStore);
        var ensuredScriptKind = Utilities.EnsureScriptKind(fileName, scriptKind);
        if (ensuredScriptKind == ScriptKind.JSON)
        {
            var jsonResult = ParseJsonText(nodeStore, fileName, sourceText, languageVersion, syntaxCursor, setParentNodes);
            CommandLineParser.ConvertToJson(store, jsonResult, store.Length(store.Statements(jsonResult)) > 0 ? store.Expression(store.Element(store.Statements(jsonResult), 0)) : default, store.ParseDiagnostics(jsonResult), /*returnValue*/ false, /*jsonConversionNotifier*/ null);
            store.SetReferencedFiles(jsonResult, Core.EmptyArray<FileReference>());
            store.SetTypeReferenceDirectives(jsonResult, Core.EmptyArray<FileReference>());
            store.SetLibReferenceDirectives(jsonResult, Core.EmptyArray<FileReference>());
            store.SetAmdDependencies(jsonResult, Core.EmptyArray<AmdDependency>());
            store.SetHasNoDefaultLib(jsonResult, false);
            store.SetPragmas(jsonResult, PragmaMap.Empty);
            return jsonResult;
        }

        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, ensuredScriptKind, jsDocParsingMode);

        var result = ParseSourceFileWorker(languageVersion, setParentNodes, ensuredScriptKind, setExternalModuleIndicatorOverride ?? ((a0) => SetExternalModuleIndicator(store, a0)), jsDocParsingMode);

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
        scanner.UseTexts(texts); // Track O: token values go to the parse's texts
        scanner.SetOnError(scanErrorCallback);
        scanner.SetScriptTarget(this.languageVersion);
        scanner.SetLanguageVariant(languageVariant);
        scanner.SetScriptKind(this.scriptKind);
        scanner.SetJSDocParsingMode(jsDocParsingMode);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
        notParenthesizedArrow = null;
        topLevel = true;
    }

    private Node ParseSourceFileWorker(ScriptTarget languageVersion, bool setParentNodes, ScriptKind scriptKind, Action<Node> setExternalModuleIndicator, JSDocParsingMode jsDocParsingMode)
    {
        var isDeclarationFile = IsDeclarationFileName(fileName);
        if (isDeclarationFile)
        {
            contextFlags |= NodeFlags.Ambient;
        }

        sourceFlags = contextFlags;

        // Prime the scanner.
        NextToken();

        var statements = ParseList(ParsingContext.SourceElements, new F.ParseStatement());
        Debug.Assert(Token() == SyntaxKind.EndOfFileToken);
        var endHasJSDoc = HasPrecedingJSDocComment();
        var endOfFileToken = WithJSDoc(ParseTokenNode(), endHasJSDoc);

        var sourceFile = CreateSourceFile(fileName, languageVersion, scriptKind, isDeclarationFile, statements, endOfFileToken, sourceFlags, setExternalModuleIndicator);

        // A member of ReadonlyArray<T> isn't assignable to a member of T[] (and prevents a direct cast) - but this is where we set up those members so they can be readonly in the future
        ProcessCommentPragmas(store, sourceFile, sourceText);
        ProcessPragmasIntoFields(store, sourceFile, reportPragmaDiagnosticCallback);

        store.SetCommentDirectives(sourceFile, scanner.GetCommentDirectives());
        store.SetNodeCount(sourceFile, nodeCount);
        store.SetIdentifierCount(sourceFile, identifierCount);
        store.SetIdentifiers(sourceFile, texts);
        store.SetParseDiagnostics(sourceFile, Utilities.AttachFileToDiagnostics(store, parseDiagnostics, sourceFile));
        store.SetJsDocParsingMode(sourceFile, jsDocParsingMode);
        if (jsDocDiagnostics != null)
        {
            store.SetJsDocDiagnostics(sourceFile, Utilities.AttachFileToDiagnostics(store, jsDocDiagnostics, sourceFile));
        }

        if (setParentNodes)
        {
            FixupParentReferences(store, sourceFile);
        }

        return sourceFile;
    }

    // The inner function reportPragmaDiagnostic of parseSourceFileWorker (Track O, O-NA: a method, its delegate created
    // once per parser; it reads the state of the current parse).
    private void ReportPragmaDiagnostic(int pos, int end, DiagnosticMessage diagnostic)
    {
        parseDiagnostics.Add(Utilities.CreateDetachedDiagnostic(fileName, sourceText, pos, end, diagnostic));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node WithJSDoc(Node node, bool hasJSDoc)
    {
        if (!hasJSDoc)
        {
            return node;
        }

        Debug.Assert(store.JsDoc(node) == null); // Should only be called once per node
        // TypeScript: `mapDefined(getJSDocCommentRanges(node, sourceText), comment => JSDocParser.parseJSDocComment(node,
        // comment.pos, comment.end - comment.pos))`, set as the node's jsDoc if not empty. Track O (O-NA): the comment
        // ranges of getJSDocCommentRanges are iterated without a list, and the jsDoc list starts with its first comment.
        List<Node>? jsDoc = null;
        if (Utilities.HasTrailingJSDocCommentRanges(store.Kind(node)))
        {
            var trailing = new Scanner.CommentRangeIterator(sourceText, store.Pos(node), /*trailing*/ true);
            while (trailing.MoveNext(out var range))
            {
                jsDoc = AddJSDocComment(jsDoc, node, range);
            }
        }
        var leading = new Scanner.CommentRangeIterator(sourceText, store.Pos(node), /*trailing*/ false);
        while (leading.MoveNext(out var range))
        {
            jsDoc = AddJSDocComment(jsDoc, node, range);
        }
        if (jsDoc != null)
        {
            store.SetJsDoc(node, jsDoc);
        }
        if (hasDeprecatedTag)
        {
            hasDeprecatedTag = false;
            store.SetFlags(node, store.Flags(node) | NodeFlags.Deprecated);
        }
        return node;
    }

    // A comment range of getJSDocCommentRanges (it passes the filter there) parsed and added to the node's jsDoc list.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private List<Node>? AddJSDocComment(List<Node>? jsDoc, Node node, CommentRange range)
    {
        if (!Utilities.IsJSDocCommentRange(sourceText, range, store.End(node)))
        {
            return jsDoc;
        }
        var comment = JSDocParserParseJSDocComment(node, range.Pos, range.End - range.Pos);
        return comment.IsNone ? jsDoc : AppendJSDoc(jsDoc, comment);
    }

    [NoAllocUnchecked("per-file result: the jsDoc list of a node")]
    private static List<Node> AppendJSDoc(List<Node>? jsDoc, Node comment)
    {
        jsDoc ??= [];
        jsDoc.Add(comment);
        return jsDoc;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static void FixupParentReferences(NodeStore store, Node rootNode)
    {
        // normally parent references are set during binding. However, for clients that only need
        // a syntax tree, and no semantic features, then the binding process is an unnecessary
        // overhead.  This functions allows us to set all the parents, without all the expense of
        // binding.
        Utilities.SetParentRecursive(store, rootNode, /*incremental*/ true);
    }

    private Node CreateSourceFile(
        string fileName,
        ScriptTarget languageVersion,
        ScriptKind scriptKind,
        bool isDeclarationFile,
        NodeList statements,
        Node endOfFileToken,
        NodeFlags flags,
        Action<Node> setExternalModuleIndicator)
    {
        // code from createNode is inlined here so createNode won't have to deal with special case of creating source files
        // this is quite rare comparing to other nodes and createNode should be as fast as possible
        var sourceFile = factory.CreateSourceFile(statements, endOfFileToken, flags);
        Utilities.SetTextRangePosWidth(store, sourceFile, 0, sourceText.Length);
        SetFields(sourceFile);

        // If we parsed this as an external module, it may contain top-level await
        if (!isDeclarationFile && IsExternalModule(store, sourceFile) && (store.TransformFlags(sourceFile) & TransformFlags.ContainsPossibleTopLevelAwait) != 0)
        {
            var oldSourceFile = sourceFile;
            sourceFile = ReparseTopLevelAwait(sourceFile);
            if (oldSourceFile != sourceFile)
            {
                SetFields(sourceFile);
            }
        }

        return sourceFile;

        void SetFields(Node sourceFile)
        {
            store.SetSourceFileText(sourceFile, sourceText);
            store.SetBindDiagnostics(sourceFile, []);
            store.SetBindSuggestionDiagnostics(sourceFile, null);
            store.SetLanguageVersion(sourceFile, languageVersion);
            store.SetFileName(sourceFile, fileName);
            store.SetLanguageVariant(sourceFile, Utilities.GetLanguageVariant(scriptKind));
            store.SetIsDeclarationFile(sourceFile, isDeclarationFile);
            store.SetScriptKind(sourceFile, scriptKind);

            setExternalModuleIndicator(sourceFile);
            store.SetSetExternalModuleIndicator(sourceFile, setExternalModuleIndicator);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void SetDisallowInContext(bool val)
    {
        SetContextFlag(val, NodeFlags.DisallowInContext);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void SetYieldContext(bool val)
    {
        SetContextFlag(val, NodeFlags.YieldContext);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void SetDecoratorContext(bool val)
    {
        SetContextFlag(val, NodeFlags.DecoratorContext);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void SetAwaitContext(bool val)
    {
        SetContextFlag(val, NodeFlags.AwaitContext);
    }

    // Track O (O-NA): the functions passed to the context helpers below are callback structs (IParserCallback); the
    // overloads without a result type argument are for callbacks that return a node.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node DoOutsideOfContext<TCallback>(NodeFlags context, TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoOutsideOfContext<TCallback, Node>(context, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node DoInsideOfContext<TCallback>(NodeFlags context, TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoInsideOfContext<TCallback, Node>(context, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T DoOutsideOfContext<TCallback, T>(NodeFlags context, TCallback func)
        where TCallback : struct, IParserCallback<T>
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
            var result = func.Invoke(this);
            // restore the context flags we just cleared
            SetContextFlag(/*val*/ true, contextFlagsToClear);
            return result;
        }

        // no need to do anything special as we are not in any of the requested contexts
        return func.Invoke(this);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T DoInsideOfContext<TCallback, T>(NodeFlags context, TCallback func)
        where TCallback : struct, IParserCallback<T>
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
            var result = func.Invoke(this);
            // reset the context flags we just set
            SetContextFlag(/*val*/ false, contextFlagsToSet);
            return result;
        }

        // no need to do anything special as we are already in all of the requested contexts
        return func.Invoke(this);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node AllowInAnd<TCallback>(TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoOutsideOfContext<TCallback, Node>(NodeFlags.DisallowInContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T AllowInAnd<TCallback, T>(TCallback func)
        where TCallback : struct, IParserCallback<T>
    {
        return DoOutsideOfContext<TCallback, T>(NodeFlags.DisallowInContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node DisallowInAnd<TCallback>(TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoInsideOfContext<TCallback, Node>(NodeFlags.DisallowInContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node AllowConditionalTypesAnd<TCallback>(TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoOutsideOfContext<TCallback, Node>(NodeFlags.DisallowConditionalTypesContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node DisallowConditionalTypesAnd<TCallback>(TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoInsideOfContext<TCallback, Node>(NodeFlags.DisallowConditionalTypesContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node DoInYieldContext<TCallback>(TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoInsideOfContext<TCallback, Node>(NodeFlags.YieldContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node DoInDecoratorContext<TCallback>(TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoInsideOfContext<TCallback, Node>(NodeFlags.DecoratorContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T DoInAwaitContext<TCallback, T>(TCallback func)
        where TCallback : struct, IParserCallback<T>
    {
        return DoInsideOfContext<TCallback, T>(NodeFlags.AwaitContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T DoOutsideOfAwaitContext<TCallback, T>(TCallback func)
        where TCallback : struct, IParserCallback<T>
    {
        return DoOutsideOfContext<TCallback, T>(NodeFlags.AwaitContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node DoInYieldAndAwaitContext<TCallback>(TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        return DoInsideOfContext<TCallback, Node>(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T DoOutsideOfYieldAndAwaitContext<TCallback, T>(TCallback func)
        where TCallback : struct, IParserCallback<T>
    {
        return DoOutsideOfContext<TCallback, T>(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool InContext(NodeFlags flags)
    {
        return (contextFlags & flags) != 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool InYieldContext()
    {
        return InContext(NodeFlags.YieldContext);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool InDisallowInContext()
    {
        return InContext(NodeFlags.DisallowInContext);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool InDisallowConditionalTypesContext()
    {
        return InContext(NodeFlags.DisallowConditionalTypesContext);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool InDecoratorContext()
    {
        return InContext(NodeFlags.DecoratorContext);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool InAwaitContext()
    {
        return InContext(NodeFlags.AwaitContext);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private DiagnosticWithDetachedLocation? ParseErrorAtCurrentToken(DiagnosticMessage message, params object?[] args)
    {
        return ParseErrorAt(scanner.GetTokenStart(), scanner.GetTokenEnd(), message, args);
    }

    // Track O (O-NA): overloads with string arguments instead of a params array, so the callers allocate no array (the
    // diagnostic and its arguments are created on the error path below).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private DiagnosticWithDetachedLocation? ParseErrorAtCurrentToken(DiagnosticMessage message, string? arg0)
    {
        return ParseErrorAtPositionWithArguments(scanner.GetTokenStart(), scanner.GetTokenEnd() - scanner.GetTokenStart(), message, arg0, null, 1);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private DiagnosticWithDetachedLocation? ParseErrorAtCurrentToken(DiagnosticMessage message, string? arg0, string? arg1)
    {
        return ParseErrorAtPositionWithArguments(scanner.GetTokenStart(), scanner.GetTokenEnd() - scanner.GetTokenStart(), message, arg0, arg1, 2);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private DiagnosticWithDetachedLocation? ParseErrorAt(int start, int end, DiagnosticMessage message, string? arg0)
    {
        return ParseErrorAtPositionWithArguments(start, end - start, message, arg0, null, 1);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private DiagnosticWithDetachedLocation? ParseErrorAt(int start, int end, DiagnosticMessage message, string? arg0, string? arg1)
    {
        return ParseErrorAtPositionWithArguments(start, end - start, message, arg0, arg1, 2);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void ParseErrorAtRange(Node range, DiagnosticMessage message, string? arg0)
    {
        ParseErrorAtPositionWithArguments(store.Pos(range), store.End(range) - store.Pos(range), message, arg0, null, 1);
    }

    // `parseErrorAt(start, end, message, getTextOfNodeFromSourceText(sourceText, node))` (Track O: the text is taken on
    // the error path).
    [NoAllocUnchecked("error path: the message names the text of a node")]
    private void ParseErrorWithTextOfNode(int start, int end, DiagnosticMessage message, Node node)
    {
        ParseErrorAt(start, end, message, Utilities.GetTextOfNodeFromSourceText(store, sourceText, node));
    }

    [NoAllocUnchecked("error path: creates a diagnostic and its argument array")]
    private DiagnosticWithDetachedLocation? ParseErrorAtPositionWithArguments(int start, int length, DiagnosticMessage message, string? arg0, string? arg1, int count)
    {
        return count == 1 ? ParseErrorAtPosition(start, length, message, arg0) : ParseErrorAtPosition(start, length, message, arg0, arg1);
    }

    [NoAllocUnchecked("error path: creates a diagnostic")]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private DiagnosticWithDetachedLocation? ParseErrorAt(int start, int end, DiagnosticMessage message, params object?[] args)
    {
        return ParseErrorAtPosition(start, end - start, message, args);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void ParseErrorAtRange(Node range, DiagnosticMessage message, params object?[] args)
    {
        ParseErrorAt(store.Pos(range), store.End(range), message, args);
    }

    private void ScanError(DiagnosticMessage message, int length, object? arg0)
    {
        // TypeScript: `parseErrorAtPosition(scanner.getTokenEnd(), length, message, arg0)`; an undefined arg0 is
        // an argument too (`some(args)` is true, and formatting fails only if the message uses it).
        ParseErrorAtPosition(scanner.GetTokenEnd(), length, message, arg0);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int GetNodePos()
    {
        return scanner.GetTokenFullStart();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind Token()
    {
        return currentToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind NextTokenWithoutCheck()
    {
        return currentToken = scanner.Scan();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node NextTokenAnd<TCallback>(TCallback func)
        where TCallback : struct, IParserCallback<Node>
    {
        NextToken();
        return func.Invoke(this);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind NextTokenJSDoc()
    {
        return currentToken = scanner.ScanJsDocToken();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind NextJSDocCommentTextToken(bool inBackticks)
    {
        return currentToken = scanner.ScanJSDocCommentTextToken(inBackticks);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ReScanGreaterToken()
    {
        return currentToken = scanner.ReScanGreaterToken();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ReScanSlashToken()
    {
        return currentToken = scanner.ReScanSlashToken();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ReScanTemplateToken(bool isTaggedTemplate)
    {
        return currentToken = scanner.ReScanTemplateToken(isTaggedTemplate);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ReScanLessThanToken()
    {
        return currentToken = scanner.ReScanLessThanToken();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ReScanHashToken()
    {
        return currentToken = scanner.ReScanHashToken();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanJsxIdentifier()
    {
        return currentToken = scanner.ScanJsxIdentifier();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanJsxText()
    {
        return currentToken = scanner.ScanJsxToken();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanJsxAttributeValue()
    {
        return currentToken = scanner.ScanJsxAttributeValue();
    }

    /// <summary>
    /// The scanner's lookAhead and tryScan take a callback without arguments, the parser's callbacks take the parser
    /// (as typescript-go's `lookAhead(callback func(p *Parser) bool)`, internal/parser/parser.go). Track O (O-NA): both
    /// are structs; this one hands the parser's callback to the scanner.
    /// </summary>
    private readonly struct ScannerSpeculation<TCallback, T> : IScannerCallback<T>
        where TCallback : struct, IParserCallback<T>
    {
        private readonly Parser parser;
        private readonly TCallback callback;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal ScannerSpeculation(Parser parser, TCallback callback)
        {
            this.parser = parser;
            this.callback = callback;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public T Invoke() => callback.Invoke(parser);
    }

    // `() => scanner.reScanInvalidIdentifier() === SyntaxKind.Identifier` (createIdentifier).
    private readonly struct ReScanInvalidIdentifierIsIdentifier : IScannerCallback<bool>
    {
        private readonly Scanner scanner;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal ReScanInvalidIdentifierIsIdentifier(Scanner scanner)
        {
            this.scanner = scanner;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public bool Invoke() => scanner.ReScanInvalidIdentifier() == SyntaxKind.Identifier;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T SpeculationHelper<TCallback, T>(TCallback callback, SpeculationKind speculationKind)
        where TCallback : struct, IParserCallback<T>
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

        // Track O: the nodes created from here on are discarded with the arena if the parser rolls back.
        var saveStoreMark = store.GetMark();

        // If we're only looking ahead, then tell the scanner to only lookahead as well.
        // Otherwise, if we're actually speculatively parsing, then tell the scanner to do the
        // same.
        var speculation = new ScannerSpeculation<TCallback, T>(this, callback);
        var result = speculationKind != SpeculationKind.TryParse
            ? scanner.LookAhead<ScannerSpeculation<TCallback, T>, T>(speculation)
            : scanner.TryScan<ScannerSpeculation<TCallback, T>, T>(speculation);

        Debug.Assert(saveContextFlags == contextFlags);

        // If our callback returned something 'falsy' or we're just looking ahead,
        // then unconditionally restore us to where we were.
        if (!Core.IsTruthyValue(result) || speculationKind != SpeculationKind.TryParse)
        {
            currentToken = saveToken;
            if (speculationKind != SpeculationKind.Reparse)
            {
                RemoveParseDiagnosticsFrom(saveParseDiagnosticsLength);

                // Nothing refers to the nodes of a rolled-back lookahead or tryParse (a reparse keeps its nodes).
                store.Truncate(saveStoreMark);
            }
            parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
        }

        return result;
    }

    // `parseDiagnostics.length = length` (the diagnostics of a rolled-back speculation are dropped).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void RemoveParseDiagnosticsFrom(int length)
    {
        if (parseDiagnostics.Count > length)
        {
            parseDiagnostics.RemoveRange(length, parseDiagnostics.Count - length);
        }
    }

    /// <summary>
    /// Invokes the provided callback then unconditionally restores the parser to the state it
    /// was in immediately prior to invoking the callback.  The result of invoking the callback
    /// is returned from this function.
    /// (Track O: for callbacks that return a boolean; the overload with a result type argument for the others.)
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool LookAhead<TCallback>(TCallback callback)
        where TCallback : struct, IParserCallback<bool>
    {
        return SpeculationHelper<TCallback, bool>(callback, SpeculationKind.Lookahead);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T LookAhead<TCallback, T>(TCallback callback)
        where TCallback : struct, IParserCallback<T>
    {
        return SpeculationHelper<TCallback, T>(callback, SpeculationKind.Lookahead);
    }

    /// <summary>
    /// Invokes the provided callback.  If the callback returns something falsy, then it restores
    /// the parser to the state it was in immediately prior to invoking the callback.  If the
    /// callback returns something truthy, then the parser state is not rolled back.  The result
    /// of invoking the callback is returned from this function.
    /// (Track O: for callbacks that return a node; the overload with a result type argument for the others.)
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParse<TCallback>(TCallback callback)
        where TCallback : struct, IParserCallback<Node>
    {
        return SpeculationHelper<TCallback, Node>(callback, SpeculationKind.TryParse);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T TryParse<TCallback, T>(TCallback callback)
        where TCallback : struct, IParserCallback<T>
    {
        return SpeculationHelper<TCallback, T>(callback, SpeculationKind.TryParse);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAllocUnchecked("error path: finds a better message for a missing semicolon (strings, spelling suggestions)")]
    private void ParseErrorForMissingSemicolonAfter(Node node)
    {
        // Tagged template literals are sometimes used in places where only simple strings are allowed, i.e.:
        //   module `M1` {
        //   ^^^^^^^^^^^ This block is parsed as a template literal like module`M1`.
        if (FactoryNodeTests.IsTaggedTemplateExpression(store, node))
        {
            var template = store.Template(node);
            ParseErrorAt(Scanner.SkipTrivia(sourceText, store.Pos(template)), store.End(template), Diagnostics.Module_declaration_names_may_only_use_or_quoted_strings);
            return;
        }

        // Otherwise, if this isn't a well-known keyword-like identifier, give the generic fallback message.
        var expressionText = FactoryNodeTests.IsIdentifier(store, node) ? texts.GetString(UtilitiesPublic.IdText(store, node)) : null; // (Track O: a string for the error message)
        if (string.IsNullOrEmpty(expressionText) || !Scanner.IsIdentifierText(expressionText, languageVersion))
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.SemicolonToken));
            return;
        }

        var pos = Scanner.SkipTrivia(sourceText, store.Pos(node));

        // Some known keywords are likely signs of syntax being used improperly.
        switch (expressionText)
        {
            case "const":
            case "let":
            case "var":
                ParseErrorAt(pos, store.End(node), Diagnostics.Variable_declaration_not_allowed_at_this_location);
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
            ParseErrorAt(pos, store.End(node), Diagnostics.Unknown_keyword_or_identifier_Did_you_mean_0, suggestion);
            return;
        }

        // Unknown tokens are handled with their own errors in the scanner
        if (Token() == SyntaxKind.Unknown)
        {
            return;
        }

        // Otherwise, we know this some kind of unknown word, not just a missing expected semicolon.
        ParseErrorAt(pos, store.End(node), Diagnostics.Unexpected_keyword_or_identifier);
    }

    /// <summary>
    /// Reports a diagnostic error for the current token being an invalid name.
    /// </summary>
    /// <param name="nameDiagnostic">Diagnostic to report for all other cases.</param>
    /// <param name="blankDiagnostic">Diagnostic to report for the case of the name being blank (matched tokenIfBlankName).</param>
    /// <param name="tokenIfBlankName">Current token if the name was invalid for being blank (not provided / skipped).</param>
    [NoAllocUnchecked("error path: the message names the token text")]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void ParseSemicolonAfterPropertyName(Node name, Node type, Node initializer)
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

        if (!type.IsNone && !CanParseSemicolon())
        {
            if (!initializer.IsNone)
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

        if (!initializer.IsNone)
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.SemicolonToken));
            return;
        }

        ParseErrorForMissingSemicolonAfter(name);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            AddMatchingBracketInfo(lastError, openKind, closeKind, openPosition);
        }
    }

    [NoAllocUnchecked("error path: adds related information to a diagnostic")]
    private void AddMatchingBracketInfo(DiagnosticWithDetachedLocation lastError, SyntaxKind openKind, SyntaxKind closeKind, int openPosition)
    {
        Utilities.AddRelatedInfo(
            lastError,
            Utilities.CreateDetachedDiagnostic(fileName, sourceText, openPosition, 1, Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here, Scanner.TokenToString(openKind), Scanner.TokenToString(closeKind)));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ParseOptional(SyntaxKind t)
    {
        if (Token() == t)
        {
            NextToken();
            return true;
        }
        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseOptionalToken(SyntaxKind t)
    {
        if (Token() == t)
        {
            return ParseTokenNode();
        }
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseOptionalTokenJSDoc(SyntaxKind t)
    {
        if (Token() == t)
        {
            return ParseTokenNodeJSDoc();
        }
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseExpectedToken(SyntaxKind t, DiagnosticMessage? diagnosticMessage = null, string? arg0 = null)
    {
        return (ParseOptionalToken(t) is { IsNone: false } __t1 ? __t1 : CreateMissingNode(t, /*reportAtCurrentPosition*/ false, diagnosticMessage ?? Diagnostics._0_expected, !string.IsNullOrEmpty(arg0) ? arg0 : Scanner.TokenToString(t)!));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseExpectedTokenJSDoc(SyntaxKind t)
    {
        var optional = ParseOptionalTokenJSDoc(t);
        if (!optional.IsNone)
        {
            return optional;
        }
        Debug.Assert(Utilities.IsKeywordOrPunctuation(t));
        return CreateMissingNode(t, /*reportAtCurrentPosition*/ false, Diagnostics._0_expected, Scanner.TokenToString(t));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseTokenNode()
    {
        var pos = GetNodePos();
        var kind = Token();
        NextToken();
        return FinishNode(factory.CreateToken(kind), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseTokenNodeJSDoc()
    {
        var pos = GetNodePos();
        var kind = Token();
        NextTokenJSDoc();
        return FinishNode(factory.CreateToken(kind), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ParseSemicolon()
    {
        return TryParseSemicolon() || ParseExpected(SyntaxKind.SemicolonToken);
    }

    /// <summary>
    /// TypeScript: `createNodeArray(elements: T[], pos, end?, hasTrailingComma?)`. The elements are a plain array: a
    /// list under construction (Track O, step O7a), which the list store copies into its arena.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList CreateNodeArray(ListBuilder elements, int pos, int? end = null, bool? hasTrailingComma = null)
    {
        var array = NodeFactory.CreateNodeArray(store, elements, hasTrailingComma);
        Utilities.SetTextRangePosEnd(store, array, pos, end ?? scanner.GetTokenFullStart());
        return array;
    }

    /// <summary>`createNodeArray([...], pos, end?)` with an array literal.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList CreateNodeArray(ReadOnlySpan<Node> elements, int pos, int? end = null)
    {
        var array = NodeFactory.CreateNodeArray(store, elements);
        Utilities.SetTextRangePosEnd(store, array, pos, end ?? scanner.GetTokenFullStart());
        return array;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node FinishNode(Node node, int pos, int? end = null)
    {
        Utilities.SetTextRangePosEnd(store, node, pos, end ?? scanner.GetTokenFullStart());
        if (contextFlags != 0)
        {
            store.SetFlags(node, store.Flags(node) | contextFlags);
        }

        // Keep track on the node if we encountered an error while parsing it.  If we did, then
        // we cannot reuse the node incrementally.  Once we've marked this node, clear out the
        // flag so that we don't mark any subsequent nodes.
        if (parseErrorBeforeNextFinishedNode)
        {
            parseErrorBeforeNextFinishedNode = false;
            store.SetFlags(node, store.Flags(node) | NodeFlags.ThisNodeHasError);
        }

        return node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

        return CreateMissingNodeWorker(kind);
    }

    // Track O (O-NA): createMissingNode with one string argument for the message (no params array).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateMissingNode(SyntaxKind kind, bool reportAtCurrentPosition, DiagnosticMessage? diagnosticMessage, string? arg0)
    {
        if (reportAtCurrentPosition)
        {
            ParseErrorAtPositionWithArguments(scanner.GetTokenFullStart(), 0, diagnosticMessage!, arg0, null, 1);
        }
        else if (diagnosticMessage != null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage, arg0);
        }

        return CreateMissingNodeWorker(kind);
    }

    // The node createMissingNode creates.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateMissingNodeWorker(SyntaxKind kind)
    {
        var pos = GetNodePos();
        var result = kind == SyntaxKind.Identifier ? factory.CreateIdentifier(TextRef.Empty, /*originalKeywordKind*/ null) :
            UtilitiesPublic.IsTemplateLiteralKind(kind) ? factory.CreateTemplateLiteralLikeNode(kind, TextRef.Empty, TextRef.Empty, /*templateFlags*/ null) :
            kind == SyntaxKind.NumericLiteral ? factory.CreateNumericLiteral(TextRef.Empty, /*numericLiteralFlags*/ null) :
            kind == SyntaxKind.StringLiteral ? factory.CreateStringLiteral(TextRef.Empty, /*isSingleQuote*/ null) :
            kind == SyntaxKind.MissingDeclaration ? factory.CreateMissingDeclaration() :
            factory.CreateToken(kind);
        return FinishNode(result, pos);
    }

    // Track O (O5a): the interning table of the parse's texts (TextTable.Intern), keyed by the characters of a span.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef InternIdentifier(TextRef text)
    {
        return texts.Intern(text);
    }

    // An identifier that starts with two underscores has an extra underscore character prepended to it to avoid issues
    // with magic property names like '__proto__'. The 'identifiers' object is used to share a single string instance for
    // each identifier in order to reduce memory consumption.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateIdentifier(bool isIdentifier, DiagnosticMessage? diagnosticMessage = null, DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        if (isIdentifier)
        {
            identifierCount++;
            var pos = scanner.HasPrecedingJSDocLeadingAsterisks() ? scanner.GetTokenStart() : GetNodePos();
            // Store original token kind if it is not just an Identifier so we can report appropriate error later in type checker
            var originalKeywordKind = Token();
            var text = InternIdentifier(scanner.GetTokenValueRef());
            var hasExtendedUnicodeEscape = scanner.HasExtendedUnicodeEscape();
            NextTokenWithoutCheck();
            return FinishNode(factory.CreateIdentifier(text, originalKeywordKind, hasExtendedUnicodeEscape), pos);
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            ParseErrorAtCurrentToken(privateIdentifierDiagnosticMessage ?? Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
            return CreateIdentifier(/*isIdentifier*/ true);
        }

        if (Token() == SyntaxKind.Unknown && scanner.TryScan<ReScanInvalidIdentifierIsIdentifier, bool>(new ReScanInvalidIdentifierIsIdentifier(scanner)))
        {
            // Scanner has already recorded an 'Invalid character' error, so no need to add another from the parser.
            return CreateIdentifier(/*isIdentifier*/ true);
        }

        return CreateMissingIdentifier(diagnosticMessage);
    }

    // The missing identifier of createIdentifier (Track O: a method of its own, as the message needs the token text).
    [NoAllocUnchecked("error path: the message names the token text")]
    private Node CreateMissingIdentifier(DiagnosticMessage? diagnosticMessage)
    {
        identifierCount++;
        // Only for end of file because the error gets reported incorrectly on embedded script tags.
        var reportAtCurrentPosition = Token() == SyntaxKind.EndOfFileToken;

        var isReservedWord = scanner.IsReservedWord();
        var msgArg = scanner.GetTokenText();

        var defaultMessage = isReservedWord ?
            Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here :
            Diagnostics.Identifier_expected;

        return CreateMissingNode(SyntaxKind.Identifier, reportAtCurrentPosition, diagnosticMessage ?? defaultMessage, msgArg);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseBindingIdentifier(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        return CreateIdentifier(IsBindingIdentifier(), /*diagnosticMessage*/ null, privateIdentifierDiagnosticMessage);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseIdentifier(DiagnosticMessage? diagnosticMessage = null, DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        return CreateIdentifier(IsIdentifier(), diagnosticMessage, privateIdentifierDiagnosticMessage);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseIdentifierName(DiagnosticMessage? diagnosticMessage = null)
    {
        return CreateIdentifier(Scanner.TokenIsIdentifierOrKeyword(Token()), diagnosticMessage);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseIdentifierNameErrorOnUnicodeEscapeSequence()
    {
        if (scanner.HasUnicodeEscape() || scanner.HasExtendedUnicodeEscape())
        {
            ParseErrorAtCurrentToken(Diagnostics.Unicode_escape_sequence_cannot_appear_here);
        }
        return CreateIdentifier(Scanner.TokenIsIdentifierOrKeyword(Token()));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsLiteralPropertyName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) ||
            Token() == SyntaxKind.StringLiteral ||
            Token() == SyntaxKind.NumericLiteral ||
            Token() == SyntaxKind.BigIntLiteral;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsImportAttributeName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParsePropertyNameWorker(bool allowComputedPropertyNames)
    {
        if (Token() == SyntaxKind.StringLiteral || Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral)
        {
            var node = ParseLiteralNode();
            switch (node)
            {
                case var s when store.IsStringLiteral(s):
                    store.SetText(s, InternIdentifier(store.Text(s)));
                    break;
                case var n when store.IsNumericLiteral(n):
                    store.SetText(n, InternIdentifier(store.Text(n)));
                    break;
                case var b when store.IsBigIntLiteral(b):
                    store.SetText(b, InternIdentifier(store.Text(b)));
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParsePropertyName()
    {
        return ParsePropertyNameWorker(/*allowComputedPropertyNames*/ true);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseComputedPropertyName()
    {
        // PropertyName [Yield]:
        //      LiteralPropertyName
        //      ComputedPropertyName[?Yield]
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        // We parse any expression (including a comma expression). But the grammar
        // says that only an assignment expression is allowed, so the grammar checker
        // will error if it sees a comma expression.
        var expression = AllowInAnd(new F.ParseExpression());
        ParseExpected(SyntaxKind.CloseBracketToken);
        return FinishNode(factory.CreateComputedPropertyName(expression), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParsePrivateIdentifier()
    {
        var pos = GetNodePos();
        var node = factory.CreatePrivateIdentifier(InternIdentifier(scanner.GetTokenValueRef()));
        NextToken();
        return FinishNode(node, pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ParseContextualModifier(SyntaxKind t)
    {
        return Token() == t && TryParse<F.NextTokenCanFollowModifier, bool>(new F.NextTokenCanFollowModifier());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokenIsOnSameLineAndCanFollowModifier()
    {
        NextToken();
        if (scanner.HasPrecedingLineBreak())
        {
            return false;
        }
        return CanFollowModifier();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
                    return LookAhead(new F.NextTokenCanFollowDefaultKeyword());
                }
                if (Token() == SyntaxKind.TypeKeyword)
                {
                    return LookAhead(new F.NextTokenCanFollowExportModifier());
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool CanFollowExportModifier()
    {
        return Token() == SyntaxKind.AtToken
            || Token() != SyntaxKind.AsteriskToken
                && Token() != SyntaxKind.AsKeyword
                && Token() != SyntaxKind.OpenBraceToken
                && CanFollowModifier();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokenCanFollowExportModifier()
    {
        NextToken();
        return CanFollowExportModifier();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ParseAnyContextualModifier()
    {
        return UtilitiesPublic.IsModifierKind(Token()) && TryParse<F.NextTokenCanFollowModifier, bool>(new F.NextTokenCanFollowModifier());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool CanFollowModifier()
    {
        return Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.AsteriskToken
            || Token() == SyntaxKind.DotDotDotToken
            || IsLiteralPropertyName();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool CanFollowGetOrSetKeyword()
    {
        return Token() == SyntaxKind.OpenBracketToken
            || IsLiteralPropertyName();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokenCanFollowDefaultKeyword()
    {
        NextToken();
        return Token() == SyntaxKind.ClassKeyword
            || Token() == SyntaxKind.FunctionKeyword
            || Token() == SyntaxKind.InterfaceKeyword
            || Token() == SyntaxKind.AtToken
            || (Token() == SyntaxKind.AbstractKeyword && LookAhead(new F.NextTokenIsClassKeywordOnSameLine()))
            || (Token() == SyntaxKind.AsyncKeyword && LookAhead(new F.NextTokenIsFunctionKeywordOnSameLine()));
    }
}
