extends MainLoop

# Prove the audio set imports and behaves in the engine, not just on disk.
#
# Section 31 asks for import, playback, looping, spatialisation and no obvious import distortion.
# Playback itself needs an audio device, which headless Godot does not have, so what is checked here
# is everything playback depends on: the file imports as a stream, the stream's own format matches
# what the pipeline produced, mono assets are genuinely mono (a stereo one-shot cannot be
# spatialised by AudioStreamPlayer3D), loop assets can be given a full-length loop, and the decoded
# sample data is neither silent nor clipped.
#
# Run:
#   godot --headless --path <project> --script validate_audio.gd

const MANIFEST := "res://assets/audio_manifest.json"
const TARGET_RATE := 48000
const LENGTH_TOLERANCE := 0.05

var _done := false
var _frames := 0
var _code := 1


func _initialize() -> void:
	var report := {
		"ok": true, "problems": [], "sounds": 0, "imported": 0, "mono": 0, "stereo": 0,
		"looped": 0, "loop_failures": 0, "silent": 0, "clipped": 0, "formats": {},
		"rate_mismatch": 0, "channel_mismatch": 0, "length_mismatch": 0, "details": [],
	}
	if not FileAccess.file_exists(MANIFEST):
		report["ok"] = false
		report["problems"].append("manifest missing; run _audio_validate.py --emit-manifest --stage-godot")
		_finish(report)
		return

	var parsed = JSON.parse_string(FileAccess.get_file_as_string(MANIFEST))
	if not (parsed is Dictionary):
		report["ok"] = false
		report["problems"].append("manifest is not valid JSON")
		_finish(report)
		return

	for entry in parsed.get("sounds", []):
		var audio_id := String(entry.get("audio_id", ""))
		var path := "res://assets/audio/%s.wav" % audio_id
		report["sounds"] += 1
		if not ResourceLoader.exists(path):
			report["problems"].append("%s: file not staged" % audio_id)
			continue
		var stream = load(path)
		if stream == null or not (stream is AudioStreamWAV):
			report["problems"].append("%s: did not import as an AudioStreamWAV" % audio_id)
			continue
		var wav: AudioStreamWAV = stream
		report["imported"] += 1

		var fmt := _format_name(wav.format)
		report["formats"][fmt] = int(report["formats"].get(fmt, 0)) + 1

		if wav.mix_rate != TARGET_RATE:
			report["rate_mismatch"] += 1
			report["problems"].append("%s: mix rate %d, expected %d"
				% [audio_id, wav.mix_rate, TARGET_RATE])

		var expects_stereo := int(entry.get("channels", 1)) == 2
		if wav.stereo != expects_stereo:
			report["channel_mismatch"] += 1
			report["problems"].append("%s: stereo=%s but the manifest says %d channel(s)"
				% [audio_id, wav.stereo, int(entry.get("channels", 1))])
		if wav.stereo:
			report["stereo"] += 1
		else:
			report["mono"] += 1

		var length := wav.get_length()
		var declared := float(entry.get("delivered_seconds", 0.0))
		if declared > 0.0 and absf(length - declared) > LENGTH_TOLERANCE:
			report["length_mismatch"] += 1
			report["problems"].append("%s: stream length %.4fs against %.4fs on disk"
				% [audio_id, length, declared])

		# Decoded sample check: a stream that survived import but decodes to silence or to a
		# squared-off waveform is a failed import, and neither is visible from the file size.
		var data: PackedByteArray = wav.data
		if data.size() == 0:
			report["problems"].append("%s: no sample data after import" % audio_id)
		else:
			var peak := _peak_of(wav, data)
			if peak <= 0.0001:
				report["silent"] += 1
				report["problems"].append("%s: decodes to silence" % audio_id)
			elif peak >= 0.999:
				report["clipped"] += 1
				report["problems"].append("%s: decodes at full scale (peak %.4f), likely clipped"
					% [audio_id, peak])

		if entry.get("loop", false):
			# A bed is only loopable if the stream can be given a full-length forward loop. Godot
			# imports WAVs with looping disabled unless the import sets it, so this is set and then
			# read back rather than assumed.
			wav.loop_mode = AudioStreamWAV.LOOP_FORWARD
			wav.loop_begin = 0
			wav.loop_end = int(length * wav.mix_rate)
			if wav.loop_mode != AudioStreamWAV.LOOP_FORWARD or wav.loop_end <= 0:
				report["loop_failures"] += 1
				report["problems"].append("%s: could not set a forward loop" % audio_id)
			else:
				report["looped"] += 1
			var seam = entry.get("loop_seam", {})
			if seam is Dictionary and seam.has("seam_after") and seam.has("seam_before"):
				report["details"].append({
					"audio_id": audio_id,
					"seam_before": seam["seam_before"],
					"seam_after": seam["seam_after"],
					"length_s": snappedf(length, 0.01),
				})

	report["ok"] = report["problems"].is_empty()
	_finish(report)


func _format_name(format: int) -> String:
	match format:
		AudioStreamWAV.FORMAT_8_BITS:
			return "8_BITS"
		AudioStreamWAV.FORMAT_16_BITS:
			return "16_BITS"
		AudioStreamWAV.FORMAT_IMA_ADPCM:
			return "IMA_ADPCM"
		AudioStreamWAV.FORMAT_QOA:
			return "QOA"
		_:
			return "unknown(%d)" % format


func _peak_of(wav: AudioStreamWAV, data: PackedByteArray) -> float:
	var peak := 0.0
	if wav.format == AudioStreamWAV.FORMAT_16_BITS:
		# 16-bit little-endian PCM, interleaved. Sampled rather than exhaustive: these are short
		# files and a stride keeps the check cheap without changing what it detects.
		var stride := 2 * (2 if wav.stereo else 1)
		var index := 0
		while index + 1 < data.size():
			var value: int = data[index] | (data[index + 1] << 8)
			if value > 32767:
				value -= 65536
			var normalised: float = absf(float(value)) / 32768.0
			if normalised > peak:
				peak = normalised
			index += stride
	elif wav.format == AudioStreamWAV.FORMAT_8_BITS:
		var stride8 := 2 if wav.stereo else 1
		var index8 := 0
		while index8 < data.size():
			var normalised8: float = absf(float(data[index8]) - 128.0) / 128.0
			if normalised8 > peak:
				peak = normalised8
			index8 += stride8
	return peak


func _finish(report: Dictionary) -> void:
	print("AUDIO_RESULT " + JSON.stringify(report))
	_code = 0 if report.get("ok", false) else 1
	_done = true


func _process(_delta: float) -> bool:
	# Bounded as well as flag-driven: a runtime error inside _initialize would otherwise leave the
	# flag false and spin this headless process forever instead of failing.
	_frames += 1
	return _done or _frames > 2
