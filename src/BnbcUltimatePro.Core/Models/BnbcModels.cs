using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BnbcUltimatePro.Core.Models;

public enum ComplianceStatus
{
    PASS,
    FAIL,
    WARNING,
    NOT_VERIFIED
}

public enum ExposureCategory
{
    A,
    B,
    C
}

public enum WindAnalysisMethod
{
    SimplifiedEnvelope,
    DirectionalAnalytical
}

public sealed class BnbcDatabase
{
    public string CodeReference { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Disclaimer { get; set; } = string.Empty;
    public List<SeismicZoneInfo> SeismicZones { get; set; } = new();
    public List<DistrictZoneInfo> DistrictZones { get; set; } = new();
    public List<SiteClassInfo> SiteClasses { get; set; } = new();
    public List<OccupancyCategoryInfo> OccupancyCategories { get; set; } = new();
    public List<StructuralSystemInfo> StructuralSystems { get; set; } = new();
    public List<UnitWeightInfo> UnitWeights { get; set; } = new();
    public List<LiveLoadInfo> LiveLoads { get; set; } = new();
}

public sealed class SeismicZoneInfo
{
    public int Zone { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Z { get; set; }
    public string Clause { get; set; } = string.Empty;
}

public sealed class DistrictZoneInfo
{
    public string District { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public int Zone { get; set; }
    public double WindSpeedMs { get; set; }
    public string Clause { get; set; } = string.Empty;
}

public sealed class SiteClassInfo
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double S { get; set; }
    [JsonPropertyName("TB")]
    public double TB { get; set; }
    [JsonPropertyName("TC")]
    public double TC { get; set; }
    [JsonPropertyName("TD")]
    public double TD { get; set; }
    public bool RequiresSiteSpecificStudy { get; set; }
    public string Clause { get; set; } = string.Empty;
}

public sealed class OccupancyCategoryInfo
{
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [JsonPropertyName("I_seismic")]
    public double ISeismic { get; set; }
    [JsonPropertyName("I_wind")]
    public double IWind { get; set; }
    public string Clause { get; set; } = string.Empty;
}

public sealed class StructuralSystemInfo
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double R { get; set; }
    public double Omega0 { get; set; }
    public double Cd { get; set; }
    public double Ct { get; set; }
    [JsonPropertyName("m")]
    public double M { get; set; }
    public string Clause { get; set; } = string.Empty;
}

public sealed class UnitWeightInfo
{
    public string Material { get; set; } = string.Empty;
    public double UnitWeightKnM3 { get; set; }
    public string Clause { get; set; } = string.Empty;
}

public sealed class LiveLoadInfo
{
    public string Occupancy { get; set; } = string.Empty;
    public double UniformKnM2 { get; set; }
    public double ConcentratedKn { get; set; }
    public string Clause { get; set; } = string.Empty;
}

public sealed class StoryDefinition
{
    public string StoryName { get; set; } = string.Empty;
    public double HeightMeters { get; set; }
    public double ElevationMeters { get; set; }
    public double FloorAreaSqM { get; set; }
    public string OccupancyType { get; set; } = "Residential - Private Rooms & Bedrooms";
    public double AssignedLiveLoadKnM2 { get; set; } = 2.0;
    public double AssignedSuperDeadLoadKnM2 { get; set; } = 1.5;
    public double SeismicWeightKn { get; set; }
    public bool IsRoof { get; set; }
    public bool IsSoftStoryFlagged { get; set; }
}

public sealed class BuildingProject
{
    public string ProjectName { get; set; } = "New BNBC 2020 Project";
    public string ProjectNumber { get; set; } = "BNBC-2026-001";
    public string EngineerName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string District { get; set; } = "Dhaka";
    public string GeotechnicalSiteClass { get; set; } = string.Empty;
    public string GeotechnicalReportRef { get; set; } = string.Empty;
    public string OccupancyCategory { get; set; } = "II";
    public string StructuralSystemCode { get; set; } = "IMRF_RC";
    public ExposureCategory WindExposure { get; set; } = ExposureCategory.A;
    public WindAnalysisMethod WindMethod { get; set; } = WindAnalysisMethod.DirectionalAnalytical;
    public double BuildingHeightMeters { get; set; } = 21.0;
    public double PlanDimensionXMeters { get; set; } = 18.0;
    public double PlanDimensionYMeters { get; set; } = 15.0;
    public double TopographicFactorKzt { get; set; } = 1.0;
    public double WindDirectionalityFactorKd { get; set; } = 0.85;
    public double GustEffectFactorG { get; set; } = 0.85;
    public double WindwardCp { get; set; } = 0.8;
    public double LeewardCp { get; set; } = -0.5;
    public bool IncludeVerticalSeismicEv { get; set; } = true;
    public bool HasTorsionalIrregularity { get; set; } = false;
    public bool HasSoftStoryIrregularity { get; set; } = false;
    public List<StoryDefinition> Stories { get; set; } = new();
}

public sealed class SeismicCalculationResult
{
    public bool IsValid { get; set; }
    public string ValidationMessage { get; set; } = string.Empty;
    public int SeismicZone { get; set; }
    public double ZoneCoefficientZ { get; set; }
    public string SiteClass { get; set; } = string.Empty;
    public double SoilFactorS { get; set; }
    public double TbSeconds { get; set; }
    public double TcSeconds { get; set; }
    public double TdSeconds { get; set; }
    public double ImportanceFactorI { get; set; }
    public double ResponseReductionFactorR { get; set; }
    public double OverstrengthFactorOmega0 { get; set; }
    public double DeflectionAmplificationCd { get; set; }
    public double FundamentalPeriodTaSeconds { get; set; }
    public double NormalizedAccelerationCs { get; set; }
    public double DesignBaseShearCoeffSa { get; set; }
    public double MinimumBaseShearCoeffSaMin { get; set; }
    public double GoverningBaseShearCoeff { get; set; }
    public double TotalSeismicWeightKn { get; set; }
    public double DesignBaseShearKn { get; set; }
    public double VerticalSeismicFactorEvCoeff { get; set; }
    public string SeismicDesignCategory { get; set; } = string.Empty;
    public List<StoryLateralForce> StoryForces { get; set; } = new();
}

public sealed class StoryLateralForce
{
    public string StoryName { get; set; } = string.Empty;
    public double ElevationMeters { get; set; }
    public double WeightKn { get; set; }
    public double DistributionFactorCvx { get; set; }
    public double LateralForceFxKn { get; set; }
    public double CumulativeStoryShearVxKn { get; set; }
}

public sealed class WindCalculationResult
{
    public bool IsValid { get; set; }
    public string ValidationMessage { get; set; } = string.Empty;
    public double BasicWindSpeedMs { get; set; }
    public double WindImportanceFactorI { get; set; }
    public double DirectionalityFactorKd { get; set; }
    public double TopographicFactorKzt { get; set; }
    public double GustFactorG { get; set; }
    public double NetCp { get; set; }
    public double RoofVelocityPressureQzKnM2 { get; set; }
    public double TotalWindForceXKn { get; set; }
    public double TotalWindForceYKn { get; set; }
    public List<StoryWindForce> StoryWindForces { get; set; } = new();
}

public sealed class StoryWindForce
{
    public string StoryName { get; set; } = string.Empty;
    public double ElevationMeters { get; set; }
    public double TributaryHeightMeters { get; set; }
    public double Kz { get; set; }
    public double VelocityPressureQzKnM2 { get; set; }
    public double DesignPressurePKnM2 { get; set; }
    public double ForceXKn { get; set; }
    public double ForceYKn { get; set; }
}

public sealed class DeadLoadResult
{
    public double SlabSelfWeightKnM2 { get; set; }
    public double FloorFinishKnM2 { get; set; }
    public double PartitionAllowanceKnM2 { get; set; }
    public double MepAllowanceKnM2 { get; set; }
    public double TotalDeadLoadKnM2 { get; set; }
    public string FormulaBreakdown { get; set; } = string.Empty;
}

public sealed class WallLoadResult
{
    public double WallHeightMeters { get; set; }
    public double WallThicknessMeters { get; set; }
    public double PlasterThicknessMeters { get; set; }
    public double OpeningPercentage { get; set; }
    public double GrossLineLoadKnPerM { get; set; }
    public double NetLineLoadKnPerM { get; set; }
    public string ClauseReference { get; set; } = "BNBC 2020 Part 6, Table 6.2.1";
}

public sealed class LoadCombinationDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DesignType { get; set; } = "Strength (USD)";
    public string ClauseReference { get; set; } = string.Empty;
    public Dictionary<string, double> Factors { get; set; } = new();
}

public sealed class ComplianceCheckItem
{
    public string CheckId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Clause { get; set; } = string.Empty;
    public ComplianceStatus Status { get; set; }
    public string MeasuredValue { get; set; } = string.Empty;
    public string LimitValue { get; set; } = string.Empty;
    public string EngineeringRemarks { get; set; } = string.Empty;
}

public sealed class FoundationReactionSummary
{
    public string JointLabel { get; set; } = string.Empty;
    public string StoryName { get; set; } = "Base";
    public string LoadCombination { get; set; } = string.Empty;
    public double FxKn { get; set; }
    public double FyKn { get; set; }
    public double FzKn { get; set; }
    public double MxKnM { get; set; }
    public double MyKnM { get; set; }
    public double MzKnM { get; set; }
}

public sealed class EtabsElementLoadAssignmentRequest
{
    public bool AssignFrameWallLoads { get; set; }
    public string TargetFrameGroupOrLabel { get; set; } = "ALL";
    public string WallLoadPattern { get; set; } = "WALL";
    public double WallLineLoadKnPerM { get; set; }
    public bool ReplaceExistingFrameLoads { get; set; } = false;

    public bool AssignAreaFloorLoads { get; set; }
    public string TargetAreaGroupOrLabel { get; set; } = "ALL";
    public string SdlLoadPattern { get; set; } = "SDL";
    public double SdlUniformLoadKnPerM2 { get; set; }
    public string LiveLoadPattern { get; set; } = "LIVE";
    public double LiveUniformLoadKnPerM2 { get; set; }
    public bool ReplaceExistingAreaLoads { get; set; } = false;
}
