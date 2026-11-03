#!/usr/bin/env bash
# Usage: DISPLAY=:0 tools/smoke-playthrough.sh ["07-*"]  (a glob picks steps); screenshots and logs in build/smoke.
# Playthrough smoke test: one launch per step, a screenshot and a log each. DISPLAY must point at a real display;
# --disable-vsync keeps a window on a hidden workspace from waiting on frame callbacks it never gets.
cd "$(dirname "$0")/.."
SH=build/smoke
mkdir -p $SH
step() { # name frames flags...
  local name=$1 frames=$2; shift 2
  GODOT_AGENT_TIMEOUT=180 tools/godot-agent.sh --path game --disable-vsync -- "$@" --screenshot=$PWD/$SH/$name.png --frames=$frames --quit > $SH/$name.log 2>&1
  local code=$?
  local errs=$(grep -cE "^(ERROR|SCRIPT ERROR)|Unhandled|Exception|\[(worker|construction)-demo\] (no |assign failed|picker is empty)" $SH/$name.log)
  printf '%-24s exit %3s  errors %3s  %s\n' "$name" "$code" "$errs" "$([[ -f $SH/$name.png ]] && echo shot || echo NO-SHOT)"
}
only="${1:-}"
run() { [[ -z "$only" || "$1" == $only ]] && step "$@"; }
run 02-new-game-story   300 --new-game=story
run 03-radio-call       150 --colony --story --story-demo=radio
run 04-journal-m1       150 --colony --story --story-demo=journal
run 05-briefing         150 --colony --story --story-demo=briefing
run 05b-briefing-warn   150 --colony --story --story-demo=briefing-warn
run 06-garage           150 --colony --garage-demo
run 07-planner-vehicle  150 --colony --garage-demo --expedition --zone=outskirts --vehicle=handcart
run 08-world-drive      240 --colony --world-demo --vehicle=motorbike
run 09-field-vehicle    150 --scavenge --vehicle=buggy
run 10-fight-loot-pile  150 --scavenge --loot-demo=pile
run 11-salvage          150 --scavenge --loot-demo=salvage
run 12-return           900 --colony --loop-demo --autopilot
run 13-gear-bench       150 --colony --gear-demo
run 14-armory           150 --colony --crafting-demo
run 15-kitchen          150 --colony --food-demo --food-view=kitchen
run 16-dispensary       150 --colony --medicine-demo --dispensary
run 17-electronics-lab  150 --colony --power-demo --bench-tab=Devices
run 18-raid-warning     150 --colony --raid-demo=warning
run 19-raid-prep        150 --colony --raid-demo=prep
run 20-defense-battle   600 --colony --raid-demo=battle
run 21-raid-report      150 --colony --raid-demo=report
# Review fixes 1: build mode shows footprints only; "+" on a worker slot opens the picker and a click assigns.
run 22-build-footprints 300 --colony --demo --placing-demo --hour=12
run 23-worker-picker    300 --colony --worker-demo=open
run 24-worker-assign    600 --colony --worker-demo=assign
run 24b-worker-multi    600 --colony --demo --worker-demo=multi
# UI/UX rework: the expedition planner's steps, InfoTips (hover then click: the tooltip, then the detail popover with live
# numbers) and the help index. The log says which tip it opened ([help-demo]).
run 25-planner-review   300 --colony --garage-demo --expedition --zone=suburb_ruins --vehicle=buggy --expedition-step=5
run 26-infotip-travel   300 --colony --garage-demo --expedition --zone=suburb_ruins --expedition-step=1 --help-demo=travel
run 27-infotip-colony   300 --colony --demo --help-demo
run 28-infotip-field    300 --scavenge --help-demo
run 29-help-index       200 --colony --demo --help-index
# Construction: place a building and assign a builder through the "+" slot, a site at 0/50/100%, an upgrade on scaffolding.
run 30-site-assign      600 --colony --construction-demo=assign
run 31-site-0           240 --colony --construction-demo=0
run 32-site-50          240 --colony --construction-demo=50 --site-tip
run 33-site-100         240 --colony --construction-demo=100
run 34-site-upgrade     240 --colony --construction-demo=upgrade
exit 0
