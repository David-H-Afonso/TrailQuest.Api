using System.Globalization;
using System.Text.Json;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;

namespace TrailQuest.Api.Infrastructure.Garmin;

public static class HealthNormalizer
{
    public static JsonElement At(JsonElement root, string path)
    {
        foreach (var part in path.Split('.'))
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(part, out root)) return default;
        return root;
    }
    public static double? Number(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            var value = At(root, path);
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) return number;
        }
        return null;
    }
    public static string? Text(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            var value = At(root, path);
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
        }
        return null;
    }
    public static IReadOnlyList<JsonElement> Items(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().Select(x => x.Clone()).ToList();
        foreach (var key in new[] { "data", "dailySleepDTOs", "hrvSummaries", "dateWeightList", "dailyWeightSummaries" })
            if (At(root, key).ValueKind == JsonValueKind.Array) return At(root, key).EnumerateArray().Select(x => x.Clone()).ToList();
        return root.ValueKind == JsonValueKind.Object ? [root] : [];
    }

    public static DateTimeOffset? Timestamp(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            try { return number > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)) return time;
        return null;
    }

    public static IReadOnlyList<MetricDto> Metrics(HealthDocument document)
    {
        using var json = JsonDocument.Parse(document.PayloadJson);
        var root = json.RootElement;
        if (At(root, "dailySleepDTO").ValueKind == JsonValueKind.Object) root = At(root, "dailySleepDTO");
        var metrics = new List<MetricDto>();
        void Add(string id, string label, string unit, double scale, params string[] paths)
        {
            var value = Number(root, paths);
            if (value is >= 0) metrics.Add(new(id, label, Math.Round(value.Value * scale, 2), unit, document.Date, document.Source));
        }
        switch (document.Dataset)
        {
            case "daily-summary": case "wellness-day":
                Add("calories-total", "Energía total", "kcal", 1, "calories.burnedTotal", "totalKilocalories");
                Add("calories-active", "Energía activa", "kcal", 1, "calories.burnedActive", "activeKilocalories");
                Add("calories-resting", "Energía en reposo", "kcal", 1, "calories.burnedResting", "bmrKilocalories");
                Add("resting-heart-rate", "Pulso en reposo", "ppm", 1, "heartRate.restingValue", "restingHeartRate");
                Add("stress", "Estrés estimado", "/100", 1, "stress.avgValue", "stress.averageValue", "averageStressLevel");
                Add("body-battery", "Body Battery", "/100", 1, "bodyBattery.latestValue");
                Add("hydration", "Agua registrada", "ml", 1, "hydration.consumedInFractionalMl");
                Add("intensity-moderate", "Intensidad moderada", "min", 1, "moderateIntensityMinutes");
                Add("intensity-vigorous", "Intensidad vigorosa", "min", 1, "vigorousIntensityMinutes");
                break;
            case "sleep":
                Add("sleep-hours", "Tiempo de sueño", "h", 1.0 / 3600, "sleepTimeSeconds");
                Add("sleep-score", "Puntuación del sueño", "/100", 1, "sleepScores.overall.value");
                Add("sleep-deep", "Sueño profundo", "min", 1.0 / 60, "deepSleepSeconds");
                Add("sleep-light", "Sueño ligero", "min", 1.0 / 60, "lightSleepSeconds");
                Add("sleep-rem", "Sueño REM", "min", 1.0 / 60, "remSleepSeconds");
                Add("sleep-awake", "Despierto durante el sueño", "min", 1.0 / 60, "awakeSleepSeconds");
                Add("sleep-naps", "Siestas", "min", 1.0 / 60, "napTimeSeconds");
                break;
            case "heart-rate":
                Add("resting-heart-rate", "Pulso en reposo", "ppm", 1, "restingHeartRate");
                Add("heart-rate-max", "Pulso máximo", "ppm", 1, "maxHeartRate");
                Add("heart-rate-min", "Pulso mínimo", "ppm", 1, "minHeartRate");
                break;
            case "hrv": case "hrv-summary":
                Add("hrv", "HRV nocturna", "ms", 1, "hrvSummary.lastNightAvg", "lastNightAvg");
                Add("hrv-weekly", "HRV media semanal", "ms", 1, "hrvSummary.weeklyAvg", "weeklyAvg");
                break;
            case "body-battery":
                Add("body-battery-charged", "Energía recuperada", "puntos", 1, "charged");
                Add("body-battery-drained", "Energía consumida", "puntos", 1, "drained");
                var samples = Series(document).FirstOrDefault()?.Samples;
                if (samples?.Count > 0) metrics.Add(new("body-battery", "Body Battery", samples.Last().Value, "/100", document.Date, document.Source));
                break;
            case "hydration": Add("hydration", "Agua registrada", "ml", 1, "valueInML", "valueInMl", "totalIntakeInML"); break;
            case "weight": case "weight-summary": Add("weight", "Peso registrado", "kg", .001, "weight"); break;
        }
        return metrics;
    }

    public static IReadOnlyList<MetricSeriesDto> Series(HealthDocument document)
    {
        using var json = JsonDocument.Parse(document.PayloadJson);
        var root = json.RootElement;
        var samples = new List<MetricSampleDto>();
        var id = document.Dataset;
        var name = id switch { "heart-rate" => "Pulso", "body-battery" => "Body Battery", "hrv" => "HRV", _ => "" };
        if (name.Length == 0) return [];
        var array = At(root, id switch { "heart-rate" => "heartRateValues", "body-battery" => "bodyBatteryValuesArray", _ => "hrvReadings" });
        var timestampIndex = 0;
        var valueIndex = 1;
        var descriptors = At(root, id == "body-battery" ? "bodyBatteryValueDescriptorDTOList" : "heartRateValueDescriptors");
        if (descriptors.ValueKind == JsonValueKind.Array)
            foreach (var descriptor in descriptors.EnumerateArray())
            {
                var key = Text(descriptor, "bodyBatteryValueDescriptorKey", "key") ?? "";
                var index = (int?)Number(descriptor, "bodyBatteryValueDescriptorIndex", "index");
                if (index is null) continue;
                if (key.Contains("timestamp", StringComparison.OrdinalIgnoreCase)) timestampIndex = index.Value;
                if (key is "bodyBatteryLevel" or "heartrate" or "heartRate") valueIndex = index.Value;
            }
        if (array.ValueKind == JsonValueKind.Array)
            foreach (var item in array.EnumerateArray())
            {
                var at = id == "hrv" ? Timestamp(At(item, "readingTimeGMT")) :
                    item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > timestampIndex ? Timestamp(item[timestampIndex]) : null;
                var value = id == "hrv" ? Number(item, "hrvValue") :
                    item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > valueIndex && item[valueIndex].ValueKind == JsonValueKind.Number && item[valueIndex].TryGetDouble(out var number) ? number : (double?)null;
                if (at is not null && value is >= 0 && double.IsFinite(value.Value)) samples.Add(new(at.Value, value.Value));
            }
        return samples.Count == 0 ? [] : [new(id, name, id == "heart-rate" ? "ppm" : id == "hrv" ? "ms" : "/100", samples.OrderBy(x => x.AtUtc).ToList())];
    }
}
