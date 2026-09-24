#!/usr/bin/env bash
set -euo pipefail

export PS2DEV="${PS2DEV:-${HOME}/.local/ps2dev}"
export PS2SDK="${PS2SDK:-${PS2DEV}/ps2sdk}"
export GSKIT="${GSKIT:-${PS2DEV}/gsKit}"
export PATH="${PS2DEV}/bin:${PS2DEV}/ee/bin:${PS2DEV}/iop/bin:${PS2DEV}/dvp/bin:${PS2SDK}/bin:/usr/local/bin:/usr/bin:/bin"

if [[ ! -x "${PS2DEV}/ee/bin/mips64r5900el-ps2-elf-gcc" ]]; then
    printf 'PS2 EE compiler not found under %s\n' "${PS2DEV}" >&2
    printf 'Run install-toolchain-wsl.sh first.\n' >&2
    exit 1
fi

cd "$(dirname "${BASH_SOURCE[0]}")"
make "$@"
mkdir -p bin/assets
cp "${PS2SDK}/iop/irx/audsrv.irx" bin/assets/audsrv.irx
