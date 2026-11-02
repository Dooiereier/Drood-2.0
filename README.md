# Drood 2.0

A Juno: New Origins mod that adds **Drood 2.0**, a drood you can stick parts onto
anywhere (head, torso, every limb), with a built-in parachute (ram-air or round), skydiving,
persistent tethers and sound.

**The stock drood and tourist are untouched.** Every feature applies only to Drood 2.0
(`Drood2Types.IsDroodPartType`).

**Requires the Juno Harmony mod** (Harmony 2.4.2.0); Drood 2.0 doesn't ship Harmony itself.
Without it the patches can't be applied (an error is logged at startup), and the part can't be
added in the designer: its limb attach points need colliders that only a patch creates.

**Career:** Drood 2.0 unlocks once both the stock Drood and the stock Parachute are
unlocked (`Drood2TechTreePatch`). The stock tech tree has no node for it. Sandbox always
shows it.

## Project setup

This folder is already a Unity **2022.3.62f3** project: the same editor version Juno
1.4.105 was built with. `Packages/manifest.json` is copied from DesignerFactory.

On Apple Silicon, Unity's Package Manager is Intel-only and needs Rosetta 2. Without
it, the editor crashes on open (`-[NSConcreteTask terminationStatus]: task not launched`):

    softwareupdate --install-rosetta --agree-to-license

### Harmony (compile reference outside the project)

Drood 2.0 uses Harmony at runtime from the separately installed **Juno Harmony** mod, which
players must install. Harmony is deliberately not in this project or repository. To compile
(and for the editor to load the mod's assembly), Unity needs `0Harmony.dll` **2.4.2.0** (the
version Juno Harmony ships) as a local package in a folder next to the project:

    <parent folder>/Drood 2.0/                                          this project
    <parent folder>/Drood 2.0 References/com.pardeike.harmony/package.json
    <parent folder>/Drood 2.0 References/com.pardeike.harmony/0Harmony.dll

`Packages/manifest.json` refers to it as
`"com.pardeike.harmony": "file:../../Drood 2.0 References/com.pardeike.harmony"`. ModTools only
packages DLLs under `Assets/` (`ModBuilderWindowBase.GetPrecompiledAssemblies`), so this one is
never shipped. Get the DLL from the Harmony 2.4.2 release (github.com/pardeike/Harmony, `net48`
build); `package.json` needs only a name, version and `"unity": "2022.3"`.

1. Unity Hub → Add → Add project from disk → this folder.
2. Assets → Import Package → Custom Package… → `SimpleRockets2/ModTools/SimpleRockets2_ModTools.unitypackage`
   from the Juno install. Let the ModTools project setup run.
3. If the ModTools setup creates its own `Assets/Scripts/Mod.cs`, keep the one from this repo.
4. Build the mod with the ModTools build menu and enable it in Juno.

### Building

- **File name:** in the save dialog, type `Drood20`. The mod's name is "Drood 2.0", but the
  dialog suggests `Drood 2.0.sr2-mod` and the dot in "2.0" makes it add a second
  `.sr2-mod`. The file name doesn't matter to Juno; the mod still shows as "Drood 2.0".
- **Assembly name:** ModTools renames the root asmdef to the mod name, so
  `Assets/Drood 2.0.asmdef` must stay named `Drood 2.0` (and `ModData` lists
  `Drood 2.0.dll`). If a build fails with `FileNotFoundException … Temp/ModAssemblies/…dll`,
  the two don't match, or Unity hasn't recompiled yet: switch to Unity, let it compile, build
  again.
- **Landing clips aren't in this repository:** `Assets/Animations` holds Mixamo clips, which may not be published. To build the mod, supply your own clips under the same names.
- **Other Assets:** `Assets/Animations/HardLanding.fbx`, `FallFlat.fbx`, `Collapse.fbx`,
  `StandingUp.fbx` and `Victory.fbx` (the landing clips) must stay listed under Other Assets in `ModData`, or they aren't packed
  and the landing animation is skipped.
- **Harmony:** requires the "Juno Harmony" mod at runtime; the compile reference lives outside
  the project (see "Harmony" above) and isn't shipped.

### Debug logging

Diagnostics are off by default. Switch on **Settings → Mods → Drood 2.0 → Debug Logging** to
get them in `Player.log` (`~/Library/Logs/Jundroo/SimpleRockets 2/Player.log` on macOS):
the arch's attitude once a second in free fall, held-part placement, designer bone positions, arming/deploying/repacking,
hard landings, tether removal and teleport syncs.
Warnings and errors are always logged.

**Performance Log** (same settings page) writes one line every 5 seconds: the frame rate, Drood
2.0's own time per frame (average and worst frame, and its share of the frame), the costliest
steps, and the phases flown meanwhile. Switch Debug Logging off while measuring, since its lines
cost time too.

## Features

### Parts on every limb

Drood 2.0's XML has 14 surface attach points: torso, head, and left/right upper arm, forearm,
hand, thigh, shin and foot (`DroodLimbSurfaces.Segments`). In the designer
`CreateLimbCollidersPatch` (prefix on `PartScript.CreateAttachPoints`) gives each one a
collider on its bone, so parts snap onto any limb. Attached parts are fused to the drood.

- **Save format:** the points come after the stock seat point (id 0), so the torso point
  `DroodSurface` is id 1, and so on. Only ever append to `Segments`.
- **Held parts follow the limbs in flight** (`DroodLimbFollower`): everything attached to a
  segment point, plus whatever is fused onto it, moves with its bone every frame, after the
  IK. Each part's grip on its bone is measured in the designer and saved with the craft
  (`DroodHeldGrips`). Held parts are kept out of Juno's mesh merging.
- Safety limit: a group over 25 parts or 100 kg, or one with a command pod, is structure
  (e.g. a platform touching a foot), not a held part.
- `IsModRequiredForCraft` flags crafts that contain a Drood 2.0.

### Parachute (AG1)

Chosen per drood in the designer: a **ram-air** canopy (default), the **swoop** canopy or a **round** canopy.

- **AG1 on** arms the chute (not while the drood is anchored to something larger: AG1 is switched
  back off); it deploys below 10 km above sea level and 320 m/s. **AG1 off**
  repacks it instantly; it can be reused. The jetpack is off while hanging and comes back
  after landing.
- **Ram-air** (18 m², 6.6 m span, 2.7 m chord, 9 cells, in the drood's three paint colours): the
  right hand throws the pilot chute, which pulls the deployment bag out; at line stretch the
  canopy leaves the bag and opens under its slider in about 3 s, with its brakes stowed (it
  flies slow until the first press of a steering key releases them). It flies from its own attitude
  (a lift and drag polar per canopy half with a real maximum lift; about 3.1:1 at 15.6 m/s; pitch-stable, so the speed settles at trim). Turn keys pull the
  steering lines, back pulls both (brakes; the last fifth of the back input pulls on
  down to the flare's depth and then into a stall). With mouse steering the pilot has full
  authority: the brakes all the way and the flare at any speed and height, so a landing flare is
  timed by pulling it, and pulled at speed the canopy climbs. On the keys the canopy helps: the
  brakes reach less far the faster it flies (all the way at trim speed, half at 28 m/s), and held,
  the flare comes in as it slows from 12.5 m/s (sooner low and fast, up to 15.5 m/s, by the feet
  height over the sink and the ground ahead; the swoop canopy's 1.15 times, low and fast from
  16.5 m/s), its depth kept once reached; turning with
  the brakes on lets the other toggle up, so the flare still steers), forward pulls the front risers (a dive to about 29 m/s, settling at about 25 m/s while held); turning on the front risers lets
  the outside riser up. Pulled lines shape the canopy (trailing edge down), turns grow faster
  with speed.
- **Stall (ram-air):** a line pulled fully below 7.5 m/s stalls its half (not in the last 2 m:
  the flare). Stalled, a half loses its lift, partly collapses (soft cells, the span bending
  back into a horseshoe, the tip folding), flutters and buffets; the canopy falls back and
  sinks about 13 m/s (14 deep), shaking its lines. A long deep stall goes lopsided and turns;
  one line deep while slow is a spinning turn. Let up, it refills, dives and surges.
- **Opening malfunction (ram-air):** about one opening in 12, one half doesn't fill: it hangs
  partly collapsed and the canopy turns toward it. Two brake pumps (both toggles past 75% and
  back up) clear it.
- **Swoop canopy** (the Canopy setting "Swoop", meant for the Swoop challenge): the ram-air at 10.7 m² (about 2.5 lb/ft², drawn
  0.77 times its size), with less drag at speed but more while it works hard: about 17 m/s
  forward and 7 m/s down in steady flight (2.5:1), about 41 m/s on the front risers, and long
  level swoops after a dive (around 160 m from 40 m/s). It needs a well-timed flare. Its speeds
  (stall, steering) are the ram-air's times 1.3. Drop zone and swoop
  course bests are kept apart for it.
- **Round canopy** (4 m radius, 16 lines): opens in 2 s after 1 s of line payout, drifts
  slowly forward (drive slots), turns gently with the turn keys.
- **Hanging:** the drood hangs and swings under the lines, holds the risers or toggles, and
  the hands pull them visibly. In first person the mouse looks around freely
  (`FreeLookUnderCanopyPatch`).
- **Landing:** a soft touchdown runs out to a stop (in first person the view dips 3-12 cm and
  nods 1.5-5° for half a second, more the harder it is); a hard one plays the hard-landing clip
  (`Assets/Animations/HardLanding.fbx`) for a mostly vertical one (up to 6 m/s forward), the
  collapse clip (`Assets/Animations/Collapse.fbx`: the legs give way onto the belly, then up again
  with `StandingUp.fbx`) for one from 7 m/s down, or the fall-flat clip (`Assets/Animations/FallFlat.fbx`: down on the belly, then up again with
  `StandingUp.fbx`) for a faster one; movement frozen by
  `FreezeMovementDuringLandingFallPatch`. Each landing adds a line with its speeds and a grade
  to the Flight Log (10 at 0.5 m/s down and 5 m/s forward or gentler, falling evenly to 1, on
  its own scale: 4 m/s down and 14 m/s forward count as much), and a line to
  `UserData/Drood20/Landings.csv` (speeds, grade, where the flare began, a challenge jump's
  distance and score); after a 9 or 10 on level ground the drood cheers once it stands still
  (`Victory.fbx`; a movement key stops it). The canopy floats down and lies on the ground (the
  ram-air as an empty canopy, the round one as a long strip) with the lines slack; walking
  beyond the lines' reach drags it along. Water counts as landing.
- **Without air** (held still, e.g. hanging from a tether) the canopy deflates and reopens
  when the air comes back.

### Skydiving

In free fall (enough dynamic pressure, well above the ground, the chute packed) the drood
flies the arch, belly to the relative wind, with a skydiver's limb corrections. The EVA keys
steer: turn keys turn, forward tracks, back backslides, strafe slides sideways; in a track
the turn keys bank (up to 30°, held against the horizon) and the turn follows, like an
aeroplane, with the arm corrections shown. The jetpack is off in the arch. In first person the drood sees its own body
(arch and canopy), the view turns at most 95° left and right, and the mouse looks around.
In the arch and under the canopy the body is held in a still, squared pose (the head still
moves); under the canopy the legs drift together, the right a little later.

**Mouse steering:** with the game's mouse joystick switched on (its own key), the mouse steers
while skydiving and under the canopy instead of looking around: the cursor's place on the
screen is the stick (middle neutral, full input halfway to the edge, the game's deadzone and
invert settings; left/right on a curve (to the power 1.5): small moves give little, full still all).
Left/right turns; up is a track or the front risers, down a backslide or brakes and flare.
The keys keep working. A thin box around the screen's middle always shows the dead band
(narrowed for a drood: left/right an eighth of the game's setting, up/down half); under the
ram-air an outer box shows full input, labelled front risers and toggles, with a bar
on each side for that line's pull (toggles down, the flare's deeper pull on below in orange,
front risers up; a red mark where a slow line stalls); those fade when nothing changes for 3 s. AG4 "Steering Display" (off by default; the display only shows with it on) steps through
the full display, the dead band only, and off. In first person the view centres forward
when mouse steering takes over.

Flare cue (with AG4 on, in both its steps): under the canopy, from 15 m of the feet above the ground, a moving colour tape
right of the screen's middle shows the height without numbers: a fixed white mark, the colour
scale sliding past it as the drood sinks (square-root scale, so the last metres stream past
fastest): green above 6 m, yellow to 3 m (get ready), orange to 1 m (flare), red below, then a
black ground line with dark grey under it; a square beside the mark shows the colour now.

### Drop Zone Challenge (AG2)

Chosen per drood in the designer, with a ram-air or swoop canopy (no challenges with the round
one: the setting is hidden and off, its choice kept). Challenge: "Drop Zone"; crafts saved with the earlier "Drop
Zone Challenge" switch on load with it chosen. On the ground, AG2 ("Restart
Challenge", or its key) takes the drood's spot as the target (on launch the launch spot) and
puts it 3000 m above the target in the open door of the jump plane, on a level run at 40 m/s
that passes the target 1300 m to the side 30 s later; the
drood sits in the door, legs outside, and W slides it out (the timer starts at the exit); the
plane flies on for 20 s and is removed. Without the plane (not built) it starts there at rest;
at most 200 kg attached. A red and white target (50 m radius, draped over the
ground and structures, shadowed) shows during the jump and for 30 s after the landing. In the
arch the view switches to Drood Chase. A panel at the top of the screen shows the timer, then
the score out of 1000: near (350, falling with the square of the distance from the centre to
none at 50 m), soft (40 per landing grade) and quick (250 within 1:10, none from 3:00, times
the square root of the near and soft shares). The line also goes to the Flight Log; personal
bests per canopy type are kept in `UserData/Drood20/DropChallengeBests.xml`. AG2 twice within
1.5 s in the air restarts the jump; teleporting during it scores nothing.

### Swoop course (AG2)

Chosen per drood in the designer (Challenge: "Swoop Straight" or "Swoop 270 turn"; ram-air
only). Swoop Straight starts straight behind the starting line on the lane's line, facing down
the lane (Difficulty Practice 305 m up, 250 m behind; Expert 200 m, 200 m; Custom typed as
"Straight start height (m)" and "Straight start distance (m)"); the rest of this describes Swoop 270 turn, whose course and scoring both
share (their bests kept apart). On the ground, AG2
("Restart Challenge", or its key) takes the drood's spot as the entry gate and the way it faces as the
lane (on launch the launch spot), and starts it beside the course under its open canopy, 115 m
from the lane's line (plus one second of the canopy's glide, about 15-17 m) at the start distance
back from the starting line (the gate's) and at the start height, flying across the lane toward
the turn point (unmarked, as far back, 48 m from the line plus that second); the panel counts down
(a looped right-turn arrow, the road sign's 270° symbol, with "swooping turn in …", then
"TURN") and from there, about 22-26 m lower than the start (the glide to it),
a 270° turn to the right comes round onto the lane. The
panel also shows the height above the gate's ground, the distance to the gate and the speed. At launch first person with the steering display full on (AG4); a restart keeps the view and AG4 as they are. The gate: two poles 22.5 m apart and 3 m tall (the
height limit), striped orange and white every metre as height marks. With Swoop 270 turn chosen (Straight likewise, its own values), the designer shows "Difficulty": Practice (start 380 m
above the course's ground, 220 m back from the starting line) or Expert (280 m, 100 m), their
values shown greyed out, or Custom: "Swoop start height (m)" (typed, 50 to 3000) and "Swoop start
distance (m)" (0 to 2000), starting at Practice's values; a newly placed Drood 2.0 takes the last
Custom values typed. Settings → Mods → Drood 2.0 → "Swoop High Scores" and
"Drop Zone High Scores" (Reset) clear that challenge's bests after asking (the file as it was is
kept aside as a dated copy). The lane: 22.5 m wide, a line across it every 10 m to 200 m (red every 50 m) and markers on both
edges, draped over the ground. A run counts when the feet pass between the poles below their
tops; its score is how far down the lane the feet first touch the ground, however the landing
goes, or where they first sink 1 m below the gate's ground level, whichever comes first (off an
edge or down a slope the distance ends there; the landing still gives the grade). Missing the gate, passing higher than the poles, touching down before it or leaving the lane score
nothing. The panel and the Flight Log show the distance; the
best is kept with the drop challenge's bests, per variant, difficulty and canopy (as "Swoop 270
turn, Practice" and so on, ", swoop wing" for the swoop canopy; in centimetres). AG2 twice within
1.5 s in the air restarts the run.

### Sound

Generated wind while skydiving and under the canopy (Settings → Mods → Drood 2.0 → Wind
Volume), on top of the game's own wind; a canopy opening sound built from the game's own
parachute clip at line stretch. An audible altimeter in the helmet, after L&B's audibles:
in free fall (falling faster than 13 m/s, no canopy out) an altitude warning at 900 m above
the ground (2 s of beeps, 5 a second) and an alert below 450 m (a continuous siren while
falling), both at a high A (3520 Hz); off with Settings → Mods → Drood 2.0 → Audible
Altimeter. No sound files ship with the mod.

### Persistent tethers

Stock tether cycle, one press of "shoot tether" each: drood → A; then A ↔ B; then the stock
game deletes A ↔ B. With Drood 2.0, the third press keeps A ↔ B and shoots a new drood → C
tether (`KeepOldTethersPatch`). Holding Shift on the second press keeps drood → A as well and
shoots another drood → C, so the drood can be tied to several objects at once (kept tethers on
the drood go when it boards a seat or pod). Every pair you link stays linked, and kept tethers follow the
drood's tether length keys (`FollowTetherLengthPatch`). A kept tether is removed when either
end is destroyed or unloaded. The **Remove Tethers Key** setting (default `O`) removes all
Drood 2.0 tethers. Kept tethers aren't saved (stock tethers aren't either).

### Other

- **Canopy Chase and Drood Chase camera views:** while a Drood 2.0 is in control with its
  canopy out (the game's view keys cycle through them; also listed in the view panel's camera list), the camera follows behind the canopy,
  turning with it (not the drood's swing), around a point up the lines or around the drood;
  drag to look around, it stays turned until the view changes.
- **Attached Parts** (designer): what the drood does with parts on its limbs. Automatic: light
  ones (up to 100 kg and 25 parts on a limb, no command pod) are carried and follow its limbs,
  anything bigger holds the drood; Carried: everything is carried; Anchored: the drood is fixed to
  what it is attached to. Fixed to something, it rests in the calm idle instead of the falling
  animation and doesn't walk.
- **Make Drood 2.0 invisible:** a toggle in the designer's Attached Parts section, below Release on: the drood's body isn't drawn and casts no shadow, in the designer and in flight. It still collides and can be selected; carried parts, the canopy and tethers still show.
- **Let go:** Vizzy's "activate part" on the Drood 2.0, or the Attached Parts setting
  **Release on** (AG 5 to AG 10): when the craft the drood is on switches that activation group
  on (its controlling command pod, e.g. the plane's), the drood breaks off what it is fixed
  to, the way the game breaks off a part (a wing over its break force, a crash): it
  becomes its own craft and drops, keeping the parts it carries (a heavy carried part can make it creep slowly on the ground). A drood that only carries parts has nothing to let go of. Several droods can share a
  group. A seated drood leaves its seat instead, the game's own way (as the inspector's unload
  button), the player keeping control of the craft. A group already on at launch doesn't count. The drood switches itself off 2 s after a Vizzy activation, so it can be activated again. The part has no stage
  or activation group of its own, like the stock drood (the staging editor lists only the
  primary command pod's stages).
- **Course Camera:** a camera part's camera on Drood 2.0 (the part's second camera, after its
  eyes), so it is listed with the camera parts in the view panel's camera list, and mods that
  collect camera parts (PlusOne, HorseEye) find it too. With a challenge's course (the swoop's
  lane, the drop zone's target) it stands beside it, 3.5 m above the course's own height (swoop:
  20 m past the gate, 50 m to its left, above the starting line; drop zone: 50 m east of the
  target, above the target), turns to follow the drood and
  zooms to keep it (and its canopy) about half the view high; without one it looks on from
  behind and above the drood. For mods that list stock camera parts (HorseEye), a real one-part camera
  craft ("Drood 2.0 Course Camera", a stock camera, player control off) is spawned at the same
  spot and steered the same way; it is removed when the course goes and at the flight's end.
- **Fly-by camera:** the game's stationary fly-by view, which the game switches off for
  droods, is available while a Drood 2.0 is in control (the cinematic one stays off; the stock
  drood is unchanged).
- **Crosshairs:** the tether aim in the middle of the screen shows only in first and third
  person for Drood 2.0 (hidden in the chase, course and fly-by views and camera parts).
- **First-person field of view:** Settings → Mods → Drood 2.0 → First-Person Field of View
  (40–150°, default 60°); the stock drood is unchanged.
- **Look up:** in weightlessness (off the ground, out of the atmosphere) translation mode (T)
  pitches the first-person view up 80°; the drood itself doesn't turn with it. On the ground and
  in the atmosphere T leaves the view alone.
- **Update check:** at game start Drood 2.0 compares its version with the one on its
  SimpleRockets.com page and, if that's newer, offers to open the page (nothing is downloaded
  or installed). Off with Settings → Mods → Drood 2.0 → Check for Updates. Until the mod is
  published the page address is empty (`UpdateCheck.PageUrl`) and no check is made; raise the
  version in the Mod Builder before each upload, since the page shows the uploaded version.
- **Career:** see above (`Drood2TechTreePatch`).

## Credits

- The jump plane (a Cessna Caravan) is built from the SimpleRockets.com craft by
  **ExcaliburAerospace**.

## Code layout

- `Core/`: mod start-up (`Mod`), the part (`Drood2Part`: its icon and load check; the part itself is `Assets/Parts/Drood2.xml`; `Drood2Types`), settings,
  keys, logging, the update check (`UpdateCheck`), reflection of private game members (`DroodReflection`).
- `Modifier/`: the `Drood2` part modifier (`Drood2Data`, `Drood2Script`) and the flight
  behaviours with their order per frame (`Drood2FlightBehaviours`).
- `Parachute/`: `DroodParachute` (partial files: physics, ram-air shape and visuals, attitude
  model, steering, lines, risers, the landed canopy).
- `Pose/`: free fall and its pose, hanging pose, pilot chute throw, landing clip, run-out,
  hand grip, calm animator, the IK owner (`DroodIKOwner`, keeps the game's IK values).
- `DropZone/`: the Drop Zone Challenge (`DroodDropZone`), its target (`DroodDropZoneRings`),
  panel (`DroodDropChallengeHud`) and personal bests (`DroodDropChallengeBests`).
- `Swoop/`: the swoop course (`DroodSwoopCourse`) and its marks (`DroodSwoopCourseMarks`).
- `Camera/`, `Audio/`, `Limbs/`, `Tethers/`, `Patches/`.

Tuning constants are at the top of each file, with their meaning.

## Test checklist

- [ ] Log shows `[Drood 2.0] Part 'Drood 2.0' loaded from the mod's part XML.` and `Harmony patches: 7 applied, 0
      failed.`
- [ ] Designer: Drood 2.0 is in the Command category. Parts snap onto every limb, head, hands
      and feet; symmetry works; the drood can still be seated.
- [ ] The stock Drood has no extra attach points, and its tether works as before.
- [ ] Save, reload and launch: attachments stay on and follow the limbs.
- [ ] Jump from altitude: the arch, steering, then AG1: throw, bag, opening, steering, flare,
      landing (soft run-out and hard landing).
- [ ] Round canopy: deploy, steer, land.
- [ ] Quicksave and quickload in the arch and under the canopy.
- [ ] First person in the arch and under the canopy: own body, look limits, sounds.
- [ ] Tethers: link A ↔ B, then D ↔ E; destroy A; remove all with `O`.
- [ ] An older craft with Drood 2.0 still loads; without the mod the required-mods dialog
      shows.
