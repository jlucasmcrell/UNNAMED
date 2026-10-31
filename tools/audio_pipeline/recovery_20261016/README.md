# Audio recovery build (2026-10-16)

This rebuilds the combat SFX, the silent-gap sounds and the score for the sound manifest. It replaces the lost "recorded S2" set; `C:\Otherreach_Recovery\ASSET_REBUILD\AUDIO_RECOVERY_20261016.md` describes what that set was.

Staging and output live in `C:\Otherreach_Recovery\asset_work\audio_recovery\`. The raw library audio stays out of git.

Run these with `PYTHONPATH=tools python -P tools/<script> <staging root>`. Every build step is deterministic.

1. `pool.py`: cuts the source libraries into separate takes, in `src/pool/` with `index.json`. The sources are Sonniss GDC 2026, Cosmos and Kenney, extracted under `asset_work\audio\_src`.
2. `build_combat.py`: builds 24 combat families (99 files). Each impact layers a transient, a body and a thud, with the hit at the file start. Different variants use different takes. Each family carries a `pitch_jitter_semitones` value that `SoundBank` applies on every play.
3. `build_gaps.py`: builds gathering, cooking, workbench, trade, eating, drinking and salve sounds, plus `amb.interior.room_tone.01`.
4. `build_score.py <ElevenLabs music dir> <staging>`: the score, from the owner-approved ElevenLabs Music tracks in `C:\Otherreach_External_Assets\Voice_ElevenLabs\music`. It cuts each bed to a seamless loop at its best-matching pair of places, levels it under the dialogue and writes 48 kHz stereo Vorbis files. `gen_music.py` (Stable Audio Open 1.0 in the local ComfyUI, run through `run_music.ps1`, which takes the GPU lock) now only feeds `build_opening.py`, the opening cue.
5. `credits.py` writes `CREDITS_audio_recovery.txt`. `qa.py` compares the new sounds with the playtest's.
6. `install.py <staging> <asset root>` installs into an asset root, such as a playtest's `primary\`. It replaces each rebuilt family whole and backs up the manifest first.
7. `build_spells.py [credits file ...]`: the P2 spell voices - a cast, a flight or an activation and an end for each working, cut from the same library at one window per file and levelled to the magic family (cast -21.5 LUFS momentary max, flight -23, impact -19, a resolution -22, an end -24). It writes `out/spell_rows.json` and `out/CREDITS_spell_sfx.txt`, and puts the credits section into any credits file named on its command line. `install_spells.py <staging> <asset root>` installs only those families, so nothing another lane delivered is dropped.

Nobody has listened to any of this yet.
