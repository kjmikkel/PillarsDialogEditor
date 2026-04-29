#!/usr/bin/env bash
# Launch the cross-platform Avalonia build.
# Requires: .NET 8 SDK — https://dot.net
# Linux extra: libice6 libsm6 libfontconfig1 (e.g. apt install)

set -e
cd "$(dirname "$0")"

if ! command -v dotnet &>/dev/null; then
    echo "Error: .NET SDK not found."
    echo "Install from https://dot.net or your package manager."
    exit 1
fi

exec dotnet run --project DialogEditor.Avalonia/DialogEditor.Avalonia.csproj "$@"
