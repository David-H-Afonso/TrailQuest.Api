using TrailQuest.Api.Contracts;

namespace TrailQuest.Api.Infrastructure.Garmin;

public sealed record GarminDataRequest(string Path, DateOnly Date, string Key);

public static class GarminDatasets
{
    public static readonly IReadOnlyList<DatasetDto> All =
    [
        new("steps", "Pasos y distancia", "Totales diarios, sin sumar de nuevo las actividades GPS.", false, "Diaria"),
        new("activities", "Actividades y fuerza", "Actividades, detalles y entrenamiento estructurado; originales si lo autorizas.", false, "Por actividad"),
        new("daily-summary", "Resumen de bienestar", "Calorías, movimiento, intensidad, estrés y secciones disponibles.", false, "Diaria"),
        new("wellness-day", "Resumen diario ampliado", "Conserva también la respuesta del resumen diario individual.", false, "Diaria"),
        new("sleep", "Sueño", "Duración, fases, horarios, siestas y puntuaciones disponibles.", false, "Diaria"),
        new("heart-rate", "Frecuencia cardiaca", "Resúmenes y lecturas con hora; no es un sensor en directo.", false, "Muestras temporales"),
        new("hrv", "Variabilidad cardiaca", "Resumen y lecturas HRV registradas durante el descanso.", false, "Muestras temporales"),
        new("hrv-summary", "Histórico de HRV", "Resumen de variabilidad por periodo.", false, "Diaria"),
        new("body-battery", "Body Battery", "Estimación de energía, lecturas y eventos. No mide batería social.", false, "Muestras temporales"),
        new("stress", "Estrés", "Estadísticas de estrés estimadas por Garmin.", false, "Diaria"),
        new("spo2", "Oxígeno y aclimatación", "Datos disponibles según dispositivo y configuración.", false, "Según registro"),
        new("hydration", "Hidratación", "Agua registrada y objetivos disponibles.", false, "Diaria"),
        new("weight", "Peso y composición", "Entradas individuales de peso y campos registrados.", true, "Por registro"),
        new("weight-summary", "Resumen de peso", "Resúmenes diarios y de periodo.", true, "Diaria"),
        new("weight-goals", "Objetivos de peso", "Objetivos efectivos durante el periodo.", true, "Por periodo"),
        new("nutrition", "Alimentación", "Solo registros y nutrientes que existan en Garmin.", true, "Diaria"),
        new("fitness-age", "Edad de condición física", "Estimación de Garmin, no diagnóstico.", false, "Según registro"),
        new("heart-zones", "Zonas de pulso", "Configuración de zonas, no umbrales clínicos.", false, "Configuración"),
        new("wellness-activities", "Movimiento detectado", "Resúmenes de actividad de bienestar; no se suman a los pasos.", false, "Por registro"),
        new("profile-settings", "Preferencias de Garmin", "Datos de perfil y configuración; privados.", true, "Configuración"),
        new("cycle-summary", "Salud menstrual · resumen", "Información especialmente sensible. Solo con selección expresa.", true, "Diaria"),
        new("cycle-detail", "Salud menstrual · detalle", "Información especialmente sensible. Solo con selección expresa.", true, "Diaria")
    ];

    public static IEnumerable<GarminDataRequest> Requests(string id, DateOnly start, DateOnly end)
    {
        var from = start.ToString("yyyy-MM-dd");
        var until = end.ToString("yyyy-MM-dd");
        string? rangePath = id switch
        {
            "daily-summary" => $"/usersummary-service/v2/dailysummaries?startDate={from}&endDate={until}",
            "sleep" => $"/sleep-service/sleep/dailySleepSummariesByDate?startDate={from}&endDate={until}",
            "hrv-summary" => $"/hrv-service/hrv/daily/{from}/{until}",
            "body-battery" => $"/wellness-service/wellness/bodyBattery/reports/daily?startDate={from}&endDate={until}",
            "stress" => $"/usersummary-service/stats/stress/daily/{from}/{until}",
            "hydration" => $"/usersummary-service/stats/hydration/daily/{from}/{until}",
            "weight" => $"/weight-service/weight/all/{from}/{until}",
            "weight-summary" => $"/weight-service/weight/range/{from}/{until}?includeAll=true",
            "weight-goals" => $"/goal-service/goal/user/effective/weightgoal/{from}/{until}",
            "wellness-activities" => $"/wellnessactivity-service/activity/summary/range/{from}/{until}",
            "heart-zones" => "/biometric-service/heartRateZones/v2?generateDefaults=false",
            "profile-settings" => "/userprofile-service/userprofile/settings",
            _ => null
        };
        if (rangePath is not null) { yield return new(rangePath, start, $"{from}/{until}"); yield break; }
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var day = date.ToString("yyyy-MM-dd");
            var path = id switch
            {
                "wellness-day" => $"/usersummary-service/usersummary/daily?calendarDate={day}",
                "heart-rate" => $"/wellness-service/wellness/dailyHeartRate?date={day}",
                "hrv" => $"/hrv-service/hrv/{day}",
                "spo2" => $"/wellness-service/wellness/daily/spo2acclimation/{day}",
                "nutrition" => $"/nutrition-service/food/logs/{day}",
                "fitness-age" => $"/fitnessage-service/fitnessage/v2/{day}",
                "cycle-summary" => $"/periodichealth-service/menstrualcycle/summary/{day}",
                "cycle-detail" => $"/periodichealth-service/menstrualcycle/dayview/{day}?todayDate={until}",
                _ => throw new InvalidOperationException("Unknown Garmin dataset.")
            };
            yield return new(path, date, day);
        }
    }
}
