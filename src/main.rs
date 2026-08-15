use shiori::search::{SearchResult, search_files, search_text};
use shiori::workspace::Workspace;
use std::env;
use std::process::{Command, ExitCode};

const DEFAULT_LIMIT: usize = 20;
const MAX_LIMIT: usize = 100;

fn main() -> ExitCode {
    match run(env::args().skip(1).collect()) {
        Ok(()) => ExitCode::SUCCESS,
        Err(message) => {
            eprintln!("error: {message}");
            ExitCode::FAILURE
        }
    }
}

fn run(arguments: Vec<String>) -> Result<(), String> {
    let Some(command) = arguments.first().map(String::as_str) else {
        return Err(usage());
    };

    match command {
        "find" | "grep" => run_search(command, &arguments[1..]),
        "doctor" => run_doctor(),
        "--help" | "-h" | "help" => {
            println!("{}", usage());
            Ok(())
        }
        _ => Err(format!("unknown command: {command}\n{}", usage())),
    }
}

fn run_search(command: &str, arguments: &[String]) -> Result<(), String> {
    let query = arguments
        .first()
        .filter(|value| !value.starts_with('-'))
        .ok_or_else(usage)?;
    let workspace_path = option_value(arguments, "--allow").ok_or_else(|| {
        "--allow is required so Shiori cannot search outside an explicit workspace".to_owned()
    })?;
    let limit = option_value(arguments, "--limit")
        .map(|value| value.parse::<usize>().map_err(|_| "--limit must be an integer"))
        .transpose()?
        .unwrap_or(DEFAULT_LIMIT)
        .min(MAX_LIMIT);
    let workspace = Workspace::open(workspace_path).map_err(|error| error.to_string())?;
    let results = match command {
        "find" => search_files(&workspace, query, limit),
        "grep" => search_text(&workspace, query, limit),
        _ => unreachable!(),
    }
    .map_err(|error| error.to_string())?;

    for result in results {
        print_result(result);
    }
    Ok(())
}

fn option_value<'a>(arguments: &'a [String], option: &str) -> Option<&'a str> {
    arguments
        .windows(2)
        .find(|pair| pair[0] == option)
        .map(|pair| pair[1].as_str())
}

fn print_result(result: SearchResult) {
    match (result.line, result.snippet) {
        (Some(line), Some(snippet)) => println!("{}:{line}:{snippet}", result.path.display()),
        _ => println!("{}", result.path.display()),
    }
}

fn run_doctor() -> Result<(), String> {
    let ripgrep = Command::new("rg")
        .arg("--version")
        .output()
        .map(|output| output.status.success())
        .unwrap_or(false);
    println!("ripgrep: {}", if ripgrep { "available" } else { "unavailable" });
    if ripgrep {
        Ok(())
    } else {
        Err("ripgrep is required for text search".to_owned())
    }
}

fn usage() -> String {
    "Usage:\n  shiori find <query> --allow <directory> [--limit <1-100>]\n  shiori grep <query> --allow <directory> [--limit <1-100>]\n  shiori doctor".to_owned()
}
