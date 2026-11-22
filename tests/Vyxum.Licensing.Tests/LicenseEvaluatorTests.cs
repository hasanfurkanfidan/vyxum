using Shouldly;
using Xunit;

namespace Vyxum.Licensing.Tests;

public class LicenseEvaluatorTests
{
    // A fixed +03:00 zone without daylight saving (like Türkiye), so local midnight is 21:00 UTC.
    private static readonly TimeSpan Offset = TimeSpan.FromHours(3);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+03", Offset, "Test+03", "Test+03");

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly Until = new(2026, 12, 31);

    private static DateTimeOffset Local(DateOnly date, int hour = 12, int minute = 0, int second = 0) =>
        new(date.ToDateTime(new TimeOnly(hour, minute, second)), Offset);

    private static VerifiedLicense Verified(LicensePayload payload)
    {
        using var key = new TestKey("k1");
        var result = key.Reader().Read(key.Signer.Sign(payload));
        result.IsValid.ShouldBeTrue(result.Detail);
        return result.License!;
    }

    private static LicenseEvaluation Evaluate(LicensePayload payload, DateTimeOffset now, DateTimeOffset? lastSeen = null) =>
        new LicenseEvaluator(new FixedClock(now, Zone)).Evaluate(Verified(payload), lastSeen);

    [Theory]
    [InlineData(30)]
    [InlineData(0)]
    [InlineData(1)]
    public void Status_changes_day_by_day_at_the_documented_boundaries(int graceDays)
    {
        var license = Verified(TestLicenses.Subscription(From, Until, graceDays));
        var clock = new FixedClock(default, Zone);
        var evaluator = new LicenseEvaluator(clock);

        for (var day = From.AddDays(-3); day <= Until.AddDays(graceDays + 3); day = day.AddDays(1))
        {
            var expected = day < From ? LicenseStatus.NotYetValid
                : day <= Until ? LicenseStatus.Valid
                : day <= Until.AddDays(graceDays) ? LicenseStatus.Grace
                : LicenseStatus.ReadOnly;

            // First and last second of the local day give the same answer.
            foreach (var instant in new[] { Local(day, 0), Local(day, 23, 59, 59) })
            {
                clock.UtcNow = instant.ToUniversalTime();
                var evaluation = evaluator.Evaluate(license, lastSeen: null);
                evaluation.Status.ShouldBe(expected, $"{day:yyyy-MM-dd} {instant:HH:mm:ss}");
                evaluation.EffectiveDate.ShouldBe(day);
                evaluation.GraceDaysLeft.ShouldBe(
                    expected == LicenseStatus.Grace ? Until.AddDays(graceDays).DayNumber - day.DayNumber + 1 : null);
            }
        }
    }

    [Fact]
    public void Grace_of_30_days_named_boundaries()
    {
        var payload = TestLicenses.Subscription(From, Until, graceDays: 30);

        var lastValid = Evaluate(payload, Local(new DateOnly(2026, 12, 31), 23, 59, 59));
        lastValid.Status.ShouldBe(LicenseStatus.Valid);
        lastValid.GraceDaysLeft.ShouldBeNull();
        lastValid.GraceEndsOn.ShouldBe(new DateOnly(2027, 1, 30));

        var firstGrace = Evaluate(payload, Local(new DateOnly(2027, 1, 1), 0));
        firstGrace.Status.ShouldBe(LicenseStatus.Grace);
        firstGrace.GraceDaysLeft.ShouldBe(30);

        var lastGrace = Evaluate(payload, Local(new DateOnly(2027, 1, 30), 23, 59, 59));
        lastGrace.Status.ShouldBe(LicenseStatus.Grace);
        lastGrace.GraceDaysLeft.ShouldBe(1);

        var firstReadOnly = Evaluate(payload, Local(new DateOnly(2027, 1, 31), 0));
        firstReadOnly.Status.ShouldBe(LicenseStatus.ReadOnly);
        firstReadOnly.GraceDaysLeft.ShouldBeNull();
    }

    [Fact]
    public void Grace_of_0_days_goes_straight_to_read_only()
    {
        var payload = TestLicenses.Subscription(From, Until, graceDays: 0);

        Evaluate(payload, Local(Until, 23, 59, 59)).Status.ShouldBe(LicenseStatus.Valid);
        Evaluate(payload, Local(Until.AddDays(1), 0)).Status.ShouldBe(LicenseStatus.ReadOnly);
    }

    [Fact]
    public void Dates_are_compared_in_the_local_time_zone()
    {
        var payload = TestLicenses.Subscription(From, Until, graceDays: 0);

        // 20:59:59 UTC on the last day is 23:59:59 local; 21:00 UTC is already the next local day.
        Evaluate(payload, new DateTimeOffset(2026, 12, 31, 20, 59, 59, TimeSpan.Zero)).Status.ShouldBe(LicenseStatus.Valid);
        Evaluate(payload, new DateTimeOffset(2026, 12, 31, 21, 0, 0, TimeSpan.Zero)).Status.ShouldBe(LicenseStatus.ReadOnly);
    }

    [Fact]
    public void License_before_valid_from_is_not_yet_valid()
    {
        var payload = TestLicenses.Subscription(From, Until);

        var before = Evaluate(payload, Local(From.AddDays(-1), 23, 59, 59));
        before.Status.ShouldBe(LicenseStatus.NotYetValid);
        before.IsModuleLicensed("labels").ShouldBe(ModuleLicenseStatus.NotLicensed);

        Evaluate(payload, Local(From, 0)).Status.ShouldBe(LicenseStatus.Valid);
    }

    [Fact]
    public void Perpetual_license_never_expires()
    {
        var payload = TestLicenses.Sample() with { Type = LicenseType.Perpetual, ValidUntil = null };

        var evaluation = Evaluate(payload, Local(new DateOnly(2126, 6, 1)));

        evaluation.Status.ShouldBe(LicenseStatus.Valid);
        evaluation.GraceEndsOn.ShouldBeNull();
        evaluation.GraceDaysLeft.ShouldBeNull();
        evaluation.IsModuleLicensed("labels").ShouldBe(ModuleLicenseStatus.Licensed);
    }

    [Fact]
    public void Clock_rolled_back_does_not_extend_validity()
    {
        var payload = TestLicenses.Subscription(From, Until, graceDays: 30);
        var lastSeen = Local(new DateOnly(2027, 2, 15)); // already read-only
        var rolledBack = Local(new DateOnly(2026, 6, 1)); // inside the valid period

        var evaluation = Evaluate(payload, rolledBack, lastSeen);

        evaluation.Status.ShouldBe(LicenseStatus.ReadOnly);
        evaluation.EffectiveNow.ShouldBe(lastSeen);
        evaluation.LastSeen.ShouldBe(lastSeen);
    }

    [Fact]
    public void Clock_rolled_back_from_the_grace_period_stays_in_grace()
    {
        var payload = TestLicenses.Subscription(From, Until, graceDays: 30);
        var lastSeen = Local(new DateOnly(2027, 1, 10));

        var evaluation = Evaluate(payload, Local(new DateOnly(2026, 12, 1)), lastSeen);

        evaluation.Status.ShouldBe(LicenseStatus.Grace);
        evaluation.GraceDaysLeft.ShouldBe(21);
    }

    [Fact]
    public void Clock_moving_forward_uses_now_and_returns_it_as_last_seen()
    {
        var payload = TestLicenses.Subscription(From, Until);
        var now = Local(new DateOnly(2026, 6, 1));

        var evaluation = Evaluate(payload, now, lastSeen: Local(new DateOnly(2026, 5, 1)));

        evaluation.Status.ShouldBe(LicenseStatus.Valid);
        evaluation.EffectiveNow.ShouldBe(now);
        evaluation.LastSeen.ShouldBe(now);
        Evaluate(payload, now).LastSeen.ShouldBe(now);
    }

    [Theory]
    [InlineData(2026, 6, 1, ModuleLicenseStatus.Licensed)]
    [InlineData(2027, 1, 15, ModuleLicenseStatus.Grace)]
    [InlineData(2027, 3, 1, ModuleLicenseStatus.ReadOnly)]
    [InlineData(2025, 12, 1, ModuleLicenseStatus.NotLicensed)]
    public void Module_status_follows_the_license_status(int year, int month, int day, ModuleLicenseStatus expected)
    {
        var payload = TestLicenses.Subscription(From, Until, 30, "labels", "traceability");

        var evaluation = Evaluate(payload, Local(new DateOnly(year, month, day)));

        evaluation.IsModuleLicensed("labels").ShouldBe(expected);
        evaluation.IsModuleLicensed("traceability").ShouldBe(expected);
    }

    [Fact]
    public void Module_not_in_the_license_is_not_licensed()
    {
        var evaluation = Evaluate(TestLicenses.Subscription(From, Until, 30, "labels"), Local(new DateOnly(2026, 6, 1)));

        evaluation.IsModuleLicensed("reports").ShouldBe(ModuleLicenseStatus.NotLicensed);
        evaluation.IsModuleLicensed("Labels").ShouldBe(ModuleLicenseStatus.NotLicensed); // ids are compared ordinally
    }
}
