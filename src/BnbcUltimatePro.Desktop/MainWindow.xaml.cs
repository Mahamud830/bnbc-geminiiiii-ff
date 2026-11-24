using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using BnbcUltimatePro.Core.Models;
using BnbcUltimatePro.Core.Services;
using BnbcUltimatePro.EtabsIntegration;
using Microsoft.Win32;

namespace BnbcUltimatePro.Desktop;

public sealed class LoadComboRowViewModel
{
    public string Name { get; set; } = string.Empty;
    public string DesignType { get; set; } = string.Empty;
    public string ClauseReference { get; set; } = string.Empty;
    public string FactorsSummary { get; set; } = string.Empty;
}

public partial class MainWindow : Window
{
    private readonly BnbcDatabaseProvider _dbProvider;
    private readonly BnbcSeismicCalculator _seismicCalc;
    private readonly BnbcWindCalculator _windCalc;
    private readonly BnbcGravityAndComboService _gravityComboService;
    private readonly BnbcComplianceChecker _complianceChecker;
    private readonly BnbcReportGenerator _reportGenerator;
    private readonly IEtabsConnector _etabsConnector;

    private readonly ObservableCollection<StoryDefinition> _stories = new();
    private readonly ObservableCollection<FoundationReactionSummary> _reactions = new();
    private bool _isInitializing = true;

    private SeismicCalculationResult _lastSeismic = new();
    private WindCalculationResult _lastWind = new();
    private List<LoadCombinationDefinition> _lastCombos = new();
    private List<ComplianceCheckItem> _lastCompliance = new();

    public MainWindow()
    {
        InitializeComponent();

        _dbProvider = new BnbcDatabaseProvider();
        _seismicCalc = new BnbcSeismicCalculator(_dbProvider);
        _windCalc = new BnbcWindCalculator(_dbProvider);
        _gravityComboService = new BnbcGravityAndComboService();
        _complianceChecker = new BnbcComplianceChecker(_dbProvider);
        _reportGenerator = new BnbcReportGenerator();
        _etabsConnector = new Etabs22RuntimeConnector();

        PopulateDropdowns();
        LoadSampleProjectData();

        GridStories.ItemsSource = _stories;
        GridReactions.ItemsSource = _reactions;

        _isInitializing = false;
        RunEtabsDetection();
        RecalculateAll();
    }

    private void PopulateDropdowns()
    {
        CmbDistrict.ItemsSource = _dbProvider.Database.DistrictZones.Select(d => d.District).OrderBy(x => x).ToList();
        CmbOccupancyCategory.ItemsSource = _dbProvider.Database.OccupancyCategories.Select(o => o.Category).ToList();
        CmbStructuralSystem.ItemsSource = _dbProvider.Database.StructuralSystems.Select(s => s.Code).ToList();
        CmbSiteClass.ItemsSource = _dbProvider.Database.SiteClasses.Select(s => s.Code).ToList();
        CmbWindExposure.ItemsSource = Enum.GetValues(typeof(ExposureCategory));
    }

    private void LoadSampleProjectData()
    {
        TxtProjectName.Text = "Barishal Commercial-Residential Complex (G+6)";
        TxtProjectCode.Text = "BNBC-BAR-2026-07";
        TxtEngineerName.Text = "Civil & Structural Design Unit";
        CmbDistrict.SelectedItem = "Barishal";
        CmbOccupancyCategory.SelectedItem = "II";
        CmbStructuralSystem.SelectedItem = "IMRF_RC";
        CmbSiteClass.SelectedItem = "SD";
        TxtSoilReportRef.Text = "GEO-BAR-2026-BH04 (SPT N60 = 11)";
        CmbWindExposure.SelectedItem = ExposureCategory.A;
        TxtHeightM.Text = "21.0";
        TxtDimXM.Text = "18.0";
        TxtDimYM.Text = "15.0";

        _stories.Clear();
        for (int i = 1; i <= 7; i++)
        {
            bool isRoof = i == 7;
            _stories.Add(new StoryDefinition
            {
                StoryName = isRoof ? "Roof" : $"Story {i}",
                HeightMeters = 3.0,
                ElevationMeters = i * 3.0,
                FloorAreaSqM = 270.0,
                OccupancyType = isRoof ? "Roof - Accessible Flat Roof (General)" : "Residential - Private Rooms & Bedrooms",
                AssignedLiveLoadKnM2 = isRoof ? 1.5 : 2.0,
                AssignedSuperDeadLoadKnM2 = 2.2,
                SeismicWeightKn = isRoof ? 1850.0 : 2450.0,
                IsRoof = isRoof
            });
        }
    }

    private BuildingProject BuildProjectFromUi()
    {
        double.TryParse(TxtHeightM.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h);
        double.TryParse(TxtDimXM.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lx);
        double.TryParse(TxtDimYM.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ly);
        double.TryParse(TxtKzt.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double kzt);
        double.TryParse(TxtKd.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double kd);

        string districtText = CmbDistrict.SelectedItem?.ToString() ?? CmbDistrict.Text.Trim();
        if (string.IsNullOrWhiteSpace(districtText)) districtText = "Dhaka";

        return new BuildingProject
        {
            ProjectName = TxtProjectName.Text.Trim(),
            ProjectNumber = TxtProjectCode.Text.Trim(),
            EngineerName = TxtEngineerName.Text.Trim(),
            District = districtText,
            OccupancyCategory = CmbOccupancyCategory.SelectedItem?.ToString() ?? "II",
            StructuralSystemCode = CmbStructuralSystem.SelectedItem?.ToString() ?? "IMRF_RC",
            GeotechnicalSiteClass = CmbSiteClass.SelectedItem?.ToString() ?? string.Empty,
            GeotechnicalReportRef = TxtSoilReportRef.Text.Trim(),
            WindExposure = CmbWindExposure.SelectedItem is ExposureCategory exp ? exp : ExposureCategory.A,
            BuildingHeightMeters = h > 0 ? h : 21.0,
            PlanDimensionXMeters = lx > 0 ? lx : 18.0,
            PlanDimensionYMeters = ly > 0 ? ly : 15.0,
            TopographicFactorKzt = kzt > 0 ? kzt : 1.0,
            WindDirectionalityFactorKd = kd > 0 ? kd : 0.85,
            IncludeVerticalSeismicEv = ChkVerticalSeismic.IsChecked == true,
            Stories = _stories.ToList()
        };
    }

    private EtabsElementLoadAssignmentRequest BuildElementLoadRequest()
    {
        double.TryParse(TxtEtabsWallLoad.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double wallKnM);
        double.TryParse(TxtEtabsSdlLoad.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double sdlKnM2);
        double.TryParse(TxtEtabsLiveLoad.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double liveKnM2);

        return new EtabsElementLoadAssignmentRequest
        {
            AssignFrameWallLoads = ChkAssignFrameWall.IsChecked == true,
            TargetFrameGroupOrLabel = TxtEtabsBeamGroup.Text.Trim(),
            WallLineLoadKnPerM = Math.Max(0, wallKnM),
            AssignAreaFloorLoads = ChkAssignAreaSlab.IsChecked == true,
            TargetAreaGroupOrLabel = TxtEtabsSlabGroup.Text.Trim(),
            SdlUniformLoadKnPerM2 = Math.Max(0, sdlKnM2),
            LiveUniformLoadKnPerM2 = Math.Max(0, liveKnM2)
        };
    }

    private void RecalculateAll()
    {
        if (_isInitializing) return;

        var project = BuildProjectFromUi();

        var siteInfo = _dbProvider.GetSiteClass(project.GeotechnicalSiteClass);
        TxtSiteClassDetails.Text = siteInfo != null
            ? $"{siteInfo.Description} | S = {siteInfo.S}, TB = {siteInfo.TB}s, TC = {siteInfo.TC}s, TD = {siteInfo.TD}s ({siteInfo.Clause})"
            : "Select Site Class from Geotechnical Report.";

        _lastSeismic = _seismicCalc.Calculate(project);
        if (_lastSeismic.IsValid)
        {
            TxtSeismicSummary.Text =
                $"District: {project.District} | Zone {_lastSeismic.SeismicZone} (Z = {_lastSeismic.ZoneCoefficientZ:F2})
" +
                $"Soil Class: {_lastSeismic.SiteClass} (S = {_lastSeismic.SoilFactorS:F2}, TB = {_lastSeismic.TbSeconds:F2}s, TC = {_lastSeismic.TcSeconds:F2}s)
" +
                $"System: {project.StructuralSystemCode} (R = {_lastSeismic.ResponseReductionFactorR:F1}, Omega0 = {_lastSeismic.OverstrengthFactorOmega0:F1}, Cd = {_lastSeismic.DeflectionAmplificationCd:F1})
" +
                $"Period Ta = {_lastSeismic.FundamentalPeriodTaSeconds:F4} s | Cs = {_lastSeismic.NormalizedAccelerationCs:F4}
" +
                $"Governing Sa = {_lastSeismic.GoverningBaseShearCoeff:F5} (Sa,min = {_lastSeismic.MinimumBaseShearCoeffSaMin:F5})
" +
                $"Total Seismic Weight W = {_lastSeismic.TotalSeismicWeightKn:F2} kN
" +
                $"DESIGN BASE SHEAR V = {_lastSeismic.DesignBaseShearKn:F2} kN | SDC = {_lastSeismic.SeismicDesignCategory}";
            GridSeismicForces.ItemsSource = _lastSeismic.StoryForces;
        }
        else
        {
            TxtSeismicSummary.Text = $"VALIDATION ALERT:
{_lastSeismic.ValidationMessage}";
            GridSeismicForces.ItemsSource = null;
        }

        _lastWind = _windCalc.Calculate(project);
        if (_lastWind.IsValid)
        {
            TxtWindSummary.Text =
                $"Basic Wind Speed V = {_lastWind.BasicWindSpeedMs:F1} m/s | Exposure {project.WindExposure}
" +
                $"Factors: I = {_lastWind.WindImportanceFactorI:F2}, Kd = {_lastWind.DirectionalityFactorKd:F2}, Kzt = {_lastWind.TopographicFactorKzt:F2}, G = {_lastWind.GustFactorG:F2}
" +
                $"Roof Velocity Pressure qh = {_lastWind.RoofVelocityPressureQzKnM2:F4} kN/m²
" +
                $"Total Base Wind Force: Fx = {_lastWind.TotalWindForceXKn:F2} kN | Fy = {_lastWind.TotalWindForceYKn:F2} kN";
            GridWindForces.ItemsSource = _lastWind.StoryWindForces;
        }

        _lastCombos = _gravityComboService.GenerateBnbc2020Combinations(
            evFactor: _lastSeismic.IsValid ? _lastSeismic.VerticalSeismicFactorEvCoeff : 0.0,
            includeServiceability: ChkServiceCombos.IsChecked == true);

        GridCombos.ItemsSource = _lastCombos.Select(c => new LoadComboRowViewModel
        {
            Name = c.Name,
            DesignType = c.DesignType,
            ClauseReference = c.ClauseReference,
            FactorsSummary = string.Join(" + ", c.Factors.Select(kv => $"{kv.Value:F3}*{kv.Key}"))
        }).ToList();

        _lastCompliance = _complianceChecker.RunChecks(project, _lastSeismic, _lastWind, _etabsConnector.IsConnected);
        GridCompliance.ItemsSource = _lastCompliance;

        TxtStatusBar.Text = $"Calculated ({_dbProvider.Database.DistrictZones.Count} Districts Active) | Base Shear V = {_lastSeismic.DesignBaseShearKn:F2} kN | {_lastCombos.Count} BNBC 2020 Combinations.";
    }

    private void RunEtabsDetection()
    {
        var det = _etabsConnector.DetectInstallation();
        TxtEtabsDiagnostics.Text =
            $"Installed: {det.IsEtabsInstalled}
" +
            $"Directory: {(string.IsNullOrEmpty(det.InstallDirectory) ? "Not Found" : det.InstallDirectory)}
" +
            $"API DLL: {(string.IsNullOrEmpty(det.ApiDllPath) ? "Not Found" : det.ApiDllPath)}
" +
            $"Version: {det.DetectedVersion}
" +
            $"Status: {det.DiagnosticMessage}";

        TxtEtabsBadge.Text = det.IsEtabsInstalled
            ? (_etabsConnector.IsConnected ? "ETABS 22: CONNECTED" : "ETABS 22: Installed (Offline)")
            : "ETABS 22: Not Detected";
    }

    private void OnProjectInputChanged(object sender, RoutedEventArgs e) => RecalculateAll();
    private void BtnRecalculateAll_Click(object sender, RoutedEventArgs e) => RecalculateAll();

    private void BtnLoadSample_Click(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        LoadSampleProjectData();
        _isInitializing = false;
        RecalculateAll();
    }

    private void BtnComputeDeadLoad_Click(object sender, RoutedEventArgs e)
    {
        double.TryParse(TxtSlabMm.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double slab);
        double.TryParse(TxtFloorFinish.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ff);
        double.TryParse(TxtPartition.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double pw);
        var res = _gravityComboService.CalculateFloorDeadLoad(slab, ff, pw);
        TxtDeadLoadResult.Text = res.FormulaBreakdown;
        TxtEtabsSdlLoad.Text = (res.FloorFinishKnM2 + res.PartitionAllowanceKnM2 + res.MepAllowanceKnM2).ToString("F2", CultureInfo.InvariantCulture);
    }

    private void BtnComputeWallLoad_Click(object sender, RoutedEventArgs e)
    {
        double.TryParse(TxtWallStoryH.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double sh);
        double.TryParse(TxtBeamDepth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double bd);
        double.TryParse(TxtWallMm.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double wm);
        double.TryParse(TxtOpeningPct.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double op);
        var res = _gravityComboService.CalculateWallLineLoad(sh, bd, wm, 25.0, op);
        TxtWallLoadResult.Text = $"Clear H = {res.WallHeightMeters:F2} m | Gross = {res.GrossLineLoadKnPerM:F2} kN/m | Net Wall Load = {res.NetLineLoadKnPerM:F2} kN/m";
        TxtEtabsWallLoad.Text = res.NetLineLoadKnPerM.ToString("F2", CultureInfo.InvariantCulture);
    }

    private void BtnAddStory_Click(object sender, RoutedEventArgs e)
    {
        int nextIdx = _stories.Count + 1;
        double topElev = _stories.Count > 0 ? _stories.Max(s => s.ElevationMeters) : 0.0;
        _stories.Add(new StoryDefinition
        {
            StoryName = $"Story {nextIdx}",
            HeightMeters = 3.0,
            ElevationMeters = topElev + 3.0,
            FloorAreaSqM = 270.0,
            OccupancyType = "Residential - Private Rooms & Bedrooms",
            AssignedLiveLoadKnM2 = 2.0,
            AssignedSuperDeadLoadKnM2 = 2.2,
            SeismicWeightKn = 2400.0
        });
        RecalculateAll();
    }

    private void BtnDetectEtabs_Click(object sender, RoutedEventArgs e) => RunEtabsDetection();

    private void BtnConnectEtabs_Click(object sender, RoutedEventArgs e)
    {
        var (success, msg) = _etabsConnector.ConnectToActiveInstance();
        RunEtabsDetection();
        TxtEtabsDiagnostics.Text += $"

Connection Attempt:
{msg}";
        MessageBox.Show(msg, success ? "ETABS 22 Connected" : "ETABS 22 Connection Notice", MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        RecalculateAll();
    }

    private void BtnReadSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (!_etabsConnector.IsConnected)
        {
            MessageBox.Show("Connect to an active ETABS 22 session first.", "ETABS Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var snap = _etabsConnector.ReadModelSnapshot();
            TxtEtabsDiagnostics.Text =
                $"Model File: {snap.ModelFilePath}
" +
                $"Units: {snap.PresentUnits} | Stories: {snap.StoryNames.Count}
" +
                $"Frames: {snap.FrameCount} | Areas: {snap.AreaCount} | Points: {snap.PointCount}
" +
                $"Existing Patterns ({snap.ExistingLoadPatterns.Count}): {string.Join(", ", snap.ExistingLoadPatterns)}
" +
                $"Existing Combos ({snap.ExistingLoadCombos.Count}): {string.Join(", ", snap.ExistingLoadCombos)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error reading model snapshot: {ex.Message}", "ETABS Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnPreviewPush_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildProjectFromUi();
        var elemLoads = BuildElementLoadRequest();
        var snapshot = _etabsConnector.IsConnected ? _etabsConnector.ReadModelSnapshot() : new EtabsModelSnapshot();
        var preview = _etabsConnector.BuildPushPreview(project, _lastCombos, snapshot, elemLoads);

        TxtPushPreview.Text =
            $"NEW LOAD PATTERNS TO CREATE ({preview.LoadPatternsToCreate.Count}):
  {string.Join(", ", preview.LoadPatternsToCreate)}
" +
            $"EXISTING PATTERNS SKIPPED ({preview.LoadPatternsSkippedExisting.Count}):
  {string.Join(", ", preview.LoadPatternsSkippedExisting)}
" +
            $"MASS SOURCE:
  {preview.MassSourceAction}
" +
            $"ELEMENT LOAD ASSIGNMENT:
  {preview.ElementLoadAssignmentAction}
" +
            $"NEW COMBINATIONS TO CREATE ({preview.LoadCombinationsToCreate.Count}):
  {string.Join(", ", preview.LoadCombinationsToCreate.Take(6))} ...";
    }

    private void BtnExecutePush_Click(object sender, RoutedEventArgs e)
    {
        if (!_etabsConnector.IsConnected)
        {
            MessageBox.Show("Please connect to a running ETABS 22 instance before pushing data.", "ETABS Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            "This will add BNBC 2020 load patterns, mass source, optional beam/slab loads, and load combinations to your active ETABS 22 model.

Proceed?",
            "Confirm ETABS 22 Model Modification",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        var project = BuildProjectFromUi();
        var elemLoads = BuildElementLoadRequest();
        var (success, log) = _etabsConnector.ApplyToEtabsModel(project, _lastSeismic, _lastWind, _lastCombos, true, elemLoads);
        TxtPushPreview.Text = log;
        MessageBox.Show(success ? "Successfully updated ETABS 22 model." : log, "ETABS 22 Push Result", MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void BtnExtractReactions_Click(object sender, RoutedEventArgs e)
    {
        if (!_etabsConnector.IsConnected)
        {
            MessageBox.Show("Connect to an analyzed ETABS 22 model first, or click 'Load Demo Data' to preview Excel export.", "ETABS Not Connected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var serviceCombos = _lastCombos.Where(c => c.DesignType.StartsWith("Service")).Select(c => c.Name).ToList();
            var list = _etabsConnector.ExtractFoundationReactions(serviceCombos);
            _reactions.Clear();
            foreach (var r in list) _reactions.Add(r);
            TxtStatusBar.Text = $"Extracted {_reactions.Count} foundation reaction rows from ETABS 22.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reaction extraction failed: {ex.Message}", "ETABS Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDemoReactions_Click(object sender, RoutedEventArgs e)
    {
        _reactions.Clear();
        _reactions.Add(new FoundationReactionSummary { JointLabel = "C1-BASE", LoadCombination = "BNBC_S2_D+L", FzKn = 1485.4, FxKn = 12.3, FyKn = -8.7, MxKnM = 18.2, MyKnM = 24.5, MzKnM = 1.1 });
        _reactions.Add(new FoundationReactionSummary { JointLabel = "C2-BASE", LoadCombination = "BNBC_S2_D+L", FzKn = 2310.8, FxKn = -4.2, FyKn = 6.5, MxKnM = -14.0, MyKnM = 19.8, MzKnM = 0.4 });
        _reactions.Add(new FoundationReactionSummary { JointLabel = "C3-BASE", LoadCombination = "BNBC_S2_D+L", FzKn = 1920.0, FxKn = 9.1, FyKn = 11.2, MxKnM = 21.4, MyKnM = -16.3, MzKnM = 0.8 });
    }

    private void BtnGenerateReport_Click(object sender, RoutedEventArgs e)
    {
        RecalculateAll();
        var project = BuildProjectFromUi();
        TxtReportOutput.Text = _reportGenerator.GenerateMarkdownReport(
            project, _lastSeismic, _lastWind, _lastCombos, _lastCompliance, _reactions.ToList());
    }

    private void BtnExportPdf_Click(object sender, RoutedEventArgs e)
    {
        RecalculateAll();
        var project = BuildProjectFromUi();
        var dlg = new SaveFileDialog
        {
            Filter = "PDF Engineering Report (*.pdf)|*.pdf",
            FileName = $"{TxtProjectCode.Text.Trim()}_BNBC2020_Report.pdf"
        };

        if (dlg.ShowDialog() == true)
        {
            _reportGenerator.ExportToPdf(dlg.FileName, project, _lastSeismic, _lastWind, _lastCombos, _lastCompliance, _reactions.ToList());
            MessageBox.Show($"Branded PDF report saved to:
{dlg.FileName}", "PDF Exported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        RecalculateAll();
        var project = BuildProjectFromUi();
        var dlg = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"{TxtProjectCode.Text.Trim()}_BNBC2020_Data.xlsx"
        };

        if (dlg.ShowDialog() == true)
        {
            _reportGenerator.ExportToXlsx(dlg.FileName, project, _lastSeismic, _lastWind, _lastCombos, _lastCompliance, _reactions.ToList());
            MessageBox.Show($"Excel workbook (.xlsx) saved to:
{dlg.FileName}", "Excel Exported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtReportOutput.Text))
        {
            BtnGenerateReport_Click(sender, e);
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Markdown Report (*.md)|*.md|Text File (*.txt)|*.txt",
            FileName = $"{TxtProjectCode.Text.Trim()}_BNBC2020_Report.md"
        };

        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, TxtReportOutput.Text);
            MessageBox.Show($"Engineering report saved to:
{dlg.FileName}", "Report Exported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
