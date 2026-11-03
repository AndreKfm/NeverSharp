// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Parse-only driver for tsrs v0.18.0 (oxc-project/tsrs), the counterpart of harness/bench/go/parse.go.
// Driver contract: docs/benchmark-harness.md §4.1.
//
// Each file is read and parsed in one work item, as tsrs's compiler host does
// (tsrs_compiler/src/host.rs get_source_file: the OS file system's read_file, then
// tsrs_parser::parse_source_file_owned with the script kind from the file name). Parallel by default
// on rayon, which tsrs uses for parsing files (tsrs_compiler/src/filesparser.rs); --single runs the
// work items sequentially on the calling thread. The allocator is the tsrs CLI's (mimalloc,
// tsrs_cli/src/main.rs). All SourceFiles stay referenced until the end, as a Program keeps them.
//
// With NEVERSHARP_PRIORITY=high the process first raises itself to the high priority class; the
// class in effect is reported. Prints one JSON line (file count, bytes, node count, priority class).
//
// --bind (Phase 2, pre-registration §15): parse+bind. The same work item binds the file right after parsing it with
// tsrs's binder, tsrs_binder::bind_source_file(file) (tsrs_binder/src/binder.rs; takes no compiler options, as
// typescript-go 7.0.2's; Program::bind_source_files calls it through tsrs_compiler/src/fileregions.rs bind, which with
// file regions off, the default outside the CLI's --noEmit check, is this plain call). The driver name carries
// "-bind" and the JSON line adds "symbols" (sum of the files' symbol_count).
//
// --check (Phase 3, pre-registration §17, module C11): parse+bind+check of the file list as one program, as tsrs's
// command line builds and checks one (tsrs_execute/src/execute.rs): tsrs_compiler::new_program with
// ProgramOptions::new(config, host) — the cached compiler host on the OS file system wrapped with the embedded default
// library (tsrs_vfs::bundled::wrap_fs, bundled::default_lib_path(), as tsrs_execute/src/sys.rs), the root files of the
// list and TypeScript 6.0.3's default options given explicitly (check_options below, the Go driver's checkOptions;
// table in docs/benchmark-harness.md §4.1) —, then program.get_semantic_diagnostics(ctx, None) (every file, on the
// program's built-in checker pool) and program.get_global_diagnostics(ctx). The program API's defaults stay: no
// leaf-file freeing, no checker recycling (the CLI turns both on only for its own --noEmit check). Parallelism (tsrs's
// default, tsrs_compiler/src/checkerpool.rs): the checker count is half the available parallelism but every core up
// to 16, at least 4 and at most 32, and at most one checker per 32 type-checked files (default_checker_count;
// capped by the program's file count); files are assigned to checkers by directory locality (locality_associations)
// and a checker that runs out of files takes not-yet-started files from the queue with the most work left (work
// stealing, stealing_enabled); parsing runs on rayon. --single: ProgramOptions.single_threaded (one checker,
// everything sequential). The driver name carries "-check" and the JSON line adds the diagnostic counts.
//
// --check --emit (Phase 4, draft pre-registration §19, module D7): the check above with Gate D's options (emit_options:
// check_options plus declaration: true and newLine: lf), then tsrs's Program::emit(ctx, EmitOptions { write_file, .. })
// (tsrs_compiler/src/program_emit.rs, the port of typescript-go's Program.Emit) with an in-memory write_file (every output
// kept in memory, nothing written to disk). tsrs's emit runs on its compiler checker pool: each file is transformed on its
// checker's thread (files in program order within a checker's group), then printed and written on the worker pool. The
// driver name carries "-emit" instead of "-check" and the JSON line adds "outputFiles", "outputBytes", "emitDiagnostics"
// and "emitSkipped".
//
// Usage: parse-rs [--bind | --check [--emit]] [--single] <file list>

use rayon::prelude::*;
use std::sync::atomic::{AtomicI64, Ordering};
use std::sync::{Arc, Mutex};
use tsrs_ast::SourceFileParseOptions;
use tsrs_core::tspath::{normalize_path, normalize_slashes, to_path};
use tsrs_core::{CompilerOptions, ModuleDetectionKind, ModuleKind, ModuleResolutionKind, NewLineKind, P, ScriptTarget, Tristate};
use tsrs_vfs::{bundled, osvfs};

#[global_allocator]
static GLOBAL: mimalloc_safe::MiMalloc = mimalloc_safe::MiMalloc;

#[cfg(windows)]
mod priority {
    const HIGH_PRIORITY_CLASS: u32 = 0x0000_0080;
    const NORMAL_PRIORITY_CLASS: u32 = 0x0000_0020;
    unsafe extern "system" {
        fn GetCurrentProcess() -> isize;
        fn GetPriorityClass(process: isize) -> u32;
        fn SetPriorityClass(process: isize, class: u32) -> i32;
    }
    pub fn raise() {
        // SAFETY: plain Win32 calls on the pseudo handle of the current process.
        unsafe { SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS) };
    }
    pub fn current() -> &'static str {
        // SAFETY: as above.
        match unsafe { GetPriorityClass(GetCurrentProcess()) } {
            HIGH_PRIORITY_CLASS => "high",
            NORMAL_PRIORITY_CLASS => "normal",
            _ => "other",
        }
    }
}

#[cfg(not(windows))]
mod priority {
    pub fn raise() {}
    pub fn current() -> &'static str {
        "normal"
    }
}

// tsrs's thread setup: the command line runs on a thread with a 512 MiB stack (tsrs_cli/src/main.rs),
// files are parsed on a rayon pool of min(available parallelism, 32) threads with 256 MiB stacks
// (tsrs_compiler/src/program.rs worker_pool, PARSE_THREAD_CAP).
fn main() {
    let status = std::thread::Builder::new().stack_size(512 << 20).spawn(run).unwrap().join();
    std::process::exit(if status.is_ok() { 0 } else { 1 });
}

// check_options: the computed values of TypeScript 6.0.3's default options with noResolve (the C# side's program
// options, { noResolve: true }), each given explicitly, as the Go driver's checkOptions. (esModuleInterop,
// allowSyntheticDefaultImports and alwaysStrict are always on in tsrs, as in TypeScript 7; 6.0.3's defaults are on too.)
fn check_options() -> CompilerOptions {
    CompilerOptions {
        no_resolve: Tristate::True,
        target: ScriptTarget::ES2025,
        module: ModuleKind::ES2022,
        module_resolution: ModuleResolutionKind::Bundler,
        module_detection: ModuleDetectionKind::Auto,
        strict: Tristate::True,
        allow_js: Tristate::False,
        resolve_json_module: Tristate::True,
        resolve_package_json_exports: Tristate::True,
        resolve_package_json_imports: Tristate::True,
        use_define_for_class_fields: Tristate::True,
        no_unchecked_side_effect_imports: Tristate::True,
        ..Default::default()
    }
}

// emit_options (--emit, draft pre-registration §19): Gate D's options, check_options plus declaration: true and
// newLine: lf, as the Go driver's emitOptions.
fn emit_options() -> CompilerOptions {
    CompilerOptions { declaration: Tristate::True, new_line: NewLineKind::LF, ..check_options() }
}

fn run_check(files: Vec<String>, single: bool, emit: bool, priority: &str) {
    let (mut nodes, mut chars, mut total, mut errors, mut warnings, mut suggestions, mut messages) = (0i64, 0i64, 0i64, 0i64, 0i64, 0i64, 0i64);
    let mut program_files = 0;
    let file_count = files.len();
    // --emit: the outputs (name, text) kept in memory, an in-memory write_file (called from the checker and worker threads)
    let outputs: Mutex<Vec<(String, String)>> = Mutex::new(Vec::new());
    let (mut emit_diagnostics, mut emit_skipped) = (0usize, false);
    if !files.is_empty() {
        let os = osvfs::fs();
        let use_case_sensitive = os.use_case_sensitive_file_names();
        let fs: Arc<dyn tsrs_vfs::FS> = Arc::new(bundled::wrap_fs(os));
        let cwd = normalize_path(&normalize_slashes(&std::env::current_dir().unwrap().to_string_lossy()));
        let root_paths: std::collections::HashSet<_> = files.iter().map(|f| to_path(f, &cwd, use_case_sensitive)).collect();
        let host = tsrs_compiler::new_cached_fs_compiler_host(&cwd, fs, &bundled::default_lib_path(), None, None, None);
        let config = tsrs_tsoptions::new_parsed_command_line(
            P::new(if emit { emit_options() } else { check_options() }),
            files,
            Vec::new(),
            tsrs_core::tspath::ComparePathsOptions { use_case_sensitive_file_names: use_case_sensitive, current_directory: cwd.clone() },
        );
        let mut options = tsrs_compiler::ProgramOptions::new(P::new(config), host);
        if single {
            options.single_threaded = Tristate::True;
        }
        let program = tsrs_compiler::new_program(options);
        let ctx = tsrs_compiler::Context::default();
        let semantic = program.get_semantic_diagnostics(&ctx, None);
        let global = program.get_global_diagnostics(&ctx);
        for d in semantic.iter().chain(global.iter()) {
            total += 1;
            match d.category() {
                tsrs_diagnostics::Category::Error => errors += 1,
                tsrs_diagnostics::Category::Warning => warnings += 1,
                tsrs_diagnostics::Category::Suggestion => suggestions += 1,
                tsrs_diagnostics::Category::Message => messages += 1,
            }
        }
        for f in program.get_source_files() {
            if root_paths.contains(f.path()) {
                nodes += f.node_count.get() as i64;
                chars += f.text().len() as i64;
            }
        }
        program_files = program.get_source_files().len();
        if emit {
            // Program::emit on the program's checker pool: each file transformed on its checker's thread (files in
            // program order within a checker's group), printed and written on the worker pool.
            let write_file = |file_name: &str, text: &str, _data: &mut tsrs_compiler::WriteFileData| -> Result<(), String> {
                outputs.lock().unwrap().push((file_name.to_owned(), text.to_owned()));
                Ok(())
            };
            let result = program.emit(&ctx, &tsrs_compiler::EmitOptions { write_file: Some(&write_file), ..Default::default() });
            emit_diagnostics = result.diagnostics.len();
            emit_skipped = result.emit_skipped;
        }
    }
    let mode = if single { "single" } else { "parallel" };
    let outputs = outputs.into_inner().unwrap();
    let (kind, emit_fields) = if emit {
        let output_bytes: usize = outputs.iter().map(|(_, text)| text.len()).sum();
        ("emit", format!("\"outputFiles\":{},\"outputBytes\":{output_bytes},\"emitDiagnostics\":{emit_diagnostics},\"emitSkipped\":{emit_skipped},", outputs.len()))
    } else {
        ("check", String::new())
    };
    println!(
        "{{\"driver\":\"tsrs-0.18.0-{kind}-{mode}\",\"files\":{file_count},\"bytes\":{chars},\"nodes\":{nodes},\"programFiles\":{program_files},\"diagnostics\":{total},\"errors\":{errors},\"warnings\":{warnings},\"suggestions\":{suggestions},\"messages\":{messages},{emit_fields}\"priority\":\"{priority}\"}}"
    );
    std::mem::forget(outputs);
}

fn run() {
    let mut args: Vec<String> = std::env::args().skip(1).collect();
    let (mut single, mut bind, mut check, mut emit) = (false, false, false, false);
    while args.first().is_some_and(|a| a.starts_with("--")) {
        match args.remove(0).as_str() {
            "--single" => single = true,
            "--bind" => bind = true,
            "--check" => check = true,
            "--emit" => emit = true,
            other => {
                eprintln!("unknown option {other}");
                std::process::exit(2);
            }
        }
    }
    if std::env::var("NEVERSHARP_PRIORITY").is_ok_and(|v| v == "high") {
        priority::raise();
    }
    let priority = priority::current();

    let list = std::fs::read_to_string(&args[0]).expect("cannot read the file list");
    let files: Vec<String> = list.lines().map(str::trim).filter(|l| !l.is_empty()).map(normalize_slashes).collect();
    if emit && !check {
        eprintln!("--emit needs --check");
        std::process::exit(2);
    }
    if check {
        if bind {
            eprintln!("--bind and --check exclude each other");
            std::process::exit(2);
        }
        run_check(files, single, emit, priority);
        return;
    }
    let fs = osvfs::fs();
    let use_case_sensitive = fs.use_case_sensitive_file_names();
    let nodes = AtomicI64::new(0);
    let chars = AtomicI64::new(0);
    let symbols = AtomicI64::new(0);

    let parse = |file: &String| {
        let text = fs.read_file(file).unwrap_or_else(|| panic!("cannot read {file}"));
        let script_kind = tsrs_core::get_script_kind_from_file_name(file);
        let opts = SourceFileParseOptions {
            file_name: file.clone(),
            path: to_path(file, "", use_case_sensitive),
            external_module_indicator_options: Default::default(),
        };
        chars.fetch_add(text.len() as i64, Ordering::Relaxed);
        let source_file = tsrs_parser::parse_source_file_owned(opts, text, script_kind);
        nodes.fetch_add(source_file.node_count.get() as i64, Ordering::Relaxed);
        if bind {
            tsrs_binder::bind_source_file(source_file);
            symbols.fetch_add(source_file.symbol_count.get() as i64, Ordering::Relaxed);
        }
        source_file
    };
    let source_files: Vec<_> = if single {
        files.iter().map(parse).collect()
    } else {
        let threads = std::thread::available_parallelism().map_or(1, std::num::NonZero::get).min(32);
        let pool = rayon::ThreadPoolBuilder::new().num_threads(threads).stack_size(256 << 20).build().unwrap();
        pool.install(|| files.par_iter().map(parse).collect())
    };

    let mode = if single { "single" } else { "parallel" };
    if bind {
        println!(
            "{{\"driver\":\"tsrs-0.18.0-bind-{mode}\",\"files\":{},\"bytes\":{},\"nodes\":{},\"symbols\":{},\"priority\":\"{priority}\"}}",
            source_files.len(),
            chars.load(Ordering::Relaxed),
            nodes.load(Ordering::Relaxed),
            symbols.load(Ordering::Relaxed)
        );
        std::mem::forget(source_files);
        return;
    }
    println!(
        "{{\"driver\":\"tsrs-0.18.0-{mode}\",\"files\":{},\"bytes\":{},\"nodes\":{},\"priority\":\"{priority}\"}}",
        source_files.len(),
        chars.load(Ordering::Relaxed),
        nodes.load(Ordering::Relaxed)
    );
    std::mem::forget(source_files);
}
