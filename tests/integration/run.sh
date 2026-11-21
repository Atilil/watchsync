#!/usr/bin/env bash
# Usage: tests/integration/run.sh IMAGE PLUGIN_DLL PLUGIN_GUID (KEEP=1 keeps the server).
# Offline server, remote fetchers off: ids come only from the .nfo fixtures, never TMDB.
set -euo pipefail

image=$1 dll=$2 guid=$3
root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
container=watchsync-it-$$
failures=0
token=

cleanup() {
    if ((failures > 0)); then
        echo "--- WatchSync and error lines of the server log"
        podman logs "$container" 2>&1 | grep -E 'WatchSync|\[ERR\]' | tail -50 || true
    fi
    if [[ -n ${KEEP:-} ]]; then
        echo "kept: container $container, data $work"
    else
        podman rm -f "$container" >/dev/null 2>&1 || true
        # Files written by the container belong to the rootless user namespace.
        podman unshare rm -rf "$work"
    fi
}
trap cleanup EXIT

api() { # METHOD PATH [JSON BODY]
    local auth='MediaBrowser Client="integration", Device="integration", DeviceId="integration", Version="1"'
    [[ -n $token ]] && auth+=", Token=\"$token\""
    local args=(-sSf -X "$1" -H 'Content-Type: application/json' -H "Authorization: $auth")
    [[ $# -ge 3 ]] && args+=(-d "$3")
    podman exec "$container" curl "${args[@]}" "http://localhost:8096$2"
}

wait_for() { # SECONDS DESCRIPTION COMMAND...
    local deadline=$((SECONDS + $1)) what=$2
    shift 2
    until "$@" >/dev/null 2>&1; do
        ((SECONDS < deadline)) || {
            echo "timeout waiting for $what" >&2
            exit 1
        }
        sleep 1
    done
}

check() { # DESCRIPTION EXPECTED ACTUAL
    if [[ $2 == "$3" ]]; then
        echo "ok   $1"
    else
        echo "FAIL $1: expected $2, got $3"
        failures=$((failures + 1))
    fi
}

# --- Server, first without the plugin, to set up a divergence only the full sync can repair

mkdir -p "$work"/{config,cache}
"$root/tests/fixtures/media.sh" "$work/media"

podman run -d --name "$container" --network none \
    -v "$work/config:/config:Z" -v "$work/cache:/cache:Z" -v "$work/media:/media:ro,Z" \
    "$image" >/dev/null

# /health answers while the startup page is still served: wait for the JSON API.
api_ready() { api GET /System/Info/Public | jq -e .Id; }
wait_for 180 "the Jellyfin API" api_ready

api POST /Startup/Configuration '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
api GET /Startup/User >/dev/null
api POST /Startup/User '{"Name":"admin","Password":"admin"}'
api POST /Startup/Complete
login=$(api POST /Users/AuthenticateByName '{"Username":"admin","Pw":"admin"}')
token=$(jq -r .AccessToken <<<"$login")
user=$(jq -r .User.Id <<<"$login")

no_fetchers='{"LibraryOptions":{"TypeOptions":[
  {"Type":"Movie","MetadataFetchers":[],"ImageFetchers":[]},
  {"Type":"Series","MetadataFetchers":[],"ImageFetchers":[]},
  {"Type":"Season","MetadataFetchers":[],"ImageFetchers":[]},
  {"Type":"Episode","MetadataFetchers":[],"ImageFetchers":[]}]}}'
for library in movies-1080:movies movies-4k:movies tv-1080:tvshows tv-4k:tvshows; do
    name=${library%%:*}
    api POST "/Library/VirtualFolders?name=$name&collectionType=${library#*:}&paths=/media/$name&refreshLibrary=false" "$no_fetchers"
done
api POST /Library/Refresh

# 3 movies + 4 episodes, then the scan task back to idle (series ids are read last).
scan_done() {
    [[ $(api GET "/Items?Recursive=true&IncludeItemTypes=Movie,Episode" | jq .TotalRecordCount) == 7 ]] &&
        api GET /ScheduledTasks | jq -e '.[] | select(.Key == "RefreshLibrary") | .State == "Idle"'
}
wait_for 180 "the library scan" scan_done

item() { # TYPE PATH_PREFIX
    api GET "/Items?Recursive=true&IncludeItemTypes=$1&Fields=Path&userId=$user" |
        jq -er --arg path "$2" '[.Items[] | select(.Path | startswith($path)) | .Id]
      | if length == 1 then .[0] else error("\(length) items match \($path)") end'
}
# Read as an item page does: from the server's cached item, not from a fresh query.
user_data() { api GET "/Items/$1?userId=$user" | jq -r ".UserData.$2"; }
played() { user_data "$1" Played; }

dune_1080=$(item Movie /media/movies-1080/Dune)
dune_4k=$(item Movie /media/movies-4k/Dune)
arrival=$(item Movie /media/movies-1080/Arrival)
ep1_1080=$(item Episode "/media/tv-1080/Breaking Bad (2008)/Season 01/Breaking Bad - S01E01")
ep1_4k=$(item Episode "/media/tv-4k/Breaking Bad (2008)/Season 01/Breaking Bad - S01E01")
ep2_1080=$(item Episode "/media/tv-1080/Breaking Bad (2008)/Season 01/Breaking Bad - S01E02")
ep2_4k=$(item Episode "/media/tv-4k/Breaking Bad (2008)/Season 01/Breaking Bad - S01E02")

api POST "/UserPlayedItems/$ep1_1080?userId=$user" >/dev/null

# --- Install the plugin and restart

podman stop "$container" >/dev/null
mkdir -p "$work/config/plugins/WatchSync"
cp "$dll" "$work/config/plugins/WatchSync/"
# Without the real GUID, Jellyfin derives the plugin id from the folder name.
jq -n --arg guid "$guid" '{guid: $guid, name: "WatchSync", version: "0.0.0.0",
  targetAbi: "12.0.0.0", owner: "Atilili", category: "General"}' \
    >"$work/config/plugins/WatchSync/meta.json"
podman start "$container" >/dev/null
wait_for 180 "the Jellyfin API after restart" api_ready

check "plugin loaded" Active "$(api GET /Plugins | jq -r '.[] | select(.Name == "WatchSync") | .Status')"

# --- Live sync: it runs inside the save, so the other copy is updated when the call returns

api POST "/UserPlayedItems/$dune_1080?userId=$user" >/dev/null
check "movie: marked copy is played" true "$(played "$dune_1080")"
check "movie: other copy follows" true "$(played "$dune_4k")"
check "movie: unrelated movie untouched" false "$(played "$arrival")"

api DELETE "/UserPlayedItems/$dune_4k?userId=$user" >/dev/null
check "unmark: other copy unplayed" false "$(played "$dune_1080")"
check "unmark: other copy play count reset" 0 "$(user_data "$dune_1080" PlayCount)"

# Both copies last 5 s, so the resume point carries over unscaled.
api POST "/UserItems/$dune_1080/UserData?userId=$user" \
    '{"PlaybackPositionTicks":20000000,"LastPlayedDate":"2026-01-01T20:00:00Z"}' >/dev/null
check "progress: other copy resumes at the same point" 20000000 "$(user_data "$dune_4k" PlaybackPositionTicks)"

api POST "/UserPlayedItems/$ep2_1080?userId=$user" >/dev/null
check "episode: same episode in the other library follows" true "$(played "$ep2_4k")"
check "episode: other episode of the season untouched" false "$(played "$ep1_4k")"

# --- Full sync repairs the divergence made while the plugin was not installed

check "full sync: divergence before the task" "true false" "$(played "$ep1_1080") $(played "$ep1_4k")"
task=$(api GET /ScheduledTasks | jq -er '.[] | select(.Key == "WatchSyncFullSync") | .Id')
api POST "/ScheduledTasks/Running/$task"
task_done() { api GET "/ScheduledTasks/$task" | jq -e '.State == "Idle" and .LastExecutionResult != null'; }
wait_for 120 "the full sync task" task_done
check "full sync: task completed" Completed "$(api GET "/ScheduledTasks/$task" | jq -r .LastExecutionResult.Status)"
check "full sync: missing played state repaired" true "$(played "$ep1_4k")"

((failures == 0)) || exit 1
echo "all passed"
