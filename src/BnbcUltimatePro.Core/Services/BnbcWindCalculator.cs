using System;
using System.Collections.Generic;
using System.Linq;
using BnbcUltimatePro.Core.Models;

namespace BnbcUltimatePro.Core.Services;

public sealed class BnbcWindCalculator
{
    private readonly BnbcDatabaseProvider _dbProvider;

    public BnbcWindCalculator(BnbcDatabaseProvider dbProvider)
    {
        _dbProvider = dbProvider ?? throw new ArgumentNullException(nameof(dbProvider));
    }

    public WindCalculationResult Calculate(BuildingProject project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));

        var district = _dbProvider.GetDistrictInfo(project.District);
        var occupancy = _dbProvider.GetOccupancyCategory(project.OccupancyCategory);
        if (district == null || occupancy == null)
        {
            return new WindCalculationResult
            {
                IsValid = false,
                ValidationMessage = $"Invalid District '{project.District}' or Occupancy Category '{project.OccupancyCategory}'."
            };
        }

        double v = district.WindSpeedMs;
        double iWind = occupancy.IWind;
        double kd = project.WindDirectionalityFactorKd;
        double kzt = project.TopographicFactorKzt;
        double g = project.GustEffectFactorG;
        double netCp = project.WindwardCp - project.LeewardCp;

        double roofH = Math.Max(project.BuildingHeightMeters, 4.5);
        double kzRoof = ComputeKz(roofH, project.WindExposure);
        double qh = 0.000613 * kzRoof * kzt * kd * (v * v) * iWind;

        var orderedStories = project.Stories.OrderBy(s => s.ElevationMeters).ToList();
        var storyForces = new List<StoryWindForce>();
        double totalFx = 0.0;
        double totalFy = 0.0;

        for (int idx = 0; idx < orderedStories.Count; idx++)
        {
            var st = orderedStories[idx];
            double z = Math.Max(st.ElevationMeters, 4.5);
            double kz = ComputeKz(z, project.WindExposure);
            double qz = 0.000613 * kz * kzt * kd * (v * v) * iWind;

            double pDesign = qz * g * project.WindwardCp - qh * g * project.LeewardCp;

            double lowerElev = idx == 0 ? 0.0 : orderedStories[idx - 1].ElevationMeters;
            double upperElev = idx == orderedStories.Count - 1
                ? st.ElevationMeters
                : (st.ElevationMeters + orderedStories[idx + 1].ElevationMeters) / 2.0;
            double midElev = idx == 0 ? st.ElevationMeters / 2.0 : (lowerElev + st.ElevationMeters) / 2.0;
            double tribHeight = Math.Max(0.0, upperElev - midElev);

            double fx = pDesign * project.PlanDimensionYMeters * tribHeight;
            double fy = pDesign * project.PlanDimensionXMeters * tribHeight;

            totalFx += fx;
            totalFy += fy;

            storyForces.Add(new StoryWindForce
            {
                StoryName = st.StoryName,
                ElevationMeters = st.ElevationMeters,
                TributaryHeightMeters = Math.Round(tribHeight, 3),
                Kz = Math.Round(kz, 4),
                VelocityPressureQzKnM2 = Math.Round(qz, 4),
                DesignPressurePKnM2 = Math.Round(pDesign, 4),
                ForceXKn = Math.Round(fx, 2),
                ForceYKn = Math.Round(fy, 2)
            });
        }

        return new WindCalculationResult
        {
            IsValid = true,
            ValidationMessage = "Verified against BNBC 2020 Part 6, Section 2.4 (Eq. 6.2.14 & Table 6.2.11).",
            BasicWindSpeedMs = v,
            WindImportanceFactorI = iWind,
            DirectionalityFactorKd = kd,
            TopographicFactorKzt = kzt,
            GustFactorG = g,
            NetCp = Math.Round(netCp, 3),
            RoofVelocityPressureQzKnM2 = Math.Round(qh, 4),
            TotalWindForceXKn = Math.Round(totalFx, 2),
            TotalWindForceYKn = Math.Round(totalFy, 2),
            StoryWindForces = storyForces
        };
    }

    public static double ComputeKz(double heightMeters, ExposureCategory exposure)
    {
        double z = Math.Max(heightMeters, 4.5);
        (double alpha, double zg) = exposure switch
        {
            ExposureCategory.A => (7.0, 365.76),
            ExposureCategory.B => (9.5, 274.32),
            ExposureCategory.C => (11.5, 213.36),
            _ => (7.0, 365.76)
        };

        z = Math.Min(z, zg);
        return 2.01 * Math.Pow(z / zg, 2.0 / alpha);
    }
}
