using Equora.App.Services;
using Equora.App.ViewModels;
using Xunit;

namespace Equora.App.Tests;

public class SemesterTests
{
    private static readonly SemesterSettings Semester = new() { Enabled = true, Name = "秋季学期",
        StartDate = new(2026, 9, 2), EndDate = new(2026, 9, 22) };

    [Fact]
    public void WeeksFollowMondayAndRespectSemesterBoundaries()
    {
        Assert.Null(Semester.WeekNumber(new(2026, 9, 1)));
        Assert.Equal(1, Semester.WeekNumber(new(2026, 9, 6)));
        Assert.Equal(2, Semester.WeekNumber(new(2026, 9, 7)));
        Assert.Equal(4, Semester.WeekNumber(new(2026, 9, 22)));
        Assert.Null(Semester.WeekNumber(new(2026, 9, 23)));
        var slots = Semester.Slots(null, DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(10));
        Assert.Equal(new[] { 7, 14, 21 }, slots.Select(s => s.Start.Day));
        Assert.All(slots, s => Assert.Equal(9, s.Start.Hour));
    }

    [Fact]
    public void SpecificWeeksAreDeduplicatedAndPartialWeeksAreClipped()
    {
        Assert.Equal(new[] { 1, 2, 3, 4 }, Semester.ParseWeeks("1，2-4,3"));
        var slots = Semester.Slots("1,3-4", DayOfWeek.Wednesday, TimeSpan.FromHours(9), TimeSpan.FromHours(10));
        Assert.Equal(new[] { 2, 16 }, slots.Select(s => s.Start.Day));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("3-1")]
    [InlineData("1-2-3")]
    public void InvalidWeeksAreRejected(string weeks) => Assert.Throws<ArgumentException>(() => Semester.ParseWeeks(weeks));

    [Fact]
    public void InvalidDatesTimesAndDisabledModeAreRejected()
    {
        Assert.Throws<ArgumentException>(() => (Semester with { EndDate = new(2026, 8, 1) }).Validate());
        Assert.Throws<ArgumentException>(() => (Semester with { Enabled = false }).Slots(null, DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(10)));
        Assert.Throws<ArgumentException>(() => Semester.Slots(null, DayOfWeek.Monday, TimeSpan.FromHours(10), TimeSpan.FromHours(9)));
        Assert.Throws<ArgumentException>(() => Semester.Slots("1", DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(10)));
    }

    [Fact]
    public void SettingsRoundTripAndOldPreferencesRemainCompatible()
    {
        var path = Path.Combine(Path.GetTempPath(), $"semester-{Guid.NewGuid():N}.json");
        try
        {
            var store = new PreferencesStore(path);
            File.WriteAllText(path, "{\"Theme\":1}");
            Assert.False(store.Load().Semester.Enabled);
            store.Save(store.Load() with { Semester = Semester });
            Assert.Equal(Semester, store.Load().Semester);
            store.Save(store.Load() with { Semester = Semester with { Enabled = false } });
            Assert.Equal(Semester.StartDate, store.Load().Semester.StartDate);
            Assert.False(store.Load().Semester.Enabled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BatchTasksAreIndependentAndSurviveDisablingSemester()
    {
        using var service = new AppDataService(Path.Combine(Path.GetTempPath(), $"semester-{Guid.NewGuid():N}.db"), "semester-tests");
        var vm = new CalendarViewModel(service, service, new UndoService());
        var blocks = vm.CreateSemesterBlocks(Semester, "1,3", DayOfWeek.Wednesday,
            TimeSpan.FromHours(9), TimeSpan.FromHours(10), "数学", "教室 101", "#0078D4");
        Assert.Equal(2, blocks.Count);
        Assert.NotEqual(blocks[0].TaskId, blocks[1].TaskId);
        vm.SaveTimeBlock(blocks[0].Id, blocks[0].StartAt.AddHours(2), blocks[0].EndAt.AddHours(2), "教室 202", "#C32F37");
        var task = service.GetTask(blocks[0].TaskId!)!;
        service.UpdateTask(task with { Title = "数学补课" });
        Assert.Equal("数学", service.GetTask(blocks[1].TaskId!)!.Title);
        Assert.Equal(blocks[1].StartAt, service.GetBlock(blocks[1].Id)!.StartAt);
        Assert.Equal("教室 101", service.GetBlock(blocks[1].Id)!.Note);
        Assert.Throws<ArgumentException>(() => vm.CreateSemesterBlocks(Semester with { Enabled = false }, null,
            DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "数学", "", "#0078D4"));
        Assert.All(blocks, b => { Assert.NotNull(service.GetBlock(b.Id)); Assert.NotNull(service.GetTask(b.TaskId!)); });
    }
}
