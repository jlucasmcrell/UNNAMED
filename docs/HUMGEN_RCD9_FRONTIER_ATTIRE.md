# RC d9 interim frontier NPC attire

Owner ruling, 2026-10-24: villagers dress for their occupation and station. This replaces wardrobe03 and the cancelled wardrobe04 selections. Mixed-era clothes remain available for deliberately appropriate backgrounds; they are not the village default.

Status: native exports and source-game review pending. This catalogue pass is interim, not period-authentic garments. No bright blue, sneakers, kimono/silk robe, flight suit, CEO outfit, business suit or ties. Outdoor workers wear practical laced leather boots. The catalogue calls those boots “Pirate Boots”; no pirate hat or costume is used.

| NPC | Job → selected pieces | Colours (top / trousers / underlayer / boots) |
| --- | --- | --- |
| npc_kera_voss | Forge smith: heavy plain pullover, work trousers and laced leather boots; no pressed jacket (`outfit_f_casual_weekend_warrior`) | #746B60 / #4F4437 / #B2A58C / #4E3828 |
| npc_renn_vale | Outdoor waystation steward: weather coat, dark trousers and leather boots (`outfit_m_winter_frosty_evening`) | #655443 / #414038 / #B6AA91 / #4D3526 |
| npc_sel_arien | Archivist: plain collared shirt and trousers in dark quiet tones; no suit jacket, tie or office ensemble (`outfit_f_office_summer_lawyer`) | #5C5C53 / #424844 / #A49A84 / #49372E |
| npc_tavar_orr | Travelling guide: moss weather coat, earth trousers and leather boots (`outfit_m_winter_frosty_evening`) | #62694B / #514A3D / #B7AA8F / #52392A |
| npc_mara_ling | Well keeper and herb gatherer: simple calf-length dress in faded moss, practical boots (`outfit_f_extra_outfits_pack_dress`) | #73765A / #5A5141 / #B6AA8F / #604733 |
| npc_berrin_garth | Lodgekeeper: undyed wool pullover, loose earth trousers, boots for yard work (`outfit_m_extra_outfits_pack_relaxed_fit`) | #A89A80 / #5A4B3C / #B5A88F / #503A2A |
| npc_durn_skell | Cairnwold smith: charcoal overshirt, dark work trousers, brown undershirt and boots (`outfit_m_extra_outfits_pack_on_the_road`) | #55534A / #484036 / #8E7962 / #493126 |
| npc_tam_crowden | Granary keeper: simple oxblood work dress and leather boots, tied hair retained (`outfit_f_extra_outfits_pack_dress`) | #76564E / #554A3C / #AC9B81 / #503B2B |
| npc_pool_00 | Older village worker: undyed taupe everyday dress and boots (`outfit_f_extra_outfits_pack_dress`) | #99876D / #5C5041 / #B7A88E / #604632 |
| npc_pool_01 | Outdoor labourer: faded olive overshirt, brown trousers, coarse cream underlayer and boots (`outfit_m_extra_outfits_pack_on_the_road`) | #777558 / #655341 / #B4A68C / #5E422D |
| npc_pool_02 | Village worker: plain loose collared work shirt and trousers; no tie or suit (`outfit_f_office_stock_exchange`) | #A69A7F / #5E5A48 / #B5A78D / #59412D |
| npc_pool_03 | Field labourer: grey wool pullover, loose brown trousers and work boots (`outfit_m_extra_outfits_pack_relaxed_fit`) | #72716B / #6D5B46 / #B4A88F / #523B2A |
| npc_pool_04 | Outdoor village worker: muted brown pullover, earth work trousers and boots (`outfit_f_casual_weekend_warrior`) | #8D7460 / #5C5844 / #B6A88D / #5B3E2C |
| npc_pool_05 | Older village tradesman: plain faded grey shirt and earth trousers; no tie, jacket or business suit (`outfit_m_office_summer_lawyer`) | #8D8A7D / #5C4F40 / #B7AA90 / #503A2B |

Some catalogue cuts still have modern seams or trouser construction. Muted material controls cannot make these authentic period garments; the shared frontier wardrobe track replaces them later. Reuse of practical cuts is intentional. Distinct palette/ensemble combinations and existing hair distinguish NPCs; uniqueness must not force unsuitable shoes or costumes. Each ensemble differs from both player garment sets.

Colours use the documented HumGen clothing colour fields (`HG_Control` material inputs), before the native bake/export. Record intended sRGB and actual linear material values per garment, including trousers, undershirts and footwear. No runtime colour multiplier, new body shader, mesh or skeleton edits. Preserve identity/age/hair and exact native export/motion/cache checks.

Kera (Kal), Sel (Siann) and Tavar (Orenth) currently retain their previous human-shaped reference bodies for this interim attire inspection. DeepSeek owns their named race-body production. Integrate those new bodies only with their production receipts and rebuilt exact-target motion; inspect stature, camera, sit/lie and reach. The heritage session owns `people_ref` content edits; coordinate through CODEX_PROGRESS.md rather than overwrite its work.

Future period garments use the shared HumGen frontier wardrobe / armour track in `HUMGEN_WORN_ARMOUR_PLAN.md`. Owned Fab/Cosmos/Sourced meshes are source art, never inputs to generative AI.
