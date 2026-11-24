using System;
using System.Collections.Generic;
using BnbcUltimatePro.Core.Models;

namespace BnbcUltimatePro.Core.Services;

public sealed class BnbcGravityAndComboService
{
    public DeadLoadResult CalculateFloorDeadLoad(
        double slabThicknessMm,
        double floorFinishKnM2,
        double partitionAllowanceKnM2,
        double mepAllowanceKnM2 = 0.25,
        double concreteDensityKnM3 = 24.0)
    {
        double thicknessM = Math.Max(0, slabThicknessMm) / 1000.0;
        double selfWeight = thicknessM * concreteDensityKnM3;
        double total = selfWeight + Math.Max(0, floorFinishKnM2) + Math.Max(0, partitionAllowanceKnM2) + Math.Max(0, mepAllowanceKnM2);

        return new DeadLoadResult
        {
            SlabSelfWeightKnM2 = Math.Round(selfWeight, 3),
            FloorFinishKnM2 = Math.Round(floorFinishKnM2, 3),
            PartitionAllowanceKnM2 = Math.Round(partitionAllowanceKnM2, 3),
            MepAllowanceKnM2 = Math.Round(mepAllowanceKnM2, 3),
            TotalDeadLoadKnM2 = Math.Round(total, 3),
            FormulaBreakdown = $"Slab ({thicknessM:F3}m x {concreteDensityKnM3:F1} = {selfWeight:F2}) + FF ({floorFinishKnM2:F2}) + PW ({partitionAllowanceKnM2:F2}) + MEP ({mepAllowanceKnM2:F2}) = {total:F2} kN/m²"
        };
    }

    public WallLoadResult CalculateWallLineLoad(
        double storyHeightMeters,
        double beamDepthMeters,
        double wallThicknessMm,
        double plasterThicknessMm = 25.0,
        double openingPercentage = 0.0,
        double brickDensityKnM3 = 19.2,
        double plasterDensityKnM3 = 20.4)
    {
        double clearWallHeight = Math.Max(0, storyHeightMeters - beamDepthMeters);
        double wallThickM = Math.Max(0, wallThicknessMm) / 1000.0;
        double plasterThickM = Math.Max(0, plasterThicknessMm) / 1000.0;

        double brickWeightPerM2 = wallThickM * brickDensityKnM3;
        double plasterWeightPerM2 = plasterThickM * plasterDensityKnM3;
        double grossLineLoad = (brickWeightPerM2 + plasterWeightPerM2) * clearWallHeight;

        double clampedOpening = Math.Clamp(openingPercentage, 0.0, 80.0);
        double netLineLoad = grossLineLoad * (1.0 - clampedOpening / 100.0);

        return new WallLoadResult
        {
            WallHeightMeters = Math.Round(clearWallHeight, 3),
            WallThicknessMeters = Math.Round(wallThickM, 4),
            PlasterThicknessMeters = Math.Round(plasterThickM, 4),
            OpeningPercentage = clampedOpening,
            GrossLineLoadKnPerM = Math.Round(grossLineLoad, 2),
            NetLineLoadKnPerM = Math.Round(netLineLoad, 2)
        };
    }

    public List<LoadCombinationDefinition> GenerateBnbc2020Combinations(
        double evFactor,
        double liveLoadFactorForSeismic = 0.5,
        bool includeWind = true,
        bool includeSeismic = true,
        bool includeServiceability = true)
    {
        var combos = new List<LoadCombinationDefinition>
        {
            new()
            {
                Name = "BNBC_U1_1.4D",
                DesignType = "Strength (USD)",
                ClauseReference = "BNBC 2020 Sec 2.7.3.1 (1)",
                Factors = new Dictionary<string, double> { ["DEAD"] = 1.4, ["SDL"] = 1.4, ["WALL"] = 1.4 }
            },
            new()
            {
                Name = "BNBC_U2_1.2D+1.6L+0.5Lr",
                DesignType = "Strength (USD)",
                ClauseReference = "BNBC 2020 Sec 2.7.3.1 (2)",
                Factors = new Dictionary<string, double> { ["DEAD"] = 1.2, ["SDL"] = 1.2, ["WALL"] = 1.2, ["LIVE"] = 1.6, ["ROOF_LIVE"] = 0.5 }
            },
            new()
            {
                Name = "BNBC_U3_1.2D+1.6Lr+f1L",
                DesignType = "Strength (USD)",
                ClauseReference = "BNBC 2020 Sec 2.7.3.1 (3)",
                Factors = new Dictionary<string, double> { ["DEAD"] = 1.2, ["SDL"] = 1.2, ["WALL"] = 1.2, ["ROOF_LIVE"] = 1.6, ["LIVE"] = liveLoadFactorForSeismic }
            }
        };

        if (includeWind)
        {
            string[] windDirs = { "WX+", "WX-", "WY+", "WY-" };
            foreach (var w in windDirs)
            {
                combos.Add(new LoadCombinationDefinition
                {
                    Name = $"BNBC_U4_1.2D+1.6{w}+f1L+0.5Lr",
                    DesignType = "Strength (USD)",
                    ClauseReference = "BNBC 2020 Sec 2.7.3.1 (4)",
                    Factors = new Dictionary<string, double>
                    {
                        ["DEAD"] = 1.2, ["SDL"] = 1.2, ["WALL"] = 1.2,
                        [w] = 1.6, ["LIVE"] = liveLoadFactorForSeismic, ["ROOF_LIVE"] = 0.5
                    }
                });

                combos.Add(new LoadCombinationDefinition
                {
                    Name = $"BNBC_U6_0.9D+1.6{w}",
                    DesignType = "Strength (USD)",
                    ClauseReference = "BNBC 2020 Sec 2.7.3.1 (6)",
                    Factors = new Dictionary<string, double>
                    {
                        ["DEAD"] = 0.9, ["SDL"] = 0.9, ["WALL"] = 0.9,
                        [w] = 1.6
                    }
                });
            }
        }

        if (includeSeismic)
        {
            double deadPlusEv = Math.Round(1.2 + evFactor, 4);
            double deadMinusEv = Math.Round(0.9 - evFactor, 4);
            string[] eqDirs = { "EX+", "EX-", "EY+", "EY-" };

            foreach (var eq in eqDirs)
            {
                combos.Add(new LoadCombinationDefinition
                {
                    Name = $"BNBC_U5_({deadPlusEv:F3})D+1.0{eq}+f1L",
                    DesignType = "Strength (USD)",
                    ClauseReference = "BNBC 2020 Sec 2.7.3.1 (5) & Sec 2.5.5.2",
                    Factors = new Dictionary<string, double>
                    {
                        ["DEAD"] = deadPlusEv, ["SDL"] = deadPlusEv, ["WALL"] = deadPlusEv,
                        [eq] = 1.0, ["LIVE"] = liveLoadFactorForSeismic
                    }
                });

                combos.Add(new LoadCombinationDefinition
                {
                    Name = $"BNBC_U7_({deadMinusEv:F3})D+1.0{eq}",
                    DesignType = "Strength (USD)",
                    ClauseReference = "BNBC 2020 Sec 2.7.3.1 (7) & Sec 2.5.5.2",
                    Factors = new Dictionary<string, double>
                    {
                        ["DEAD"] = deadMinusEv, ["SDL"] = deadMinusEv, ["WALL"] = deadMinusEv,
                        [eq] = 1.0
                    }
                });
            }
        }

        if (includeServiceability)
        {
            combos.Add(new LoadCombinationDefinition
            {
                Name = "BNBC_S1_D",
                DesignType = "Serviceability (ASD)",
                ClauseReference = "BNBC 2020 Sec 2.7.2.1 (1)",
                Factors = new Dictionary<string, double> { ["DEAD"] = 1.0, ["SDL"] = 1.0, ["WALL"] = 1.0 }
            });

            combos.Add(new LoadCombinationDefinition
            {
                Name = "BNBC_S2_D+L",
                DesignType = "Serviceability (ASD)",
                ClauseReference = "BNBC 2020 Sec 2.7.2.1 (2)",
                Factors = new Dictionary<string, double> { ["DEAD"] = 1.0, ["SDL"] = 1.0, ["WALL"] = 1.0, ["LIVE"] = 1.0, ["ROOF_LIVE"] = 1.0 }
            });

            if (includeWind)
            {
                foreach (var w in new[] { "WX+", "WX-", "WY+", "WY-" })
                {
                    combos.Add(new LoadCombinationDefinition
                    {
                        Name = $"BNBC_S5_D+0.75L+0.75(0.6{w})",
                        DesignType = "Serviceability (ASD)",
                        ClauseReference = "BNBC 2020 Sec 2.7.2.1 (6)",
                        Factors = new Dictionary<string, double>
                        {
                            ["DEAD"] = 1.0, ["SDL"] = 1.0, ["WALL"] = 1.0,
                            ["LIVE"] = 0.75, [w] = 0.45
                        }
                    });
                }
            }

            if (includeSeismic)
            {
                double sDeadPlusEv = Math.Round(1.0 + 0.7 * evFactor, 4);
                foreach (var eq in new[] { "EX+", "EX-", "EY+", "EY-" })
                {
                    combos.Add(new LoadCombinationDefinition
                    {
                        Name = $"BNBC_S6_({sDeadPlusEv:F3})D+0.75L+0.525{eq}",
                        DesignType = "Serviceability (ASD)",
                        ClauseReference = "BNBC 2020 Sec 2.7.2.1 (6) & Sec 2.5.5.2",
                        Factors = new Dictionary<string, double>
                        {
                            ["DEAD"] = sDeadPlusEv, ["SDL"] = sDeadPlusEv, ["WALL"] = sDeadPlusEv,
                            ["LIVE"] = 0.75, [eq] = 0.525
                        }
                    });
                }
            }
        }

        return combos;
    }
}
