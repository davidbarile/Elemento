#!/usr/bin/env python3
import json
import re
import sys
from pathlib import Path


PROJECT_ID = "dgmpxhxsfgfxaidxwmzq"
FUNCTIONS_DIR = Path(__file__).resolve().parent
SHARED_DIR = FUNCTIONS_DIR / "_shared"
IMPORT_RE = re.compile(r'(?:import|export)\s+(?:[^"\']*?\s+from\s+)?["\']([^"\']+)["\']', re.DOTALL)


def shared_dependencies(entrypoint: Path) -> list[Path]:
    pending = [entrypoint]
    seen: set[Path] = set()
    shared: set[Path] = set()

    while pending:
        source = pending.pop().resolve()
        if source in seen:
            continue
        seen.add(source)
        for specifier in IMPORT_RE.findall(source.read_text(encoding="utf-8")):
            if not specifier.startswith("."):
                continue
            dependency = (source.parent / specifier).resolve()
            if SHARED_DIR not in dependency.parents and dependency != SHARED_DIR:
                continue
            if "golden" in dependency.parts or dependency.name.endswith(".test.ts"):
                continue
            if not dependency.is_file():
                raise SystemExit(f"missing shared import: {dependency}")
            shared.add(dependency)
            pending.append(dependency)
    return sorted(shared)


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit(f"usage: {Path(sys.argv[0]).name} FUNCTION_NAME")

    name = sys.argv[1]
    if not re.fullmatch(r"[a-z0-9-]+", name):
        raise SystemExit("invalid function name")
    function_dir = FUNCTIONS_DIR / name
    entrypoint = function_dir / "index.ts"
    if not entrypoint.is_file():
        raise SystemExit(f"unknown function: {name}")

    files = [{"name": "index.ts", "content": f"$file:{entrypoint.resolve()}"}]
    files.extend(
        {
            "name": Path("..").joinpath("_shared", dependency.relative_to(SHARED_DIR)).as_posix(),
            "content": f"$file:{dependency}",
        }
        for dependency in shared_dependencies(entrypoint)
    )
    print(json.dumps({
        "project_id": PROJECT_ID,
        "name": name,
        "entrypoint_path": "index.ts",
        "verify_jwt": True,
        "files": files,
    }))


if __name__ == "__main__":
    main()
