# Developer & CI/CD Build Guide (v2.0.0)

- `BnbcUltimatePro.sln` — Root .NET 8 solution
- `src/BnbcUltimatePro.Core` — 64-district BNBC 2020 database, seismic/wind/gravity/wall engines, native ISO PDF 1.4 writer, and OpenXML `.xlsx` workbook writer
- `src/BnbcUltimatePro.EtabsIntegration` — Runtime COM connector for ETABS 22 (`ETABSv1.dll`) supporting patterns, mass source, frame distributed loads, area uniform loads, combinations, and base joint reaction extraction
- `src/BnbcUltimatePro.Desktop` — WPF desktop application
- `tests/BnbcUltimatePro.Tests` — Automated xUnit verification suite
