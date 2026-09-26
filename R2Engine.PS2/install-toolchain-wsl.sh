#!/usr/bin/env bash
set -euo pipefail

version="v2.0.0"
install_root="${HOME}/.local/ps2dev"
archive="${HOME}/.cache/ps2dev-ubuntu-${version}.tar.gz"
url="https://github.com/ps2dev/ps2dev/releases/download/${version}/ps2dev-ubuntu-latest.tar.gz"

for command in curl tar make python3; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        printf 'Missing Linux prerequisite: %s\n' "${command}" >&2
        printf 'On Ubuntu, run: sudo apt update && sudo apt install -y curl make python3 genisoimage libmpc3 libmpfr6 libgmp10 zlib1g\n' >&2
        exit 1
    fi
done

mkdir -p "${HOME}/.cache" "${install_root}"

if [[ ! -f "${archive}" ]]; then
    curl -L --fail --progress-bar -o "${archive}" "${url}"
fi

tar -xzf "${archive}" --strip-components=1 -C "${install_root}"
"${install_root}/ee/bin/mips64r5900el-ps2-elf-gcc" --version | head -n 1

printf 'PS2DEV=%s\n' "${install_root}"
printf 'PS2SDK=%s/ps2sdk\n' "${install_root}"
printf 'GSKIT=%s/gsKit\n' "${install_root}"
