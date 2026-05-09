# Pillars Dialog Editor — developer shortcuts
# Usage: make <target>

.PHONY: run build test clean

run:
	dotnet run --project DialogEditor.Avalonia/DialogEditor.Avalonia.csproj

build:
	dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj

test:
	dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj

clean:
	dotnet clean
