#!/usr/bin/env bash
# AC-8 (phase 3a): the same headless autoplay with effects Off, High, and High with skip animations must end in the same
# run state. Prints each run's digest line and fails when they differ. Headless only (never opens a window).
# Usage: tools/juice-determinism.sh [steps=120] [extra scavenge args...]
set -uo pipefail
cd "$(dirname "$0")/.."
steps=${1:-120}; shift || true
out=$(mktemp -d)
run() { timeout 600 godot-mono --headless --path game -- --scavenge --autoplay="$steps" --quit-after-autoplay "$@" > "$out/$(echo "$*" | tr -c 'a-z0-9' _).log" 2>&1; }
run --effects=off "$@" &
run --effects=high --juice-log "$@" &
run --effects=high --skip-animations "$@" &
wait
grep -h "Autoplay: done" "$out"/*.log
grep -h "\[juice\]" "$out"/*.log
digests=$(grep -ho "state [0-9A-F]*" "$out"/*.log | sort -u | wc -l)
runs=$(grep -l "Autoplay: done" "$out"/*.log | wc -l)
rm -rf "$out"
[[ $runs -eq 3 && $digests -eq 1 ]] && echo "juice-determinism: OK (3 runs, 1 state)" || { echo "juice-determinism: FAILED ($runs runs, $digests states)"; exit 1; }
