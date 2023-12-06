using System;

namespace Unlimotion.ViewModel;

/// <summary>
/// Вспомогательный класс для работы с датами
/// </summary>
public static class DateEx
{
    /// <summary>
    /// Завтра
    /// </summary>
    public static DateTime Tomorrow => DateTime.Today.AddDays(1);

    /// <summary>
    /// Следуюший понедельник
    /// </summary>
    public static DateTime NextMonday
    {
        get
        {
            var select = DateTime.Today.AddDays(1);
            while (select.DayOfWeek != DayOfWeek.Monday)
            {
                select = select.AddDays(1);
            }
            return select;
        }
    }

    /// <summary>
    /// Следующая пятница
    /// </summary>
    public static DateTime NextFriday
    {
        get
        {
            var select = DateTime.Today.AddDays(1);
            while (select.DayOfWeek != DayOfWeek.Friday)
            {
                select = select.AddDays(1);
            }
            return select;
        }
    }

    /// <summary>
    /// Первый день следующего месяца
    /// </summary>
    public static DateTime FirstDayNextMonth
    {
        get
        {
            var today = DateTime.Today;
            var select = new DateTime(today.Year, today.Month, 1).AddMonths(1);
            return select;
        }
    }
}