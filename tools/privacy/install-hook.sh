#!/usr/bin/env bash
# Install a pre-commit hook that runs .github/scripts/privacy-guard.sh --staged.
# Run from anywhere inside the repo: bash tools/privacy/install-hook.sh
# Idempotent. Never overwrites a different pre-commit hook: it prints how to
# chain the guard instead and exits 1.
set -eu

hooks=$(git rev-parse --git-path hooks) # honours core.hooksPath and worktrees
hook="$hooks/pre-commit"
marker='# privacy-guard hook'
cmd='bash .github/scripts/privacy-guard.sh --staged'

if [ -e "$hook" ] && ! grep -q "$marker" "$hook"; then
  echo "A different pre-commit hook already exists: $hook" >&2
  echo "Not overwriting it. To chain the guard, add these lines to it:" >&2
  echo "  cd \"\$(git rev-parse --show-toplevel)\" && $cmd || exit 1" >&2
  exit 1
fi

mkdir -p "$hooks"
cat > "$hook" <<EOF
#!/usr/bin/env bash
$marker (installed by tools/privacy/install-hook.sh)
cd "\$(git rev-parse --show-toplevel)" || exit 1
exec $cmd
EOF
chmod +x "$hook"
echo "Installed $hook"
