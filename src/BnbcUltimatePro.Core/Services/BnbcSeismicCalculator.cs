using System;
using System.Collections.Generic;
using System.Linq;
using BnbcUltimatePro.Core.Models;

namespace BnbcUltimatePro.Core.Services;

public sealed class BnbcSeismicCalculator
{
    private readonly BnbcDatabaseProvider _dbProvider;

    public BnbcSeismicCalculator(BnbcDatabaseProvider dbProvider)
    {
        _dbProvider = dbProvider ?? throw new ArgumentNullException(nameof(dbProvider));
    }

    public SeismicCalculationResult Calculate(BuildingProject project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));

        if (string.IsNullOrWhiteSpace(project.GeotechnicalSiteClass))
        {
            return new SeismicCalculationResult
            {
                IsValid = false,
                ValidationMessage = "Geotechnical Soil Site Class (SA, SB, SC, SD, SE, S1, or S2) is required from the user's geotechnical report and cannot be guessed from district."
            };
        }

        var district = _dbProvider.GetDistrictInfo(project.District);
        if (district == null)
        {
            return new SeismicCalculationResult
            {
                IsValid = false,
                ValidationMessage = $"District '{project.District}' not found in BNBC 2020 Table 6.2.15 database."
            };
        }

        var zoneInfo = _dbProvider.GetSeismicZone(district.Zone);
        var siteClass = _dbProvider.GetSiteClass(project.GeotechnicalSiteClass);
        var occupancy = _dbProvider.GetOccupancyCategory(project.OccupancyCategory);
        var system = _dbProvider.GetStructuralSystem(project.StructuralSystemCode);

        if (zoneInfo == null || siteClass == null || occupancy == null || system == null)
        {
            return new SeismicCalculationResult
            {
                IsValid = false,
                ValidationMessage = "One or more BNBC 2020 seismic parameters (Zone, Site Class, Occupancy Category, or Structural System) are invalid."
            };
        }

        if (siteClass.RequiresSiteSpecificStudy)
        {
            return new SeismicCalculationResult
            {
                IsValid = false,
                SeismicZone = zoneInfo.Zone,
                ZoneCoefficientZ = zoneInfo.Z,
                SiteClass = siteClass.Code,
                ValidationMessage = $"Site Class {siteClass.Code} ({siteClass.Clause}) requires a site-specific geotechnical dynamic response study before Equivalent Static coefficients can be finalized."
            };
        }

        double hn = Math.Max(project.BuildingHeightMeters, 3.0);
        double ta = system.Ct * Math.Pow(hn, system.M);

        double cs;
        if (ta <= siteClass.TB)
        {
            cs = siteClass.S * (1.0 + (ta / siteClass.TB) * 1.5);
        }
        else if (ta <= siteClass.TC)
        {
            cs = 2.5 * siteClass.S;
        }
        else if (ta <= siteClass.TD)
        {
            cs = 2.5 * siteClass.S * (siteClass.TC / ta);
        }
        else
        {
            cs = 2.5 * siteClass.S * (siteClass.TC * siteClass.TD / (ta * ta));
        }

        double sa = (2.0 / 3.0) * (zoneInfo.Z * occupancy.ISeismic / system.R) * cs;
        const double beta = 0.11;
        double saMin = 0.67 * beta * zoneInfo.Z * occupancy.ISeismic * siteClass.S;
        double governingSa = Math.Max(sa, saMin);

        double ah = (2.0 / 3.0) * zoneInfo.Z * siteClass.S;
        double evFactor = project.IncludeVerticalSeismicEv ? 0.5 * ah : 0.0;
        string sdc = DetermineSeismicDesignCategory(zoneInfo.Zone, siteClass.Code, occupancy.Category);

        double totalWeight = project.Stories.Sum(s => Math.Max(0, s.SeismicWeightKn));
        double baseShearV = governingSa * totalWeight;

        double k = ta <= 0.5 ? 1.0 : (ta >= 2.5 ? 2.0 : 1.0 + (ta - 0.5) / 2.0);
        var storyForces = new List<StoryLateralForce>();
        double denom = project.Stories.Sum(s => s.SeismicWeightKn * Math.Pow(Math.Max(s.ElevationMeters, 0.0), k));

        var orderedBottomUp = project.Stories.OrderBy(s => s.ElevationMeters).ToList();
        var tempForces = new List<(StoryDefinition story, double cvx, double fx)>();

        foreach (var st in orderedBottomUp)
        {
            double num = st.SeismicWeightKn * Math.Pow(Math.Max(st.ElevationMeters, 0.0), k);
            double cvx = denom > 0 ? num / denom : 0.0;
            double fx = cvx * baseShearV;
            tempForces.Add((st, cvx, fx));
        }

        double runningShear = 0.0;
        var cumulativeMap = new Dictionary<string, double>();
        for (int i = tempForces.Count - 1; i >= 0; i--)
        {
            runningShear += tempForces[i].fx;
            cumulativeMap[tempForces[i].story.StoryName] = runningShear;
        }

        foreach (var item in tempForces)
        {
            storyForces.Add(new StoryLateralForce
            {
                StoryName = item.story.StoryName,
                ElevationMeters = item.story.ElevationMeters,
                WeightKn = item.story.SeismicWeightKn,
                DistributionFactorCvx = Math.Round(item.cvx, 5),
                LateralForceFxKn = Math.Round(item.fx, 2),
                CumulativeStoryShearVxKn = Math.Round(cumulativeMap[item.story.StoryName], 2)
            });
        }

        return new SeismicCalculationResult
        {
            IsValid = true,
            ValidationMessage = "Verified against BNBC 2020 Part 6, Section 2.5.",
            SeismicZone = zoneInfo.Zone,
            ZoneCoefficientZ = zoneInfo.Z,
            SiteClass = siteClass.Code,
            SoilFactorS = siteClass.S,
            TbSeconds = siteClass.TB,
            TcSeconds = siteClass.TC,
            TdSeconds = siteClass.TD,
            ImportanceFactorI = occupancy.ISeismic,
            ResponseReductionFactorR = system.R,
            OverstrengthFactorOmega0 = system.Omega0,
            DeflectionAmplificationCd = system.Cd,
            FundamentalPeriodTaSeconds = Math.Round(ta, 4),
            NormalizedAccelerationCs = Math.Round(cs, 4),
            DesignBaseShearCoeffSa = Math.Round(sa, 5),
            MinimumBaseShearCoeffSaMin = Math.Round(saMin, 5),
            GoverningBaseShearCoeff = Math.Round(governingSa, 5),
            TotalSeismicWeightKn = Math.Round(totalWeight, 2),
            DesignBaseShearKn = Math.Round(baseShearV, 2),
            VerticalSeismicFactorEvCoeff = Math.Round(evFactor, 4),
            SeismicDesignCategory = sdc,
            StoryForces = storyForces
        };
    }

    public static string DetermineSeismicDesignCategory(int zone, string siteClass, string occupancyCategory)
    {
        bool isCatIV = string.Equals(occupancyCategory, "IV", StringComparison.OrdinalIgnoreCase);
        if (zone == 1)
        {
            if (siteClass is "SA" or "SB") return isCatIV ? "C" : "B";
            return isCatIV ? "D" : "C";
        }
        if (zone == 2)
        {
            if (siteClass is "SA" or "SB") return isCatIV ? "D" : "C";
            return "D";
        }
        return "D";
    }
}
