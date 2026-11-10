namespace TrailQuest.Api.Domain;

public static class TimeZoneIds
{
    public static string ForClient(string id) => TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) ? iana : id;
}
