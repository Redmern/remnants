#!/usr/bin/env bash
# Phase 3a regression check (AC-8): the same headless autoplay, run with effects Off, High, and High with skip animations,
# must end in the same run state (a SHA-256 of the run's save). Repeated over zones and two autoplay policies ("attack":
# best hit chance else move; "cards": the first playable card first, through the hand's play path). Each run must report
# the effects level and skip setting it was asked for, or the check fails.
#
# What it shows: for these command sequences the presentation (pops, shake, hit-stop, cards, music, CRT) never feeds back
# into the sim. What it does not: other sequences, multiplayer, or live input during a hit-stop. It is a regression
# check, not a proof; the proof by design is that Core never reads GameSettings and the juice code only reads events.
# Headless only (never opens a window).
# Usage: tools/juice-determinism.sh [steps=120] [zones="suburb_ruins core_spire"] [policies="attack cards"]
set -uo pipefail
cd "$(dirname "$0")/.."
steps=${1:-120}
zones=${2:-"suburb_ruins core_spire"}
policies=${3:-"attack cards"}
out=$(mktemp -d)
fail=0
run() {
  local log=$1; shift
  timeout 900 godot-mono --headless --path game -- --scavenge --autoplay="$steps" --quit-after-autoplay "$@" > "$log" 2>&1
}
for zone in $zones; do
  for policy in $policies; do
    tag="$zone/$policy"
    base=(--zone="$zone" --autoplay-policy="$policy")
    run "$out/off.log" "${base[@]}" --effects=off &
    run "$out/high.log" "${base[@]}" --effects=high --juice-log &
    run "$out/skip.log" "${base[@]}" --effects=high --skip-animations &
    wait
    digests=()
    for want in "off:Off:False" "high:High:False" "skip:High:True"; do
      IFS=: read -r name level skip <<< "$want"
      line=$(grep -E "^Autoplay: done, policy $policy, .*, effects $level, skip $skip, state [0-9A-F]{16}$" "$out/$name.log" | tail -1)
      if [[ -z "$line" ]]; then
        echo "$tag $name: no 'Autoplay: done' line with effects $level / skip $skip"; fail=1; continue
      fi
      echo "$tag $name: ${line#Autoplay: done, }"
      if [[ $policy == cards && $line =~ " 0 cards played" ]]; then echo "$tag $name: the card policy played no card"; fail=1; fi
      digests+=("${line##* }")
    done
    stops=$(grep -c "^\[juice\] round" "$out/high.log")
    echo "$tag: $stops rounds with hit-stop (High)"
    if [[ ${#digests[@]} -ne 3 || $(printf '%s\n' "${digests[@]}" | sort -u | wc -l) -ne 1 ]]; then
      echo "$tag: FAILED (states differ or a run is missing)"; fail=1
    fi
  done
done
rm -rf "$out"
[[ $fail -eq 0 ]] && echo "juice-determinism: OK" || { echo "juice-determinism: FAILED"; exit 1; }
