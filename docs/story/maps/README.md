# Map drafts (Acts 2–4)

Handmade maps and fixed rooms in the committed `HandmadeMapDef` format (story-campaign `docs/story/FORMAT.md`). Drafts:
they live here, not in `data/story/maps`, until stage 2. Every file was checked with a script that mirrors
`HandmadeMaps.Validate` (equal row widths, known characters, spawn and exit on full maps, fixtures at known markers,
known enemies and items, every passable tile reachable from the spawn; rooms: every tile reachable from a doorway).

| File | Kind | Size | Mission | Notes |
|---|---|---|---|---|
| `m7_drowned_pharmacy` | map | 30×26 | M7 | south entrance, flooded hall, food court west, clinic NE behind `clinic_door` |
| `m11_depot_sheds` | map | 30×30 | M11 | spawn west; hatch `H` under the pylon; bunker below with five log terminals, Iris and the cot |
| `m12_east_yard` | map | 34×24 | M12 | two platforms, cargo cars (solid), the engine car with `door_a` → `door_b` → `engine`; turret inside |
| `m16_relay_core` | map | 32×28 | M16 | bench room west, core room north-centre with turrets at its door, vents north and south |
| `m17_gate` | map | 34×26 | M17 floor 1 | gate wall with the `gate` door and turrets; `flank` for the Tally; `convoy` for the Long Haul |
| `m17_choir` | map | 34×30 | M17 floor 2 | 18 choir tiles in rows (neutral `linked`), two ushers on a loop, `taken` tiles in the rows |
| `m17_core` | map | 28×28 | M17 floor 3 | ring room round the core column; `core` is the reach marker for the key-carrier |
| `perimeter_linked` | map | 28×18 | M14 | treeline on three sides (`tree_w`, `tree_n`, `tree_e`), the yard round the Mast, 4 tower posts |
| `m8_atrium` | room | 12×11 | M8 | fountain, drone circuit corners `c1`..`c4`, camera, the drone core |
| `m9_armoury` | room | 16×12 | M9 | Cole at the door, the Tally line, two halls, the vault locker; `vent` on the east wall |
| `m10_store` | room | 10×7 | M10 | a flooded shoe shop; Seven in the middle |
| `m13_uplink` | room | 18×11 | M13 | three relay nodes beside pylons, the dish, the uplink log |
| `m15_medwing` | room | 16×9 | M15 | four tables, the orderly warden, three surgical caches, the T4 bench |
| `side_yellow_kitchen` | room | 10×7 | Seven's Name | the kitchen, a still Husk at the table, the window |
| `side_tally_camp` | room | 14×10 | Marrow's Debt, Cole's Last Job (ally) | fire, Tally, Cole, family, the strongbox |
| `side_hideout` | room | 8×6 | Doc's Rounds | the patient, Husks at the door |
| `side_choir_nursery` | room | 14×8 | Children of the Choir | three Linked children, two minders, a camera |
| `side_dead_drop` | room | 7×5 | Dead Drop | the locker with the crate |

## For stage 2

- **Doors and reachability.** The committed validator counts every device except cameras and turrets as blocking, so
  a room behind a `Door` (Iris's bunker, the engine car, the Spire gate) fails "reachable from the spawn". Doors are
  meant to open; the check here counts `Door` devices as passable. Stage 2 should make the Core validator do the same.
- **New items** these maps hold: `drone_core`, `diesel_engine`, `surgical_kit`, `marrow_ledger`, `trade_crate` (see
  survivors.md → New data ids). The validator reports them as new, not unknown.
- **Markers with no fixture** are objective and trigger markers (`hatch`, `cot`, `yard_gate`, `cart`, `vent_n`, `lift`,
  `stairs`, `mid`, `taken`, …), named in the mission specs.
- Act 1's perimeter maps use the `outskirts` zone; `perimeter_linked` does the same.
