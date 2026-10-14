// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Oracle 2: canonical AST dump via the typescript-go 7.0.2 parser.
// Built inside the pinned typescript-go module with `go build -overlay`
// (see build-go.mjs), because internal/ packages cannot be imported from
// outside that module. The checkout itself is never modified.
//
// Writes the same format as format.mjs. Positions are converted from Go's
// UTF-8 byte offsets to UTF-16 code unit offsets.
//
// Usage: astdump-go <corpus dir>

package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"unicode/utf8"

	"github.com/microsoft/typescript-go/internal/ast"
	"github.com/microsoft/typescript-go/internal/core"
	"github.com/microsoft/typescript-go/internal/parser"
	"github.com/microsoft/typescript-go/internal/tspath"
)

type unit struct {
	ID          string `json:"id"`
	Test        string `json:"test"`
	Unit        string `json:"unit"`
	FileName    string `json:"fileName"`
	Kind        string `json:"kind"`
	Declaration bool   `json:"declaration"`
}

var scriptKinds = map[string]core.ScriptKind{
	"TS":   core.ScriptKindTS,
	"TSX":  core.ScriptKindTSX,
	"JS":   core.ScriptKindJS,
	"JSX":  core.ScriptKindJSX,
	"JSON": core.ScriptKindJSON,
}

// Names per bit, from internal/ast/nodeflags.go at the pinned commit.
var flagNames = []struct {
	bit  ast.NodeFlags
	name string
}{
	{ast.NodeFlagsLet, "Let"},
	{ast.NodeFlagsConst, "Const"},
	{ast.NodeFlagsUsing, "Using"},
	{ast.NodeFlagsReparsed, "Reparsed"},
	{ast.NodeFlagsSynthesized, "Synthesized"},
	{ast.NodeFlagsOptionalChain, "OptionalChain"},
	{ast.NodeFlagsExportContext, "ExportContext"},
	{ast.NodeFlagsContainsThis, "ContainsThis"},
	{ast.NodeFlagsHasImplicitReturn, "HasImplicitReturn"},
	{ast.NodeFlagsHasExplicitReturn, "HasExplicitReturn"},
	{ast.NodeFlagsDisallowInContext, "DisallowInContext"},
	{ast.NodeFlagsYieldContext, "YieldContext"},
	{ast.NodeFlagsDecoratorContext, "DecoratorContext"},
	{ast.NodeFlagsAwaitContext, "AwaitContext"},
	{ast.NodeFlagsDisallowConditionalTypesContext, "DisallowConditionalTypesContext"},
	{ast.NodeFlagsThisNodeHasError, "ThisNodeHasError"},
	{ast.NodeFlagsJavaScriptFile, "JavaScriptFile"},
	{ast.NodeFlagsThisNodeOrAnySubNodesHasError, "ThisNodeOrAnySubNodesHasError"},
	{ast.NodeFlagsHasAsyncFunctions, "HasAsyncFunctions"},
	{ast.NodeFlagsPossiblyContainsDynamicImport, "PossiblyContainsDynamicImport"},
	{ast.NodeFlagsPossiblyContainsImportMeta, "PossiblyContainsImportMeta"},
	{ast.NodeFlagsHasJSDoc, "HasJSDoc"},
	{ast.NodeFlagsJSDoc, "JSDoc"},
	{ast.NodeFlagsAmbient, "Ambient"},
	{ast.NodeFlagsInWithStatement, "InWithStatement"},
	{ast.NodeFlagsJsonFile, "JsonFile"},
	{ast.NodeFlagsPossiblyContainsDeprecatedTag, "PossiblyContainsDeprecatedTag"},
	{ast.NodeFlagsUnreachable, "Unreachable"},
	{ast.NodeFlagsReparserTransformedLiteral, "ReparserTransformedLiteral"},
}

func nodeFlagNames(n *ast.Node) []string {
	var names []string
	known := ast.NodeFlags(0)
	for _, f := range flagNames {
		known |= f.bit
		if n.Flags&f.bit == 0 {
			continue
		}
		name := f.name
		switch {
		case n.Kind == ast.KindIdentifier && f.bit == ast.NodeFlagsContainsThis:
			name = "IdentifierHasExtendedUnicodeEscape"
		case n.Kind == ast.KindIdentifier && f.bit == ast.NodeFlagsHasAsyncFunctions:
			name = "IdentifierIsInJSDocNamespace"
		case n.Kind == ast.KindModuleDeclaration && f.bit == ast.NodeFlagsOptionalChain:
			name = "NestedNamespace"
		}
		names = append(names, name)
	}
	if rest := n.Flags &^ known; rest != 0 {
		names = append(names, fmt.Sprintf("Unknown0x%x", uint32(rest)))
	}
	// Derived attributes, prefixed with "=": information TypeScript encodes as a
	// NodeFlag but typescript-go stores in a node field. The comparer maps them.
	if n.Kind == ast.KindModuleDeclaration {
		switch n.AsModuleDeclaration().Keyword {
		case ast.KindNamespaceKeyword:
			names = append(names, "=Namespace")
		case ast.KindGlobalKeyword:
			names = append(names, "=GlobalAugmentation")
		}
	}
	sort.Strings(names)
	return names
}

// utf16Offsets maps every UTF-8 byte offset of text (0..len) to a UTF-16 offset.
func utf16Offsets(text string) []int {
	offsets := make([]int, len(text)+1)
	u := 0
	for i := 0; i < len(text); {
		r, size := utf8.DecodeRuneInString(text[i:])
		for k := 0; k < size; k++ {
			offsets[i+k] = u
		}
		if r >= 0x10000 {
			u += 2
		} else {
			u++
		}
		i += size
	}
	offsets[len(text)] = u
	return offsets
}

func dump(u unit, text string) (out string) {
	defer func() {
		if r := recover(); r != nil {
			out = fmt.Sprintf("!panic %v\n", r)
		}
	}()
	opts := ast.SourceFileParseOptions{
		FileName: u.FileName,
		Path:     tspath.ToPath(u.FileName, "/", true),
	}
	file := parser.ParseSourceFile(opts, text, scriptKinds[u.Kind])
	offsets := utf16Offsets(text)
	pos := func(p int) int {
		if p < 0 || p >= len(offsets) {
			return p
		}
		return offsets[p]
	}

	var b strings.Builder
	var visit func(n *ast.Node, depth int)
	visit = func(n *ast.Node, depth int) {
		b.WriteString(strings.Repeat("  ", depth))
		b.WriteString(strings.TrimPrefix(n.Kind.String(), "Kind"))
		fmt.Fprintf(&b, " %d %d", pos(n.Pos()), pos(n.End()))
		if names := nodeFlagNames(n); len(names) > 0 {
			b.WriteString(" ")
			b.WriteString(strings.Join(names, "|"))
		}
		b.WriteString("\n")
		n.ForEachChild(func(child *ast.Node) bool {
			visit(child, depth+1)
			return false
		})
	}
	visit(file.AsNode(), 0)

	writeDiagnostics := func(section string, diagnostics []*ast.Diagnostic) {
		for _, d := range diagnostics {
			start := pos(d.Pos())
			fmt.Fprintf(&b, "!%s %d %d %d\n", section, d.Code(), start, pos(d.End())-start)
		}
	}
	writeDiagnostics("diag", file.Diagnostics())
	writeDiagnostics("jsdiag", file.JSDiagnostics())
	writeDiagnostics("jsdocdiag", file.JSDocDiagnostics())
	return b.String()
}

func main() {
	corpus := os.Args[1]
	raw, err := os.ReadFile(filepath.Join(corpus, "manifest.json"))
	if err != nil {
		panic(err)
	}
	var manifest struct {
		Units []unit `json:"units"`
	}
	if err := json.Unmarshal(raw, &manifest); err != nil {
		panic(err)
	}
	outDir := filepath.Join(corpus, "dumps", "go")
	os.RemoveAll(outDir)
	if err := os.MkdirAll(outDir, 0o755); err != nil {
		panic(err)
	}
	panics := 0
	for _, u := range manifest.Units {
		text, err := os.ReadFile(filepath.Join(corpus, "units", u.ID+".txt"))
		if err != nil {
			panic(err)
		}
		out := dump(u, string(text))
		if strings.HasPrefix(out, "!panic") {
			panics++
		}
		if err := os.WriteFile(filepath.Join(outDir, u.ID+".txt"), []byte(out), 0o644); err != nil {
			panic(err)
		}
	}
	fmt.Printf("go: units %d, panics %d\n", len(manifest.Units), panics)
}
