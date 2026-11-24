using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BnbcUltimatePro.Core.Models;

namespace BnbcUltimatePro.Core.Services;

public sealed class BnbcDatabaseProvider
{
    private static readonly Lazy<BnbcDatabase> _lazyDb = new(LoadEmbeddedDatabase);

    public BnbcDatabase Database => _lazyDb.Value;

    private static BnbcDatabase LoadEmbeddedDatabase()
    {
        var assembly = typeof(BnbcDatabaseProvider).GetTypeInfo().Assembly;
        const string resourceName = "BnbcUltimatePro.Core.Data.bnbc2020_database.json";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded BNBC 2020 database resource '{resourceName}' was not found.");
        }

        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var db = JsonSerializer.Deserialize<BnbcDatabase>(json, options)
                 ?? throw new InvalidOperationException("Failed to deserialize BNBC 2020 database.");
        return db;
    }

    public DistrictZoneInfo? GetDistrictInfo(string districtName)
    {
        if (string.IsNullOrWhiteSpace(districtName)) return null;
        return Database.DistrictZones.FirstOrDefault(d =>
            string.Equals(d.District.Trim(), districtName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public SeismicZoneInfo? GetSeismicZone(int zone)
    {
        return Database.SeismicZones.FirstOrDefault(z => z.Zone == zone);
    }

    public SiteClassInfo? GetSiteClass(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return Database.SiteClasses.FirstOrDefault(s =>
            string.Equals(s.Code.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public OccupancyCategoryInfo? GetOccupancyCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        return Database.OccupancyCategories.FirstOrDefault(o =>
            string.Equals(o.Category.Trim(), category.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public StructuralSystemInfo? GetStructuralSystem(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return Database.StructuralSystems.FirstOrDefault(s =>
            string.Equals(s.Code.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
