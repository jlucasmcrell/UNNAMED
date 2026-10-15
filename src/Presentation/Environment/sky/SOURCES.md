# Sky textures: sources and licences

Made 2026-10-15 from public spacecraft and telescope imagery with ordinary image operations only (resample, flip, roll, latitude
remap, colour ramp, blend, crop). No generative tool touched any of them. The script is
`C:\Otherreach_Recovery\asset_work\sky\prep_sky.py`; the downloaded originals and their NASA metadata are kept in
`C:\Otherreach_Recovery\asset_work\sky\sources\`.

| File | Made from | Credit | Licence |
|---|---|---|---|
| `giant_bands.jpg` (4096x2048) | PIA07782, Cassini's cylindrical map of Jupiter (2000 flyby): north/south swapped, longitude rolled, latitudes re-banded, recoloured to a rust-ochre-cream ramp | NASA/JPL/Space Science Institute | Public domain (NASA/JPL media policy; credit requested) |
| `ring_profile.png` (2048x4) | PIA06175, Cassini "Panoramic Rings" natural-colour mosaic: one radial line through the ansa (C ring to F ring), colour and opacity | NASA/JPL/Space Science Institute | Public domain (as above) |
| `moon_pale.jpg` (2048x1024) | PIA14928 Rhea global map (2012) blended with PIA14931 Tethys global map (2012), mirrored and rolled | NASA/JPL-Caltech/Space Science Institute | Public domain (as above) |
| `moon_wisp.jpg` (2048x1024) | PIA12814 Dione global map (2010), mirrored and rolled, with Rhea's fine crater detail | NASA/JPL/Space Science Institute; NASA/JPL-Caltech/Space Science Institute | Public domain (as above) |
| `moon_first.jpg` (2048x1024) | PIA14931 Tethys global map modulated by PIA12814 Dione's albedo; replaces Earth's Moon on Sky3D's moon | NASA/JPL-Caltech/Space Science Institute; NASA/JPL/Space Science Institute | Public domain (as above) |
| `galaxy.jpg` (2048x2048) | heic0602a, Hubble's Pinwheel Galaxy (M101): centre crop, background taken to black, soft round edge | ESA/Hubble & NASA; Hubble data K.D. Kuntz, F. Bresolin, J. Trauger, J. Mould, Y.-H. Chu; CFHT/J.-C. Cuillandre/Coelum; NOAO/AURA/NSF G. Jacoby, B. Bohannan, M. Hanna | CC BY 4.0 (credit required in the game's notices) |

The night's star map stays Sky3D's ESO/S. Brunier Milky Way panorama (eso0932a, CC BY 4.0, credited in THIRD_PARTY_NOTICES as
`Milkyway_LICENSE.md`); Otherreach turns and mirrors it in the sky shader so its constellations are not Earth's.
