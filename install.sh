#!/bin/sh
# Installs the connapse CLI on macOS or Linux from a GitHub release. No .NET needed.
#
#   curl -fsSL https://raw.githubusercontent.com/Destrayon/connapse-cli/main/install.sh | sh
#
# Pin a version (prereleases too) or pick the directory:
#
#   curl -fsSL .../install.sh | sh -s -- --version v0.5.1-alpha.1 --install-dir /usr/local/bin
#
# The same settings can come from CONNAPSE_VERSION and CONNAPSE_INSTALL_DIR.
# The default directory is ~/.local/bin, which needs no sudo.
#
# Written as one function called on the last line, so a download cut off midway runs nothing.

set -eu

REPO="Destrayon/connapse-cli"

say() { printf 'connapse-install: %s\n' "$*"; }
err() { printf 'connapse-install: error: %s\n' "$*" >&2; exit 1; }

usage() {
  cat <<'EOF'
Install the connapse CLI.

Options:
  --version <tag>       Release to install, e.g. v0.5.0 or v0.5.1-alpha.1 (default: latest)
  --install-dir <dir>   Where to put the binary (default: ~/.local/bin)
  -h, --help            Show this help

Environment: CONNAPSE_VERSION, CONNAPSE_INSTALL_DIR (flags win).
EOF
}

download() {
  # $1 url, $2 output file. Fails on HTTP errors.
  if command -v curl >/dev/null 2>&1; then
    curl --proto '=https' --tlsv1.2 -fsSL --retry 3 -o "$2" "$1"
  elif command -v wget >/dev/null 2>&1; then
    wget -q --https-only -O "$2" "$1"
  else
    err "need curl or wget to download"
  fi
}

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | cut -d' ' -f1
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | cut -d' ' -f1
  fi
}

detect_asset() {
  os=$(uname -s)
  arch=$(uname -m)
  case "$os" in
    Linux)
      # The Linux build links against glibc, so it will not start on musl systems like Alpine.
      if ldd --version 2>&1 | grep -qi musl; then
        err "musl-based Linux (e.g. Alpine) is not supported; use 'dotnet tool install -g Connapse.CLI' instead"
      fi
      case "$arch" in
        x86_64 | amd64) echo "connapse-cli-linux-x64" ;;
        *) err "no Linux build for $arch yet; only x86_64 is published. Use 'dotnet tool install -g Connapse.CLI' instead" ;;
      esac
      ;;
    Darwin)
      # A shell running under Rosetta reports x86_64 on Apple Silicon; sysctl tells the truth.
      if [ "$arch" = x86_64 ] && (sysctl hw.optional.arm64 2>/dev/null || true) | grep -q ': 1'; then
        arch=arm64
      fi
      case "$arch" in
        arm64 | aarch64) echo "connapse-cli-osx-arm64" ;;
        x86_64) echo "connapse-cli-osx-x64" ;;
        *) err "unsupported macOS architecture: $arch" ;;
      esac
      ;;
    MINGW* | MSYS* | CYGWIN* | Windows_NT)
      err "on Windows, run in PowerShell: irm https://raw.githubusercontent.com/$REPO/main/install.ps1 | iex"
      ;;
    *) err "unsupported OS: $os" ;;
  esac
}

main() {
  version="${CONNAPSE_VERSION:-}"
  install_dir="${CONNAPSE_INSTALL_DIR:-$HOME/.local/bin}"

  while [ $# -gt 0 ]; do
    case "$1" in
      --version) [ $# -ge 2 ] || err "--version needs a value"; version="$2"; shift 2 ;;
      --version=*) version="${1#*=}"; shift ;;
      --install-dir | --to) [ $# -ge 2 ] || err "$1 needs a value"; install_dir="$2"; shift 2 ;;
      --install-dir=* | --to=*) install_dir="${1#*=}"; shift ;;
      -h | --help) usage; exit 0 ;;
      *) err "unknown option: $1 (see --help)" ;;
    esac
  done

  asset=$(detect_asset)

  if [ -z "$version" ] || [ "$version" = latest ]; then
    base="https://github.com/$REPO/releases/latest/download"
    label="latest release"
  else
    case "$version" in v*) ;; *) version="v$version" ;; esac
    base="https://github.com/$REPO/releases/download/$version"
    label="$version"
  fi

  tmp=$(mktemp -d 2>/dev/null || mktemp -d -t connapse)
  trap 'rm -rf "$tmp"' EXIT INT TERM

  say "downloading $asset ($label)"
  download "$base/$asset" "$tmp/connapse" || err "download failed: $base/$asset (does that release exist?)"

  # Releases from v0.5.1 on publish checksums.txt; older ones have none, so skip the check there.
  if download "$base/checksums.txt" "$tmp/checksums.txt" 2>/dev/null; then
    expected=$(awk -v f="$asset" '$2 == f || $2 == "*" f { print $1 }' "$tmp/checksums.txt")
    actual=$(sha256_of "$tmp/connapse")
    if [ -z "$expected" ]; then
      err "checksums.txt has no entry for $asset"
    elif [ -z "$actual" ]; then
      say "warning: no sha256sum or shasum found; skipping checksum check"
    elif [ "$expected" != "$actual" ]; then
      err "checksum mismatch for $asset: expected $expected, got $actual"
    else
      say "checksum verified"
    fi
  else
    say "this release has no checksums.txt; skipping checksum check"
  fi

  chmod +x "$tmp/connapse"

  if ! mkdir -p "$install_dir" 2>/dev/null || [ ! -w "$install_dir" ]; then
    if command -v sudo >/dev/null 2>&1; then
      say "$install_dir is not writable; using sudo"
      sudo mkdir -p "$install_dir"
      sudo mv "$tmp/connapse" "$install_dir/connapse"
    else
      err "$install_dir is not writable; pass --install-dir or set CONNAPSE_INSTALL_DIR"
    fi
  else
    mv "$tmp/connapse" "$install_dir/connapse"
  fi

  say "installed to $install_dir/connapse"
  "$install_dir/connapse" --version

  case ":$PATH:" in
    *":$install_dir:"*) ;;
    *)
      say "$install_dir is not on your PATH. Add it, for example:"
      say "  echo 'export PATH=\"$install_dir:\$PATH\"' >> ~/.profile   # or ~/.zshrc, ~/.bashrc"
      ;;
  esac
}

main "$@"
