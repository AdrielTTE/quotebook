namespace Quotebook.Core;

/// <summary>Money maths in one place: decimals, rounded to 2 places, away from zero.</summary>
public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
