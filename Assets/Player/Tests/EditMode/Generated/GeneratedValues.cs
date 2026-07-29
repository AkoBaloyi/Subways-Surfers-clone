using System;
using System.Collections.Generic;

namespace SubwaySurfers.Player.Tests.Generated
{
    public static class GeneratedValues
    {
        private static readonly float[] NonFinite =
        {
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity
        };

        public static float NextFiniteFloat(Random random, float minimum, float maximum)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            ValidateFiniteRange(minimum, maximum);
            if (minimum == maximum) return minimum;

            var sample = (double)minimum + (((double)maximum - minimum) * random.NextDouble());
            var result = (float)sample;
            if (result < minimum) return minimum;
            if (result > maximum) return maximum;
            return result;
        }

        public static float NextNumericCase(Random random, float minimum, float maximum)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            ValidateFiniteRange(minimum, maximum);

            var category = random.Next(0, 8);
            if (category < NonFinite.Length) return NonFinite[category];
            if (category == 3) return minimum;
            if (category == 4) return maximum;
            if (category == 5 && minimum <= 0f && maximum >= 0f) return 0f;
            return NextFiniteFloat(random, minimum, maximum);
        }

        public static IReadOnlyList<float> NonFiniteCases()
        {
            return (float[])NonFinite.Clone();
        }

        public static IReadOnlyList<float> ElapsedTimePartitions(
            Random random,
            float totalElapsedTime,
            int maximumPartitionCount)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (!IsFinite(totalElapsedTime) || totalElapsedTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(totalElapsedTime));
            if (maximumPartitionCount < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumPartitionCount));

            var count = random.Next(1, maximumPartitionCount + 1);
            var cuts = new float[count + 1];
            cuts[0] = 0f;
            cuts[count] = totalElapsedTime;
            for (var index = 1; index < count; index++)
                cuts[index] = (float)(random.NextDouble() * totalElapsedTime);
            Array.Sort(cuts);

            var result = new float[count];
            for (var index = 0; index < count; index++)
                result[index] = cuts[index + 1] - cuts[index];
            return result;
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void ValidateFiniteRange(float minimum, float maximum)
        {
            if (!IsFinite(minimum) || !IsFinite(maximum) || minimum > maximum)
                throw new ArgumentOutOfRangeException(nameof(minimum));
        }
    }
}
