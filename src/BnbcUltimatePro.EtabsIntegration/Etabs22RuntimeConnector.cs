using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BnbcUltimatePro.Core.Models;
using Microsoft.Win32;

namespace BnbcUltimatePro.EtabsIntegration;

public sealed class EtabsDetectionResult
{
    public bool IsEtabsInstalled { get; set; }
    public string InstallDirectory { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ApiDllPath { get; set; } = string.Empty;
    public string DetectedVersion { get; set; } = string.Empty;
    public bool IsRunningInstanceAvailable { get; set; }
    public string DiagnosticMessage { get; set; } = string.Empty;
}

public sealed class EtabsModelSnapshot
{
    public string ModelFilePath { get; set; } = string.Empty;
    public string PresentUnits { get; set; } = string.Empty;
    public List<string> StoryNames { get; set; } = new();
    public List<double> StoryElevations { get; set; } = new();
    public int FrameCount { get; set; }
    public int AreaCount { get; set; }
    public int PointCount { get; set; }
    public List<string> ExistingLoadPatterns { get; set; } = new();
    public List<string> ExistingLoadCases { get; set; } = new();
    public List<string> ExistingLoadCombos { get; set; } = new();
}

public sealed class EtabsPushPreview
{
    public List<string> LoadPatternsToCreate { get; set; } = new();
    public List<string> LoadPatternsSkippedExisting { get; set; } = new();
    public List<string> LoadCasesToConfigure { get; set; } = new();
    public List<string> LoadCombinationsToCreate { get; set; } = new();
    public List<string> LoadCombinationsSkippedExisting { get; set; } = new();
    public string MassSourceAction { get; set; } = string.Empty;
    public string ElementLoadAssignmentAction { get; set; } = string.Empty;
}

public interface IEtabsConnector
{
    bool IsConnected { get; }
    EtabsDetectionResult DetectInstallation();
    (bool Success, string Message) ConnectToActiveInstance();
    void Disconnect();
    EtabsModelSnapshot ReadModelSnapshot();
    EtabsPushPreview BuildPushPreview(BuildingProject project, IReadOnlyList<LoadCombinationDefinition> combinations, EtabsModelSnapshot snapshot, EtabsElementLoadAssignmentRequest? elementLoads = null);
    (bool Success, string Log) ApplyToEtabsModel(BuildingProject project, SeismicCalculationResult seismic, WindCalculationResult wind, IReadOnlyList<LoadCombinationDefinition> combinations, bool userConfirmed, EtabsElementLoadAssignmentRequest? elementLoads = null);
    List<FoundationReactionSummary> ExtractFoundationReactions(IReadOnlyList<string> targetCombos);
}

public sealed class Etabs22RuntimeConnector : IEtabsConnector
{
    private dynamic? _etabsObject;
    private dynamic? _sapModel;
    private Assembly? _etabsApiAssembly;

    public bool IsConnected => _sapModel != null;

    public EtabsDetectionResult DetectInstallation()
    {
        var result = new EtabsDetectionResult();

        if (!OperatingSystem.IsWindows())
        {
            result.DiagnosticMessage = "ETABS 22 integration requires a Windows 10/11 host with Computers and Structures ETABS 22 installed.";
            return result;
        }

        string[] candidateFolders =
        {
            @"C:\Program Files\Computers and Structures\ETABS 22",
            @"C:\Program Files\Computers and Structures\ETABS 21",
            @"C:\Program Files\Computers and Structures\ETABS 20"
        };

        string? foundDir = candidateFolders.FirstOrDefault(Directory.Exists) ?? TryFindInstallPathFromRegistry();

        if (string.IsNullOrWhiteSpace(foundDir) || !Directory.Exists(foundDir))
        {
            result.IsEtabsInstalled = false;
            result.DiagnosticMessage = "ETABS 22 installation directory not found in standard Program Files or Windows Registry paths. Install ETABS 22 locally to enable live model automation.";
            return result;
        }

        result.IsEtabsInstalled = true;
        result.InstallDirectory = foundDir;

        string exePath = Path.Combine(foundDir, "ETABS.exe");
        string apiDll = Path.Combine(foundDir, "ETABSv1.dll");
        result.ExecutablePath = File.Exists(exePath) ? exePath : string.Empty;
        result.ApiDllPath = File.Exists(apiDll) ? apiDll : string.Empty;

        if (!string.IsNullOrEmpty(result.ApiDllPath))
        {
            try
            {
                var asmName = AssemblyName.GetAssemblyName(result.ApiDllPath);
                result.DetectedVersion = asmName.Version?.ToString() ?? "Detected (ETABSv1.dll)";
            }
            catch
            {
                result.DetectedVersion = "ETABSv1.dll Present";
            }
        }
        else
        {
            result.DetectedVersion = "ETABSv1.dll Missing";
        }

        try
        {
            Type? helperType = Type.GetTypeFromProgID("CSI.ETABS.API.ETABSObject");
            result.IsRunningInstanceAvailable = helperType != null;
        }
        catch
        {
            result.IsRunningInstanceAvailable = false;
        }

        result.DiagnosticMessage = !string.IsNullOrEmpty(result.ApiDllPath)
            ? $"Detected ETABS installation at '{foundDir}' (API: {result.DetectedVersion})."
            : $"Found ETABS folder '{foundDir}', but ETABSv1.dll was not located.";

        return result;
    }

    [SupportedOSPlatform("windows")]
    private static string? TryFindInstallPathFromRegistry()
    {
        string[] regKeys =
        {
            @"SOFTWARE\Computers and Structures, Inc.\ETABS",
            @"SOFTWARE\Computers and Structures, Inc.\ETABS",
            @"SOFTWARE\WOW6432Node\Computers and Structures, Inc.\ETABS"
        };

        foreach (string keyPath in regKeys)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
                object? val = key?.GetValue("InstallDir") ?? key?.GetValue("Path");
                if (val is string path && Directory.Exists(path))
                {
                    return path;
                }
            }
            catch
            {
            }
        }
        return null;
    }

    public (bool Success, string Message) ConnectToActiveInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, "Live ETABS 22 connection is only supported on Windows 10/11.");
        }

        var detection = DetectInstallation();
        if (!detection.IsEtabsInstalled || string.IsNullOrEmpty(detection.ApiDllPath))
        {
            return (false, detection.DiagnosticMessage);
        }

        try
        {
            _etabsApiAssembly ??= Assembly.LoadFrom(detection.ApiDllPath);
            Type? helperType = _etabsApiAssembly.GetType("ETABSv1.Helper");
            if (helperType == null)
            {
                return (false, "Loaded ETABSv1.dll, but type 'ETABSv1.Helper' was not found.");
            }

            dynamic helper = Activator.CreateInstance(helperType)
                             ?? throw new InvalidOperationException("Failed to instantiate ETABSv1.Helper.");

            _etabsObject = helper.GetObject("CSI.ETABS.API.ETABSObject");
            if (_etabsObject == null)
            {
                return (false, "No running ETABS 22 instance found. Please open your model in ETABS 22 first, then click Connect.");
            }

            _sapModel = _etabsObject.SapModel;
            if (_sapModel == null)
            {
                return (false, "Connected to ETABSObject, but SapModel interface is null.");
            }

            _sapModel.SetPresentUnits(6); // kN_m_C
            string modelFile = (string)_sapModel.GetModelFilename();
            return (true, $"Connected to ETABS 22 ({detection.DetectedVersion}). Active model: {Path.GetFileName(modelFile)}");
        }
        catch (COMException comEx)
        {
            _etabsObject = null;
            _sapModel = null;
            return (false, $"COM Error connecting to ETABS 22 (0x{comEx.ErrorCode:X8}): Ensure ETABS 22 is running under the same Windows user privilege level.");
        }
        catch (Exception ex)
        {
            _etabsObject = null;
            _sapModel = null;
            return (false, $"Failed to bind ETABS 22 API: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        _sapModel = null;
        _etabsObject = null;
    }

    public EtabsModelSnapshot ReadModelSnapshot()
    {
        if (_sapModel == null)
        {
            throw new InvalidOperationException("Not connected to ETABS 22.");
        }

        _sapModel.SetPresentUnits(6);
        var snapshot = new EtabsModelSnapshot
        {
            ModelFilePath = (string)_sapModel.GetModelFilename(),
            PresentUnits = "kN, m, C"
        };

        int numStories = 0;
        string[] storyNames = Array.Empty<string>();
        _sapModel.Story.GetNameList(ref numStories, ref storyNames);
        if (storyNames != null)
        {
            snapshot.StoryNames.AddRange(storyNames);
            foreach (var st in storyNames)
            {
                double elev = 0;
                _sapModel.Story.GetElevation(st, ref elev);
                snapshot.StoryElevations.Add(elev);
            }
        }

        int nFrames = 0;
        string[] frameNames = Array.Empty<string>();
        _sapModel.FrameObj.GetNameList(ref nFrames, ref frameNames);
        snapshot.FrameCount = nFrames;

        int nAreas = 0;
        string[] areaNames = Array.Empty<string>();
        _sapModel.AreaObj.GetNameList(ref nAreas, ref areaNames);
        snapshot.AreaCount = nAreas;

        int nPoints = 0;
        string[] pointNames = Array.Empty<string>();
        _sapModel.PointObj.GetNameList(ref nPoints, ref pointNames);
        snapshot.PointCount = nPoints;

        int nPat = 0;
        string[] patNames = Array.Empty<string>();
        _sapModel.LoadPatterns.GetNameList(ref nPat, ref patNames);
        if (patNames != null) snapshot.ExistingLoadPatterns.AddRange(patNames);

        int nCases = 0;
        string[] caseNames = Array.Empty<string>();
        _sapModel.LoadCases.GetNameList(ref nCases, ref caseNames);
        if (caseNames != null) snapshot.ExistingLoadCases.AddRange(caseNames);

        int nCombos = 0;
        string[] comboNames = Array.Empty<string>();
        _sapModel.RespCombo.GetNameList(ref nCombos, ref comboNames);
        if (comboNames != null) snapshot.ExistingLoadCombos.AddRange(comboNames);

        return snapshot;
    }

    public EtabsPushPreview BuildPushPreview(
        BuildingProject project,
        IReadOnlyList<LoadCombinationDefinition> combinations,
        EtabsModelSnapshot snapshot,
        EtabsElementLoadAssignmentRequest? elementLoads = null)
    {
        var requiredPatterns = new[]
        {
            "DEAD", "SDL", "WALL", "LIVE", "ROOF_LIVE",
            "EX+", "EX-", "EY+", "EY-",
            "WX+", "WX-", "WY+", "WY-"
        };

        var existingPatSet = new HashSet<string>(snapshot.ExistingLoadPatterns, StringComparer.OrdinalIgnoreCase);
        var existingComboSet = new HashSet<string>(snapshot.ExistingLoadCombos, StringComparer.OrdinalIgnoreCase);

        var actions = new List<string>();
        if (elementLoads?.AssignFrameWallLoads == true && elementLoads.WallLineLoadKnPerM > 0)
        {
            actions.Add($"Frame Line Load: {elementLoads.WallLineLoadKnPerM:F2} kN/m ({elementLoads.WallLoadPattern}) -> Group/Frame '{elementLoads.TargetFrameGroupOrLabel}'");
        }
        if (elementLoads?.AssignAreaFloorLoads == true)
        {
            actions.Add($"Area Uniform Loads: SDL={elementLoads.SdlUniformLoadKnPerM2:F2} kN/m², LIVE={elementLoads.LiveUniformLoadKnPerM2:F2} kN/m² -> Group/Area '{elementLoads.TargetAreaGroupOrLabel}'");
        }

        var preview = new EtabsPushPreview
        {
            MassSourceAction = "Configure Mass Source 'BNBC_MSSRC' = 1.0(DEAD) + 1.0(SDL) + 1.0(WALL) + 0.25(LIVE) per BNBC 2020 Sec 2.5.5.1",
            ElementLoadAssignmentAction = actions.Count > 0 ? string.Join(" | ", actions) : "None selected (Patterns, Mass Source & Combinations only)"
        };

        foreach (var pat in requiredPatterns)
        {
            if (existingPatSet.Contains(pat))
                preview.LoadPatternsSkippedExisting.Add(pat);
            else
                preview.LoadPatternsToCreate.Add(pat);
        }

        foreach (var c in combinations)
        {
            if (existingComboSet.Contains(c.Name))
                preview.LoadCombinationsSkippedExisting.Add(c.Name);
            else
                preview.LoadCombinationsToCreate.Add(c.Name);
        }

        preview.LoadCasesToConfigure.AddRange(requiredPatterns);
        return preview;
    }

    public (bool Success, string Log) ApplyToEtabsModel(
        BuildingProject project,
        SeismicCalculationResult seismic,
        WindCalculationResult wind,
        IReadOnlyList<LoadCombinationDefinition> combinations,
        bool userConfirmed,
        EtabsElementLoadAssignmentRequest? elementLoads = null)
    {
        if (!userConfirmed)
        {
            return (false, "Operation aborted: User confirmation is required before modifying the active ETABS 22 model.");
        }

        if (_sapModel == null)
        {
            return (false, "Not connected to an active ETABS 22 session.");
        }

        try
        {
            _sapModel.SetPresentUnits(6); // kN_m_C
            if ((bool)_sapModel.GetModelIsLocked())
            {
                return (false, "ETABS model is currently locked after analysis. Unlock the model in ETABS 22 before pushing loads and combinations.");
            }

            var snapshot = ReadModelSnapshot();
            var preview = BuildPushPreview(project, combinations, snapshot, elementLoads);
            var logLines = new List<string>();

            var patternTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["DEAD"] = 1, ["SDL"] = 2, ["WALL"] = 2, ["LIVE"] = 3, ["ROOF_LIVE"] = 11,
                ["EX+"] = 5, ["EX-"] = 5, ["EY+"] = 5, ["EY-"] = 5,
                ["WX+"] = 6, ["WX-"] = 6, ["WY+"] = 6, ["WY-"] = 6
            };

            foreach (var pat in preview.LoadPatternsToCreate)
            {
                int patType = patternTypes.TryGetValue(pat, out int t) ? t : 8;
                double selfWt = string.Equals(pat, "DEAD", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
                int ret = _sapModel.LoadPatterns.Add(pat, patType, selfWt, true);
                logLines.Add($"Load Pattern '{pat}': {(ret == 0 ? "Created" : $"Code {ret}")}");
            }

            string[] massPatterns = { "DEAD", "SDL", "WALL", "LIVE" };
            double[] massMults = { 1.0, 1.0, 1.0, 0.25 };
            int msRet = _sapModel.PropMaterial.SetMassSource_1("BNBC_MSSRC", false, false, true, true, 4, massPatterns, massMults);
            logLines.Add($"Mass Source 'BNBC_MSSRC': {(msRet == 0 ? "Configured (1.0 DL + 0.25 LL)" : $"Status {msRet}")}");

            if (elementLoads?.AssignFrameWallLoads == true && elementLoads.WallLineLoadKnPerM > 0)
            {
                string targetFrame = string.IsNullOrWhiteSpace(elementLoads.TargetFrameGroupOrLabel) ? "ALL" : elementLoads.TargetFrameGroupOrLabel.Trim();
                int fRet = _sapModel.FrameObj.SetLoadDistributed(
                    targetFrame,
                    elementLoads.WallLoadPattern,
                    1,
                    10,
                    0.0,
                    1.0,
                    elementLoads.WallLineLoadKnPerM,
                    elementLoads.WallLineLoadKnPerM,
                    "Global",
                    true,
                    elementLoads.ReplaceExistingFrameLoads,
                    1);
                logLines.Add($"Frame Wall Load ({elementLoads.WallLineLoadKnPerM:F2} kN/m on Group '{targetFrame}'): {(fRet == 0 ? "Assigned" : $"Status {fRet}")}");
            }

            if (elementLoads?.AssignAreaFloorLoads == true)
            {
                string targetArea = string.IsNullOrWhiteSpace(elementLoads.TargetAreaGroupOrLabel) ? "ALL" : elementLoads.TargetAreaGroupOrLabel.Trim();
                if (elementLoads.SdlUniformLoadKnPerM2 > 0)
                {
                    int aSdlRet = _sapModel.AreaObj.SetLoadUniform(
                        targetArea,
                        elementLoads.SdlLoadPattern,
                        elementLoads.SdlUniformLoadKnPerM2,
                        10,
                        elementLoads.ReplaceExistingAreaLoads,
                        "Global",
                        1);
                    logLines.Add($"Area SDL Load ({elementLoads.SdlUniformLoadKnPerM2:F2} kN/m² on Group '{targetArea}'): {(aSdlRet == 0 ? "Assigned" : $"Status {aSdlRet}")}");
                }
                if (elementLoads.LiveUniformLoadKnPerM2 > 0)
                {
                    int aLiveRet = _sapModel.AreaObj.SetLoadUniform(
                        targetArea,
                        elementLoads.LiveLoadPattern,
                        elementLoads.LiveUniformLoadKnPerM2,
                        10,
                        elementLoads.ReplaceExistingAreaLoads,
                        "Global",
                        1);
                    logLines.Add($"Area LIVE Load ({elementLoads.LiveUniformLoadKnPerM2:F2} kN/m² on Group '{targetArea}'): {(aLiveRet == 0 ? "Assigned" : $"Status {aLiveRet}")}");
                }
            }

            foreach (var combo in combinations)
            {
                if (preview.LoadCombinationsSkippedExisting.Contains(combo.Name)) continue;
                int cRet = _sapModel.RespCombo.Add(combo.Name, 0);
                if (cRet == 0)
                {
                    foreach (var kv in combo.Factors)
                    {
                        _sapModel.RespCombo.SetCaseList(combo.Name, 0, kv.Key, kv.Value);
                    }
                    logLines.Add($"Combo '{combo.Name}': Created");
                }
            }

            _sapModel.View.RefreshView(0, false);
            return (true, string.Join(Environment.NewLine, logLines));
        }
        catch (Exception ex)
        {
            return (false, $"ETABS 22 automation error: {ex.Message}");
        }
    }

    public List<FoundationReactionSummary> ExtractFoundationReactions(IReadOnlyList<string> targetCombos)
    {
        if (_sapModel == null)
        {
            throw new InvalidOperationException("Not connected to ETABS 22.");
        }

        _sapModel.SetPresentUnits(6);
        var results = new List<FoundationReactionSummary>();

        _sapModel.Results.Setup.DeselectAllCasesAndCombosForOutput();
        foreach (var combo in targetCombos)
        {
            _sapModel.Results.Setup.SetComboSelectedForOutput(combo, true);
        }

        int numberResults = 0;
        string[] obj = Array.Empty<string>();
        string[] elm = Array.Empty<string>();
        string[] loadCase = Array.Empty<string>();
        string[] stepType = Array.Empty<string>();
        double[] stepNum = Array.Empty<double>();
        double[] f1 = Array.Empty<double>();
        double[] f2 = Array.Empty<double>();
        double[] f3 = Array.Empty<double>();
        double[] m1 = Array.Empty<double>();
        double[] m2 = Array.Empty<double>();
        double[] m3 = Array.Empty<double>();

        int ret = _sapModel.Results.JointReact("", 2, ref numberResults, ref obj, ref elm, ref loadCase, ref stepType, ref stepNum, ref f1, ref f2, ref f3, ref m1, ref m2, ref m3);
        if (ret == 0 && numberResults > 0)
        {
            for (int i = 0; i < numberResults; i++)
            {
                results.Add(new FoundationReactionSummary
                {
                    JointLabel = obj[i],
                    LoadCombination = loadCase[i],
                    FxKn = Math.Round(f1[i], 2),
                    FyKn = Math.Round(f2[i], 2),
                    FzKn = Math.Round(f3[i], 2),
                    MxKnM = Math.Round(m1[i], 2),
                    MyKnM = Math.Round(m2[i], 2),
                    MzKnM = Math.Round(m3[i], 2)
                });
            }
        }

        return results;
    }
}
