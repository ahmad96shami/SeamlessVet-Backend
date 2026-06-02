using FluentAssertions;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Visits;

/// <summary>
/// M17 task 5/10 — pure unit tests of the hotel-style night-count rule
/// (<see cref="NightStayChargeCalculator.CountNights"/>), with the boundary pinned at the checkout
/// time. No database. The default checkout time is 12:00; one case varies it to prove it's a knob.
/// </summary>
public sealed class NightStayChargeCalculatorTests
{
    private static readonly TimeOnly Noon = new(12, 0);

    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 6, day, hour, minute, 0);

    [Theory]
    // check-in afternoon, check-out next morning before noon → exactly one night
    [InlineData(1, 15, 0, 2, 10, 0, 1)]
    // late checkout (past noon) the next day → two nights
    [InlineData(1, 15, 0, 2, 14, 0, 2)]
    // leaving EXACTLY at the checkout time makes the deadline → one night, not two
    [InlineData(1, 15, 0, 2, 12, 0, 1)]
    // one minute past the checkout time → the extra night is charged
    [InlineData(1, 15, 0, 2, 12, 1, 2)]
    // multi-night, late checkout
    [InlineData(1, 10, 0, 3, 13, 0, 3)]
    // multi-night, early (before-noon) checkout
    [InlineData(1, 10, 0, 3, 11, 0, 2)]
    // same-day day-use, never passes noon → zero nights (a day-use, not a boarding)
    [InlineData(1, 9, 0, 1, 11, 0, 0)]
    // same-day but stays past noon → one night
    [InlineData(1, 9, 0, 1, 14, 0, 1)]
    // overnight starting late evening, out before noon → one night
    [InlineData(1, 23, 0, 2, 11, 0, 1)]
    public void CountNights_HotelRule(
        int inDay, int inHour, int inMin, int outDay, int outHour, int outMin, int expected)
        => NightStayChargeCalculator
            .CountNights(D(inDay, inHour, inMin), D(outDay, outHour, outMin), Noon)
            .Should().Be(expected);

    [Fact]
    public void CountNights_ZeroForNonPositiveStay()
    {
        NightStayChargeCalculator.CountNights(D(1, 12), D(1, 12), Noon).Should().Be(0);
        NightStayChargeCalculator.CountNights(D(2, 12), D(1, 12), Noon).Should().Be(0);
    }

    [Theory]
    // checkout time is configurable: same stay, different boundary. Out at 09:30.
    // boundary 09:00 → 09:30 is past it → 2 nights; boundary 10:00 → before it → 1 night.
    [InlineData(9, 2)]
    [InlineData(10, 1)]
    public void CountNights_RespectsConfigurableCheckoutHour(int checkoutHour, int expected)
        => NightStayChargeCalculator
            .CountNights(D(1, 15, 0), D(2, 9, 30), new TimeOnly(checkoutHour, 0))
            .Should().Be(expected);
}
