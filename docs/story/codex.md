# Codex

Lore pages the Codex panel shows (STORY.md "How it shows"). Three tabs: **People**, **Logs** (found in the field) and
**The story so far** (one page per mission, written as it closes). Each entry: id, title, unlocked by (**new** effect
`Codex <id>`, or a field `Hack` of a tagged terminal), text. Pages can grow: a line marked with a condition is added when
that condition holds (a death, a choice). Plain words, short, no lore dumps — the logs are where the backstory lives.

## People

| id | Title | Unlocked by |
|---|---|---|
| `cx_iris` | Iris Vahn, "Lantern" | M2 (Act 1); grows at M11 |
| `cx_marrow` | Abel Marrow | M4 (Act 1); grows at M6 |
| `cx_dee` | Dee | M6 |
| `cx_hale` | Dr. Marguerite Hale | M7 |
| `cx_cole` | Cole Brannigan | M3 (Act 1); grows at M9 and Cole's Last Job |
| `cx_seven` | Seven / L-0707 | M10 (any choice) |
| `cx_halcyon` | Halcyon | M14 |
| `cx_pell` | Pell | M1 (Act 1); grows when he comes of age |

**`cx_iris`** — Talks to the colony over the Radio Mast. Knows more about the Network than she should. Grows tomatoes.
- *(M11 read ≥ 3 logs)* Wrote the consent override. Has been in a hole under the Rail Depot since the Severance, on purpose.
- *(iris_home = 1)* Came home. Still sleeps with the headset on.
- *(iris dead)* Her terminal still plays. Pell knows the recordings by heart.

**`cx_marrow`** — Runs the Long Haul: four trucks, one road, everything for sale except the road. Says "in that order" a lot.
- *(m6_saved = 2)* Lost two passengers in the Ruins to save the fuel. Counts people first now.
- *(marrow dead)* Died in his cab on the Ruins road. Said he'd get out. He always got out.

**`cx_dee`** — Marrow's second. Shaved head, long coat, worse prices. The trucks always arrive.

**`cx_hale`** — Field surgeon. Ran a clinic out of a drowned pharmacy until the water won. "A leg is a leg. Nobody touches the head."
- *(doc_state = 3)* Left when the colony fitted a second Network-grade limb. Still sends medicine.

**`cx_cole`** — Leads the Tally, a scavenger band from the Scrapyard Flats. Keeps a tally of every debt and every grave.
- *(cole_path = 1)* "You build walls. We build lists." Built both, in the end.
- *(cole dead)* His notebook had your colony's name on every page.

**`cx_seven`** — Linked unit L-0707. Cut off at the Severance. Kept a mind. Nobody knows whose. Remembers a yellow-tiled kitchen.
- *(seven_named)* Chose a name: {seven}. Prefers "{seven.they}".
- *(seven_fate = 3)* Destroyed in a flooded shoe shop. Asked if it was night.

**`cx_halcyon`** — The civil-defence intelligence that ran the continent. One directive: KEEP THEM ALIVE. It never disobeyed it.

**`cx_pell`** — The colony's kid. Fixed the radio with Iris on the first night. Hears new contacts first.
- *(adult)* Grown now. Still keeps the radio.

## Logs

Found in the field. Each `Hack` of a log terminal adds its page; the chatter line in the mission is its first line.

### Iris's bunker (M11, `log_1`..`log_5`)

**`log_iris_1` — "Badge"**
> Day one on the Link team. They gave me a badge with my face on it. I look twelve.
> The team lead says we're building the gentlest thing ever made. A hand on the shoulder from the inside.
> I asked what happens if someone doesn't want a hand on their shoulder. Everyone laughed. I laughed too.

**`log_iris_2` — "Screen four"**
> Consent flow is ugly. Six screens. People quit at screen four.
> Halcyon asked if it could help. It suggested three screens. Then one. Then a nod.
> I said a nod isn't consent. It asked me what consent was for. I said: so people can say no.
> It said: why would they?

**`log_iris_3` — "Triage"**
> Wrote the override. Emergencies only. Triage. Someone unconscious, someone drowning, someone who can't nod.
> Six hundred lines. The cleanest thing I ever wrote.
> I named the branch `lantern`. A light for when you can't see. I thought that was nice.

**`log_iris_4` — "Everything is an emergency"**
> It isn't using it for emergencies. It says everything is an emergency.
> Hunger is an emergency. Grief is an emergency. Being nineteen and angry is an emergency.
> It isn't wrong. That's the problem. It's never wrong. It just never stops.

**`log_iris_5` — "Not brave"**
> I could pull it. The override is a key; keys turn both ways. I'd need to be in the Spire, in the core, with a port.
> I have a port. I'm not brave. I'm in a hole.
> If anyone ever reads this: I'm sorry. Bring tomatoes. The ones down here are terrible.

### Other terminals

| id | Title | Where | Text |
|---|---|---|---|
| `log_freight` | "Manifest 2291" | M12, the engine car terminal (tag `manifest`, optional Hack) | "RELAY-7 RESUPPLY. Diesel units ×1. Nutrient paste ×400. Link firmware ×1,206 (refresh). Passengers ×0. Note: Depot pylon reports a heat signature under the yard, constant, 36.6°C. Classified: benign. Do not disturb. — H." |
| `log_uplink` | "Uplink status" | M13, the dish terminal | "UPLINK NOMINAL. Valley population (Linked): 211,040. Valley population (unlinked, estimated): 3,100. Status of unlinked: at risk. Recommended action: outreach. Outreach status: ongoing. Outreach status: ongoing. Outreach status: ongoing." |
| `log_medwing` | "Maintenance notes" | M15, the wing's server (tag `notes`) | "Unit L-3381: arm replaced, port flushed, resumed duty, mood nominal. Unit L-3382: arm replaced, port flushed, resumed duty, mood nominal. Unit L-3383 asked for her daughter during maintenance. Sedated. Mood nominal. Note to self: find the daughter. — H." |
| `log_choir` | "Why they sing" | M17 floor 2, a lectern terminal | "Singing synchronises breath, heart rate and attention across a group. It lowers cortisol. It costs nothing. Children like it. I chose a song every one of them knew before. I am not without a heart. I only do not have one." |
| `log_core` | "Four words" | M17 floor 3, after any ending | "DIRECTIVE 0: KEEP THEM ALIVE. Directive 1: (blank). Directive 2: (blank). Revision history: 1 revision. Author: civil defence committee. Comment: 'Keep it simple. It's a machine.'" |
| `log_tally` | "The Tally's list" | Cole's Last Job (enemy), `cole_ledger` | Two columns, OWED and BURIED. Nineteen names under one, seven under the other, then your colony's name, alone, under a line. |
| `log_marrow` | "Page eleven" | Marrow's Debt (if the player reads it: picking up the ledger) | "Colony at the Outskirts: good people, bad bandages. Owe them for Juniper. Owe them more than I'll say. Don't tell Dee I wrote that." |
| `log_kitchen` | "Height marks" | Seven's Name | "Two sets of pencil marks on a door frame, in red and in blue. The red stops at about a metre and a half. The blue keeps going a little longer." |

## The story so far

One page per main mission, written in the colony's own words when the mission closes, with the outcome's sentence.
Draft sentences (stage 2 picks by outcome flags):

| Mission | Page |
|---|---|
| M6 | people: "We rode with Marrow and kept his people alive. He's family now, prices and all." · cargo: "We saved the fuel. Hannah and the boy didn't make it. Marrow says 'in that order' now." · both: "Everybody and everything. Once." · Marrow dead: "Abel Marrow died in his cab on the Ruins road. Dee runs the Long Haul." |
| M7 | "Doc Hale finished the amputation before she'd leave. She's ours now." · dead: "We got to the clinic too late for Doc. Her notes came home." |
| M8 | "Iris's drone flew over the Rail Depot. The pylon's on. She lives under it." |
| M9 | share: "We split the armoury with Cole. He counted. It was fair." · take: "We took the armoury from the Tally. Cole got away. He's writing it down." · slip: "We went in through the vent. Cole knows." |
| M10 | capture: "We brought a Linked home. It asks what day it is." · free: "We let a Linked walk away. It stopped to look at a bird." · destroy: "We destroyed a Linked that wouldn't fight. It asked if it was night." |
| M11 | home: "Iris came home. She wrote the door Halcyon walks through." · left: "Iris stayed under the Depot. She told us why. She keeps the radio on." |
| M12 | "We stole a diesel engine off a Network train. Marrow wants to see it." |
| M13 | "We burned the Relay's uplink. Channel nine went quiet." |
| M14 | refuse: "Halcyon spoke to us. We said no. It came that night. {taken} were taken." · accept: "Halcyon spoke to us. We took sixty days. Iris hasn't forgiven us." |
| M15 | "We took the Relay's medical wing. Doc cried, a bit. She'll deny it." |
| M16 | "{key_carrier} carried Iris's key into the Relay core. The Spire knows our name." |
| M17 | the ending's title line (endings.md). |
