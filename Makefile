# Pillars Dialog Editor — developer shortcuts
# Usage: make <target>

.PHONY: run run-avalonia run-wpf build build-avalonia build-wpf test clean

# Default: run the cross-platform Avalonia build
run: run-avalonia

run-avalonia:
	dotnet run --project DialogEditor.Avalonia/DialogEditor.Avalonia.csproj

# WPF build — Windows only
run-wpf:
	dotnet run --project DialogEditor.WPF/DialogEditor.WPF.csproj

build: build-avalonia build-wpf

build-avalonia:
	dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj

build-wpf:
	dotnet build DialogEditor.WPF/DialogEditor.WPF.csproj

test:
	dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj

clean:
	dotnet clean
