#!/bin/sh
set -eu
cd "${0%/*}"
export DOTNET_EnableDiagnostics=0
export RESOURCE_NAME="${APP_ID:-rambutan.milattanzio_rambutan}"
export RAMBUTAN_BUNDLED_TOOLS=1
export RAMBUTAN_TOOLS_DIR="$PWD/app/tools"
export PATH="$PWD/app/tools:$PWD/app/tools/ffmpeg:$PATH"
export LD_LIBRARY_PATH="$PWD/app/tools/ffmpeg:$PWD/app${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec ./app/Rambutan.App "$@"
