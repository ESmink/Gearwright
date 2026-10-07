# Gearwright sound decisions

The maintainer reviews sound in-game. Implement the best current choice, build,
and install it so feedback comes from normal play. Standalone audition clips
are optional diagnostics.

| Kind | Maximum range | Design |
| --- | ---: | --- |
| Constant noise | 2 blocks | Audible at normal head height beside the machine, while still fairly quiet. Avoid persistent tones, sharp textures, and annoying repetition. At least 180 seconds of varied source content before repeating. Examples: Automatic Bellow, water streaming, airflow. |
| Irrigation water | 5 blocks | Quiet but audible below overhead sprinklers and irrigators and across nearby crops. Full near-field gain through 2.5 blocks, then a gentle fade. At least 180 seconds of varied source content. |
| Informational | 5 blocks | Restrained volume, distinct textures, softened transients, and enough duration to represent the whole visible mechanism. Follow actual motion and pause when it stalls. Examples: pressure creaks and Sender or Receiver work. |
| Warning | 20 blocks | Normal volume, with a somewhat sharper attack. A future pipe burst belongs here; this rule does not add pressure damage. |

Use original synthesized audio or inspected installed vanilla samples. Do not
use the existing Pixabay recordings or sample them for new assets. Preserve
their public asset codes, files, and attribution until removal is authorized.

Keep range and playback defaults in `code/Audio/MachineSoundPolicy.cs` aligned
with this table. Long loops need actual varied content; changing pitch or volume
on a short recording is not enough. Continuous playback must fade near the
range boundary and stop when the player moves outside it. Release audio on
unload and removal. Play informational cues only for authoritative work that
actually proceeds, and rate-limit pressure creaks.

## Accepted feedback

- 2026-10-03: Standalone clips are difficult to judge. Implement the best guess
  and let the maintainer listen in-game.
- 2026-10-03: Adopt the 2/5/20-block categories and the three-minute minimum
  for continuous source content. Keep constant noise quiet and avoid monotone
  or sharp sounds.
- 2026-10-03: Do not use the existing third-party recordings in new audio work.
- 2026-10-03: The first continuous pass was inaudible. Raise close-range
  audibility and remove stacked attenuation while retaining the two-block cap.
- 2026-10-03: Informational sounds were slightly loud, much too short, and
  did not represent the models. Lower their gain and synthesize full mechanical
  phases from the approved models and animations rather than generic clicks.
- 2026-10-03: The revised timing was better, but air still sounded like static
  and was slightly loud; other machines mostly sounded like constant rubbing.
  Lower airflow and replace sustained broadband mechanical textures with
  damped material contacts, stick-slip gate movement, roller compression,
  quiet gaps, and several variations of each work sequence.
- 2026-10-03: The contact-based pneumatic/hydraulic revision is passable for
  now. Keep it as the provisional baseline and add the Small Flywheel,
  Reciprocating Pump and Overrunning Transmission. Extending the generator
  retains the accepted synthesis seeds and playback settings.
- 2026-10-07: Improve the current generated effects substantially and give
  them more detail. Refine the original synthesis throughout the catalogue:
  distinct material bodies, quiet surface motion, contact rebounds, frame
  settling, changing air pressure and layered liquid motion. Keep existing
  action timing, playback gains and range limits. This is a new implementation
  for in-game evaluation; it has not yet received listening approval.
- 2026-10-07: Irrigation is inaudible and its range is too small; ordinary
  pipe flow sounds like creaking at low pressure. Extend only sprinkler and
  irrigator water to five blocks, strengthen their water body, and remove
  pitched cavity sweeps from ordinary pipe flow. Creaks require two continuous
  seconds strictly above 100 kPa and stop when pressure falls to 100 kPa or
  below. Exactly 100 kPa and brief pressure spikes produce no creak.
- 2026-10-07: A loaded Overrunning Transmission can falsely report freewheeling
  after shaft speeds synchronize or during loaded tooth recovery. The tooltip,
  pawl pose and sound must use the server solver's engagement state. Independent
  speed comparisons must not release a pawl that is still carrying load.
- 2026-10-07, later listening pass: Creaks still start too early; transmission
  and irrigation have artificial tones, harsh repeated clicks and too much
  constant noise. Supersede the 100 kPa/two-second setting with five seconds
  strictly above 500 kPa, full intensity at 2000 kPa, lower gain and a 20–90
  second pressure-dependent cooldown. Replace irrigation bubble synthesis with
  filtered, recomposed installed watering/pouring detail. Replace the ratchet's
  resonant brass and flex layers with short dry contacts and quieter intermittent
  bearings. Keep the server engagement source and the existing ranges.
- 2026-10-08: The new mix is much better. Make irrigator pouring quieter and
  less frequent, and remove the rapid start/stop effect; the sprinkler does
  not have this problem. The irrigator recording itself fell below 20% of
  its overall RMS 3.84 times per second, spending 32.8% of its duration there.
  Replace its separated short pours with overlapping 1.8–3.4-second watering
  excerpts. Compensate their overlap so boundaries do not pump the volume.
  Add independent, quiet 0.22–0.48-second pour accents every 7–14 seconds.
  Keep the sprinkler composition, runtime gains and range unchanged.

Record subsequent feedback here, update the rules in `AGENTS.md`, and adjust
the relevant sounds, policy constants, and tests together. Listening judgment
belongs to the maintainer; automated checks establish signal and playback
properties, not realism or pleasantness.

## Current motion and gain mapping

Continuous playback uses gain 0.5, with airflow reduced to 0.4. Flywheel,
pump mechanism and transmission bearings use 0.32, 0.36 and 0.16 respectively.
It has full
near-field gain through 1.25
blocks and a smooth fade to silence at two blocks. The engine reference
distance is 1.9 blocks, so it does not apply another fade throughout the
audible near field. A small outer interval keeps engine distance models valid.
Irrigation instead retains full near-field gain through 2.5 blocks and fades
to silence at five, with a 4.9-block engine reference distance. Its source
level is about 0.05 RMS for sprinklers and 0.043 for irrigators, with playback
gain 0.5: lower than the rejected
0.075–0.080 synthetic beds, with recorded wet detail and no added hiss floor.
An active sprinkler or irrigator uses its dedicated water layer instead of
also loading the ordinary pipe bed, preserving nearby voice capacity.
Machine audio uses the sound-effects volume setting. Informational work uses
gain 0.34; pressure creaks use gain 0.03–0.16 after five seconds above 500 kPa.
Their severity rises gradually to 2000 kPa. They stop at 500 kPa or below and
repeat every 60–90 seconds just above onset, or 20–30 seconds at full severity.
These are listening thresholds; they do not add pressure damage.

The Small Flywheel follows its live network speed and eight-spoke phase,
including coasting on its standalone network. Its four-minute source combines
diffuse rim air, quiet wooden bearing motion and subdued frame settling.
The pump bed has leather and iron motion; transmission bearings use sparse,
soft bearing movement without discrete modal pings. These beds have no fixed pitched hum or independent valve
or pawl rhythm. Runtime still supplies recurring contacts from live motion.

The Reciprocating Pump reads the same presentation frame as the piston and
checks. A four-minute dry mechanism bed follows piston travel; existing original
water/air sources are mixed only for actual intake or delivery throughput.
Dry-side stroke reversal and wet-check closure produce one softened contact
cluster, with four variations. A stalled piston fades its mechanical layer;
actual intake or discharge can keep flowing through an open displayed wet
check, including paused filling and stored-pressure release. Remaining
wet-check movement can still close once. Recurring valve contacts
stay within two blocks.

The Overrunning Transmission has a quiet four-minute intermittent bearing source.
Freewheeling clicks follow the drop at each relative tooth boundary. Three
120-degree-spaced pawls on the 15-tooth ring share that phase, so they sound
as one cluster, not three unrelated rhythms. Equal-speed shafts have no
ratchet clicks. The tooltip, pawl pose and sound share a transient server lock
snapshot, including its handedness and both network IDs. Changes are limited
to ten updates per second; a one-second heartbeat initializes late observers.
Missing, expired or mismatched state suppresses contacts until a current
snapshot arrives. Loaded phase correction cannot create freewheel clicks.
Engagement/release changes use a separate four-variation,
0.4-second cue at up to five blocks, with gain 0.17 and a softer release.
The seating contact decays within 0.17 seconds; the rest of that compatible
cue is quiet. Freewheel ticks include a 30 ms codec lead-in, decay before
0.07 seconds and
use gain at most 0.12, scaled by relative shaft motion. Engagement cues have
a 0.75-second cooldown so repeated state changes cannot form a rattle.
Direction changes, missing networks and frames over 0.25 seconds rebase
instead of replaying missed contacts. Sources skip excess events, retain at
most one active contact voice, and release cached variations after two idle
seconds or on leaving range; at most eight contact sources are retained per
client alongside the existing eight-loop limit. All playback is client-only
presentation and adds no saved fields; the transient lock packet is described
in `COMPATIBILITY.md`.

Recurring contact clusters supplement the four-minute continuous source
content; their rate is set by visible motion rather than looping a short
recording. The source build checks contact resonance and softened peaks.

| Mechanism | Content duration | Audible motion |
| --- | ---: | --- |
| Sender | 1.6 seconds | About five eased tooth contacts follow each rack stroke, with a frame stop, a low air launch, and a lighter empty return. Frames: rise 0–150, launch 180–240, return 250–360. |
| Receiver | 1.6 seconds | Fourteen geared contacts over the cam cycle, short gate flexes, an item catch and soft leather roller compression. Frames: gate opening through 110, arrival 115–145, fall 145–190, feed from 190, gate closure 235–355. |
| Router preparation | 0.4 seconds | Gate movement and carriage alignment follow preparation. An already aligned carriage has no invented turning sound. |
| Router transit | 1 second | Catch, gate closure, indexed carriage turn, gate opening and launch follow `PneumaticRouterMotion`. |
| Plain outlet arrival | 0.9 seconds | Sliding and settling after committed chest insertion; it does not imitate a powered gate or tray. |
| Pressure creak | 1.8 seconds | A longer irregular strain texture, with a soft attack and rate-limited playback. |

The source timings come from the approved model definitions, their review
frames, and the runtime progress calculation. Client work audio reads the
replicated mechanism phase, including Sender empty return. It resumes at
the current phase after a stall or range change and releases on parking or
unload. It never drives simulation or creates a new saved field.

Air and bellows have overlapping, nonperiodic breaths. Low pressure, throat
turbulence and a much quieter air edge rise and fall at different points in
each breath. Bellows add leather movement, wooden-frame contacts and soft
compression. Water uses changing phrases of many small wet impacts and
collapsing cavities above a subdued flow bed. Pipe flow uses diffuse liquid
motion without low pitched cavity sweeps; outlets have scattered splashes;
sprinklers and irrigators instead use inspected installed
`survival:sounds/effect/watering-loop.ogg` and
`survival:sounds/effect/water-pour.ogg`. The generator removes rumble and bright
spray hiss, then composes different excerpts, lengths, overlaps and wet impact
groups. Sprinklers have crossing ribbons of fine spray; irrigators have
continuous overlapping water with much quieter occasional pouring accents.
The irrigator body is leveled separately from its sparse pours, which use
0.008–0.012 RMS locally and are not normalized across their silent intervals.
Neither adds synthetic bubble pitches
or a continuous noise floor. All continuous sources contain four minutes
of actual evolving content, with a soft peak ceiling to keep sparse droplets
from overwhelming their quieter detail.

Moving mechanisms use damped, inharmonic iron, brass, wood and leather modes.
A rounded contact force excites the body, short surface grain adds texture,
and higher resonances decay before the lower body. Small rebounds and frame
settling follow the contact. Quieter tooth-face and guide motion supports rack
contacts; Receiver rollers have leather compression and surface detail. Gate
releases retain gaps. Broadband friction does not carry the whole cycle.
Each moving mechanism has four recordings with different contact strengths,
resonances and small timing offsets. A new cycle avoids its previous variation.
Pausing or temporarily leaving range retains the same variation so the action
resumes consistently.

The build audits clipping, loop seams, short-term loudness, high-frequency
energy, airflow pressure variation and mechanical spectral flatness. Short
Vorbis cues use 20 ms pages to retain encoder preroll timing and their full
decoded duration. Work contacts are clipped at the end of a cue; only loop
content wraps. All assets are encoded and checked in staging before promotion.

`python -m unittest discover tests/audio -v` checks the decoded catalogue's
sample counts, softened edges, varied long content, distinct work variations
and Sender launch/return phases. Irrigator checks also bound decoded quiet
gaps and short-term surges, plus the pour layer's gain and spacing.
`python tools/audio/inspect_loop_continuity.py <recording.ogg>` reports the
50 ms envelope independently of the playback controller. Runtime contracts
verify that a minute of ten-per-second irrigation state updates starts and
seeks only once, brief pressure dips retain that voice, and sustained loss
of pressure or leaving range still releases it.
`tools/Test-Project.ps1` includes these checks.
They do not establish how natural or pleasant the result sounds in-game.

For a matched-gain comparison, preserve the previous `sounds/machines` folder
under `generated/machine-sound-review/current/before/machines` before building,
then run `python tools/audio/review_machine_sounds.py`. The fixed review folder
receives separate before/after mechanism and flow/bearing WAV reels, plus a
timeline listing each cue and playback gain. Reels use the runtime category
gains without independent loudness normalization. They are optional diagnostics;
normal play remains the final listening check.

Rebuild with `tools/audio/Build-MachineSounds.ps1`; it discovers the installed
game, or accepts `-VintageStoryPath`. `water_sound_sources.py --game-assets`
audits the two inputs. Their logical references and hashes are recorded in the
manifest; the source recordings are not copied into the repository. Verification
of the packaged recordings does not need access to the original game samples.
