using Dynastream.Fit;
using TrailQuest.Api.Application;
using TrailQuest.Api.Infrastructure.Garmin;

namespace TrailQuest.Api.Tests;

public sealed class FitReaderTests
{
    private static byte[] SyntheticFit()
    {
        using var output = new MemoryStream();
        var encoder = new Encode(ProtocolVersion.V20); encoder.Open(output);
        var file = new FileIdMesg(); file.SetType(Dynastream.Fit.File.Activity); file.SetManufacturer(Manufacturer.Development); file.SetProduct(1); encoder.Write(file);
        var time = new Dynastream.Fit.DateTime(System.DateTime.SpecifyKind(new System.DateTime(2026,11,9,8,0,0), DateTimeKind.Utc));
        var session = new SessionMesg(); session.SetStartTime(time); session.SetSport(Sport.Training); session.SetSubSport(SubSport.StrengthTraining); session.SetTotalTimerTime(1200); encoder.Write(session);
        var record = new RecordMesg(); record.SetTimestamp(time); record.SetPositionLat((int)(28.45 / 180 * 2147483648)); record.SetPositionLong((int)(-16.25 / 180 * 2147483648)); encoder.Write(record);
        var second = new RecordMesg(); second.SetTimestamp(time); second.SetPositionLat((int)(28.46 / 180 * 2147483648)); second.SetPositionLong((int)(-16.24 / 180 * 2147483648)); encoder.Write(second);
        var set = new SetMesg(); set.SetSetType(SetType.Active); set.SetRepetitions(10); set.SetWeight(25); set.SetCategory(0, ExerciseCategory.BenchPress); encoder.Write(set);
        var rest = new SetMesg(); rest.SetSetType(SetType.Rest); rest.SetRepetitions(999); encoder.Write(rest);
        encoder.Close(); return output.ToArray();
    }
    [Fact]
    public void Official_decoder_reads_recorded_GPS_and_active_sets_without_counting_rest()
    {
        var result = ActivityFileReader.Read(SyntheticFit());
        Assert.Equal("fit", result.Format); Assert.Equal(2, result.Track.Count);
        Assert.Equal(28.45, result.Track[0].Latitude, 5);
        var exercise = Assert.Single(result.Exercises);
        Assert.Equal(10, exercise.Repetitions); Assert.Equal(250, exercise.VolumeKg);
        Assert.Equal("BenchPress", exercise.Name); Assert.Equal(1200, result.DurationSeconds);
    }
    [Fact]
    public void Corrupt_FIT_is_rejected_without_using_partial_measurements()
    {
        var bytes = SyntheticFit(); bytes[^1] ^= 0xff;
        Assert.Throws<AppException>(() => ActivityFileReader.Read(bytes));
    }
}
