// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Oracle 3: canonical AST dump via the tsrs v0.18.0 parser, a port of harness/astdump/go/main.go
// (same format, same flag names, same derived attributes, positions converted from UTF-8 byte
// offsets to UTF-16 code units). tsrs is a port of typescript-go, so its AST and flags are Go's.
//
// Usage: astdump-rs <corpus dir>   (writes <corpus>/dumps/rs/<id>.txt)

use std::fmt::Write as _;
use std::panic::{AssertUnwindSafe, catch_unwind};
use tsrs_ast::{Kind, Node, NodeFlags, SourceFileParseOptions};
use tsrs_core::{P, ScriptKind, TextPos, tspath::to_path};

const FLAG_NAMES: &[(NodeFlags, &str)] = &[
    (NodeFlags::Let, "Let"),
    (NodeFlags::Const, "Const"),
    (NodeFlags::Using, "Using"),
    (NodeFlags::Reparsed, "Reparsed"),
    (NodeFlags::Synthesized, "Synthesized"),
    (NodeFlags::OptionalChain, "OptionalChain"),
    (NodeFlags::ExportContext, "ExportContext"),
    (NodeFlags::ContainsThis, "ContainsThis"),
    (NodeFlags::HasImplicitReturn, "HasImplicitReturn"),
    (NodeFlags::HasExplicitReturn, "HasExplicitReturn"),
    (NodeFlags::DisallowInContext, "DisallowInContext"),
    (NodeFlags::YieldContext, "YieldContext"),
    (NodeFlags::DecoratorContext, "DecoratorContext"),
    (NodeFlags::AwaitContext, "AwaitContext"),
    (NodeFlags::DisallowConditionalTypesContext, "DisallowConditionalTypesContext"),
    (NodeFlags::ThisNodeHasError, "ThisNodeHasError"),
    (NodeFlags::JavaScriptFile, "JavaScriptFile"),
    (NodeFlags::ThisNodeOrAnySubNodesHasError, "ThisNodeOrAnySubNodesHasError"),
    (NodeFlags::HasAsyncFunctions, "HasAsyncFunctions"),
    (NodeFlags::PossiblyContainsDynamicImport, "PossiblyContainsDynamicImport"),
    (NodeFlags::PossiblyContainsImportMeta, "PossiblyContainsImportMeta"),
    (NodeFlags::HasJSDoc, "HasJSDoc"),
    (NodeFlags::JSDoc, "JSDoc"),
    (NodeFlags::Ambient, "Ambient"),
    (NodeFlags::InWithStatement, "InWithStatement"),
    (NodeFlags::JsonFile, "JsonFile"),
    (NodeFlags::PossiblyContainsDeprecatedTag, "PossiblyContainsDeprecatedTag"),
    (NodeFlags::Unreachable, "Unreachable"),
    (NodeFlags::ReparserTransformedLiteral, "ReparserTransformedLiteral"),
];

fn flag_names(n: &Node) -> Vec<String> {
    let flags = n.flags();
    let mut names = Vec::new();
    let mut known = NodeFlags::empty();
    for &(bit, name) in FLAG_NAMES {
        known |= bit;
        if !flags.contains(bit) {
            continue;
        }
        let name = match (n.kind(), name) {
            (Kind::Identifier, "ContainsThis") => "IdentifierHasExtendedUnicodeEscape",
            (Kind::Identifier, "HasAsyncFunctions") => "IdentifierIsInJSDocNamespace",
            (Kind::ModuleDeclaration, "OptionalChain") => "NestedNamespace",
            _ => name,
        };
        names.push(name.to_string());
    }
    let rest = flags.bits() & !known.bits();
    if rest != 0 {
        names.push(format!("Unknown0x{rest:x}"));
    }
    if n.kind() == Kind::ModuleDeclaration {
        match n.as_module_declaration().keyword() {
            Kind::NamespaceKeyword => names.push("=Namespace".to_string()),
            Kind::GlobalKeyword => names.push("=GlobalAugmentation".to_string()),
            _ => {}
        }
    }
    names.sort();
    names
}

// Every UTF-8 byte offset of text (0..=len) mapped to a UTF-16 offset.
fn utf16_offsets(text: &str) -> Vec<u32> {
    let mut offsets = vec![0u32; text.len() + 1];
    let mut u = 0u32;
    for (i, c) in text.char_indices() {
        for k in 0..c.len_utf8() {
            offsets[i + k] = u;
        }
        u += c.len_utf16() as u32;
    }
    offsets[text.len()] = u;
    offsets
}

fn script_kind(kind: &str) -> ScriptKind {
    match kind {
        "TS" => ScriptKind::TS,
        "TSX" => ScriptKind::TSX,
        "JS" => ScriptKind::JS,
        "JSX" => ScriptKind::JSX,
        "JSON" => ScriptKind::JSON,
        other => panic!("unknown kind {other}"),
    }
}

fn dump(file_name: &str, kind: &str, text: &str) -> String {
    let opts = SourceFileParseOptions {
        file_name: file_name.to_string(),
        path: to_path(file_name, "/", true),
        external_module_indicator_options: Default::default(),
    };
    let file = tsrs_parser::parse_source_file(opts, text, script_kind(kind));
    let offsets = utf16_offsets(text);
    let pos = |p: TextPos| -> i64 {
        match p.source_offset() {
            Some(o) if (o as usize) < offsets.len() => i64::from(offsets[o as usize]),
            Some(o) => i64::from(o),
            None => -1,
        }
    };
    let mut out = String::new();
    fn visit(n: P<Node>, depth: usize, out: &mut String, pos: &dyn Fn(TextPos) -> i64) {
        let node: &Node = n.get();
        for _ in 0..depth {
            out.push_str("  ");
        }
        let _ = write!(out, "{:?} {} {}", node.kind(), pos(node.pos()), pos(node.end()));
        let names = flag_names(node);
        if !names.is_empty() {
            out.push(' ');
            out.push_str(&names.join("|"));
        }
        out.push('\n');
        node.for_each_child(&mut |child: P<Node>| {
            visit(child, depth + 1, out, pos);
            false
        });
    }
    visit(file.as_node(), 0, &mut out, &pos);
    let mut section = |name: &str, diagnostics: &[P<tsrs_ast::Diagnostic>]| {
        for d in diagnostics {
            let d = d.get();
            let start = d.pos().map_or(-1, |p| i64::from(offsets.get(p as usize).copied().unwrap_or(p)));
            let end = d.end().map_or(-1, |p| i64::from(offsets.get(p as usize).copied().unwrap_or(p)));
            let _ = writeln!(out, "!{name} {} {start} {}", d.code(), end - start);
        }
    };
    section("diag", file.diagnostics());
    section("jsdiag", file.js_diagnostics());
    section("jsdocdiag", file.jsdoc_diagnostics());
    out
}

// Parsing recurses deeply: run on a thread with a 512 MiB stack, as the tsrs command line does.
fn main() {
    std::thread::Builder::new().stack_size(512 << 20).spawn(run).unwrap().join().unwrap();
}

fn run() {
    let corpus = std::env::args().nth(1).expect("usage: astdump-rs <corpus dir>");
    let manifest: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(format!("{corpus}/manifest.json")).unwrap()).unwrap();
    let out_dir = format!("{corpus}/dumps/rs");
    let _ = std::fs::remove_dir_all(&out_dir);
    std::fs::create_dir_all(&out_dir).unwrap();
    std::panic::set_hook(Box::new(|_| {}));
    let mut panics = 0;
    let units = manifest["units"].as_array().unwrap();
    for u in units {
        let id = u["id"].as_str().unwrap();
        let text = std::fs::read_to_string(format!("{corpus}/units/{id}.txt")).unwrap();
        let (file_name, kind) = (u["fileName"].as_str().unwrap(), u["kind"].as_str().unwrap());
        let out = match catch_unwind(AssertUnwindSafe(|| dump(file_name, kind, &text))) {
            Ok(s) => s,
            Err(e) => {
                panics += 1;
                let msg = e.downcast_ref::<String>().cloned().or_else(|| e.downcast_ref::<&str>().map(|s| s.to_string())).unwrap_or_default();
                format!("!panic {msg}\n")
            }
        };
        std::fs::write(format!("{out_dir}/{id}.txt"), out).unwrap();
    }
    println!("rs: units {}, panics {panics}", units.len());
}
