using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Dynastream.Fit;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Catalog;

namespace TrailQuest.Api.Infrastructure.Garmin;

public sealed record DecodedActivity(string Format, string Hash, IReadOnlyList<TrackPointDto> Track,
    IReadOnlyList<ExerciseDto> Exercises, DateTimeOffset? StartedAtUtc, string? Sport, double? DurationSeconds, double? DistanceMeters);

public static class ActivityFileReader
{
    public const int MaximumBytes = 32 * 1024 * 1024;

    public static DecodedActivity Read(byte[] original)
    {
        if (original.Length is 0 or > MaximumBytes) throw Invalid();
        try
        {
            var bytes = original;
            if (bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K')
            {
                using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                if (archive.Entries.Count > 32 || archive.Entries.Sum(x => x.Length) > MaximumBytes) throw Invalid();
                var entry = archive.Entries.FirstOrDefault(x => new[] { ".fit", ".gpx", ".tcx" }.Contains(Path.GetExtension(x.Name).ToLowerInvariant()))
                    ?? throw new AppException("activity_format", 400, "The archive has no supported FIT, GPX or TCX activity.");
                if (entry.Length > MaximumBytes || entry.Length > Math.Max(entry.CompressedLength, 1) * 200) throw Invalid();
                using var stream = entry.Open();
                using var output = new MemoryStream();
                var chunk = new byte[16384];
                int read;
                while ((read = stream.Read(chunk)) > 0) { if (output.Length + read > MaximumBytes) throw Invalid(); output.Write(chunk, 0, read); }
                bytes = output.ToArray(); // Never extract archive paths to the filesystem.
            }
            return bytes.Length > 12 && bytes.AsSpan(8, 4).SequenceEqual(".FIT"u8) ? ReadFit(bytes) : ReadXml(bytes);
        }
        catch (AppException) { throw; }
        catch (Exception error) when (error is InvalidDataException or XmlException or FitException or FormatException or ArgumentException or OverflowException)
        { throw Invalid(); }
    }

    private static DecodedActivity ReadFit(byte[] bytes)
    {
        var points = new List<TrackPointDto>();
        var exercises = new List<ExerciseDto>();
        DateTimeOffset? started = null;
        string? sport = null;
        double? duration = null, distance = null;
        var decoder = new Decode();
        var messages = 0;
        decoder.MesgEvent += (_, args) =>
        {
            if (++messages > 1_000_000) throw Invalid();
            if (args.mesg.Num == MesgNum.Record)
            {
                var record = new RecordMesg(args.mesg);
                var lat = record.GetPositionLat(); var lon = record.GetPositionLong();
                if (lat is not null && lon is not null)
                {
                    var latitude = lat.Value * 180.0 / 2147483648.0;
                    var longitude = lon.Value * 180.0 / 2147483648.0;
                    var time = record.GetTimestamp()?.GetDateTime();
                    AddPoint(points, new(latitude, longitude, time is null ? null : new DateTimeOffset(System.DateTime.SpecifyKind(time.Value, DateTimeKind.Utc)), record.GetEnhancedAltitude() ?? record.GetAltitude()));
                }
            }
            else if (args.mesg.Num == MesgNum.Set)
            {
                var set = new SetMesg(args.mesg);
                if (set.GetSetType() != SetType.Active || set.GetRepetitions() is not > 0) return;
                var reps = set.GetRepetitions(); var weight = set.GetWeight();
                var category = set.GetCategory(0);
                var name = typeof(ExerciseCategory).GetFields().FirstOrDefault(f => f.FieldType == typeof(ushort) && Equals(f.GetValue(null), category))?.Name ?? "Ejercicio sin clasificar";
                exercises.Add(new(name, 1, reps, weight, weight is > 0 ? reps * weight : null));
            }
            else if (args.mesg.Num == MesgNum.Session)
            {
                var session = new SessionMesg(args.mesg);
                var time = session.GetStartTime()?.GetDateTime();
                started = time is null ? null : new DateTimeOffset(System.DateTime.SpecifyKind(time.Value, DateTimeKind.Utc));
                sport = session.GetSubSport()?.ToString() ?? session.GetSport()?.ToString();
                duration = session.GetTotalTimerTime(); distance = session.GetTotalDistance();
            }
        };
        using var stream = new MemoryStream(bytes);
        if (!decoder.IsFIT(stream)) throw Invalid();
        stream.Position = 0;
        if (!decoder.CheckIntegrity(stream)) throw Invalid();
        stream.Position = 0;
        if (!decoder.Read(stream)) throw Invalid();
        return new("fit", Convert.ToHexString(SHA256.HashData(bytes)), points, exercises, started ?? points.FirstOrDefault()?.AtUtc, sport, duration, distance);
    }

    private static DecodedActivity ReadXml(byte[] bytes)
    {
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes });
        var document = XDocument.Load(reader);
        var format = document.Root?.Name.LocalName.ToLowerInvariant();
        if (format is not ("gpx" or "trainingcenterdatabase")) throw new AppException("activity_format", 400, "Use a FIT, GPX or TCX activity file.");
        var points = new List<TrackPointDto>();
        foreach (var node in document.Descendants().Where(x => x.Name.LocalName is "trkpt" or "Trackpoint"))
        {
            string? Child(string name) => node.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value;
            var latitude = node.Attribute("lat")?.Value ?? Child("LatitudeDegrees");
            var longitude = node.Attribute("lon")?.Value ?? Child("LongitudeDegrees");
            if (!double.TryParse(latitude, CultureInfo.InvariantCulture, out var lat) || !double.TryParse(longitude, CultureInfo.InvariantCulture, out var lon)) continue;
            var timeText = Child("time") ?? Child("Time");
            DateTimeOffset? at = DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) ? timestamp.ToUniversalTime() : null;
            double? elevation = double.TryParse(Child("ele") ?? Child("AltitudeMeters"), CultureInfo.InvariantCulture, out var height) ? height : null;
            AddPoint(points, new(lat, lon, at, elevation));
        }
        if (points.Count == 0) throw new AppException("activity_no_track", 400, "This file has no usable GPS track.");
        var duration = points.First().AtUtc is { } first && points.Last().AtUtc is { } last && last >= first ? (last - first).TotalSeconds : (double?)null;
        var distance = points.Zip(points.Skip(1), Distance).Sum();
        return new(format == "gpx" ? "gpx" : "tcx", Convert.ToHexString(SHA256.HashData(bytes)), points, [], points.First().AtUtc, null, duration, distance);
    }

    private static void AddPoint(List<TrackPointDto> points, TrackPointDto point)
    {
        if (points.Count >= 100_000 || !double.IsFinite(point.Latitude) || !double.IsFinite(point.Longitude) || point.Latitude is < -90 or > 90 || point.Longitude is < -180 or > 180 ||
            (point.ElevationMeters.HasValue && !double.IsFinite(point.ElevationMeters.Value))) throw Invalid();
        points.Add(point);
    }
    public static double Distance(TrackPointDto a, TrackPointDto b)
    {
        const double radians = Math.PI / 180;
        var x = Math.Pow(Math.Sin((b.Latitude - a.Latitude) * radians / 2), 2) +
            Math.Cos(a.Latitude * radians) * Math.Cos(b.Latitude * radians) * Math.Pow(Math.Sin((b.Longitude - a.Longitude) * radians / 2), 2);
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(x, 0, 1)));
    }
    private static AppException Invalid() => new("activity_file_invalid", 400, "The activity file is invalid or exceeds the safe import limits.");
}
