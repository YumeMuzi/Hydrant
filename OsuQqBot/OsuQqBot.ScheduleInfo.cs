﻿using OsuQqBot.AttributedFunctions;
using System;

namespace OsuQqBot
{
    public partial class OsuQqBot
    {
        private class ScheduleInfo
        {
            public ScheduleType Type { get; }
            public TimeSpan Time { get; }
            public IRegularly Action { get; }
            public DateTime NextRun { get; set; }
            public TimeSpan WaitTime
            {
                get
                {
                    var result = NextRun - DateTime.UtcNow;
                    return result < TimeSpan.Zero ? TimeSpan.Zero : result;
                }
            }
            public ScheduleInfo(ScheduleType type, TimeSpan time, IRegularly action)
            {
                Type = type;
                Time = time;
                Action = action;
                switch (Type)
                {
                    case ScheduleType.ByInterval:
                        NextRun = DateTime.UtcNow;
                        break;
                    case ScheduleType.Daily:
                        DateTime next = DateTime.UtcNow;
                        if (next.TimeOfDay < Time) next.AddDays(1);
                        NextRun = next;
                        break;
                    default:
                        throw new ArgumentException("类型不对", nameof(type));
                }
            }

            public bool ShouldRun() => NextRun < DateTime.UtcNow;

            public void Next()
            {
                switch (Type)
                {
                    case ScheduleType.ByInterval:
                        NextRun += Time;
                        if (ShouldRun()) NextRun = DateTime.UtcNow;
                        break;
                    case ScheduleType.Daily:
                        do
                        {
                            NextRun = NextRun.AddDays(1);
                        } while (ShouldRun());
                        break;
                    default:
                        break;
                }
            }
        }

        private enum ScheduleType
        {
            ByInterval = 0,
            Daily = 1,
        }
    }
}