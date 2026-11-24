# BNBC ULTIMATE PRO v2.0 — All 64 Districts, PDF/Excel Export & ETABS 22 Suite

**BNBC ULTIMATE PRO v2.0** includes:
1. **Complete 64 Districts BNBC 2020 Database:** Verified Seismic Zones (Table 6.2.15) and Basic Wind Speeds (Table 6.2.8) for all 64 districts across all 8 divisions of Bangladesh.
2. **Direct Frame & Area Load Assignment in ETABS 22:** Assign computed masonry partition wall line loads (`kN/m`) directly to beam groups and floor SDL/LIVE uniform loads (`kN/m²`) directly to slab groups in ETABS 22 alongside load patterns, mass source, and combinations.
3. **Native PDF (`.pdf`) & Excel (`.xlsx`) Report Export:** Export multi-page engineering calculation reports directly to `.pdf` and multi-sheet workbooks (`BNBC_Summary`, `Lateral_Forces`, `Foundation_Reactions`) to `.xlsx` with zero external runtime dependencies.

## Exact GitHub Upload & Automated Build Steps
1. Extract `BNBC_ULTIMATE_PRO_v2_GITHUB_PACKAGE.zip` on your PC.
2. Upload all extracted files and folders (including `.github/`) into the **root** of a new GitHub repository and commit to `main`.
3. Open the **Actions** tab -> **Windows Build & Installer Pipeline** -> download **`BNBC_Ultimate_Pro_Setup_Windows_x64`** from Artifacts and extract `BNBC_Ultimate_Pro_Setup.exe`.
