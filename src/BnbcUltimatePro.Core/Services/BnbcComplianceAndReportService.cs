using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using BnbcUltimatePro.Core.Models;

namespace BnbcUltimatePro.Core.Services;

public sealed class BnbcComplianceChecker
{
    private readonly BnbcDatabaseProvider _dbProvider;

    public BnbcComplianceChecker(BnbcDatabaseProvider dbProvider)
    {
        _dbProvider = dbProvider ?? throw new ArgumentNullException(nameof(dbProvider));
    }

    public List<ComplianceCheckItem> RunChecks(
        BuildingProject project,
        SeismicCalculationResult seismic,
        WindCalculationResult wind,
        bool etabsConnected,
        double? maxStoryDriftRatio = null,
        double? maxWindDriftRatio = null,
        double? torsionalIrregularityRatio = null)
    {
        var items = new List<ComplianceCheckItem>();

        bool hasSoilClass = !string.IsNullOrWhiteSpace(project.GeotechnicalSiteClass);
        bool hasSoilRef = !string.IsNullOrWhiteSpace(project.GeotechnicalReportRef);
        items.Add(new ComplianceCheckItem
        {
            CheckId = "BNBC-GEO-01",
            Category = "Geotechnical Input",
            Title = "Site-Specific Geotechnical Soil Classification",
            Clause = "BNBC 2020 Part 6, Table 6.2.13",
            Status = !hasSoilClass
                ? ComplianceStatus.FAIL
                : (hasSoilRef ? ComplianceStatus.PASS : ComplianceStatus.WARNING),
            MeasuredValue = hasSoilClass ? $"Site Class {project.GeotechnicalSiteClass}" : "Not Specified",
            LimitValue = "Verified SA–SE from Soil Report",
            EngineeringRemarks = !hasSoilClass
                ? "Soil class must be entered from a geotechnical report; never guessed from district."
                : (hasSoilRef ? $"Verified via report: {project.GeotechnicalReportRef}" : "Soil class selected, but geotechnical report reference is blank.")
        });

        var sys = _dbProvider.GetStructuralSystem(project.StructuralSystemCode);
        var district = _dbProvider.GetDistrictInfo(project.District);
        if (sys != null && district != null)
        {
            bool omrfProhibited = sys.Code == "OMRF_RC" && district.Zone >= 2;
            bool imrfProhibitedInZone4 = sys.Code == "IMRF_RC" && district.Zone == 4;
            bool heightLimitExceeded = sys.Code == "IMRF_RC" && district.Zone == 3 && project.BuildingHeightMeters > 40.0;

            ComplianceStatus sysStatus = (omrfProhibited || imrfProhibitedInZone4 || heightLimitExceeded)
                ? ComplianceStatus.FAIL
                : ComplianceStatus.PASS;

            items.Add(new ComplianceCheckItem
            {
                CheckId = "BNBC-SYS-02",
                Category = "Seismic Detailing",
                Title = "Structural System Eligibility & Height Limit",
                Clause = "BNBC 2020 Part 6, Table 6.2.19",
                Status = sysStatus,
                MeasuredValue = $"{sys.Code} in Zone {district.Zone} (H = {project.BuildingHeightMeters:F1} m)",
                LimitValue = district.Zone == 4 ? "SMRF or Dual System Required" : "Permitted per Table 6.2.19",
                EngineeringRemarks = omrfProhibited
                    ? "Ordinary RC Moment Frame (OMRF) is not permitted in Seismic Zones 2, 3, or 4."
                    : (imrfProhibitedInZone4
                        ? "Intermediate RC Moment Frame (IMRF) is prohibited in Seismic Zone 4 (Z = 0.36). Use SMRF or Dual System."
                        : (heightLimitExceeded
                            ? "IMRF height exceeds 40m limit in Seismic Zone 3."
                            : "Structural system is permitted for the selected seismic zone and building height."))
            });
        }
        else
        {
            items.Add(new ComplianceCheckItem
            {
                CheckId = "BNBC-SYS-02",
                Category = "Seismic Detailing",
                Title = "Structural System Eligibility & Height Limit",
                Clause = "BNBC 2020 Part 6, Table 6.2.19",
                Status = ComplianceStatus.NOT_VERIFIED,
                MeasuredValue = "Missing System or District",
                LimitValue = "Valid BNBC System & District",
                EngineeringRemarks = "Select valid district and structural system."
            });
        }

        items.Add(new ComplianceCheckItem
        {
            CheckId = "BNBC-EQ-03",
            Category = "Seismic Base Shear",
            Title = "Minimum Design Base Shear Coefficient (Sa >= Sa,min)",
            Clause = "BNBC 2020 Part 6, Eq. 6.2.36",
            Status = !seismic.IsValid ? ComplianceStatus.NOT_VERIFIED : ComplianceStatus.PASS,
            MeasuredValue = seismic.IsValid ? $"Governing Sa = {seismic.GoverningBaseShearCoeff:F5} (V = {seismic.DesignBaseShearKn:F1} kN)" : "N/A",
            LimitValue = seismic.IsValid ? $"Sa,min = {seismic.MinimumBaseShearCoeffSaMin:F5}" : "N/A",
            EngineeringRemarks = seismic.IsValid
                ? (seismic.DesignBaseShearCoeffSa < seismic.MinimumBaseShearCoeffSaMin
                    ? "Minimum base shear limit Eq. 6.2.36 governs over period-dependent Sa."
                    : "Period-dependent Sa governs and exceeds Sa,min.")
                : seismic.ValidationMessage
        });

        bool hasStories = project.Stories.Count > 0 && project.Stories.All(s => s.SeismicWeightKn > 0);
        items.Add(new ComplianceCheckItem
        {
            CheckId = "BNBC-MOD-04",
            Category = "Model Integrity",
            Title = "Story Elevations & Seismic Mass Definition",
            Clause = "BNBC 2020 Part 6, Sec 2.5.5.1",
            Status = hasStories ? ComplianceStatus.PASS : ComplianceStatus.FAIL,
            MeasuredValue = $"{project.Stories.Count} stories (Total W = {project.Stories.Sum(s => s.SeismicWeightKn):F1} kN)",
            LimitValue = ">= 1 story with positive seismic weight",
            EngineeringRemarks = hasStories
                ? "All stories have positive seismic weight (DL + 25% LL minimum where applicable)."
                : "One or more stories have zero or undefined seismic weight."
        });

        double driftLimit = project.OccupancyCategory is "III" or "IV" ? 0.015 : 0.020;
        if (maxStoryDriftRatio.HasValue)
        {
            bool driftOk = maxStoryDriftRatio.Value <= driftLimit;
            items.Add(new ComplianceCheckItem
            {
                CheckId = "BNBC-DRF-05",
                Category = "Seismic Drift",
                Title = "Amplified Inelastic Story Drift Ratio (Cd * delta_e / h)",
                Clause = "BNBC 2020 Part 6, Table 6.2.21 & Eq. 6.2.44",
                Status = driftOk ? ComplianceStatus.PASS : ComplianceStatus.FAIL,
                MeasuredValue = $"{maxStoryDriftRatio.Value:F5}",
                LimitValue = $"<= {driftLimit:F3}",
                EngineeringRemarks = driftOk
                    ? "Amplified inelastic story drift is within BNBC 2020 Table 6.2.21 allowable limit."
                    : "Amplified story drift exceeds BNBC 2020 Table 6.2.21 limit. Increase lateral stiffness."
            });
        }
        else
        {
            items.Add(new ComplianceCheckItem
            {
                CheckId = "BNBC-DRF-05",
                Category = "Seismic Drift",
                Title = "Amplified Inelastic Story Drift Ratio (Cd * delta_e / h)",
                Clause = "BNBC 2020 Part 6, Table 6.2.21 & Eq. 6.2.44",
                Status = ComplianceStatus.NOT_VERIFIED,
                MeasuredValue = etabsConnected ? "Analysis Not Run / No Drift Data" : "ETABS Not Connected",
                LimitValue = $"<= {driftLimit:F3}",
                EngineeringRemarks = "Connect to an analyzed ETABS 22 model and extract story displacements to verify drift."
            });
        }

        if (maxWindDriftRatio.HasValue)
        {
            bool windOk = maxWindDriftRatio.Value <= 0.002;
            items.Add(new ComplianceCheckItem
            {
                CheckId = "BNBC-WND-06",
                Category = "Wind Serviceability",
                Title = "Overall Wind Sway Deflection Ratio (Delta_max / H)",
                Clause = "BNBC 2020 Part 6, Sec 1.5.6.2",
                Status = windOk ? ComplianceStatus.PASS : ComplianceStatus.FAIL,
                MeasuredValue = $"{maxWindDriftRatio.Value:F5} (H/{(maxWindDriftRatio.Value > 0 ? 1.0 / maxWindDriftRatio.Value : 9999):F0})",
                LimitValue = "<= 0.00200 (H/500)",
                EngineeringRemarks = windOk
                    ? "Top wind sway satisfies H/500 serviceability limit."
                    : "Top wind sway exceeds H/500 limit."
            });
        }
        else
        {
            items.Add(new ComplianceCheckItem
            {
                CheckId = "BNBC-WND-06",
                Category = "Wind Serviceability",
                Title = "Overall Wind Sway Deflection Ratio (Delta_max / H)",
                Clause = "BNBC 2020 Part 6, Sec 1.5.6.2",
                Status = ComplianceStatus.NOT_VERIFIED,
                MeasuredValue = "Not Extracted from ETABS",
                LimitValue = "<= 0.00200 (H/500)",
                EngineeringRemarks = "Run wind analysis in ETABS 22 and extract top joint deflection."
            });
        }

        if (torsionalIrregularityRatio.HasValue)
        {
            ComplianceStatus torStatus = torsionalIrregularityRatio.Value > 1.4
                ? ComplianceStatus.FAIL
                : (torsionalIrregularityRatio.Value > 1.2 ? ComplianceStatus.WARNING : ComplianceStatus.PASS);
            items.Add(new ComplianceCheckItem
            {
                CheckId = "BNBC-TOR-07",
                Category = "Plan Irregularity",
                Title = "Torsional Irregularity Ratio (Delta_max / Delta_avg)",
                Clause = "BNBC 2020 Part 6, Sec 2.5.3.2",
                Status = torStatus,
                MeasuredValue = $"{torsionalIrregularityRatio.Value:F3}",
                LimitValue = "<= 1.20 (Regular), <= 1.40 (Extreme Limit)",
                EngineeringRemarks = torsionalIrregularityRatio.Value > 1.4
                    ? "Extreme torsional irregularity (> 1.4). Rebalance shear walls/columns."
                    : (torsionalIrregularityRatio.Value > 1.2
                        ? "Torsional irregularity (> 1.2) detected. Apply accidental torsion amplification factor Ax."
                        : "Plan exhibits torsional regularity.")
            });
        }
        else
        {
            items.Add(new ComplianceCheckItem
            {
                CheckId = "BNBC-TOR-07",
                Category = "Plan Irregularity",
                Title = "Torsional Irregularity Ratio (Delta_max / Delta_avg)",
                Clause = "BNBC 2020 Part 6, Sec 2.5.3.2",
                Status = ComplianceStatus.NOT_VERIFIED,
                MeasuredValue = "Not Extracted from ETABS",
                LimitValue = "<= 1.20",
                EngineeringRemarks = "Requires diaphragm corner displacement results from ETABS 22."
            });
        }

        return items;
    }
}

public sealed class BnbcReportGenerator
{
    public string GenerateMarkdownReport(
        BuildingProject project,
        SeismicCalculationResult seismic,
        WindCalculationResult wind,
        IReadOnlyList<LoadCombinationDefinition> combinations,
        IReadOnlyList<ComplianceCheckItem> complianceItems,
        IReadOnlyList<FoundationReactionSummary> reactions)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# BNBC ULTIMATE PRO — Structural Engineering Calculation Report");
        sb.AppendLine($"**Design Code:** Bangladesh National Building Code (BNBC 2020), Part 6");
        sb.AppendLine($"**Generated On:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("## 1. Project & Building Metadata");
        sb.AppendLine($"- **Project Name:** {project.ProjectName} (`{project.ProjectNumber}`)");
        sb.AppendLine($"- **Client / Engineer:** {project.ClientName} / {project.EngineerName}");
        sb.AppendLine($"- **Location (District):** {project.District}");
        sb.AppendLine($"- **Geotechnical Soil Class:** {project.GeotechnicalSiteClass} (Ref: {project.GeotechnicalReportRef})");
        sb.AppendLine($"- **Occupancy Category:** {project.OccupancyCategory}");
        sb.AppendLine($"- **Structural System:** {project.StructuralSystemCode}");
        sb.AppendLine($"- **Dimensions (H x Lx x Ly):** {project.BuildingHeightMeters:F2} m x {project.PlanDimensionXMeters:F2} m x {project.PlanDimensionYMeters:F2} m");
        sb.AppendLine();

        sb.AppendLine("## 2. Seismic Hazard & Equivalent Static Analysis (BNBC 2020 Sec 2.5)");
        if (seismic.IsValid)
        {
            sb.AppendLine($"- **Seismic Zone & Z:** Zone {seismic.SeismicZone} (Z = {seismic.ZoneCoefficientZ:F2})");
            sb.AppendLine($"- **Soil Factor & Corner Periods:** S = {seismic.SoilFactorS:F2}, TB = {seismic.TbSeconds:F2} s, TC = {seismic.TcSeconds:F2} s, TD = {seismic.TdSeconds:F2} s");
            sb.AppendLine($"- **System Factors:** R = {seismic.ResponseReductionFactorR:F1}, Omega0 = {seismic.OverstrengthFactorOmega0:F1}, Cd = {seismic.DeflectionAmplificationCd:F1}, I = {seismic.ImportanceFactorI:F2}");
            sb.AppendLine($"- **Fundamental Period (Ta):** {seismic.FundamentalPeriodTaSeconds:F4} s");
            sb.AppendLine($"- **Governing Base Shear Coefficient (Sa):** {seismic.GoverningBaseShearCoeff:F5}");
            sb.AppendLine($"- **Total Seismic Weight (W):** {seismic.TotalSeismicWeightKn:F2} kN");
            sb.AppendLine($"- **Design Base Shear (V):** **{seismic.DesignBaseShearKn:F2} kN**");
            sb.AppendLine();
            sb.AppendLine("| Story | Elevation (m) | Weight W_i (kN) | C_vx | Lateral Force F_x (kN) | Story Shear V_x (kN) |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (var sf in seismic.StoryForces.OrderByDescending(x => x.ElevationMeters))
            {
                sb.AppendLine($"| {sf.StoryName} | {sf.ElevationMeters:F2} | {sf.WeightKn:F2} | {sf.DistributionFactorCvx:F4} | {sf.LateralForceFxKn:F2} | {sf.CumulativeStoryShearVxKn:F2} |");
            }
        }
        else
        {
            sb.AppendLine($"> **Validation Error:** {seismic.ValidationMessage}");
        }
        sb.AppendLine();

        sb.AppendLine("## 3. Wind Hazard Analysis (BNBC 2020 Sec 2.4)");
        if (wind.IsValid)
        {
            sb.AppendLine($"- **Basic Wind Speed (V):** {wind.BasicWindSpeedMs:F1} m/s (Exposure {project.WindExposure})");
            sb.AppendLine($"- **Factors:** I = {wind.WindImportanceFactorI:F2}, Kd = {wind.DirectionalityFactorKd:F2}, Kzt = {wind.TopographicFactorKzt:F2}, G = {wind.GustFactorG:F2}");
            sb.AppendLine($"- **Roof Velocity Pressure (qh):** {wind.RoofVelocityPressureQzKnM2:F4} kN/m2");
            sb.AppendLine($"- **Total Base Wind Force:** Fwx = {wind.TotalWindForceXKn:F2} kN, Fwy = {wind.TotalWindForceYKn:F2} kN");
        }
        sb.AppendLine();

        sb.AppendLine("## 4. BNBC 2020 Compliance Checklist");
        sb.AppendLine("| ID | Check | Clause | Status | Measured | Limit | Remarks |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var c in complianceItems)
        {
            sb.AppendLine($"| {c.CheckId} | {c.Title} | {c.Clause} | **{c.Status}** | {c.MeasuredValue} | {c.LimitValue} | {c.EngineeringRemarks} |");
        }
        sb.AppendLine();

        sb.AppendLine($"## 5. Generated Load Combinations ({combinations.Count} Total)");
        foreach (var combo in combinations)
        {
            string terms = string.Join(" + ", combo.Factors.Select(kv => $"{kv.Value:F3}({kv.Key})"));
            sb.AppendLine($"- **{combo.Name}** [{combo.DesignType}]: `{terms}` — *{combo.ClauseReference}*");
        }
        sb.AppendLine();

        if (reactions.Count > 0)
        {
            sb.AppendLine("## 6. Extracted Foundation Reactions");
            sb.AppendLine("| Joint | Combo | Fz (kN) | Fx (kN) | Fy (kN) | Mx (kN-m) | My (kN-m) |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (var r in reactions)
            {
                sb.AppendLine($"| {r.JointLabel} | {r.LoadCombination} | {r.FzKn:F2} | {r.FxKn:F2} | {r.FyKn:F2} | {r.MxKnM:F2} | {r.MyKnM:F2} |");
            }
        }

        return sb.ToString();
    }

    public void ExportToPdf(
        string outputPath,
        BuildingProject project,
        SeismicCalculationResult seismic,
        WindCalculationResult wind,
        IReadOnlyList<LoadCombinationDefinition> combinations,
        IReadOnlyList<ComplianceCheckItem> complianceItems,
        IReadOnlyList<FoundationReactionSummary> reactions)
    {
        var lines = new List<(string text, bool isHeader)>();
        lines.Add(("BNBC ULTIMATE PRO - STRUCTURAL CALCULATION REPORT", true));
        lines.Add(($"Code: Bangladesh National Building Code (BNBC 2020) Part 6 | Date: {DateTime.Now:yyyy-MM-dd}", false));
        lines.Add(($"Project: {project.ProjectName} ({project.ProjectNumber})", false));
        lines.Add(($"Engineer: {project.EngineerName} | District: {project.District} | Soil Class: {project.GeotechnicalSiteClass} ({project.GeotechnicalReportRef})", false));
        lines.Add(($"Occupancy Cat: {project.OccupancyCategory} | System: {project.StructuralSystemCode} | Height: {project.BuildingHeightMeters:F2} m", false));
        lines.Add(("", false));

        lines.Add(("1. SEISMIC HAZARD & BASE SHEAR (BNBC 2020 SEC 2.5)", true));
        if (seismic.IsValid)
        {
            lines.Add(($"Zone {seismic.SeismicZone} (Z={seismic.ZoneCoefficientZ:F2}), Site {seismic.SiteClass} (S={seismic.SoilFactorS:F2}), I={seismic.ImportanceFactorI:F2}, R={seismic.ResponseReductionFactorR:F1}, Cd={seismic.DeflectionAmplificationCd:F1}", false));
            lines.Add(($"Period Ta = {seismic.FundamentalPeriodTaSeconds:F4} s | Cs = {seismic.NormalizedAccelerationCs:F4} | Governing Sa = {seismic.GoverningBaseShearCoeff:F5}", false));
            lines.Add(($"Total Seismic Weight W = {seismic.TotalSeismicWeightKn:F2} kN | DESIGN BASE SHEAR V = {seismic.DesignBaseShearKn:F2} kN (SDC {seismic.SeismicDesignCategory})", false));
            foreach (var sf in seismic.StoryForces.OrderByDescending(s => s.ElevationMeters))
            {
                lines.Add(($"  {sf.StoryName,-10} | Elev: {sf.ElevationMeters,5:F2}m | Wi: {sf.WeightKn,8:F1} kN | Cvx: {sf.DistributionFactorCvx:F4} | Fx: {sf.LateralForceFxKn,7:F2} kN | Vx: {sf.CumulativeStoryShearVxKn,8:F2} kN", false));
            }
        }
        else
        {
            lines.Add(($"VALIDATION ALERT: {seismic.ValidationMessage}", false));
        }
        lines.Add(("", false));

        lines.Add(("2. WIND HAZARD SUMMARY (BNBC 2020 SEC 2.4)", true));
        if (wind.IsValid)
        {
            lines.Add(($"Basic Wind Speed V = {wind.BasicWindSpeedMs:F1} m/s | Exposure {project.WindExposure} | Roof qh = {wind.RoofVelocityPressureQzKnM2:F4} kN/m2", false));
            lines.Add(($"Total Base Wind Shear: Fwx = {wind.TotalWindForceXKn:F2} kN | Fwy = {wind.TotalWindForceYKn:F2} kN", false));
        }
        lines.Add(("", false));

        lines.Add(("3. BNBC 2020 COMPLIANCE VERIFICATION", true));
        foreach (var c in complianceItems)
        {
            lines.Add(($"[{c.Status}] {c.CheckId} - {c.Title} ({c.Clause}): {c.MeasuredValue} (Limit: {c.LimitValue})", false));
        }
        lines.Add(("", false));

        lines.Add(($"4. LOAD COMBINATIONS ({combinations.Count} GENERATED)", true));
        foreach (var cb in combinations.Take(18))
        {
            string terms = string.Join(" + ", cb.Factors.Select(kv => $"{kv.Value:F2}*{kv.Key}"));
            lines.Add(($"  {cb.Name}: {terms}", false));
        }
        if (combinations.Count > 18)
        {
            lines.Add(($"  ... and {combinations.Count - 18} additional serviceability/directional combinations.", false));
        }

        if (reactions.Count > 0)
        {
            lines.Add(("", false));
            lines.Add(("5. EXTRACTED BASE FOUNDATION REACTIONS", true));
            foreach (var r in reactions.Take(25))
            {
                lines.Add(($"  Joint {r.JointLabel,-8} | {r.LoadCombination,-20} | Fz={r.FzKn,8:F2} kN | Fx={r.FxKn,6:F2} | Fy={r.FyKn,6:F2} | Mx={r.MxKnM,6:F2} | My={r.MyKnM,6:F2}", false));
            }
        }

        const int linesPerPage = 48;
        var pages = new List<List<(string text, bool isHeader)>>();
        for (int i = 0; i < lines.Count; i += linesPerPage)
        {
            pages.Add(lines.Skip(i).Take(linesPerPage).ToList());
        }
        if (pages.Count == 0) pages.Add(new List<(string, bool)> { ("BNBC ULTIMATE PRO REPORT", true) });

        WriteMultiPagePdf(outputPath, pages);
    }

    private static void WriteMultiPagePdf(string path, List<List<(string text, bool isHeader)>> pages)
    {
        var objects = new List<string>();
        objects.Add("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj");

        int firstPageObjId = 5;
        var pageKids = new List<string>();
        for (int p = 0; p < pages.Count; p++)
        {
            pageKids.Add($"{firstPageObjId + p * 2} 0 R");
        }
        objects.Add($"2 0 obj << /Type /Pages /Kids [{string.Join(" ", pageKids)}] /Count {pages.Count} >> endobj");
        objects.Add("3 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Courier >> endobj");
        objects.Add("4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >> endobj");

        for (int p = 0; p < pages.Count; p++)
        {
            int pageObjId = firstPageObjId + p * 2;
            int contentObjId = pageObjId + 1;

            var streamSb = new StringBuilder();
            streamSb.AppendLine("BT");
            int y = 800;
            foreach (var (rawText, isHeader) in pages[p])
            {
                string safe = EscapePdfAscii(rawText);
                if (safe.Length > 98) safe = safe[..98];
                if (isHeader)
                {
                    streamSb.AppendLine($"/F2 11 Tf 36 {y} Td ({safe}) Tj");
                }
                else
                {
                    streamSb.AppendLine($"/F1 8.5 Tf 36 {y} Td ({safe}) Tj");
                }
                streamSb.AppendLine("0 0 Td");
                y -= 15;
            }
            streamSb.AppendLine($"/F1 8 Tf 36 28 Td (Page {p + 1} of {pages.Count} - Generated by BNBC ULTIMATE PRO) Tj");
            streamSb.AppendLine("ET");

            string streamContent = streamSb.ToString();
            byte[] streamBytes = Encoding.ASCII.GetBytes(streamContent);

            objects.Add($"{pageObjId} 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjId} 0 R >> endobj");
            objects.Add($"{contentObjId} 0 obj << /Length {streamBytes.Length} >> stream
{streamContent}endstream endobj");
        }

        using var ms = new MemoryStream();
        void WriteAscii(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        WriteAscii("%PDF-1.4
");
        var offsets = new List<long> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(ms.Position);
            WriteAscii(obj + "
");
        }

        long xrefPos = ms.Position;
        WriteAscii($"xref
0 {objects.Count + 1}
");
        WriteAscii("0000000000 65535 f 
");
        for (int i = 1; i < offsets.Count; i++)
        {
            WriteAscii($"{offsets[i]:D10} 00000 n 
");
        }
        WriteAscii($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>
startxref
{xrefPos}
%%EOF");

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static string EscapePdfAscii(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            char c = ch switch
            {
                '—' or '–' => '-',
                '²' => '2',
                '≥' => '>',
                '≤' => '<',
                _ => ch <= 126 && ch >= 32 ? ch : ' '
            };
            if (c == '(' || c == ')' || c == '\') sb.Append('\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public void ExportToXlsx(
        string outputPath,
        BuildingProject project,
        SeismicCalculationResult seismic,
        WindCalculationResult wind,
        IReadOnlyList<LoadCombinationDefinition> combinations,
        IReadOnlyList<ComplianceCheckItem> complianceItems,
        IReadOnlyList<FoundationReactionSummary> reactions)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        void AddEntry(string entryName, string xml)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }

        AddEntry("[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet2.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet3.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
</Types>");

        AddEntry("_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

        AddEntry("xl/workbook.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""BNBC_Summary"" sheetId=""1"" r:id=""rId1""/>
    <sheet name=""Lateral_Forces"" sheetId=""2"" r:id=""rId2""/>
    <sheet name=""Foundation_Reactions"" sheetId=""3"" r:id=""rId3""/>
  </sheets>
</workbook>");

        AddEntry("xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet2.xml""/>
  <Relationship Id=""rId3"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet3.xml""/>
  <Relationship Id=""rId4"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>");

        AddEntry("xl/styles.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""2"">
    <font><sz val=""11""/><name val=""Calibri""/></font>
    <font><b/><sz val=""11""/><color rgb=""FFFFFFFF""/><name val=""Calibri""/></font>
  </fonts>
  <fills count=""3"">
    <fill><patternFill patternType=""none""/></fill>
    <fill><patternFill patternType=""gray125""/></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FF1B365D""/><bgColor indexed=""64""/></patternFill></fill>
  </fills>
  <borders count=""1""><border><left/><right/><top/><bottom/><diagonal/></border></borders>
  <cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>
  <cellXfs count=""2"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/>
    <xf numFmtId=""0"" fontId=""1"" fillId=""2"" borderId=""0"" xfId=""0"" applyFont=""1"" applyFill=""1""/>
  </cellXfs>
</styleSheet>");

        var s1Rows = new List<object[]>
        {
            new object[] { "BNBC ULTIMATE PRO - BNBC 2020 STRUCTURAL SUMMARY", "", "", "" },
            new object[] { "Project Name", project.ProjectName, "Project Code", project.ProjectNumber },
            new object[] { "District", project.District, "Soil Site Class", project.GeotechnicalSiteClass },
            new object[] { "Seismic Zone", seismic.SeismicZone, "Zone Factor Z", seismic.ZoneCoefficientZ },
            new object[] { "Design Base Shear V (kN)", seismic.DesignBaseShearKn, "Governing Sa", seismic.GoverningBaseShearCoeff },
            new object[] { "Basic Wind Speed (m/s)", wind.BasicWindSpeedMs, "Base Wind Shear X (kN)", wind.TotalWindForceXKn },
            new object[] { "", "", "", "" },
            new object[] { "Check ID", "Compliance Check", "Status", "Measured Value" }
        };
        foreach (var c in complianceItems)
        {
            s1Rows.Add(new object[] { c.CheckId, c.Title, c.Status.ToString(), c.MeasuredValue });
        }
        AddEntry("xl/worksheets/sheet1.xml", BuildWorksheetXml(s1Rows, headerRowIndices: new HashSet<int> { 0, 7 }));

        var s2Rows = new List<object[]>
        {
            new object[] { "Story", "Elevation (m)", "Seismic Weight (kN)", "Cvx", "Seismic Fx (kN)", "Story Shear Vx (kN)" }
        };
        foreach (var sf in seismic.StoryForces.OrderByDescending(x => x.ElevationMeters))
        {
            s2Rows.Add(new object[] { sf.StoryName, sf.ElevationMeters, sf.WeightKn, sf.DistributionFactorCvx, sf.LateralForceFxKn, sf.CumulativeStoryShearVxKn });
        }
        AddEntry("xl/worksheets/sheet2.xml", BuildWorksheetXml(s2Rows, headerRowIndices: new HashSet<int> { 0 }));

        var s3Rows = new List<object[]>
        {
            new object[] { "Joint Label", "Story", "Load Combination", "Fx (kN)", "Fy (kN)", "Fz (kN)", "Mx (kN-m)", "My (kN-m)", "Mz (kN-m)" }
        };
        foreach (var r in reactions)
        {
            s3Rows.Add(new object[] { r.JointLabel, r.StoryName, r.LoadCombination, r.FxKn, r.FyKn, r.FzKn, r.MxKnM, r.MyKnM, r.MzKnM });
        }
        AddEntry("xl/worksheets/sheet3.xml", BuildWorksheetXml(s3Rows, headerRowIndices: new HashSet<int> { 0 }));
    }

    private static string BuildWorksheetXml(List<object[]> rows, HashSet<int> headerRowIndices)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
        sb.AppendLine(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");

        for (int r = 0; r < rows.Count; r++)
        {
            int rowNum = r + 1;
            int styleIdx = headerRowIndices.Contains(r) ? 1 : 0;
            sb.Append($"<row r="{rowNum}">");
            var row = rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                string colRef = $"{(char)('A' + c)}{rowNum}";
                object val = row[c] ?? string.Empty;
                if (val is int or long or float or double or decimal)
                {
                    string numStr = Convert.ToDouble(val, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    sb.Append($"<c r="{colRef}" s="{styleIdx}"><v>{numStr}</v></c>");
                }
                else
                {
                    string escaped = SecurityElement.Escape(val.ToString()) ?? string.Empty;
                    sb.Append($"<c r="{colRef}" t="inlineStr" s="{styleIdx}"><is><t>{escaped}</t></is></c>");
                }
            }
            sb.AppendLine("</row>");
        }

        sb.AppendLine("</sheetData></worksheet>");
        return sb.ToString();
    }
}
