using System;
// 模块：引擎 / 执行。
// 职责范围：执行显式业务CT探针。

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Automation
{
    public partial class ProcessEngine
    {
        public bool RunCycleTimeProbe(ProcHandle evt, CycleTimeProbe operation)
        {
            if (evt == null || operation == null)
            {
                MarkAlarm(evt, "CT探针参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (string.IsNullOrWhiteSpace(operation.TaskKey)
                || string.IsNullOrWhiteSpace(operation.SegmentName))
            {
                MarkAlarm(evt, "CT探针任务标识和分段名称不能为空");
                throw CreateAlarmException(evt, evt.alarmMsg);
            }

            long now = Stopwatch.GetTimestamp();
            CycleTimeProbeState state;
            double segmentMilliseconds;
            double cycleMilliseconds;
            int segmentIndex;
            if (operation.StartNewCycle)
            {
                state = new CycleTimeProbeState
                {
                    CycleStartedTicks = now,
                    LastProbeTicks = now,
                    SegmentIndex = 0
                };
                evt.CycleTimeProbes[operation.TaskKey] = state;
                segmentMilliseconds = 0d;
                cycleMilliseconds = 0d;
                segmentIndex = 0;
            }
            else
            {
                if (!evt.CycleTimeProbes.TryGetValue(operation.TaskKey, out state))
                {
                    MarkAlarm(evt, $"CT探针任务尚未开始:{operation.TaskKey}");
                    throw CreateAlarmException(evt, evt.alarmMsg);
                }
                segmentMilliseconds = Math.Max(0L, now - state.LastProbeTicks) * 1000d / Stopwatch.Frequency;
                cycleMilliseconds = Math.Max(0L, now - state.CycleStartedTicks) * 1000d / Stopwatch.Frequency;
                state.LastProbeTicks = now;
                state.SegmentIndex++;
                segmentIndex = state.SegmentIndex;
            }

            var writes = new List<KeyValuePair<DicValue, double>>();
            AddCycleTimeWrite(evt, operation.SegmentMillisecondsVariableName,
                segmentMilliseconds, "分段耗时变量", writes);
            AddCycleTimeWrite(evt, operation.CycleMillisecondsVariableName,
                cycleMilliseconds, "累计耗时变量", writes);
            var previousValues = new List<KeyValuePair<DicValue, string>>();
            foreach (KeyValuePair<DicValue, double> write in writes)
            {
                previousValues.Add(new KeyValuePair<DicValue, string>(write.Key, write.Key.Value));
                if (!Context.ValueStore.SetValueByIndexForProcess(
                    write.Key.Index,
                    write.Value.ToString("R", CultureInfo.InvariantCulture),
                    evt.procId,
                    evt.GetOperationSource()))
                {
                    foreach (KeyValuePair<DicValue, string> previous in previousValues)
                    {
                        Context.ValueStore.SetValueByIndexForProcess(
                            previous.Key.Index, previous.Value, evt.procId,
                            $"CT探针写入回滚:{evt.RunId:D}");
                    }
                    MarkAlarm(evt, $"CT探针结果写入失败:{write.Key.Name}");
                    throw CreateAlarmException(evt, evt.alarmMsg);
                }
            }

            PublishCycleTimeSample(new CycleTimeProbeSample
            {
                RunId = evt.RunId,
                ProcId = evt.procId,
                ProcIndex = evt.procNum,
                TaskKey = operation.TaskKey,
                SegmentName = operation.SegmentName,
                SegmentIndex = segmentIndex,
                CycleStarted = operation.StartNewCycle,
                SegmentMilliseconds = segmentMilliseconds,
                CycleMilliseconds = cycleMilliseconds,
                RecordedAtUtc = DateTime.UtcNow
            });
            return true;
        }

        private void AddCycleTimeWrite(ProcHandle evt, string variableName, double value,
            string label, List<KeyValuePair<DicValue, double>> writes)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return;
            }
            if (Context?.ValueStore == null
                || !Context.ValueStore.TryGetValueByNameForProcess(variableName, evt.procId, out DicValue variable)
                || variable == null
                || !string.Equals(variable.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                MarkAlarm(evt, $"{label}不存在或类型不是double:{variableName}");
                throw CreateAlarmException(evt, evt.alarmMsg);
            }
            writes.Add(new KeyValuePair<DicValue, double>(variable, value));
        }

    }
}
