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
// (one checker, everything sequential). --checkers N (pre-registration §18):
// CompilerOptions.Checkers = N, the pool's N checkers, file i to checker i % N;
// the driver name ends in "-nN". The driver name carries "-check" and the JSON
// line adds the diagnostic counts.
//
// --check --emit (Phase 4, draft pre-registration §19, module D7): the check
// above with Gate D's options (emitOptions: checkOptions plus declaration: true
// and newLine: lf), then program.Emit(ctx, EmitOptions{WriteFile}) with an
// in-memory WriteFile (every output kept in memory, nothing written to disk):
// one work item per emitted file on the program's work group, each with the
// emit resolver of its file's checker (newEmitHost). The driver name carries
// "-emit" instead of "-check" and the JSON line adds "outputFiles",
// "outputBytes", "emitDiagnostics" and "emitSkipped".
//
// Usage: parse-go [--bind | --check [--emit] [--checkers N]] [--single] <file list>

package main

import (
	"context"
	"fmt"
	"os"
	"runtime"
	"strconv"
	"strings"
	"sync"
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

// emitOptions (--emit, draft pre-registration §19): Gate D's options, checkOptions
// plus declaration: true and newLine: lf (the C# side's emit program options).
func emitOptions() *core.CompilerOptions {
	options := checkOptions()
	options.Declaration = core.TSTrue
	options.NewLine = core.NewLineKindLF
	return options
}

func runCheck(files []string, single bool, checkers int, emit bool, priority string) {
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
	// --emit: the outputs (name, text) kept in memory, an in-memory WriteFile
	var outputNames, outputTexts []string
	var outputBytes int64
	emitDiagnostics, emitSkipped := 0, false
	if len(files) > 0 {
		host := compiler.NewCachedFSCompilerHost(cwd, fs, bundled.LibPath(), nil, nil)
		options := checkOptions()
		if emit {
			options = emitOptions()
		}
		if checkers > 0 {
			options.Checkers = &checkers
		}
		config := tsoptions.NewParsedCommandLine(options, files, tspath.ComparePathsOptions{UseCaseSensitiveFileNames: fs.UseCaseSensitiveFileNames(), CurrentDirectory: cwd})
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
		if emit {
			// Program.Emit on the program's checker pool (one work item per file,
			// each with its file's checker); WriteFile is called from those work items.
			var mu sync.Mutex
			result := program.Emit(ctx, compiler.EmitOptions{WriteFile: func(fileName string, text string, data *compiler.WriteFileData) error {
				mu.Lock()
				outputNames = append(outputNames, fileName)
				outputTexts = append(outputTexts, text)
				outputBytes += int64(len(text))
				mu.Unlock()
				return nil
			}})
			emitDiagnostics = len(result.Diagnostics)
			emitSkipped = result.EmitSkipped
		}
	}
	mode := "parallel"
	if single {
		mode = "single"
	} else if checkers > 0 {
		mode += "-n" + strconv.Itoa(checkers)
	}
	kind, emitFields := "check", ""
	if emit {
		kind = "emit"
		emitFields = fmt.Sprintf("\"outputFiles\":%d,\"outputBytes\":%d,\"emitDiagnostics\":%d,\"emitSkipped\":%t,", len(outputNames), outputBytes, emitDiagnostics, emitSkipped)
	}
	fmt.Printf("{\"driver\":\"tsgo-7.0.2-%s-%s\",\"files\":%d,\"bytes\":%d,\"nodes\":%d,\"programFiles\":%d,\"diagnostics\":%d,\"errors\":%d,\"warnings\":%d,\"suggestions\":%d,\"messages\":%d,%s\"priority\":\"%s\"}\n",
		kind, mode, len(files), chars, nodes, programFiles, total, errors, warnings, suggestions, messages, emitFields, priority)
	runtime.KeepAlive(outputTexts)
}

func main() {
	single, bind, check, emit := false, false, false, false
	checkers := 0
	args := os.Args[1:]
	for len(args) > 0 && strings.HasPrefix(args[0], "--") {
		switch args[0] {
		case "--single":
			single = true
		case "--bind":
			bind = true
		case "--check":
			check = true
		case "--emit":
			emit = true
		case "--checkers":
			n, err := 0, error(nil)
			if len(args) > 1 {
				n, err = strconv.Atoi(args[1])
			}
			if err != nil || n < 1 {
				fmt.Fprintln(os.Stderr, "--checkers needs a positive count")
				os.Exit(2)
			}
			checkers = n
			args = args[1:]
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
	if checkers > 0 && !check {
		fmt.Fprintln(os.Stderr, "--checkers needs --check")
		os.Exit(2)
	}
	if emit && !check {
		fmt.Fprintln(os.Stderr, "--emit needs --check")
		os.Exit(2)
	}
	if check {
		if bind {
			fmt.Fprintln(os.Stderr, "--bind and --check exclude each other")
			os.Exit(2)
		}
		if checkers > 0 && single {
			fmt.Fprintln(os.Stderr, "--checkers and --single exclude each other")
			os.Exit(2)
		}
		runCheck(files, single, checkers, emit, priority)
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
