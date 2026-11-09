// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using Onvif.Open.Recorder.Hosting;
using Onvif.Open.Recorder.Tenancy;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public sealed class RecordingScheduleTest
    {
        // 2026-11-09 is a Monday.
        private static DateTime Local(int day, int hour, int minute = 0) => new DateTime(2026, 11, day, hour, minute, 0);

        [Fact]
        public void BusinessHoursShouldBeWeekdaysFromEightToSix()
        {
            var schedule = RecordingSchedule.From(RecordingSchedule.BusinessHours);

            Assert.True(schedule.IsOnAt(Local(9, 8)));
            Assert.Equal(TimeSpan.FromHours(1), schedule.RemainingAt(Local(9, 17)));
            Assert.False(schedule.IsOnAt(Local(9, 18)));
            Assert.False(schedule.IsOnAt(Local(9, 7, 59)));
            Assert.False(schedule.IsOnAt(Local(14, 12)));
        }

        [Fact]
        public void HoursPastMidnightShouldBelongToTheDayTheyStart()
        {
            var schedule = RecordingSchedule.From(new List<ScheduleWindow> { new ScheduleWindow { Days = { DayOfWeek.Friday }, Start = "22:00", End = "06:00" } });

            Assert.True(schedule.IsOnAt(Local(13, 23)));
            Assert.Equal(TimeSpan.FromHours(4), schedule.RemainingAt(Local(14, 2)));
            Assert.False(schedule.IsOnAt(Local(14, 22, 30)));
            Assert.False(schedule.IsOnAt(Local(12, 23)));

            var untilMidnight = RecordingSchedule.From(new List<ScheduleWindow> { new ScheduleWindow { Days = { DayOfWeek.Sunday }, Start = "20:00", End = "00:00" } });
            Assert.Equal(TimeSpan.FromMinutes(30), untilMidnight.RemainingAt(Local(15, 23, 30)));
        }

        [Fact]
        public void InvalidHoursShouldBeExplained()
        {
            Assert.Single(RecordingSchedule.Check(new List<ScheduleWindow>()));
            var errors = RecordingSchedule.Check(new List<ScheduleWindow>
            {
                new ScheduleWindow { Days = { }, Start = "08:00", End = "18:00" },
                new ScheduleWindow { Days = { DayOfWeek.Monday }, Start = "8 am", End = "18:00" },
                new ScheduleWindow { Days = { DayOfWeek.Monday }, Start = "09:00", End = "09:00" }
            });
            Assert.Equal(3, errors.Count);
            Assert.Empty(RecordingSchedule.Check(RecordingSchedule.BusinessHours));
        }

        [Fact]
        public void SavedHoursShouldBeTidy()
        {
            var json = RecordingSchedule.Serialize(new List<ScheduleWindow> { new ScheduleWindow { Days = { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Monday }, Start = "7:30", End = "16:05" } });
            var window = Assert.Single(RecordingSchedule.Deserialize(json));
            Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Sunday }, window.Days);
            Assert.Equal(("07:30", "16:05"), (window.Start, window.End));
            Assert.Null(RecordingSchedule.Serialize(new List<ScheduleWindow>()));
        }

        [Fact]
        public void TriggersShouldFollowTheSitesTimeAndEndWithTheHours()
        {
            var schedule = RecordingSchedule.From(RecordingSchedule.BusinessHours);
            var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

            // 07:30 UTC is 08:30 in Paris (winter time).
            Assert.Equal(ScheduledRecording.Hold, ScheduledRecording.TriggerFor(schedule, paris, new DateTimeOffset(2026, 11, 9, 7, 30, 0, TimeSpan.Zero)));
            Assert.Null(ScheduledRecording.TriggerFor(schedule, TimeZoneInfo.Utc, new DateTimeOffset(2026, 11, 9, 7, 30, 0, TimeSpan.Zero)));
            Assert.Equal(TimeSpan.FromSeconds(30), ScheduledRecording.TriggerFor(schedule, paris, new DateTimeOffset(2026, 11, 9, 16, 59, 30, TimeSpan.Zero)));
            Assert.Null(ScheduledRecording.TriggerFor(schedule, paris, new DateTimeOffset(2026, 11, 9, 17, 0, 0, TimeSpan.Zero)));
        }
    }
}
