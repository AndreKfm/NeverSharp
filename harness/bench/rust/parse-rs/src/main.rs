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
// Usage: parse-rs [--bind] [--single] <file list>

use rayon::prelude::*;
use std::sync::atomic::{AtomicI64, Ordering};
use tsrs_ast::SourceFileParseOptions;
use tsrs_core::tspath::{normalize_slashes, to_path};
use tsrs_vfs::osvfs;

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

fn run() {
    let mut args: Vec<String> = std::env::args().skip(1).collect();
    let (mut single, mut bind) = (false, false);
    while args.first().is_some_and(|a| a.starts_with("--")) {
        match args.remove(0).as_str() {
            "--single" => single = true,
            "--bind" => bind = true,
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
