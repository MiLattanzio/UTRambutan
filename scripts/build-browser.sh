#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
image="${RAMBUTAN_BROWSER_BUILD_IMAGE:-utrambutan-browser-qt5-build:ut24.04}"
if ! docker image inspect "$image" >/dev/null 2>&1; then
  docker build -t "$image" tools/Rambutan.Browser
fi
mkdir -p artifacts/native-browser
docker run --rm --user "$(id -u):$(id -g)" \
  --mount "type=bind,src=$PWD,dst=/work" --workdir /work --entrypoint sh "$image" -c '
  set -eu
  /usr/lib/qt5/bin/qmllint tools/Rambutan.Browser/Browser.qml \
    > artifacts/native-browser/qmllint.log 2>&1
  cmake -S tools/Rambutan.Browser -B artifacts/native-browser/build-qt5 \
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_SYSTEM_NAME=Linux -DCMAKE_SYSTEM_PROCESSOR=aarch64 \
    -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++ \
    -DCMAKE_PREFIX_PATH=/usr/lib/aarch64-linux-gnu/cmake \
    -DCMAKE_FIND_ROOT_PATH=/usr/aarch64-linux-gnu \
    -DCMAKE_FIND_ROOT_PATH_MODE_PROGRAM=NEVER
  cmake --build artifacts/native-browser/build-qt5 --parallel 2
  dpkg-query -W qtbase5-dev:arm64 qtwebengine5-dev:arm64 > artifacts/native-browser/qt-versions.txt
  '
cp artifacts/native-browser/build-qt5/rambutan-browser artifacts/native-browser/
