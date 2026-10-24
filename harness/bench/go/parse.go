// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Parse-only driver for typescript-go 7.0.2. Built inside the pinned
// typescript-go module via `go build -overlay` (see build-go.mjs) with the
// same toolset and flags as Microsoft's tsgo release binary.
//
// Each file is read and parsed in one work item, as compilerHost.GetSourceFile
// (internal/compiler/host.go) does, scheduled on typescript-go's own
// core.WorkGroup: parallel by default, single-threaded with --single (the
// same switch tsgo's --singleThreaded flips).
//
// All SourceFiles stay referenced until the end, as a Program keeps them, so
// memory and GC behavior reflect a compiler's parse phase.
//
// With NEVERSHARP_PRIORITY=high the process first raises itself to the high
// priority class (priority_windows.go); the class in effect is reported.
//
// Prints one JSON line (file count, bytes, node count, priority class).
//
// --bind (Phase 2, pre-registration §15): parse+bind. The same work item binds
// the file right after parsing it with typescript-go's binder,
// binder.BindSourceFile(sourceFile) (internal/binder/binder.go; in 7.0.2 it
// takes no compiler options, Program.BindSourceFiles calls it the same way).
// The driver name carries "-bind" and the JSON line adds "symbols" (sum of
// the files' SymbolCount).
//
// Usage: parse-go [--bind] [--single] <file list>

package main

import (
	"fmt"
	"os"
	"strings"
	"sync/atomic"

	"github.com/microsoft/typescript-go/internal/ast"
	"github.com/microsoft/typescript-go/internal/binder"
	"github.com/microsoft/typescript-go/internal/core"
	"github.com/microsoft/typescript-go/internal/parser"
	"github.com/microsoft/typescript-go/internal/tspath"
	"github.com/microsoft/typescript-go/internal/vfs/osvfs"
)

func main() {
	single, bind := false, false
	args := os.Args[1:]
	for len(args) > 0 && strings.HasPrefix(args[0], "--") {
		switch args[0] {
		case "--single":
			single = true
		case "--bind":
			bind = true
		default:
			fmt.Fprintln(os.Stderr, "unknown option "+args[0])
			os.Exit(2)
		}
		args = args[1:]
	}
	priority := currentPriority()
	if os.Getenv("NEVERSHARP_PRIORITY") == "high" {
		priority = raisePriority()
	}
	list, err := os.ReadFile(args[0])
	if err != nil {
		panic(err)
	}
	var files []string
	for _, line := range strings.Split(string(list), "\n") {
		if line = strings.TrimSpace(line); line != "" {
			files = append(files, tspath.NormalizeSlashes(line))
		}
	}

	fs := osvfs.FS()
	var nodes, chars, symbols atomic.Int64
	sourceFiles := make([]*ast.SourceFile, len(files))
	wg := core.NewWorkGroup(single)
	for i, file := range files {
		wg.Queue(func() {
			text, ok := fs.ReadFile(file)
			if !ok {
				panic("cannot read " + file)
			}
			opts := ast.SourceFileParseOptions{FileName: file, Path: tspath.ToPath(file, "", fs.UseCaseSensitiveFileNames())}
			sourceFile := parser.ParseSourceFile(opts, text, core.GetScriptKindFromFileName(file))
			if bind {
				binder.BindSourceFile(sourceFile)
				symbols.Add(int64(sourceFile.SymbolCount))
			}
			sourceFiles[i] = sourceFile
			nodes.Add(int64(sourceFile.NodeCount))
			chars.Add(int64(len(text)))
		})
	}
	wg.RunAndWait()
	mode := "parallel"
	if single {
		mode = "single"
	}
	if bind {
		fmt.Printf("{\"driver\":\"tsgo-7.0.2-bind-%s\",\"files\":%d,\"bytes\":%d,\"nodes\":%d,\"symbols\":%d,\"priority\":\"%s\"}\n", mode, len(sourceFiles), chars.Load(), nodes.Load(), symbols.Load(), priority)
		return
	}
	fmt.Printf("{\"driver\":\"tsgo-7.0.2-%s\",\"files\":%d,\"bytes\":%d,\"nodes\":%d,\"priority\":\"%s\"}\n", mode, len(sourceFiles), chars.Load(), nodes.Load(), priority)
}
