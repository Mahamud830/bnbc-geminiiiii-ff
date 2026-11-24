using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BnbcUltimatePro.Core.Models;
using BnbcUltimatePro.Core.Services;
using BnbcUltimatePro.EtabsIntegration;
using Xunit;

namespace BnbcUltimatePro.Tests;

public class BnbcCalculationAndComplianceTests
{
    private readonly BnbcDatabaseProvider _db = new();

    [Fact]
    public void Database_Contains_All_64_Bangladesh_Districts_And_4_Zones()
    {
        Assert.Equal(64, _db.Database.DistrictZones.Count);
        Assert.Equal(64, _db.Database.DistrictZones.Select(d => d.District).Distinct().Count());
        Assert.Equal(4, _db.Database.SeismicZones.Count);
        Assert.Equal(0.12, _db.GetSeismicZone(1)!.Z);
        Assert.Equal(0.20, _db.GetSeismicZone(2)!.Z);
        Assert.Equal(0.28, _db.GetSeismicZone(3)!.Z);
        Assert.Equal(0.36, _db.GetSeismicZone(4)!.Z);
    }

    [Fact]
    public void SeismicCalculator_Rejects_Missing_Geotechnical_SoilClass()
    {
        var calc = new BnbcSeismicCalculator(_db);
        var project = new BuildingProject
        {
            District = "Barishal",
            GeotechnicalSiteClass = "",
            OccupancyCategory = "II",
            StructuralSystemCode = "IMRF_RC",
            BuildingHeightMeters = 21.0,
            Stories = new List<StoryDefinition>
            {
                new() { StoryName = "Story 1", ElevationMeters = 3.0, SeismicWeightKn = 2000 }
            }
        };

        var result = calc.Calculate(project);
        Assert.False(result.IsValid);
        Assert.Contains("geotechnical report", result.ValidationMessage);
    }

    [Fact]
    public void SeismicCalculator_Computes_Accurate_BaseShear_And_StoryDistribution()
    {
        var calc = new BnbcSeismicCalculator(_db);
        var project = new BuildingProject
        {
            District = "Dhaka",
            GeotechnicalSiteClass = "SD",
            OccupancyCategory = "II",
            StructuralSystemCode = "IMRF_RC",
            BuildingHeightMeters = 15.0,
            Stories = new List<StoryDefinition>
            {
                new() { StoryName = "Story 1", ElevationMeters = 3.0, SeismicWeightKn = 2000 },
                new() { StoryName = "Story 2", ElevationMeters = 6.0, SeismicWeightKn = 2000 },
                new() { StoryName = "Story 3", ElevationMeters = 9.0, SeismicWeightKn = 2000 },
                new() { StoryName = "Story 4", ElevationMeters = 12.0, SeismicWeightKn = 2000 },
                new() { StoryName = "Roof", ElevationMeters = 15.0, SeismicWeightKn = 1500 }
            }
        };

        var result = calc.Calculate(project);
        Assert.True(result.IsValid);
        Assert.Equal(2, result.SeismicZone);
        Assert.Equal(9500.0, result.TotalSeismicWeightKn);
        Assert.True(result.DesignBaseShearKn > 0);
        Assert.Equal(result.DesignBaseShearKn, result.StoryForces.Sum(f => f.LateralForceFxKn), 1);
    }

    [Fact]
    public void WindCalculator_Computes_Bnbc2020_VelocityPressure_For_All_Districts()
    {
        var windCalc = new BnbcWindCalculator(_db);
        foreach (var dist in _db.Database.DistrictZones)
        {
            var project = new BuildingProject
            {
                District = dist.District,
                OccupancyCategory = "II",
                WindExposure = ExposureCategory.A,
                BuildingHeightMeters = 12.0,
                PlanDimensionXMeters = 16.0,
                PlanDimensionYMeters = 12.0,
                Stories = new List<StoryDefinition>
                {
                    new() { StoryName = "Story 1", ElevationMeters = 3.0 },
                    new() { StoryName = "Roof", ElevationMeters = 12.0 }
                }
            };

            var result = windCalc.Calculate(project);
            Assert.True(result.IsValid);
            Assert.Equal(dist.WindSpeedMs, result.BasicWindSpeedMs);
            Assert.True(result.TotalWindForceXKn > 0);
        }
    }

    [Fact]
    public void LoadCombinationGenerator_Includes_VerticalSeismic_And_Prevents_Duplicates()
    {
        var service = new BnbcGravityAndComboService();
        var combos = service.GenerateBnbc2020Combinations(evFactor: 0.09, includeServiceability: true);

        Assert.NotEmpty(combos);
        Assert.Equal(combos.Count, combos.Select(c => c.Name).Distinct().Count());
        Assert.Contains(combos, c => c.Name == "BNBC_U1_1.4D");
        Assert.Contains(combos, c => c.Name.StartsWith("BNBC_U5_"));
    }

    [Fact]
    public void ComplianceChecker_Distinguishes_Pass_Fail_Warning_NotVerified()
    {
        var checker = new BnbcComplianceChecker(_db);
        var seismicCalc = new BnbcSeismicCalculator(_db);
        var windCalc = new BnbcWindCalculator(_db);

        var project = new BuildingProject
        {
            District = "Sylhet",
            GeotechnicalSiteClass = "SD",
            GeotechnicalReportRef = "",
            OccupancyCategory = "II",
            StructuralSystemCode = "IMRF_RC",
            BuildingHeightMeters = 18.0,
            Stories = new List<StoryDefinition>
            {
                new() { StoryName = "Story 1", ElevationMeters = 3.0, SeismicWeightKn = 2200 }
            }
        };

        var sRes = seismicCalc.Calculate(project);
        var wRes = windCalc.Calculate(project);
        var checks = checker.RunChecks(project, sRes, wRes, etabsConnected: false);

        Assert.Contains(checks, c => c.Status == ComplianceStatus.FAIL);
        Assert.Contains(checks, c => c.Status == ComplianceStatus.WARNING);
        Assert.Contains(checks, c => c.Status == ComplianceStatus.PASS);
        Assert.Contains(checks, c => c.Status == ComplianceStatus.NOT_VERIFIED);
    }

    [Fact]
    public void EtabsConnector_Builds_DuplicateFree_PushPreview_With_Frame_And_Area_Loads()
    {
        var connector = new Etabs22RuntimeConnector();
        var service = new BnbcGravityAndComboService();
        var combos = service.GenerateBnbc2020Combinations(0.08);

        var snapshot = new EtabsModelSnapshot
        {
            ExistingLoadPatterns = new List<string> { "DEAD", "LIVE" },
            ExistingLoadCombos = new List<string> { "BNBC_U1_1.4D" }
        };

        var elemLoads = new EtabsElementLoadAssignmentRequest
        {
            AssignFrameWallLoads = true,
            TargetFrameGroupOrLabel = "PERIMETER_BEAMS",
            WallLineLoadKnPerM = 8.5,
            AssignAreaFloorLoads = true,
            TargetAreaGroupOrLabel = "TYPICAL_SLABS",
            SdlUniformLoadKnPerM2 = 2.45,
            LiveUniformLoadKnPerM2 = 2.0
        };

        var preview = connector.BuildPushPreview(new BuildingProject(), combos, snapshot, elemLoads);
        Assert.Contains("DEAD", preview.LoadPatternsSkippedExisting);
        Assert.DoesNotContain("DEAD", preview.LoadPatternsToCreate);
        Assert.Contains("PERIMETER_BEAMS", preview.ElementLoadAssignmentAction);
        Assert.Contains("TYPICAL_SLABS", preview.ElementLoadAssignmentAction);
    }

    [Fact]
    public void ReportGenerator_Exports_Valid_Pdf_And_Xlsx_Files()
    {
        var generator = new BnbcReportGenerator();
        var project = new BuildingProject
        {
            ProjectName = "Test Tower",
            ProjectNumber = "TST-01",
            District = "Barishal",
            GeotechnicalSiteClass = "SD",
            Stories = new List<StoryDefinition> { new() { StoryName = "Story 1", ElevationMeters = 3.0, SeismicWeightKn = 2000 } }
        };
        var sRes = new BnbcSeismicCalculator(_db).Calculate(project);
        var wRes = new BnbcWindCalculator(_db).Calculate(project);
        var combos = new BnbcGravityAndComboService().GenerateBnbc2020Combinations(0.05);
        var checks = new BnbcComplianceChecker(_db).RunChecks(project, sRes, wRes, false);
        var reactions = new List<FoundationReactionSummary>
        {
            new() { JointLabel = "J1", LoadCombination = "BNBC_S2_D+L", FzKn = 1500.0 }
        };

        string pdfPath = Path.Combine(Path.GetTempPath(), "bnbc_test_report.pdf");
        string xlsxPath = Path.Combine(Path.GetTempPath(), "bnbc_test_report.xlsx");

        generator.ExportToPdf(pdfPath, project, sRes, wRes, combos, checks, reactions);
        generator.ExportToXlsx(xlsxPath, project, sRes, wRes, combos, checks, reactions);

        Assert.True(File.Exists(pdfPath));
        Assert.StartsWith("%PDF-1.4", File.ReadAllText(pdfPath)[..10]);

        Assert.True(File.Exists(xlsxPath));
        using (var zip = ZipFile.OpenRead(xlsxPath))
        {
            Assert.Contains(zip.Entries, e => e.FullName == "xl/workbook.xml");
            Assert.Contains(zip.Entries, e => e.FullName == "xl/worksheets/sheet3.xml");
        }

        File.Delete(pdfPath);
        File.Delete(xlsxPath);
    }
}
