#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
serial="${1:?Usage: scripts/install-click.sh ADB_SERIAL}"
package="$(python3 -c 'import json; m=json.load(open("packaging/click/manifest.json")); print(m["name"]+"_"+m["version"]+"_"+m["architecture"]+".click")')"
adb -s "$serial" push "artifacts/click/$package" "/home/phablet/$package"
adb -s "$serial" shell "gdbus call --system --dest com.lomiri.click --object-path /com/lomiri/click --method com.lomiri.click.Install /home/phablet/$package"
printf 'Installato. Aprire Rambutan dal launcher Lomiri.\n'
