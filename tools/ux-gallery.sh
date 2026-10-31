#!/usr/bin/env bash
# The UI/UX before/after gallery: captures every screen into docs/ux/SET (JPG, 1600 wide) and writes docs/ux/README.md,
# which shows each screen's before and after side by side.
#   tools/ux-gallery.sh after        # capture docs/ux/after (or "before" on an old checkout)
#   tools/ux-gallery.sh --page       # only rewrite docs/ux/README.md
# Windowed runs go through tools/godot-agent.sh (a hidden workspace, which renders slowly), JOBS at a time (default 4).
set -uo pipefail
cd "$(dirname "$0")/.."
set_=${1:?usage: tools/ux-gallery.sh after|before|--page}
jobs=${JOBS:-4}

# name frames flags...
SHOTS=$(cat <<'EOF'
menu 200
menu_settings 150 --menu-settings
new_game_choice 150 --new-game-choice
multiplayer 150 --multiplayer
colony 150 --colony
colony_demo 200 --colony --demo
build_menu 150 --colony --build-menu
family 200 --colony --family-demo
survivor_skills 200 --colony --family-demo --survivor-tab=1
survivor_body 200 --colony --family-demo --survivor-tab=2
survivor_family 200 --colony --family-demo --survivor-tab=3
crafting 250 --colony --crafting-demo
perks 250 --colony --perks-demo --survivor-tab=4
cybernetics_tree 250 --colony --cyber-demo --cyber-tree
medbay_dispensary 250 --colony --medicine-demo --dispensary
body_conditions 250 --colony --medicine-demo --survivor-tab=2
field_blood_bag 200 --scavenge --medicine-demo
cybernetics_body 250 --colony --cyber-demo --survivor-tab=2
cybernetics_medbay 250 --colony --cyber-demo
study 250 --colony --perks-demo
expedition 150 --colony --expedition
expedition_vehicle 250 --colony --garage-demo --expedition --zone=suburb_ruins --vehicle=buggy
expedition_step1 250 --colony --garage-demo --expedition --zone=suburb_ruins --expedition-step=1
expedition_step2 250 --colony --garage-demo --expedition --zone=suburb_ruins --expedition-step=2
expedition_step3 250 --colony --garage-demo --expedition --zone=suburb_ruins --expedition-step=3
expedition_step5 250 --colony --garage-demo --expedition --zone=suburb_ruins --vehicle=buggy --expedition-step=5
world_map 150 --colony --world-demo
world_vehicle 250 --colony --world-demo --vehicle=buggy
after_action 400 --colony --loop-demo --autopilot
archive 300 --colony --schematics-demo --archive
research 300 --colony --schematics-demo --research
trader 300 --colony --schematics-demo --radio=trader --collapse-log
radio_calls 300 --colony --schematics-demo --radio=calls
pause_menu 150 --colony --crafting-demo --pause-menu
settings 150 --colony --settings
garage 250 --colony --garage-demo
gear_bench 150 --colony --gear-demo
kitchen 150 --colony --food-demo --food-view=kitchen
electronics_lab 150 --colony --power-demo --bench-tab=Devices
story_radio 150 --colony --story --story-demo=radio
story_journal 150 --colony --story --story-demo=journal
story_briefing 150 --colony --story --story-demo=briefing
story_codex 150 --colony --story --story-demo=codex
raid_placement 150 --colony --raid-demo=placement
raid_warning 150 --colony --raid-demo=warning
raid_prep 150 --colony --raid-demo=prep
raid_battle 600 --colony --raid-demo=battle
raid_report 150 --colony --raid-demo=report
scavenge 200 --scavenge
scavenge_autoplay 600 --scavenge --autoplay=6
scavenge_pause 200 --scavenge --pause-menu
scavenge_vehicle 250 --scavenge --vehicle=truck --zone=suburb_ruins
fog_demo 3000 --scavenge --fog-demo
fog_overwatch 3000 --scavenge --fog-demo --select-unit=watcher
loot_pile 150 --scavenge --loot-demo=pile
salvage 150 --scavenge --loot-demo=salvage
tech_hack 200 --scavenge --tech-demo=hack
tech_drone 200 --scavenge --tech-demo=drone
story_mission 200 --scavenge --story-mission=m6
infotip_planner 300 --colony --garage-demo --expedition --zone=suburb_ruins --expedition-step=1 --help-demo=travel
infotip_colony 300 --colony --demo --help-demo
infotip_field 300 --scavenge --help-demo
help_index 200 --colony --demo --help-index
EOF
)

page() {
  local md=docs/ux/README.md
  {
    echo "# UI/UX before / after"
    echo
    echo "Every screen before the UI/UX rework (\`before/\`, commit e2df81b) and after it (\`after/\`), captured by"
    echo "\`tools/ux-gallery.sh\` at the same flags. Screens new in the rework have no before. The plan per screen is in"
    echo "[UX_AUDIT.md](../UX_AUDIT.md)."
    echo
    echo "| Screen | Before | After |"
    echo "|---|---|---|"
    while read -r name _; do
      [[ -z "$name" ]] && continue
      local b="—" a="—"
      [[ -f docs/ux/before/$name.jpg ]] && b="![$name before](before/$name.jpg)"
      [[ -f docs/ux/after/$name.jpg ]] && a="![$name after](after/$name.jpg)"
      echo "| \`$name\` | $b | $a |"
    done <<<"$SHOTS"
  } >"$md"
  echo "wrote $md"
}

if [[ "$set_" == "--page" ]]; then page; exit 0; fi

raw=build/ux/$set_
out=docs/ux/$set_
mkdir -p "$raw" "$out"
shot() {
  local name=$1 frames=$2; shift 2
  GODOT_AGENT_TIMEOUT=900 tools/godot-agent.sh --path game --disable-vsync --resolution 1920x1080 -- "$@" \
    --screenshot="$PWD/$raw/$name.png" --frames="$frames" --quit >"$raw/$name.log" 2>&1
  local errs
  errs=$(grep -cE "^(ERROR|SCRIPT ERROR)|Unhandled|Exception" "$raw/$name.log")
  if [[ -f $raw/$name.png ]]; then
    magick "$raw/$name.png" -resize 1600x -quality 80 "$out/$name.jpg"
    printf '%-22s errors %3s\n' "$name" "$errs"
  else
    printf '%-22s NO SHOT (see %s)\n' "$name" "$raw/$name.log"
  fi
}
export -f shot
export raw out
while read -r line; do
  [[ -n "$line" ]] && echo "$line"
done <<<"$SHOTS" | xargs -P "$jobs" -I{} bash -c 'shot {}'
page
