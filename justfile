set shell := ["bash", "-euo", "pipefail", "-c"]

project := "Jellyfin.Plugin.WatchSync"
plugin_guid := "aa4fe598-07fd-41b5-bf1d-65abab9980db"
build_dir := project / "bin/Release/net10.0"

jellyfin_image := "docker.io/jellyfin/jellyfin:12.4.20261026-211555"
dev_dir := justfile_directory() / ".dev"
dev_port := env("WATCHSYNC_DEV_PORT", "8096")
dev_container := "watchsync-dev"
dev_plugin_dir := dev_dir / "config/plugins/WatchSync"

# List the recipes
default:
    @just --list

# Compile the plugin (Release)
build:
    dotnet build -c Release {{ project }}

# Run the unit tests
test:
    dotnet test -c Release

# Run every pre-commit check on the whole repo (same as CI)
lint:
    pre-commit run --all-files --show-diff-on-failure

# Build the release zip for VERSION (e.g. 2.0.0) into artifacts/, with meta.json from build.yaml
package VERSION:
    #!/usr/bin/env bash
    set -euo pipefail
    [[ "{{ VERSION }}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "VERSION must be X.Y.Z, got {{ VERSION }}" >&2; exit 1; }
    # Jellyfin parses System.Version, where a missing 4th component is -1 (2.0.0 < 2.0.0.0).
    version="{{ VERSION }}.0"
    meta="{{ project }}/build.yaml"
    name=$(yq -r '.name' "$meta" | tr '[:upper:]' '[:lower:]')
    out="artifacts/$name-$version"
    rm -rf "$out" "$out.zip"
    mkdir -p "$out"

    dotnet build -c Release -p:Version="{{ VERSION }}" "{{ project }}"
    for artifact in $(yq -r '.artifacts[]' "$meta"); do cp "{{ build_dir }}/$artifact" "$out/"; done

    yq -o=json "{
      \"guid\": .guid, \"name\": .name, \"version\": \"$version\", \"targetAbi\": .targetAbi,
      \"framework\": .framework, \"owner\": .owner, \"category\": .category,
      \"overview\": .overview, \"description\": .description, \"changelog\": .changelog,
      \"timestamp\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"
    }" "$meta" > "$out/meta.json"

    (cd "$out" && zip -q -X "../$name-$version.zip" ./*)
    md5=$(md5sum "$out.zip" | cut -d' ' -f1)

    # Release notes from the Conventional Commits since the previous tag.
    tag="v{{ VERSION }}"
    if git rev-parse -q --verify "refs/tags/$tag" >/dev/null; then
      git cliff --current --strip all > "$out.md"
    else
      git cliff --unreleased --tag "$tag" --strip all > "$out.md"
    fi
    printf '\n---\nMD5 `%s`\n' "$md5" >> "$out.md"

    echo "$out.zip  $md5"
    echo "$out.md"

# Check that tool versions pinned outside the flake match what the flake provides
check-pins:
    #!/usr/bin/env bash
    set -euo pipefail
    status=0
    check() {
      local name=$1 flake=$2 pinned=$3 where=$4
      if [[ "$flake" == "$pinned" ]]; then
        echo "ok    $name $flake"
      else
        echo "DRIFT $name: flake provides $flake, $where says $pinned"
        status=1
      fi
    }
    check "dotnet SDK" \
      "$(nix eval --inputs-from . --raw nixpkgs#dotnet-sdk_10.version)" \
      "$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' global.json)" \
      global.json
    check just \
      "$(nix eval --inputs-from . --raw nixpkgs#just.version)" \
      "$(sed -n 's/.*just-version: *//p' .github/workflows/ci.yml)" \
      .github/workflows/ci.yml
    if [[ -f .github/workflows/release.yml ]]; then
      check just \
        "$(nix eval --inputs-from . --raw nixpkgs#just.version)" \
        "$(sed -n 's/.*just-version: *//p' .github/workflows/release.yml)" \
        .github/workflows/release.yml
      check git-cliff \
        "$(nix eval --inputs-from . --raw nixpkgs#git-cliff.version)" \
        "$(sed -n 's/.*tool: *git-cliff@//p' .github/workflows/release.yml)" \
        .github/workflows/release.yml
    fi
    exit "$status"

# Start the local Jellyfin test instance
dev-up:
    #!/usr/bin/env bash
    set -euo pipefail
    mkdir -p "{{ dev_dir }}"/{config,cache,media} "{{ dev_plugin_dir }}"
    cat > "{{ dev_plugin_dir }}/meta.json" <<'EOF'
    {
      "guid": "{{ plugin_guid }}",
      "name": "WatchSync",
      "version": "0.0.0.0",
      "targetAbi": "12.0.0.0",
      "owner": "Atilili",
      "category": "General",
      "description": "Local development build."
    }
    EOF
    podman run -d --replace --name "{{ dev_container }}" \
      -p "{{ dev_port }}:8096" \
      -v "{{ dev_dir }}/config:/config:Z" \
      -v "{{ dev_dir }}/cache:/cache:Z" \
      -v "{{ dev_dir }}/media:/media:Z" \
      "{{ jellyfin_image }}" >/dev/null
    echo "Jellyfin: http://localhost:{{ dev_port }}"

# Generate test media: the same movie and episodes in a 1080p and a 4K library (ids in .nfo files)
dev-media:
    #!/usr/bin/env bash
    set -euo pipefail
    media="{{ dev_dir }}/media"
    # A global LD_LIBRARY_PATH (alsa-lib from the host profile) breaks the flake ffmpeg.
    video() { # $1 = output file, $2 = resolution
      [[ -f "$1" ]] || env -u LD_LIBRARY_PATH ffmpeg -loglevel error -f lavfi -i "testsrc=duration=5:size=$2:rate=24" -c:v libx264 -pix_fmt yuv420p "$1"
    }
    movie() { # $1 library, $2 resolution, $3 title, $4 year, $5 tmdb, $6 imdb
      local dir="$media/$1/$3 ($4)"
      mkdir -p "$dir"
      video "$dir/$3 ($4).mkv" "$2"
      printf '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n<movie>\n  <title>%s</title>\n  <year>%s</year>\n  <uniqueid type="tmdb" default="true">%s</uniqueid>\n  <uniqueid type="imdb">%s</uniqueid>\n</movie>\n' \
        "$3" "$4" "$5" "$6" > "$dir/$3 ($4).nfo"
    }
    show() { # $1 library, $2 resolution
      local dir="$media/$1/Breaking Bad (2008)"
      mkdir -p "$dir/Season 01"
      printf '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n<tvshow>\n  <title>Breaking Bad</title>\n  <year>2008</year>\n  <uniqueid type="tvdb" default="true">81189</uniqueid>\n  <uniqueid type="imdb">tt0903747</uniqueid>\n</tvshow>\n' \
        > "$dir/tvshow.nfo"
      for e in 01 02; do video "$dir/Season 01/Breaking Bad - S01E$e.mkv" "$2"; done
    }
    movie movies-1080 1920x1080 Dune 2021 438631 tt1160419
    movie movies-4k 3840x2160 Dune 2021 438631 tt1160419
    movie movies-1080 1920x1080 Arrival 2016 329865 tt2543164
    show tv-1080 1920x1080
    show tv-4k 3840x2160
    find "$media" -type f | sort

# Build, install the plugin into the local instance and restart it
dev-deploy: build
    #!/usr/bin/env bash
    set -euo pipefail
    since=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    # Stop before copying: Jellyfin reads the loaded DLL from disk, overwriting it live
    # makes the old plugin crash on shutdown ("Bad IL range").
    # `podman restart` races rootless pasta for the host port: stop, then retry start.
    podman stop "{{ dev_container }}" >/dev/null
    cp "{{ build_dir }}/{{ project }}".{dll,pdb} "{{ dev_plugin_dir }}/"
    for _ in $(seq 10); do podman start "{{ dev_container }}" >/dev/null 2>&1 && break; sleep 1; done
    timeout 180 bash -c 'until curl -sf http://localhost:{{ dev_port }}/health >/dev/null; do sleep 2; done'
    podman logs --since "$since" "{{ dev_container }}" 2>&1 | grep -E 'WatchSync|\[ERR\]' || true

# Follow the WatchSync lines of the Jellyfin log
dev-logs:
    podman logs -f "{{ dev_container }}" 2>&1 | grep --line-buffered -E 'WatchSync|\[ERR\]'

# Stop the local instance (data in .dev/ is kept)
dev-down:
    podman stop "{{ dev_container }}"
