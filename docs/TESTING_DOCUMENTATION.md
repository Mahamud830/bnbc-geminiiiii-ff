# Testing Documentation — v2.0.0

All 8 automated xUnit tests verify:
- All 64 districts and 4 seismic zones in BNBC 2020
- Mandatory geotechnical soil report input enforcement
- Seismic base shear & story lateral force distribution
- Wind velocity pressure across all 64 districts
- Duplicate-free load combinations with vertical seismic $E_v$
- Compliance statuses (`PASS`, `FAIL`, `WARNING`, `NOT_VERIFIED`)
- ETABS 22 push preview with frame wall loads and area slab loads
- Native `.pdf` and OpenXML `.xlsx` file generation
