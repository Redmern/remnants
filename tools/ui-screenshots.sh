#!/usr/bin/env bash
# Capture every HUD screen as PNG (for UI reviews and before/after comparisons). Needs a display.
#   tools/ui-screenshots.sh OUTDIR
# On Wayland it passes --display-driver wayland --disable-vsync so an unfocused window still renders.
set -uo pipefail
out=${1:?usage: tools/ui-screenshots.sh OUTDIR}
mkdir -p "$out"
cd "$(dirname "$0")/.."
driver=()
[[ -n "${WAYLAND_DISPLAY:-}" ]] && driver=(--display-driver wayland --disable-vsync)

shot() {
  local name=$1 frames=$2; shift 2
  timeout 120 godot-mono "${driver[@]}" --path game -- "$@" --screenshot="$out/$name.png" --frames="$frames" --quit \
    >"$out/$name.log" 2>&1 || echo "FAILED: $name (see $out/$name.log)"
}

shot menu              200
shot menu_settings     150 --menu-settings
shot multiplayer       150 --multiplayer
shot colony            150 --colony
shot colony_demo       200 --colony --demo
shot build_menu        150 --colony --build-menu
shot family            200 --colony --family-demo
shot survivor_skills   200 --colony --family-demo --survivor-tab=1
shot survivor_body     200 --colony --family-demo --survivor-tab=2
shot survivor_family   200 --colony --family-demo --survivor-tab=3
shot crafting          250 --colony --crafting-demo
shot perks             250 --colony --perks-demo --survivor-tab=4
shot cybernetics_tree  250 --colony --cyber-demo --cyber-tree
shot cybernetics_body  250 --colony --cyber-demo --survivor-tab=2
shot cybernetics_medbay 250 --colony --cyber-demo
shot study             250 --colony --perks-demo
shot expedition        150 --colony --expedition
shot world_map         150 --colony --world-demo
shot after_action      400 --colony --loop-demo --autopilot
shot archive           300 --colony --schematics-demo --archive
shot research          300 --colony --schematics-demo --research
shot trader            300 --colony --schematics-demo --radio=trader --collapse-log
shot radio_calls       300 --colony --schematics-demo --radio=calls
shot pause_menu        150 --colony --crafting-demo --pause-menu
shot settings          150 --colony --settings
shot scavenge          200 --scavenge
shot scavenge_autoplay 600 --scavenge --autoplay=6
shot scavenge_pause    200 --scavenge --pause-menu
shot fog_demo          3000 --scavenge --fog-demo
shot fog_overwatch     3000 --scavenge --fog-demo --select-unit=watcher
shot garage            250 --colony --garage-demo
shot expedition_vehicle 250 --colony --garage-demo --expedition --zone=suburb_ruins --vehicle=buggy
shot world_vehicle     250 --colony --world-demo --vehicle=buggy
shot scavenge_vehicle  250 --scavenge --vehicle=truck --zone=suburb_ruins
