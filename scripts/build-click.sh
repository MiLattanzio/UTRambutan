#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
export NUGET_PACKAGES="${NUGET_PACKAGES:-$PWD/.nuget/packages}"
dotnet publish src/Rambutan.App/Rambutan.App.csproj -c Release -r linux-arm64 \
  --self-contained true -p:PublishAot=false -p:PublishTrimmed=false -p:PublishSingleFile=false \
  -p:RestoreLockedMode=true -o artifacts/linux-arm64
python3 scripts/prepare-native.py
scripts/build-browser.sh
mkdir -p artifacts/native-tools/browser
cp artifacts/native-browser/rambutan-browser artifacts/native-tools/browser/
if [[ "${RAMBUTAN_BROWSER_INPUT_TEST:-0}" == 1 ]]; then
  cp artifacts/native-browser/build/rambutan-browser-input-test artifacts/native-tools/browser/rambutan-browser
fi
mkdir -p artifacts/click
stage_dir="$(mktemp -d "$PWD/artifacts/click/stage.XXXXXX")"
mkdir -p "$stage_dir/app"
cp -a artifacts/linux-arm64/. "$stage_dir/app/"
cp -a artifacts/native-tools "$stage_dir/app/tools"
cp packaging/click/* "$stage_dir/"
chmod +x "$stage_dir/launch.sh" "$stage_dir/app/Rambutan.App"
if ! desktop-file-validate "$stage_dir/rambutan.desktop" > artifacts/click/desktop-validation.txt 2>&1; then
  # Click requires a package-relative icon; newer desktop-file-utils calls that
  # a future error. Keep this one known format mismatch, reject any other error.
  python3 - <<'CHECK'
from pathlib import Path
lines = Path('artifacts/click/desktop-validation.txt').read_text().splitlines()
if not lines or any('is an icon name with an extension' not in line for line in lines):
    raise SystemExit('Desktop validation failed; see artifacts/click/desktop-validation.txt')
CHECK
fi
image="${CLICK_BUILD_IMAGE:-clickable/amd64-ut24.04-2.x-arm64:latest}"
docker image inspect "$image" --format '{{.Id}} {{json .RepoDigests}}' > artifacts/click/build-image.txt
docker run --rm --user "$(id -u):$(id -g)" \
  --mount "type=bind,src=$PWD/artifacts/click,dst=/work" --workdir /work \
  --entrypoint click "$image" build --no-validate "${stage_dir##*/}"
package="$(python3 -c 'import json; m=json.load(open("packaging/click/manifest.json")); print(m["name"]+"_"+m["version"]+"_"+m["architecture"]+".click")')"
sha256sum "artifacts/click/$package" | tee artifacts/click/SHA256SUMS
set +e
docker run --rm --user "$(id -u):$(id -g)" \
  --mount "type=bind,src=$PWD/artifacts/click,dst=/work,readonly" --workdir /work \
  --entrypoint click-review "$image" "$package" > artifacts/click/review.txt 2>&1
review_status=$?
set -e
printf '%s\n' "$review_status" > artifacts/click/review-exit-code.txt
cat artifacts/click/review.txt
printf 'Click: artifacts/click/%s\nReview exit code: %s\n' "$package" "$review_status"
exit "$review_status"
