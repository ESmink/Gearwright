"""Signal and phase regressions for original, locally generated machine audio."""

import itertools
import json
from pathlib import Path
import sys
import unittest

import av
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/audio'))
import machine_sound_assets as sounds
from inspect_loop_continuity import continuity_profile


def decode(path):
    with av.open(str(path)) as container:
        return np.concatenate([frame.to_ndarray().reshape(-1) for frame in container.decode(audio=0)])


def rms(values):
    return float(np.sqrt(np.mean(values ** 2)))


class MachineSoundTests(unittest.TestCase):
    def test_late_work_contacts_do_not_wrap_into_an_earlier_phase(self):
        work = np.zeros(10)
        sounds.add(work, np.ones(5), 8)
        np.testing.assert_array_equal(work, [0] * 8 + [1, 1])
        sounds.add(work, np.ones(5), 12)
        np.testing.assert_array_equal(work, [0] * 8 + [1, 1])
        loop = np.zeros(10)
        sounds.add(loop, np.ones(5), 8, wrap=True)
        np.testing.assert_array_equal(loop, [1, 1, 1, 0, 0, 0, 0, 0, 1, 1])

    def test_public_catalogue_and_animation_durations_remain_compatible(self):
        expected_loops = {'airflow', 'bellows', 'water-pipe', 'water-outlet', 'water-sprinkler',
                          'water-irrigator', 'flywheel', 'pump-mechanism', 'transmission-bearing'}
        expected_events = {'sender-work': 1.6, 'receiver-work': 1.6, 'router-turn': 1.,
                           'router-prepare': .4, 'outlet-arrival': .9, 'pump-valve': .14,
                           'ratchet-pawl': .1, 'ratchet-engage': .4,
                           'pressure-creak1': 1.8, 'pressure-creak2': 1.8, 'pressure-creak3': 1.8}
        for base in ('sender-work', 'receiver-work', 'router-turn', 'router-prepare',
                     'pump-valve', 'ratchet-pawl', 'ratchet-engage'):
            for variation in range(2, 5):
                expected_events[f'{base}-{variation}'] = expected_events[base]
        manifest = json.loads(sounds.MANIFEST.read_text(encoding='utf-8'))
        self.assertEqual(set(manifest['files']), {name + '.ogg' for name in expected_loops | expected_events.keys()})
        for name, entry in manifest['files'].items():
            with self.subTest(name=name):
                duration = 240. if name[:-4] in expected_loops else expected_events[name[:-4]]
                self.assertAlmostEqual(entry['format']['seconds'], duration, places=3)

    def test_encoded_sources_have_headroom_detail_and_varied_long_content(self):
        for name in sounds.LOOPS + sounds.EVENT_ASSETS:
            with self.subTest(name=name):
                values = decode(sounds.OUTPUT / (name + '.ogg'))
                declared = sounds.ogg_info(sounds.OUTPUT / (name + '.ogg'))['seconds']
                self.assertEqual(len(values), round(declared * sounds.RATE))
                self.assertTrue(np.isfinite(values).all())
                self.assertLess(float(np.max(np.abs(values))), .8)
                self.assertLess(abs(float(values.mean())), .001)
                sounds.check_character(name, sounds.waveform_profile(values))
                if name in sounds.LOOPS:
                    self.assertGreaterEqual(len(values), 180 * sounds.RATE)
                    self.assertGreater(rms(values), .025)
                    self.assertLess(rms(values), .12)
                    # Detect a short reused bed, including copies with changed
                    # gain. Compare waveform and envelope across the timeline.
                    clips = [values[second * sounds.RATE:(second + 2) * sounds.RATE]
                             for second in (3, 20, 61, 97, 143, 181, 219)]
                    for first, second in itertools.combinations(clips, 2):
                        self.assertLess(abs(float(np.corrcoef(first, second)[0, 1])), .45)
                    blocks = values[:len(values) // 2400 * 2400].reshape(-1, 2400)
                    levels = np.sqrt(np.mean(blocks ** 2, axis=1))
                    self.assertGreater(float(levels.std() / levels.mean()), .12)
                    self.assertLessEqual(abs(float(values[0] - values[-1])),
                                         float(np.quantile(np.abs(np.diff(values)), .999)))
                else:
                    self.assertLess(abs(float(values[0])), .002)
                    self.assertLess(abs(float(values[-1])), .002)

    def test_sender_air_launch_and_lighter_return_keep_their_model_phases(self):
        for variation in range(4):
            values = sounds.make_event('sender-work', 8106 + variation * 100)
            lift = values[round(.04 * sounds.RATE):round(.65 * sounds.RATE)]
            launch = values[round(.80 * sounds.RATE):round(1.067 * sounds.RATE)]
            returning = values[round(1.12 * sounds.RATE):round(1.50 * sounds.RATE)]
            self.assertGreater(rms(launch), .015)
            self.assertGreater(rms(returning), .015)
            self.assertLess(rms(returning), rms(lift))
            power = abs(np.fft.rfft(launch)) ** 2
            frequencies = np.fft.rfftfreq(len(launch), 1 / sounds.RATE)
            self.assertLess(float(power[frequencies > 1800].sum() / power.sum()), .01)

    def test_pipe_water_has_no_low_pitched_strain_and_irrigation_has_audible_body(self):
        values = decode(sounds.OUTPUT / 'water-pipe.ogg')
        power = abs(np.fft.rfft(values)) ** 2
        frequencies = np.fft.rfftfreq(len(values), 1 / sounds.RATE)
        self.assertLess(float(power[frequencies < 250].sum() / power.sum()), .08)
        for name in ('water-sprinkler', 'water-irrigator'):
            with self.subTest(name=name):
                values = decode(sounds.OUTPUT / (name + '.ogg'))
                self.assertGreater(rms(values), .040)
                self.assertLess(rms(values), .060)

    def test_ratchet_contacts_are_dry_and_do_not_ring_between_teeth(self):
        for base, tail_start in (('ratchet-pawl', .070), ('ratchet-engage', .17)):
            for suffix in ('', '-2', '-3', '-4'):
                with self.subTest(cue=base + suffix):
                    values = decode(sounds.OUTPUT / (base + suffix + '.ogg'))
                    self.assertLess(rms(values), .05)
                    tail = values[round(tail_start * sounds.RATE):]
                    self.assertLess(float(np.sum(tail ** 2) / np.sum(values ** 2)), .01)

    def test_irrigator_recording_has_no_rapid_gaps_or_loud_pour_surges(self):
        values = decode(sounds.OUTPUT / 'water-irrigator.ogg')
        profile = continuity_profile(values, sounds.RATE)
        self.assertLess(profile['quietFraction'], .005, profile)
        self.assertLess(profile['quietEntriesPerSecond'], .05, profile)
        self.assertLessEqual(profile['longestQuietSeconds'], .1, profile)
        self.assertLess(profile['peak50msRms'], .10, profile)
        self.assertLess(profile['envelopeVariation'], .45, profile)

    def test_irrigator_pours_are_sparse_and_quieter_than_the_water_body(self):
        # Source-independent layer checks also cover future installed samples.
        rng = np.random.default_rng(174)
        sources = [rng.normal(size=sounds.RATE * seconds) for seconds in (7, 2)]
        body, pours = sounds.irrigator_layers(np.random.default_rng(8105), sources)
        # Detect phrases in 10 ms windows, not individual waveform zero crossings.
        width = sounds.RATE // 100
        windows = pours.reshape(-1, width)
        active = np.sqrt(np.mean(windows ** 2, axis=1)) > 1e-5
        transitions = np.diff(np.r_[False, active, False].astype(int))
        starts, ends = np.flatnonzero(transitions == 1), np.flatnonzero(transitions == -1)
        self.assertGreater(len(starts), 15)
        self.assertLess(len(starts), 35)
        self.assertLess(float(active.mean()), .06)
        self.assertGreater(float(np.min(starts[1:] - ends[:-1])) * width / sounds.RATE, 6)
        self.assertGreaterEqual(float(np.min(np.diff(starts))) * width / sounds.RATE, 6.9)
        self.assertLessEqual(float(np.max(np.diff(starts))) * width / sounds.RATE, 14.1)
        self.assertLess(rms(windows[active]), rms(body) * .3)
        self.assertLess(float(abs(pours).max()), .065)

    def test_ratchet_sources_keep_their_complete_timing_before_encoding(self):
        # Timing checks are independent of optional platform-specific Vorbis
        # encoders. The encoded, committed Ogg cues are decoded above.
        for base in ('ratchet-pawl', 'ratchet-engage'):
            for variation in range(1, sounds.WORK_VARIATIONS + 1):
                name = base if variation == 1 else f'{base}-{variation}'
                with self.subTest(cue=name):
                    values = sounds.make_event(name, 8200 + variation)
                    self.assertEqual(len(values), round(sounds.WORK_SECONDS[base] * sounds.RATE))
                    self.assertTrue(np.isfinite(values).all())
                    self.assertLess(abs(float(values[0])), .002)
                    self.assertLess(abs(float(values[-1])), .002)

    def test_variants_change_contact_detail_without_changing_cycle_length(self):
        for name in sounds.VARIED_WORK:
            with self.subTest(name=name):
                clips = [decode(sounds.OUTPUT / (name + suffix + '.ogg')) for suffix in ('', '-2', '-3', '-4')]
                for first, second in itertools.combinations(clips, 2):
                    self.assertEqual(len(first), len(second))
                    self.assertLess(abs(float(np.corrcoef(first, second)[0, 1])), .90)


if __name__ == '__main__':
    unittest.main()
