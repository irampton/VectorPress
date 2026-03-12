#!/usr/bin/env bash

set -euo pipefail

DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCAL_DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
USE_SUDO=0

log() {
  printf '[vectorpress-setup] %s\n' "$*"
}

warn() {
  printf '[vectorpress-setup] warning: %s\n' "$*" >&2
}

die() {
  printf '[vectorpress-setup] error: %s\n' "$*" >&2
  exit 1
}

have_cmd() {
  command -v "$1" >/dev/null 2>&1
}

run_as_root() {
  if [[ $EUID -eq 0 ]]; then
    "$@"
    return
  fi

  if (( USE_SUDO )); then
    sudo "$@"
    return
  fi

  die "This step requires root privileges. Re-run with sudo or ensure sudo is installed."
}

append_path_hint() {
  local shell_rc
  for shell_rc in "$HOME/.bashrc" "$HOME/.zshrc"; do
    if [[ ! -f "$shell_rc" ]]; then
      continue
    fi

    if grep -Fq 'export PATH="$HOME/.dotnet:$PATH"' "$shell_rc"; then
      continue
    fi

    printf '\nexport PATH="$HOME/.dotnet:$PATH"\n' >> "$shell_rc"
    log "Added .NET PATH hint to $shell_rc"
  done
}

ensure_sudo() {
  if [[ $EUID -eq 0 ]]; then
    USE_SUDO=0
    return
  fi

  if have_cmd sudo; then
    USE_SUDO=1
    return
  fi

  USE_SUDO=0
}

dotnet_is_ready() {
  if ! have_cmd dotnet; then
    return 1
  fi

  local major
  major="$(dotnet --version 2>/dev/null | cut -d. -f1 || true)"
  [[ "$major" =~ ^[0-9]+$ ]] || return 1
  (( major >= 10 ))
}

ensure_os_release() {
  if [[ -f /etc/os-release ]]; then
    # shellcheck disable=SC1091
    source /etc/os-release
  fi
}

can_elevate() {
  [[ $EUID -eq 0 || $USE_SUDO -eq 1 ]]
}

deb_package_installed() {
  dpkg -s "$1" >/dev/null 2>&1
}

rpm_package_installed() {
  rpm -q "$1" >/dev/null 2>&1
}

pacman_package_installed() {
  pacman -Q "$1" >/dev/null 2>&1
}

install_linux_gui_deps() {
  ensure_os_release

  if have_cmd apt-get; then
    if deb_package_installed libice6 &&
       deb_package_installed libsm6 &&
       deb_package_installed libfontconfig1 &&
       deb_package_installed curl &&
       deb_package_installed ca-certificates; then
      log "Linux GUI dependencies already installed."
      return
    fi

    if ! can_elevate; then
      warn "Skipping Linux GUI dependency install because sudo/root is unavailable."
      return
    fi

    run_as_root apt-get update
    run_as_root apt-get install -y curl ca-certificates libice6 libsm6 libfontconfig1
    return
  fi

  if have_cmd dnf; then
    if rpm_package_installed libICE &&
       rpm_package_installed libSM &&
       rpm_package_installed fontconfig &&
       rpm_package_installed curl &&
       rpm_package_installed ca-certificates; then
      log "Linux GUI dependencies already installed."
      return
    fi

    if ! can_elevate; then
      warn "Skipping Linux GUI dependency install because sudo/root is unavailable."
      return
    fi

    run_as_root dnf install -y curl ca-certificates libICE libSM fontconfig
    return
  fi

  if have_cmd zypper; then
    if ! can_elevate; then
      warn "Skipping Linux GUI dependency install because sudo/root is unavailable."
      return
    fi

    run_as_root zypper --non-interactive install curl ca-certificates libICE6 libSM6 fontconfig
    return
  fi

  if have_cmd pacman; then
    if pacman_package_installed libice &&
       pacman_package_installed libsm &&
       pacman_package_installed fontconfig &&
       pacman_package_installed curl &&
       pacman_package_installed ca-certificates; then
      log "Linux GUI dependencies already installed."
      return
    fi

    if ! can_elevate; then
      warn "Skipping Linux GUI dependency install because sudo/root is unavailable."
      return
    fi

    run_as_root pacman -Sy --noconfirm curl ca-certificates libice libsm fontconfig
    return
  fi

  warn "Unsupported Linux package manager for GUI dependency install. Continuing."
}

install_dotnet_via_microsoft_apt() {
  ensure_os_release
  [[ "${ID:-}" == "ubuntu" || "${ID:-}" == "debian" || "${ID_LIKE:-}" == *debian* ]] || return 1

  local packages_config
  local version_id
  version_id="${VERSION_ID:-}"

  case "${ID:-}" in
    ubuntu)
      packages_config="https://packages.microsoft.com/config/ubuntu/${version_id}/packages-microsoft-prod.deb"
      ;;
    debian)
      packages_config="https://packages.microsoft.com/config/debian/${version_id}/packages-microsoft-prod.deb"
      ;;
    *)
      packages_config=""
      ;;
  esac

  [[ -n "$packages_config" ]] || return 1

  if ! can_elevate; then
    return 1
  fi

  run_as_root apt-get update
  run_as_root apt-get install -y curl ca-certificates gpg

  local temp_deb
  temp_deb="$(mktemp --suffix=.deb)"
  curl -fsSL "$packages_config" -o "$temp_deb"
  run_as_root dpkg -i "$temp_deb"
  rm -f "$temp_deb"

  run_as_root apt-get update
  run_as_root apt-get install -y "dotnet-sdk-${DOTNET_CHANNEL}"
}

install_dotnet_via_package_manager() {
  ensure_os_release

  if ! can_elevate && ! have_cmd brew; then
    return 1
  fi

  if have_cmd apt-get; then
    install_dotnet_via_microsoft_apt && return 0
    return 1
  fi

  if have_cmd dnf; then
    run_as_root dnf install -y "dotnet-sdk-${DOTNET_CHANNEL}" && return 0
    return 1
  fi

  if have_cmd zypper; then
    run_as_root zypper --non-interactive install "dotnet-sdk-${DOTNET_CHANNEL}" && return 0
    return 1
  fi

  if have_cmd pacman; then
    run_as_root pacman -Sy --noconfirm dotnet-sdk && return 0
    return 1
  fi

  if have_cmd brew; then
    brew install dotnet && return 0
    return 1
  fi

  return 1
}

install_dotnet_locally() {
  mkdir -p "$LOCAL_DOTNET_ROOT"

  local script_path
  script_path="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$script_path"
  bash "$script_path" --channel "$DOTNET_CHANNEL" --quality GA --install-dir "$LOCAL_DOTNET_ROOT"
  rm -f "$script_path"

  export PATH="$LOCAL_DOTNET_ROOT:$PATH"
  append_path_hint
}

ensure_dotnet() {
  if dotnet_is_ready; then
    log ".NET SDK already installed: $(dotnet --version)"
    return
  fi

  if [[ -x "$LOCAL_DOTNET_ROOT/dotnet" ]]; then
    export PATH="$LOCAL_DOTNET_ROOT:$PATH"
    if dotnet_is_ready; then
      log ".NET SDK available in $LOCAL_DOTNET_ROOT: $(dotnet --version)"
      append_path_hint
      return
    fi
  fi

  if install_dotnet_via_package_manager; then
    log "Installed .NET SDK via package manager."
    return
  fi

  warn "Package-manager install did not succeed. Falling back to a local user install."
  install_dotnet_locally
  log "Installed .NET SDK locally in $LOCAL_DOTNET_ROOT."
}

ensure_avalonia_templates() {
  if dotnet new list | grep -Fq 'avalonia.app'; then
    log "Avalonia templates already installed."
    return
  fi

  dotnet new install Avalonia.Templates
}

restore_repo() {
  cd "$REPO_ROOT"
  dotnet restore VectorPress.slnx
}

print_next_steps() {
  cat <<EOF

VectorPress setup complete.

Next steps:
  1. Restart your shell if this is the first local .NET install.
  2. Run: dotnet build "$REPO_ROOT/VectorPress.slnx"
  3. Run: dotnet run --project "$REPO_ROOT/src/VectorPress.App/VectorPress.App.csproj"
EOF
}

main() {
  ensure_sudo

  if ! have_cmd curl; then
    if ! can_elevate && ! have_cmd brew; then
      die "curl is required but sudo/root is unavailable to install it."
    fi

    if have_cmd apt-get || have_cmd dnf || have_cmd zypper || have_cmd pacman || have_cmd brew; then
      log "Installing curl."
      if have_cmd apt-get; then
        run_as_root apt-get update
        run_as_root apt-get install -y curl ca-certificates
      elif have_cmd dnf; then
        run_as_root dnf install -y curl ca-certificates
      elif have_cmd zypper; then
        run_as_root zypper --non-interactive install curl ca-certificates
      elif have_cmd pacman; then
        run_as_root pacman -Sy --noconfirm curl ca-certificates
      else
        brew install curl
      fi
    else
      die "curl is required but no supported package manager was found."
    fi
  fi

  case "$(uname -s)" in
    Linux)
      install_linux_gui_deps
      ;;
    Darwin)
      log "macOS detected. Skipping Linux GUI dependency install."
      ;;
    *)
      warn "Unsupported OS for package-managed dependencies. Trying .NET local install only."
      ;;
  esac

  ensure_dotnet
  export PATH="$LOCAL_DOTNET_ROOT:$PATH"
  ensure_avalonia_templates
  restore_repo
  print_next_steps
}

main "$@"
