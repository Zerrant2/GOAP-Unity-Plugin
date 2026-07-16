using System;
using System.Collections.Generic;

namespace Practice.GOAP
{
    internal readonly struct GoapStateKey : IEquatable<GoapStateKey>
    {
        private readonly ulong[] _booleanWords;
        private readonly int[] _scalarValues;
        private readonly int _hashCode;

        public GoapStateKey(GoapWorldState state, IReadOnlyList<GoapFact> orderedFacts)
        {
            var booleanCount = 0;
            var scalarCount = 0;
            for (var index = 0; index < orderedFacts.Count; index++)
            {
                if (orderedFacts[index].ValueType == GoapFactType.Boolean)
                {
                    booleanCount++;
                }
                else
                {
                    scalarCount++;
                }
            }

            _booleanWords = new ulong[(booleanCount + 63) / 64];
            _scalarValues = new int[scalarCount];
            var booleanIndex = 0;
            var scalarIndex = 0;
            var hash = 17;
            for (var index = 0; index < orderedFacts.Count; index++)
            {
                var fact = orderedFacts[index];
                var value = state.GetValue(fact);
                if (fact.ValueType == GoapFactType.Boolean)
                {
                    if (value.Boolean)
                    {
                        _booleanWords[booleanIndex / 64] |= 1UL << (booleanIndex % 64);
                    }

                    booleanIndex++;
                    continue;
                }

                var scalar = fact.ValueType == GoapFactType.Float
                    ? BitConverter.SingleToInt32Bits(value.Float)
                    : value.Integer;
                _scalarValues[scalarIndex++] = scalar;
            }

            for (var index = 0; index < _booleanWords.Length; index++)
            {
                hash = unchecked(hash * 31 + _booleanWords[index].GetHashCode());
            }

            for (var index = 0; index < _scalarValues.Length; index++)
            {
                hash = unchecked(hash * 31 + _scalarValues[index]);
            }

            _hashCode = hash;
        }

        public bool Equals(GoapStateKey other)
        {
            return EqualArrays(_booleanWords, other._booleanWords) &&
                   EqualArrays(_scalarValues, other._scalarValues);
        }

        public override bool Equals(object obj)
        {
            return obj is GoapStateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        private static bool EqualArrays<T>(T[] left, T[] right) where T : IEquatable<T>
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (!left[index].Equals(right[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
