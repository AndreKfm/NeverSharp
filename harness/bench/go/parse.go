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
// --check (Phase 3, pre-registration §17, module C10): parse+bind+check of the
// file list as one program, as tsgo's command line builds and checks one:
// compiler.NewProgram with tsgo's own cached compiler host on the real file
// system (NewCachedFSCompilerHost, the default library next to the executable:
// bundled.LibPath(), build-go.mjs copies typescript-go's lib files there), the
// root files of the list and TypeScript 6.0.3's default options given
// explicitly (checkOptions below; table in docs/benchmark-harness.md §4.1),
// then program.GetSemanticDiagnostics(ctx, nil) (every file, on the program's
// default checker pool: 4 checkers, file i to checker i % 4) and
// program.GetGlobalDiagnostics(ctx). --single: ProgramOptions.SingleThreaded
// (one checker, everything sequential). The driver name carries "-check" and
// the JSON line adds the diagnostic counts.
//
// Usage: parse-go [--bind | --check] [--single] <file list>

package main

import (
	"context"
	"fmt"
	"os"
	"strings"
	"sync/atomic"

	"github.com/microsoft/typescript-go/internal/ast"
	"github.com/microsoft/typescript-go/internal/binder"
	"github.com/microsoft/typescript-go/internal/bundled"
	"github.com/microsoft/typescript-go/internal/compiler"
	"github.com/microsoft/typescript-go/internal/core"
	"github.com/microsoft/typescript-go/internal/diagnostics"
	"github.com/microsoft/typescript-go/internal/parser"
	"github.com/microsoft/typescript-go/internal/tsoptions"
	"github.com/microsoft/typescript-go/internal/tspath"
	"github.com/microsoft/typescript-go/internal/vfs/osvfs"
)

// checkOptions: the computed values of TypeScript 6.0.3's default options with
// noResolve (the C# side's program options, { noResolve: true }), each given
// explicitly so that typescript-go's TypeScript 7 defaults do not apply.
// (esModuleInterop, allowSyntheticDefaultImports and alwaysStrict are always
// on in TypeScript 7 and deprecated as options; 6.0.3's defaults are on too.)
func checkOptions() *core.CompilerOptions {
	return &core.CompilerOptions{
		NoResolve:                    core.TSTrue,
		Target:                       core.ScriptTargetES2025,
		Module:                       core.ModuleKindES2022,
		ModuleResolution:             core.ModuleResolutionKindBundler,
		ModuleDetection:              core.ModuleDetectionKindAuto,
		Strict:                       core.TSTrue,
		AllowJs:                      core.TSFalse,
		ResolveJsonModule:            core.TSTrue,
		ResolvePackageJsonExports:    core.TSTrue,
		ResolvePackageJsonImports:    core.TSTrue,
		UseDefineForClassFields:      core.TSTrue,
		NoUncheckedSideEffectImports: core.TSTrue,
	}
}

func runCheck(files []string, single bool, priority string) {
	fs := osvfs.FS()
	cwd, err := os.Getwd()
	if err != nil {
		panic(err)
	}
	cwd = tspath.NormalizePath(cwd)
	singleThreaded := core.TSFalse
	if single {
		singleThreaded = core.TSTrue
	}
	var nodes, chars, total, errors, warnings, suggestions, messages int64
	programFiles := 0
	if len(files) > 0 {
		host := compiler.NewCachedFSCompilerHost(cwd, fs, bundled.LibPath(), nil, nil)
		config := tsoptions.NewParsedCommandLine(checkOptions(), files, tspath.ComparePathsOptions{UseCaseSensitiveFileNames: fs.UseCaseSensitiveFileNames(), CurrentDirectory: cwd})
		program := compiler.NewProgram(compiler.ProgramOptions{Config: config, Host: host, SingleThreaded: singleThreaded})
		ctx := context.Background()
		semantic := program.GetSemanticDiagnostics(ctx, nil)
		global := program.GetGlobalDiagnostics(ctx)
		for _, list := range [][]*ast.Diagnostic{semantic, global} {
			for _, d := range list {
				total++
				switch d.Category() {
				case diagnostics.CategoryError:
					errors++
				case diagnostics.CategoryWarning:
					warnings++
				case diagnostics.CategorySuggestion:
					suggestions++
				default:
					messages++
				}
			}
		}
		rootPaths := make(map[tspath.Path]bool, len(files))
		for _, f := range files {
			rootPaths[tspath.ToPath(f, cwd, fs.UseCaseSensitiveFileNames())] = true
		}
		for _, f := range program.GetSourceFiles() {
			if rootPaths[f.Path()] {
				nodes += int64(f.NodeCount)
				chars += int64(len(f.Text()))
			}
		}
		programFiles = len(program.GetSourceFiles())
	}
	mode := "parallel"
	if single {
		mode = "single"
	}
	fmt.Printf("{\"driver\":\"tsgo-7.0.2-check-%s\",\"files\":%d,\"bytes\":%d,\"nodes\":%d,\"programFiles\":%d,\"diagnostics\":%d,\"errors\":%d,\"warnings\":%d,\"suggestions\":%d,\"messages\":%d,\"priority\":\"%s\"}\n",
		mode, len(files), chars, nodes, programFiles, total, errors, warnings, suggestions, messages, priority)
}

func main() {
	single, bind, check := false, false, false
	args := os.Args[1:]
	for len(args) > 0 && strings.HasPrefix(args[0], "--") {
		switch args[0] {
		case "--single":
			single = true
		case "--bind":
			bind = true
		case "--check":
			check = true
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
	if check {
		if bind {
			fmt.Fprintln(os.Stderr, "--bind and --check exclude each other")
			os.Exit(2)
		}
		runCheck(files, single, priority)
		return
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
