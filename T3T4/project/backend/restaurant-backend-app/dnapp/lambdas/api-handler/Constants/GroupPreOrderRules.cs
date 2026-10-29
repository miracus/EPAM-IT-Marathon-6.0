using APIHandler.Constants.Dishes;
using APIHandler.Entities;
using System;
using System.Globalization;

namespace APIHandler.Constants;

/// <summary>Business rules of the group pre-order extensions (US13–US21).</summary>
public static class GroupPreOrderRules
{
    /// <summary>Default cut-off: changes close this many hours before the reservation (US13). Override with GROUP_CUTOFF_HOURS.</summary>
    public const double DefaultCutoffHours = 2;

    /// <summary>Auto-reminder goes out this many minutes before the cut-off (US14 AC3).</summary>
    public const int AutoRemindMinutesBeforeCutoff = 30;

    /// <summary>At most one manual reminder per guest within this period (US14 AC4).</summary>
    public const int ReminderCooldownMinutes = 15;

    public const int NoteMaxLength = 80;

    public const int AllergiesMaxLength = 120;

    public static double CutoffHours
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable("GROUP_CUTOFF_HOURS");
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hours) && hours >= 0
                ? hours
                : DefaultCutoffHours;
        }
    }

    /// <summary>
    /// Reservation times are stored without a time zone and treated as UTC everywhere in the backend
    /// (see ReservationService / OrderService), so an unspecified kind is read as UTC here as well.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>The cut-off in the same representation as the reservation start time (for the API).</summary>
    public static DateTime CutoffAt(Reservation reservation) => reservation.StartTime.AddHours(-CutoffHours);

    public static DateTime CutoffAtUtc(Reservation reservation) => AsUtc(reservation.StartTime).AddHours(-CutoffHours);

    public static bool IsCutoffPassed(Reservation reservation, DateTime utcNow) => utcNow >= CutoffAtUtc(reservation);

    public static long SecondsUntilCutoff(Reservation reservation, DateTime utcNow)
        => Math.Max(0, (long)Math.Floor((CutoffAtUtc(reservation) - utcNow).TotalSeconds));

    /// <summary>US19 AC2: desserts are served after mains by default, everything else on arrival.</summary>
    public static string DefaultServeTime(DishType dishType)
        => dishType == DishType.Dessert ? ServeTimes.AfterMains : ServeTimes.OnArrival;
}

public static class ServeTimes
{
    public const string OnArrival = "OnArrival";
    public const string AfterMains = "AfterMains";

    public static bool IsValid(string? value) => value is OnArrival or AfterMains;
}

public static class GuestNoticeTypes
{
    public const string DishRemoved = "DishRemoved";
    public const string Reminder = "Reminder";
    public const string ChangeRequested = "ChangeRequested";
    public const string PaymentRequested = "PaymentRequested";
}

public static class PaymentModes
{
    public const string EachPaysOwn = "EachPaysOwn";
    public const string OwnerPaysAll = "OwnerPaysAll";
    public const string SplitEqually = "SplitEqually";

    public static bool IsValid(string? value) => value is EachPaysOwn or OwnerPaysAll or SplitEqually;
}
