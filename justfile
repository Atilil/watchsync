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

# Build, install the plugin into the local instance and restart it
dev-deploy: build
    #!/usr/bin/env bash
    set -euo pipefail
    cp "{{ build_dir }}/{{ project }}".{dll,pdb} "{{ dev_plugin_dir }}/"
    since=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    # `podman restart` races rootless pasta for the host port: stop, then retry start.
    podman stop "{{ dev_container }}" >/dev/null
    for _ in $(seq 10); do podman start "{{ dev_container }}" >/dev/null 2>&1 && break; sleep 1; done
    timeout 180 bash -c 'until curl -sf http://localhost:{{ dev_port }}/health >/dev/null; do sleep 2; done'
    podman logs --since "$since" "{{ dev_container }}" 2>&1 | grep -E 'WatchSync|\[ERR\]' || true

# Follow the WatchSync lines of the Jellyfin log
dev-logs:
    podman logs -f "{{ dev_container }}" 2>&1 | grep --line-buffered -E 'WatchSync|\[ERR\]'

# Stop the local instance (data in .dev/ is kept)
dev-down:
    podman stop "{{ dev_container }}"
