# RC d9 interim frontier NPC attire

Owner ruling, 2026-10-24: villagers dress for their occupation and station. This replaces wardrobe03 and the cancelled wardrobe04 selections. Mixed-era clothes remain available for deliberately appropriate backgrounds; they are not the village default.

Status: all fourteen native exports and asset/motion/cache gates pass. Fresh source-game frames are in `rcd9_review08`; Claude visual acceptance is pending. This catalogue pass is interim, not period-authentic garments. No bright blue, sneakers, kimono/silk robe, flight suit, CEO outfit, business suit or ties. Outdoor workers wear practical laced leather boots. The catalogue calls those boots “Pirate Boots”; no pirate hat or costume is used.

| NPC | Job → selected pieces | Actual baked clothing colours (sRGB targets) |
| --- | --- | --- |
| npc_kera_voss | Forge smith: heavy plain pullover, work trousers and laced leather boots; no pressed jacket (`outfit_f_casual_weekend_warrior`) | Skinny Jeans: #4F4437; Sweater 2: #746B60; Pirate Boots: #4E3828 |
| npc_renn_vale | Outdoor waystation steward: weather coat, dark trousers and leather boots (`outfit_m_winter_frosty_evening`) | Parka: #655443; Skinny Jeans: #414038; TSHIRT Masked: #B6AA91; Pirate Boots: #4D3526 |
| npc_sel_arien | Archivist: plain collared shirt and trousers in dark quiet tones; no suit jacket, tie or office ensemble (`outfit_f_office_summer_lawyer`) | Dress Pants: #424844; Dress Shirt: #5C5C53; Pirate Boots: #49372E |
| npc_tavar_orr | Travelling guide: moss weather coat, earth trousers and leather boots (`outfit_m_winter_frosty_evening`) | Parka: #62694B; Skinny Jeans: #514A3D; TSHIRT Masked: #B7AA8F; Pirate Boots: #52392A |
| npc_mara_ling | Well keeper / herb gatherer: practical moss overshirt, linen underlayer, earth trousers and boots; avoid collapsed native skirt (`outfit_f_casual_stylish_casual`) | Flannel: #73765A; Skinny Jeans: #5A5141; TSHIRT: #B6AA8F; Pirate Boots: #604733 |
| npc_berrin_garth | Lodgekeeper: undyed wool pullover, loose earth trousers, boots for yard work (`outfit_m_extra_outfits_pack_relaxed_fit`) | Baggy Jeans: #5A4B3C; Sweater 2: #A89A80; Pirate Boots: #503A2A |
| npc_durn_skell | Cairnwold smith: charcoal overshirt, dark work trousers, brown undershirt and boots (`outfit_m_extra_outfits_pack_on_the_road`) | Baggy Jeans: #484036; Flannel: #55534A; TSHIRT: #8E7962; Pirate Boots: #493126 |
| npc_tam_crowden | Granary keeper: oxblood heavy pullover, work trousers and boots; avoid collapsed native skirt (`outfit_f_casual_weekend_warrior`) | Skinny Jeans: #554A3C; Sweater 2: #76564E; Pirate Boots: #503B2B |
| npc_pool_00 | Older village worker: plain undyed collared work shirt, earth trousers and boots; no suit or tie; avoid collapsed native skirt (`outfit_f_office_stock_exchange`) | Chinos: #5C5041; Dress Shirt: #99876D; Pirate Boots: #604632 |
| npc_pool_01 | Outdoor labourer: faded olive overshirt, brown trousers, coarse cream underlayer and boots (`outfit_m_extra_outfits_pack_on_the_road`) | Baggy Jeans: #655341; Flannel: #777558; TSHIRT: #B4A68C; Pirate Boots: #5E422D |
| npc_pool_02 | Village worker: plain loose collared work shirt and trousers; no tie or suit (`outfit_f_office_stock_exchange`) | Chinos: #5E5A48; Dress Shirt: #A69A7F; Pirate Boots: #59412D |
| npc_pool_03 | Field labourer: grey wool pullover, loose brown trousers and work boots (`outfit_m_extra_outfits_pack_relaxed_fit`) | Baggy Jeans: #6D5B46; Sweater 2: #72716B; Pirate Boots: #523B2A |
| npc_pool_04 | Outdoor village worker: muted brown pullover, earth work trousers and boots (`outfit_f_casual_weekend_warrior`) | Skinny Jeans: #5C5844; Sweater 2: #8D7460; Pirate Boots: #5B3E2C |
| npc_pool_05 | Older village tradesman: plain faded grey shirt and earth trousers; no tie, jacket or business suit (`outfit_m_office_summer_lawyer`) | Dress Pants: #5C4F40; Dress Shirt: #8D8A7D; Pirate Boots: #503A2B |

Some catalogue cuts still have modern seams or trouser construction. Muted material controls cannot make these authentic period garments; the shared frontier wardrobe track replaces them later. Reuse of practical cuts is intentional. Distinct palette/ensemble combinations and existing hair distinguish NPCs; uniqueness must not force unsuitable shoes or costumes. Each ensemble differs from both player garment sets.

Colours use the documented HumGen clothing colour fields (`HG_Control` material inputs), before the native bake/export. Record intended sRGB and actual linear material values per garment, including trousers, undershirts and footwear. No runtime colour multiplier, new body shader, mesh or skeleton edits. Preserve identity/age/hair and exact native export/motion/cache checks.

Kera (Kal), Sel (Siann) and Tavar (Orenth) currently retain their previous human-shaped reference bodies for this interim attire inspection. DeepSeek owns their named race-body production. Integrate those new bodies only with their production receipts and rebuilt exact-target motion; inspect stature, camera, sit/lie and reach. The heritage session owns `people_ref` content edits; coordinate through CODEX_PROGRESS.md rather than overwrite its work.

Future period garments use the shared HumGen frontier wardrobe / armour track in `HUMGEN_WORN_ARMOUR_PLAN.md`. Owned Fab/Cosmos/Sourced meshes are source art, never inputs to generative AI.

The exact final payload is `rcd9_runtime05/RC_D9_PAYLOAD_RECEIPT.json`. Raw review PNGs and body assets are backed in `G:/My Drive/OTHERREACH_RESTORE/RC_D9_FRONTIER_ATTIRE_20261024`; the development root uses directory junctions and is not a portable package. The accepted RC d8 build remains intact.

Native long-dress leg/hem deformation was rejected in review07. Mara, Tam and pool00 use practical work tops/trousers in the final review08 pass. Collared shirts use the separate HG_Control Buttons colour field, warm neutral #6D6455; the earlier main fabric _C0 input alone left cyan buttons. No body, joint, bind or animation edits were used.
