# Audio recovery build (2026-10-16)

This rebuilds the combat SFX, the silent-gap sounds and an interim score for the sound manifest. It replaces the lost "recorded S2" set; `C:\Otherreach_Recovery\ASSET_REBUILD\AUDIO_RECOVERY_20261016.md` describes what that set was.

Staging and output live in `C:\Otherreach_Recovery\asset_work\audio_recovery\`. The raw library audio stays out of git.

Run these with `PYTHONPATH=tools python -P tools/<script> <staging root>`. Every build step is deterministic.

1. `pool.py`: cuts the source libraries into separate takes, in `src/pool/` with `index.json`. The sources are Sonniss GDC 2026, Cosmos and Kenney, extracted under `asset_work\audio\_src`.
2. `build_combat.py`: builds 24 combat families (99 files). Each impact layers a transient, a body and a thud, with the hit at the file start. Different variants use different takes. Each family carries a `pitch_jitter_semitones` value that `SoundBank` applies on every play.
3. `build_gaps.py`: builds gathering, cooking, workbench, trade, eating, drinking and salve sounds, plus `amb.interior.room_tone.01`.
4. `gen_music.py`: generates the score with Stable Audio Open 1.0 in the local ComfyUI, from text prompts only. Run it through `run_music.ps1`, which takes the GPU lock. `build_music.py` then cuts the output into seamless loops and writes 48 kHz stereo Vorbis files. Naming cues (`gen_music.py <root> mus.explore`) generates only those. `build_tension.py` then cuts the tension layer, `mus.tension.01`, from the low band of `mus.combat.01`.
5. `credits.py` writes `CREDITS_audio_recovery.txt`. `qa.py` compares the new sounds with the playtest's.
6. `install.py <staging> <asset root>` installs into an asset root, such as a playtest's `primary\`. It replaces each rebuilt family whole and backs up the manifest first.

Nobody has listened to any of this yet.
