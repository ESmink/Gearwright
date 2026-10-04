# Gearwright sound decisions

The maintainer reviews sound in-game. Implement the best current choice, build,
and install it so feedback comes from normal play. Standalone audition clips
are optional diagnostics.

| Kind | Maximum range | Design |
| --- | ---: | --- |
| Constant noise | 2 blocks | Audible at normal head height beside the machine, while still fairly quiet. Avoid persistent tones, sharp textures, and annoying repetition. At least 180 seconds of varied source content before repeating. Examples: Automatic Bellow, water streaming, airflow. |
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

Record subsequent feedback here, update the rules in `AGENTS.md`, and adjust
the relevant sounds, policy constants, and tests together. Listening judgment
belongs to the maintainer; automated checks establish signal and playback
properties, not realism or pleasantness.

## Current motion and gain mapping

Continuous playback uses gain 0.5, with airflow reduced to 0.4. Flywheel,
pump mechanism and transmission bearings use 0.32, 0.36 and 0.28 respectively.
It has full
near-field gain through 1.25
blocks and a smooth fade to silence at two blocks. The engine reference
distance is 1.9 blocks, so it does not apply another fade throughout the
audible near field. A small outer interval keeps engine distance models valid.
Machine audio uses the sound-effects volume setting. Informational work uses
gain 0.34; pressure creaks use a lower pressure-dependent gain.

The Small Flywheel follows its live network speed and eight-spoke phase,
including coasting on its standalone network. Its four-minute source combines
diffuse rim air with changing damped bearing/frame contacts. It has no fixed
pitched hum.

The Reciprocating Pump reads the same presentation frame as the piston and
checks. A four-minute dry mechanism bed follows piston travel; existing original
water/air sources are mixed only for actual intake or delivery throughput.
Dry-side stroke reversal and wet-check closure produce one softened contact
cluster, with four variations. A stalled piston fades its mechanical layer;
actual intake or discharge can keep flowing through an open displayed wet
check, including paused filling and stored-pressure release. Remaining
wet-check movement can still close once. Recurring valve contacts
stay within two blocks.

The Overrunning Transmission has a quiet four-minute bearing/frame source.
Freewheeling clicks follow the drop at each relative tooth boundary. Three
120-degree-spaced pawls on the 15-tooth ring share that phase, so they sound
as one cluster, not three unrelated rhythms. Equal-speed shafts have no
ratchet clicks. Engagement/release changes use a separate four-variation,
0.4-second cue at up to five blocks, with gain 0.27 and a softer release.
Direction changes, missing networks and frames over 0.25 seconds rebase
instead of replaying missed contacts. Sources skip excess events, retain at
most one active contact voice, and release cached variations after two idle
seconds or on leaving range; at most eight contact sources are retained per
client alongside the existing eight-loop limit. All playback is client-only
presentation and adds no saved or wire fields.

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

Air and bellows have overlapping, nonperiodic breaths with moving low-frequency
pressure resonances and very little high-frequency hiss. Bellows add small
wooden-frame contacts. Water layers emphasize irregular droplets and bubbles
above a subdued flow bed. All continuous sources still contain four minutes
of actual evolving content.

Moving mechanisms use damped, inharmonic iron, brass, wood and leather modes;
broadband friction does not carry their whole cycle. Each has four recordings
with different contact strengths, resonances and small timing offsets. A new
cycle avoids its previous variation. Pausing or temporarily leaving range
retains the same variation so the action resumes consistently.

The build audits clipping, loop seams, airflow's high-frequency energy and
pressure-envelope variation, and the mechanical recordings' spectral flatness.
These checks reject a return to the steady broadband design. They do not
establish how natural or pleasant the result sounds in-game.
