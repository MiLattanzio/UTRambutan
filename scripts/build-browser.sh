#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
image="${RAMBUTAN_BROWSER_BUILD_IMAGE:-utrambutan-browser-build:ut24.04}"
if ! docker image inspect "$image" >/dev/null 2>&1; then
  docker build -t "$image" tools/Rambutan.Browser
fi
mkdir -p artifacts/native-browser
docker run --rm --user "$(id -u):$(id -g)" \
  --mount "type=bind,src=$PWD,dst=/work" --workdir /work --entrypoint sh "$image" -c '
  set -eu
  /usr/lib/qt6/bin/qmllint --bare -I /usr/lib/aarch64-linux-gnu/qt6/qml \
    --unqualified disable --uncreatable-type error --non-list-property error \
    --read-only-property error tools/Rambutan.Browser/Browser.qml \
    > artifacts/native-browser/qmllint.log 2>&1
  cmake -S tools/Rambutan.Browser -B artifacts/native-browser/build \
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_SYSTEM_NAME=Linux -DCMAKE_SYSTEM_PROCESSOR=aarch64 \
    -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++ \
    -DQT_HOST_PATH=/usr -DCMAKE_PREFIX_PATH=/usr/lib/aarch64-linux-gnu/cmake \
    -DCMAKE_FIND_ROOT_PATH=/usr/aarch64-linux-gnu \
    -DCMAKE_FIND_ROOT_PATH_MODE_PROGRAM=NEVER
  cmake --build artifacts/native-browser/build --parallel 2
  dpkg-query -W qt6-base-dev:arm64 qt6-webengine-dev:arm64 > artifacts/native-browser/qt-versions.txt
  '
cp artifacts/native-browser/build/rambutan-browser artifacts/native-browser/
