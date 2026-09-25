# RAZER gate - Otherreach Phase-A complete prototype baseline (pre-visual-overhaul)

Machine: RAZER; GPU NVIDIA GeForce RTX 4070 Ti (1.4.351); CPU AMD Ryzen 7 5800X3D 8-Core Processor, 16 threads; Windows 10.0.26300; Godot 4.7.2-stable (official), forward_plus; resolution (1920, 1080); VSync Disabled.
Environment: environment.txt. Runs: run0_cold, run1, run2, run3.

## Verdict: FAIL pending review

Not comfortably above the target (the bar: a 1% low of 72 FPS or better and a p99 within 16.67 ms in every gameplay segment of every warm run, no repeatable hitch over 33 ms).

Findings:
- magic - sustained 60 (1% low at or above 60 FPS, median at most 12.5 ms) in only 0 of 3 warm runs

## run0_cold

Route: warmup 0 waypoints, conversation goal met in 16.3 s, magic goal met in 4.5 s, combat goal met in 30.2 s, loot goal met in 16.9 s, third_person 18 waypoints, first_person 17 waypoints; the character was struck 2 times and died 0 times (a clean capture: 0 and 0)

| Segment | Frames | s | Avg FPS | 1% low | p50 ms | p95 | p99 | max | >16.7 | >25 | >33.3 | >50 | CPU p99 | GPU p99 | VRAM MB | WS MB | 60 by 1% low |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| warmup | 519 | 4.9 | 106.7 | 18.1 | 8.333 | 10.417 | 14.903 | 150 | 2 | 2 | 2 | 2 | 2.52 | 10.77 | 5,814 | 4,700 | False |
| conversation | 1876 | 16.2 | 115.8 | 100.9 | 8.766 | 9.259 | 9.259 | 14.95 | 0 | 0 | 0 | 0 | 1.76 | 9.39 | 5,820 | 2,201 | True |
| magic | 489 | 4.4 | 111.2 | 22.8 | 8.423 | 9.259 | 16.242 | 137.255 | 2 | 2 | 1 | 1 | 1.53 | 8.81 | 5,821 | 2,199 | False |
| combat | 3135 | 30 | 104.4 | 81.9 | 9.829 | 10.18 | 10.417 | 39.591 | 1 | 1 | 1 | 0 | 1.53 | 10.55 | 5,822 | 2,244 | True |
| loot | 1736 | 16.8 | 103.4 | 71.2 | 9.722 | 10.417 | 11.427 | 16.975 | 3 | 0 | 0 | 0 | 1.11 | 10.29 | 5,823 | 2,216 | True |
| third_person | 17044 | 149.9 | 113.7 | 85.4 | 8.574 | 10.417 | 10.678 | 65.804 | 2 | 2 | 2 | 1 | 1.91 | 10.37 | 5,822 | 2,221 | True |
| first_person | 17810 | 149.9 | 118.8 | 86.7 | 8.333 | 10.141 | 10.751 | 24.956 | 7 | 0 | 0 | 0 | 1.68 | 10.35 | 5,822 | 2,208 | True |

nvidia-smi: VRAM used peak 6,853 MiB; GPU utilisation p95 98%.

- autosave to auto_01 taken at 300.0 s of play (first_person): frames then 8.36, 10.21, 9.09 ms, segment median 8.33 ms
- auto_01 written in the background (first_person): frames then 8.33, 8.33, 8.38 ms, segment median 8.33 ms

Hitches over 25 ms: 7 - warmup 0.2s 150.0ms; warmup 0.3s 116.8ms; magic 1.6s 137.3ms; magic 3.3s 32.7ms; combat 16.5s 39.6ms; third_person 43.9s 36.3ms; third_person 58.5s 65.8ms

## run1

Route: warmup 0 waypoints, conversation goal met in 16.3 s, magic goal met in 4.5 s, combat goal met in 25.6 s, loot goal met in 19.8 s, third_person 18 waypoints, first_person 17 waypoints; the character was struck 1 times and died 0 times (a clean capture: 0 and 0)

| Segment | Frames | s | Avg FPS | 1% low | p50 ms | p95 | p99 | max | >16.7 | >25 | >33.3 | >50 | CPU p99 | GPU p99 | VRAM MB | WS MB | 60 by 1% low |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| warmup | 546 | 4.9 | 112 | 17.7 | 8.333 | 9.091 | 15.557 | 142.484 | 2 | 2 | 2 | 2 | 2.20 | 8.91 | 5,814 | 4,267 | False |
| conversation | 1890 | 16.2 | 116.7 | 96.6 | 8.647 | 9.091 | 9.524 | 15.162 | 0 | 0 | 0 | 0 | 1.67 | 9.30 | 5,820 | 1,906 | True |
| magic | 504 | 4.4 | 114.7 | 48.2 | 8.468 | 9.091 | 11.111 | 35.933 | 3 | 2 | 1 | 0 | 1.32 | 8.71 | 5,821 | 1,917 | False |
| combat | 2714 | 25.5 | 106.5 | 84.8 | 9.573 | 10 | 10 | 29.766 | 1 | 1 | 0 | 0 | 1.41 | 10.31 | 5,822 | 1,922 | True |
| loot | 2056 | 19.7 | 104.4 | 92.9 | 9.722 | 10 | 10.018 | 17.182 | 1 | 0 | 0 | 0 | 0.84 | 10.30 | 5,823 | 1,907 | True |
| third_person | 17201 | 149.9 | 114.8 | 88.4 | 8.504 | 10.275 | 10.606 | 30.878 | 2 | 2 | 0 | 0 | 1.19 | 10.28 | 5,822 | 1,906 | True |
| first_person | 17984 | 149.9 | 120 | 88.3 | 8.333 | 10 | 10.606 | 25.002 | 6 | 1 | 0 | 0 | 1.17 | 10.22 | 5,822 | 1,902 | True |

nvidia-smi: VRAM used peak 6,880 MiB; GPU utilisation p95 98%.

- autosave to auto_01 taken at 300.0 s of play (first_person): frames then 8.33, 8.33, 8.33 ms, segment median 8.33 ms
- auto_01 written in the background (first_person): frames then 8.33, 8.33, 8.33 ms, segment median 8.33 ms

Hitches over 25 ms: 8 - warmup 0.1s 142.5ms; warmup 0.3s 130.8ms; magic 1.5s 35.9ms; magic 3.3s 27.6ms; combat 16.5s 29.8ms; third_person 43.9s 29.9ms; third_person 58.4s 30.9ms; first_person 47.2s 25.0ms

## run2

Route: warmup 0 waypoints, conversation goal met in 16.3 s, magic goal met in 4.7 s, combat goal met in 29.8 s, loot goal met in 16.5 s, third_person 18 waypoints, first_person 17 waypoints; the character was struck 2 times and died 0 times (a clean capture: 0 and 0)

| Segment | Frames | s | Avg FPS | 1% low | p50 ms | p95 | p99 | max | >16.7 | >25 | >33.3 | >50 | CPU p99 | GPU p99 | VRAM MB | WS MB | 60 by 1% low |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| warmup | 543 | 4.9 | 111.4 | 18 | 8.333 | 9.091 | 15.801 | 133.333 | 3 | 2 | 2 | 2 | 2.17 | 8.98 | 5,814 | 4,265 | False |
| conversation | 1873 | 16.2 | 115.6 | 98.4 | 8.749 | 9.259 | 9.524 | 16.667 | 0 | 0 | 0 | 0 | 1.67 | 9.38 | 5,820 | 1,910 | True |
| magic | 519 | 4.5 | 114.8 | 55.2 | 8.437 | 9.091 | 12.388 | 30.601 | 2 | 1 | 0 | 0 | 1.21 | 8.78 | 5,821 | 1,873 | False |
| combat | 3111 | 29.7 | 104.8 | 82.7 | 9.722 | 10.247 | 10.417 | 43.509 | 1 | 1 | 1 | 0 | 1.40 | 10.48 | 5,822 | 1,908 | True |
| loot | 1700 | 16.3 | 104.1 | 88.7 | 9.524 | 10.251 | 10.417 | 16.667 | 0 | 0 | 0 | 0 | 0.83 | 10.29 | 5,822 | 1,890 | True |
| third_person | 17106 | 149.9 | 114.1 | 84.5 | 8.55 | 10.341 | 10.628 | 39.138 | 6 | 2 | 2 | 0 | 1.18 | 10.34 | 5,822 | 1,893 | True |
| first_person | 17914 | 149.9 | 119.5 | 92.3 | 8.333 | 10 | 10.606 | 16.154 | 0 | 0 | 0 | 0 | 1.16 | 10.21 | 5,822 | 1,898 | True |

nvidia-smi: VRAM used peak 6,819 MiB; GPU utilisation p95 98%.

- autosave to auto_01 taken at 300.0 s of play (first_person): frames then 8.33, 8.33, 8.33 ms, segment median 8.33 ms
- auto_01 written in the background (first_person): frames then 8.33, 8.33, 8.33 ms, segment median 8.33 ms

Hitches over 25 ms: 6 - warmup 0.1s 133.3ms; warmup 0.3s 133.3ms; magic 1.5s 30.6ms; combat 16.4s 43.5ms; third_person 43.9s 37.4ms; third_person 58.5s 39.1ms

## run3

Route: warmup 0 waypoints, conversation goal met in 16.3 s, magic goal met in 4.7 s, combat goal met in 28.5 s, loot goal met in 17.0 s, third_person 18 waypoints, first_person 17 waypoints; the character was struck 1 times and died 0 times (a clean capture: 0 and 0)

| Segment | Frames | s | Avg FPS | 1% low | p50 ms | p95 | p99 | max | >16.7 | >25 | >33.3 | >50 | CPU p99 | GPU p99 | VRAM MB | WS MB | 60 by 1% low |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| warmup | 537 | 4.9 | 110.3 | 17.6 | 8.392 | 9.091 | 16.667 | 141.641 | 2 | 2 | 2 | 2 | 2.85 | 9.05 | 5,814 | 4,266 | False |
| conversation | 1871 | 16.2 | 115.7 | 97.9 | 8.717 | 9.091 | 9.722 | 12.5 | 0 | 0 | 0 | 0 | 1.65 | 9.34 | 5,820 | 1,909 | True |
| magic | 516 | 4.5 | 114.3 | 57.7 | 8.468 | 9.091 | 12.255 | 29.922 | 2 | 1 | 0 | 0 | 1.27 | 8.78 | 5,821 | 1,906 | False |
| combat | 2996 | 28.4 | 105.5 | 83.2 | 9.722 | 10.071 | 10.417 | 36.637 | 1 | 1 | 1 | 0 | 1.39 | 10.37 | 5,822 | 1,906 | True |
| loot | 1752 | 16.8 | 104 | 89.1 | 9.524 | 10.417 | 10.417 | 16.667 | 0 | 0 | 0 | 0 | 0.88 | 10.20 | 5,823 | 1,908 | True |
| third_person | 17185 | 149.9 | 114.7 | 83.3 | 8.52 | 10.32 | 10.685 | 38.085 | 8 | 2 | 2 | 0 | 1.18 | 10.38 | 5,822 | 1,902 | True |
| first_person | 18034 | 149.9 | 120.3 | 93.2 | 8.333 | 10 | 10.606 | 13.247 | 0 | 0 | 0 | 0 | 1.16 | 10.14 | 5,822 | 1,886 | True |

nvidia-smi: VRAM used peak 6,870 MiB; GPU utilisation p95 98%.

- autosave to auto_01 taken at 300.0 s of play (first_person): frames then 8.17, 8.33, 8.33 ms, segment median 8.33 ms
- auto_01 written in the background (first_person): frames then 8.33, 8.33, 8.33 ms, segment median 8.33 ms

Hitches over 25 ms: 6 - warmup 0.1s 133.3ms; warmup 0.3s 141.6ms; magic 1.5s 29.9ms; combat 16.4s 36.6ms; third_person 43.9s 37.0ms; third_person 58.5s 38.1ms

## Cold-run-only hitches (first use: reported, not scored)

none

