using System;
using System.Collections.Generic;

namespace StillWrong.Core.Oracle
{
    public interface IMeasurementSource
    {
        bool TargetExists(string targetName, int frameOffset);
        bool TryGetValue(ProbeCall probe, int frameOffset, out MeasuredValue value);
    }

    public sealed class InMemoryMeasurementSource : IMeasurementSource
    {
        private readonly Dictionary<int, HashSet<string>> existingTargets = new Dictionary<int, HashSet<string>>();
        private readonly Dictionary<int, Dictionary<string, MeasuredValue>> valuesByFrame = new Dictionary<int, Dictionary<string, MeasuredValue>>();

        public void SetTargetExists(int frameOffset, string targetName, bool exists)
        {
            HashSet<string> set;
            if (!existingTargets.TryGetValue(frameOffset, out set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                existingTargets[frameOffset] = set;
            }
            if (exists)
            {
                set.Add(targetName);
            }
            else
            {
                set.Remove(targetName);
            }
        }

        public void AddExistingTargets(int frameOffset, IEnumerable<string> targetNames)
        {
            HashSet<string> set;
            if (!existingTargets.TryGetValue(frameOffset, out set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                existingTargets[frameOffset] = set;
            }
            foreach (string name in targetNames)
            {
                set.Add(name);
            }
        }

        public void AddValue(int frameOffset, ProbeCall probe, MeasuredValue value)
        {
            Dictionary<string, MeasuredValue> frameValues;
            if (!valuesByFrame.TryGetValue(frameOffset, out frameValues))
            {
                frameValues = new Dictionary<string, MeasuredValue>(StringComparer.Ordinal);
                valuesByFrame[frameOffset] = frameValues;
            }
            frameValues[probe.StableKey()] = value;
        }

        public bool TargetExists(string targetName, int frameOffset)
        {
            if (targetName == null || targetName.Length == 0)
            {
                return true;
            }
            HashSet<string> set;
            if (!existingTargets.TryGetValue(frameOffset, out set))
            {
                return false;
            }
            return set.Contains(targetName);
        }

        public bool TryGetValue(ProbeCall probe, int frameOffset, out MeasuredValue value)
        {
            value = null;
            Dictionary<string, MeasuredValue> frameValues;
            if (!valuesByFrame.TryGetValue(frameOffset, out frameValues))
            {
                return false;
            }
            return frameValues.TryGetValue(probe.StableKey(), out value);
        }
    }
}
