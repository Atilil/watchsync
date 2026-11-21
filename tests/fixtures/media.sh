#!/usr/bin/env bash
# Usage: tests/fixtures/media.sh DIR. The same movie and episodes in 1080p and 4K
# libraries, ids in .nfo files. Arrival exists only in 1080p (negative control).
set -euo pipefail

media=$1

# A global LD_LIBRARY_PATH (alsa-lib from the host profile) breaks the flake ffmpeg.
video() { # $1 = output file, $2 = resolution
    [[ -f "$1" ]] || env -u LD_LIBRARY_PATH ffmpeg -loglevel error \
        -f lavfi -i "testsrc=duration=5:size=$2:rate=24" -c:v libx264 -pix_fmt yuv420p "$1"
}

movie() { # $1 library, $2 resolution, $3 title, $4 year, $5 tmdb, $6 imdb
    local dir="$media/$1/$3 ($4)"
    mkdir -p "$dir"
    video "$dir/$3 ($4).mkv" "$2"
    printf '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n<movie>\n  <title>%s</title>\n  <year>%s</year>\n  <uniqueid type="tmdb" default="true">%s</uniqueid>\n  <uniqueid type="imdb">%s</uniqueid>\n</movie>\n' \
        "$3" "$4" "$5" "$6" >"$dir/$3 ($4).nfo"
}

show() { # $1 library, $2 resolution
    local dir="$media/$1/Breaking Bad (2008)"
    mkdir -p "$dir/Season 01"
    printf '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n<tvshow>\n  <title>Breaking Bad</title>\n  <year>2008</year>\n  <uniqueid type="tvdb" default="true">81189</uniqueid>\n  <uniqueid type="imdb">tt0903747</uniqueid>\n</tvshow>\n' \
        >"$dir/tvshow.nfo"
    for e in 01 02; do video "$dir/Season 01/Breaking Bad - S01E$e.mkv" "$2"; done
}

movie movies-1080 1920x1080 Dune 2021 438631 tt1160419
movie movies-4k 3840x2160 Dune 2021 438631 tt1160419
movie movies-1080 1920x1080 Arrival 2016 329865 tt2543164
show tv-1080 1920x1080
show tv-4k 3840x2160
