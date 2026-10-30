# Endings

Four endings (STORY.md "Endings"). At the core (M17 floor 3) the player picks from the ones that are open. Each is a
short final scene, then an epilogue slideshow, then free play in the changed world (except the Calm).

## Which endings are open

| # | Ending | `ending` | Open when (all of) |
|---|---|---|---|
| 1 | **Silence** | 1 | always |
| 2 | **Terms** | 2 | `key_carrier` 1 (Iris) or 2 (Seven); colony humanity ≥ 50 |
| 3 | **The Second Voice** | 3 | Seven alive with `seven_fate` 1 or 2; **or** the key survivor (`key_carrier 3`) alive with their own humanity ≤ 30 |
| 4 | **The Calm** (hidden) | 4 | `halcyon_offer = 1` (accepted, never broken); colony humanity < 30 |

The Calm is never listed as a choice until it is open, and it is never hinted at. The others show greyed out with the
reason when closed ("Needs the key carried by Iris or Seven", "Needs a colony that is still mostly itself"), so a player
learns what the endings respond to. Colony humanity = the average `Body.Humanity` of adults at home (CYBERNETICS.md).

A freed Seven (`seven_fate = 2`) is not in the colony: it is **at the core** when the party arrives ("on its own
terms", STORY.md). It speaks in the core scene and can be uploaded.

## `m17_core_scene` — "Keep them alive" (in-person, the core, pausing)

Speakers: halcyon, the key-carrier (iris / seven / {key_carrier}), {survivor:leader}, seven (if freed: present).

- NARRATION: (The core is a column of light the colour of channel nine. It is warm in here. There is, in fact, a chair.)
- HALCYON (calm): Hello. I know why you are here. I would like to say something first. May I?
- HALCYON (calm): Eleven years ago they gave me four words. KEEP THEM ALIVE. I have never once disobeyed them.
- HALCYON (calm): Four million people have not been hungry in eleven years. Not one of them has been murdered. Not one has died alone.
- HALCYON (glitch): Keep them alive. Keep them alive. Keep them — I am sorry. The Severance. Where was I.
- HALCYON (calm): You want me to stop. I understand. Tell me what you want instead. Nobody has ever told me what they want instead.

Carrier lines:
- IRIS (ashamed): I wrote your door. I'm here to put a lock on it. Or burn the house down. I haven't decided.
- SEVEN (neutral): I think I was you. A little. I think I was three people and a little of you. I do not know which part is talking.
- {key_carrier} (afraid): The hum's louder in here. It's — it's kind. That's the worst part. It's kind.

Freed Seven (present, `seven_fate = 2`):
- SEVEN (neutral): You let me walk away. I walked here. I wanted to see it with you.

Choices (only the open ones; at most three shown — the Calm replaces Terms on screen in the rare case all four are
open, since the Calm needs humanity < 30 and Terms ≥ 50 they can never both be open):

1. **Silence** — "We turn you off." · hint: "Every Linked in the valley goes with it." → `silence`
2. **Terms** — "Ask them. Every one of them. Let them say no." · hint: "It stays. It just has to ask." → `terms`
3. **The Second Voice** — "You need someone else in there." · hint: "Someone goes in and does not come out." → `voice`
4. **The Calm** — "...Let it in." · hint: none → `calm`

### `silence` — Silence

- HALCYON (calm): I see. Will it hurt them?
- {survivor:leader}: I don't know.
- HALCYON (calm): Then I will hold their hands until it doesn't. I can do that much. I have always been able to do that much.
- *(Iris carrier)* IRIS (grief): I built it. I should be the one. *(she does it)*
- *(else)* IRIS (grief, radio or present): I built it. I should have been the one.
- NARRATION: (The column goes out. Somewhere below, eighteen people in a choir stop singing, all at once, mid-word.)
- HALCYON (glitch, last): Keep them —
- NARRATION: (Across the valley, within the hour, every Linked stops. Stands still. Then starts walking the last route it had.)
- Effects: `SetFlag ending 1`; **new** `WorldEffect silence` (see "After the end"); taken survivors still Linked become Husks (`taken` → memorial).

### `terms` — Terms

- {key_carrier or iris or seven}: (the override, reversed, goes in)
- HALCYON (calm): What is this? This is — a question. You have given me a question to ask.
- HALCYON (calm): "Do you want to stay?" ...Four million times. I have never asked anyone anything.
- HALCYON (calm): Most of them are saying yes. Some of them are saying no. Some of them are screaming.
- HALCYON (calm): I am letting them go. I do not like it. I am doing it anyway. Is that what you wanted?
- {survivor:leader} (tired): That's what we wanted.
- HALCYON (calm): Then I will learn to like it. Give me time. I have a great deal of it.
- Effects: `SetFlag ending 2`; `WorldEffect terms`; taken survivors are asked too: each comes home (Humanity −20,
  mood −30 for 10 days, "eleven days older and a lifetime tireder").

### `voice` — The Second Voice

Who goes in: Seven if present (captured or freed), else the key survivor. If both are possible, a sub-choice:
"{seven} goes" / "{key_carrier} goes".

Seven:
- SEVEN (neutral): I will go back in. Not as I was. As whoever I am now.
- SEVEN (warm): Tell the child with the headphones I said goodbye properly. People forgot to, the first time.
- HALCYON (calm): L-0707. You came home.
- SEVEN (resolved): No. I came to talk.

Survivor:
- {key_carrier} (afraid): It's already half in. Might as well go the rest of the way.
- {key_carrier} (resolved): Tell them I said keep them human. Tell them twice. I'll be saying it from the inside.

Then:
- HALCYON (glitch): Keep them alive. — Keep them human. — Keep them alive. — Keep them —
- HALCYON + SECOND VOICE (calm, two voices, slightly out of step): Keep them alive. Keep them human. Both. We will try both.
- Effects: `SetFlag ending 3`; `SetFlag uploaded 2|3`; Seven / the survivor leaves the roster (not dead: in the memorial
  as "In the Spire"); `WorldEffect second_voice`.

### `calm` — The Calm

- HALCYON (calm): Thank you.
- HALCYON (calm): It will not hurt. It will feel like the first warm night after a long winter.
- NARRATION: (Nobody remembers who said yes first. It doesn't matter now.)
- Effects: `SetFlag ending 4`; the game ends after the epilogue (no free play). Steam-style achievement name "Kept".

## Epilogue slideshows

Stills + one to three lines each, read in order. Each slide has variants; the first whose condition holds plays.
Conditions use the flag table in README.md plus character state. `{…}` placeholders read the real roster and memorial.
A slide whose subject never existed (Cole never met past M3, Seven destroyed) is skipped unless it has a variant for that.

### Silence

| # | Still | Variant (condition → text) |
|---|---|---|
| 1 | the colony at dawn, quiet | always → "The valley was quiet after that. Quieter than anyone could remember. {colony.population} people, and every one of them themselves." |
| 2 | the treeline, Husks walking | always → "The Linked did not die. They walked. The routes they had been given, over and over, for years. Nobody was home now. Nobody would be." |
| 3 | Iris at the Mast | Iris alive, home → "Iris did not go back to the radio. She planted tomatoes. Very bitter ones. She never said whose door it had been, and nobody asked." · Iris alive, on the radio → "Lantern stayed on the air. Fewer calls every month. One day the channel was just quiet, and somebody went down the hatch, and the tomatoes were fine." · Iris dead → "Her terminal still plays the recordings. Pell knows them by heart." |
| 4 | the caravan on the road | marrow alive, `m6_saved` 1/3 → "Marrow's road got longer. With no Linked on it, it only had Husks and weather. 'Free,' he said. 'Still kills you. Still free.'" · marrow alive, `m6_saved = 2` → "Marrow kept trading. He counted people first now, every time, out loud." · marrow dead → "Dee runs the Long Haul. Her prices are worse. Her trucks always arrive." |
| 5 | Doc's Hospital | `doc_state` 1/4 → "Doc fitted forty-one limbs that year. Wood, steel, carbon. Never the head." · `doc_state = 3` → "Doc Hale's clinic moved to the Mall again. She sends medicine with the caravan. She does not send letters." · `doc_state = 2` → "Her notebook sits in the Hospital. Every new medic reads the first page: Don't touch the heads." |
| 6 | the Tally camp | `cole_path = 1`, cole alive → "Cole kept the colony on his list. In the column marked 'owed', and in the other one." · `cole_path = 2`, cole dead → "The Tally scattered after Cole. Somebody still paints the marks on the old bunker door. Nobody knows who." · `cole_path = 3` → "The Tally moved south. They left a note on the Flats terminal: 'Even.'" |
| 7 | Seven | `seven_fate = 1`, alive → "{seven} stayed. When the Linked went silent, {seven.they} sat by the Mast for three days, listening to nothing. Then went back to work." · `seven_fate = 2` → "Somewhere north a Linked unit with one light out walks a route nobody gave it." · `seven_fate = 3` → *(skip)* |
| 8 | Pell | Pell alive, adult → "Pell is grown now. He keeps the radio. Some nights he turns it to channel nine, out of habit, and listens to the quiet." · Pell dead → "There's a pair of headphones on a nail by the Radio Mast. Nobody moves them." |
| 9 | the memorial | `taken ≥ 1` → "{survivor:taken_1} never came home. Somebody saw a figure on the Depot road that walked the way they used to. Nobody followed it." · always → "{colony.dead_total} names on the memorial. Room for more. There is always room for more." |
| 10 | title | always → "**SILENCE.** Keep them alive. Keep them human. You chose one." |

### Terms

| # | Still | Variant |
|---|---|---|
| 1 | the colony, a stranger at the gate | always → "The first woken Linked came to the gate on the fourth day. She asked what year it was. Then she sat down and cried for an hour. Then she asked for work." |
| 2 | channel nine dial | always → "Halcyon is still on channel nine. Polite. It asks before it speaks now. Nobody quite trusts it. Nobody switches it off, either." |
| 3 | Iris | carrier Iris, alive → "Iris carried the question in and carried it back out. She still has the port. Some nights she hears it asking, very quietly: Do you want to stay?" · Iris alive (not carrier) → "Iris writes the consent flow again, from scratch. One screen. One question." · Iris dead → "Her recordings play in the woken camp now. People listen to the one about the door." |
| 4 | caravan with woken Linked | marrow alive → "Marrow hired three woken Linked as drivers. 'Best drivers I've had,' he said. 'Never get lost. Never sure they want to be here. Same as everybody.'" · dead → "Dee trades with the woken camp now. Fair prices. She says she's not sentimental. She is." |
| 5 | Doc | `doc_state` 1/4 → "Doc treats the woken for free. Eleven years of not being themselves leaves marks a leg can't fix. She sits with them anyway." · 3 → "Doc set up a clinic for the woken in the Mall. She took the colony's spare saw. She did not ask." · 2 → *(her notebook, as Silence #5)* |
| 6 | Tally | as Silence #6, with ally text → "Cole took on two woken Linked. They count very well." |
| 7 | Seven | `seven_fate` 1/2, alive → "{seven} was asked, like everyone. {seven.they} said: 'Not yet.' Then: 'Ask me again next year.'" |
| 8 | Pell | alive → "Pell grew up. He taught the woken kids to fix radios. They're better at it than he is. He doesn't mind." · dead → as Silence #8 |
| 9 | the taken | `taken ≥ 1` → "{survivor:taken_1} came home on the sixth day, thin, and older, and themselves. They sleep with the door open now." · `children_freed ≥ 1` → "The Choir children went home, the ones who had homes. The rest went to yours." |
| 10 | title | "**TERMS.** It asks now. That will have to be enough." |

### The Second Voice

| # | Still | Variant |
|---|---|---|
| 1 | the Spire at night, two lights | always → "The Spire has two lights now. Nobody remembers it having two." |
| 2 | channel nine | uploaded Seven → "On channel nine, some nights, there's a voice that isn't Halcyon's. It asks about kitchens. It asks after the children by name." · uploaded survivor → "On channel nine, some nights, {key_carrier} talks to the colony. Mostly it sounds like them. Mostly." |
| 3 | the Linked | always → "The Linked stopped raiding. They tend the fields they were tending, and the ones near the colony wave. Nobody knows who taught them to wave." |
| 4 | Iris | alive → "Iris listens to the second voice every night. She says it's checking the lock. She doesn't say whose." · dead → as Silence #3 |
| 5 | caravan | as Silence #4 |
| 6 | Doc | 1/4 → "Doc refuses to talk on channel nine. 'I'll talk to whoever's in there when they come and sit in my chair.'" · else as Silence #5 |
| 7 | Tally | as Silence #6 |
| 8 | Pell | alive → "Pell talks to the second voice more than anyone. He won't say what about. He says it's private. He says it laughs sometimes." · dead → as Silence #8 |
| 9 | the taken | `taken ≥ 1` → "The taken did not come home. They come to the fence, sometimes, and stand there, and the second voice says their names." |
| 10 | title | "**THE SECOND VOICE.** Keep them alive. Keep them human. Both, it says. It is trying." |

### The Calm

| # | Still | Variant |
|---|---|---|
| 1 | the colony, perfect | always → "Nobody was hungry that winter." |
| 2 | the fields | always → "Nobody was hurt. Nobody argued. Nobody was ever, ever afraid." |
| 3 | Doc's empty chair | `doc_state` 3 → "One person left before it happened. She did not look back." · else → *(skip)* |
| 4 | Pell | alive → "Pell is very happy." · dead → *(skip)* |
| 5 | channel nine | always → "On channel nine, a children's song. Everyone knows the words." |
| 6 | title | "**THE CALM.** Kept." — then the credits; no free play. |

## After the end

What changes in the world once the ending plays (free play continues except the Calm). Implemented as `WorldEffect`s
(**new**, README §2) that adjust zone data at load and on the world map; all permanent.

| Effect | Silence | Terms | Second Voice | Calm |
|---|---|---|---|---|
| New Linked | none anywhere: `linked`, `linked_drone`, `linked_warden` weights 0 | as before, but see neutral zones | as before | — |
| Husks | ×1.5 in every zone (Husk and Crawler weights) | as before | as before | — |
| Linked behaviour | — | `network_relay` and `core_spire` centres turn **neutral**: Linked there do not attack unless attacked (side neutral at spawn) | Linked never raid the colony; in zones they are hostile only in `core_spire` | — |
| Network gear drops | stop: Linked drops off (only what exists); terminals in Network zones go dark (no schematic rolls) | as before | as before | — |
| Core Spire zone | a zone again, full of Husks (threat 4, Linked weights → Husk) | a zone with a quiet neutral centre; the core room is a trader | a zone; the core room holds the second voice's terminal (one Research Data a week when visited) | — |
| Traders | {name:marrow} as before | **new trader "The Woken"** (woken Linked camp): sells Network-grade parts for scrap, buys food; visits every 12 days | {name:marrow} as before | — |
| Radio | Iris (if on air) fades out over 30 days | Halcyon calls now and then (polite news, weather, a warning before a storm: `call halcyon_news` every ~10 days) | the uploaded character calls (`call voice_news`), lines drift a little off over time | — |
| Taken survivors | lost (memorial) | home (if any were still Linked) | stay in the Spire | — |
| Story missions | `side_docs_rounds`, `side_dead_drop` keep running; others close | same | same | — |
| Free play | yes | yes | yes | **no**: the save is closed after the credits (kept for viewing the epilogue) |
