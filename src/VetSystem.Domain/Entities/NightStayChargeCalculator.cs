namespace VetSystem.Domain.Entities;

/// <summary>
/// Pure hotel-style night-count math for a boarding episode (مبيت, PRD §18.6). Nights are billed
/// against a configurable daily checkout time: a guest still present <b>past</b> that time on a day
/// is charged for another night. Kept dependency-free in the Domain so the boundary cases are
/// unit-testable without a database (M17 task 5).
/// </summary>
/// <remarks>
/// Inputs are <b>local wall-clock</b> <see cref="DateTime"/>s — the caller converts the stored
/// (UTC) instants to the clinic's local time before counting, so the checkout boundary is evaluated
/// in the clinic's day. The rule:
/// <list type="bullet">
///   <item>A stay that ends on or before the checkout time on day <c>D</c> is billed through <c>D-1</c>.</item>
///   <item>Staying <b>past</b> the checkout time on day <c>D</c> bills that night too (through <c>D</c>).</item>
///   <item>Leaving <b>exactly at</b> the checkout time makes the deadline — that night is not charged.</item>
/// </list>
/// A same-day stay that never passes the checkout time is <c>0</c> nights (a day-use, not a boarding).
/// </remarks>
public static class NightStayChargeCalculator
{
    /// <summary>
    /// Nights accrued between <paramref name="checkIn"/> and <paramref name="checkOut"/> under the
    /// hotel rule with the given daily <paramref name="checkoutTime"/>. Returns <c>0</c> when the
    /// stay is non-positive or never crosses a billable night.
    /// </summary>
    public static int CountNights(DateTime checkIn, DateTime checkOut, TimeOnly checkoutTime)
    {
        if (checkOut <= checkIn)
        {
            return 0;
        }

        // The last night billed: the checkout day itself only if the guest stayed past the checkout
        // time, otherwise the day before.
        var lastBillableDate = TimeOnly.FromDateTime(checkOut) > checkoutTime
            ? DateOnly.FromDateTime(checkOut)
            : DateOnly.FromDateTime(checkOut).AddDays(-1);

        var nights = lastBillableDate.DayNumber - DateOnly.FromDateTime(checkIn).DayNumber + 1;
        return Math.Max(0, nights);
    }
}
