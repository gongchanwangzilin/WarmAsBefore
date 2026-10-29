using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.RealWorld;


public sealed class TimeProvider
{
    public TimeOfDayInfo Now() => new() { Now = DateTime.Now, Holiday = GetHoliday() };

    private static string? GetHoliday() => DateTime.Now switch
    {
        { Month: 1, Day: 1 } => "元旦",
        { Month: 2, Day: 14 } => "情人节",
        { Month: 3, Day: 8 } => "妇女节",
        { Month: 5, Day: 1 } => "劳动节",
        { Month: 10, Day: 1 } => "国庆节",
        { Month: 12, Day: 25 } => "圣诞节",
        _ => null
    };
}
