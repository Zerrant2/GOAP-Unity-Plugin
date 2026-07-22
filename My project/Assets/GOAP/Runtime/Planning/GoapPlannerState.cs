using System;
using System.Collections.Generic;

namespace Practice.GOAP
{
    internal readonly struct GoapPlannerFactSlot
    {
        public bool IsBoolean { get; }
        public int Index { get; }

        public GoapPlannerFactSlot(bool isBoolean, int index)
        {
            IsBoolean = isBoolean;
            Index = index;
        }
    }

    internal readonly struct GoapCompiledCondition
    {
        public bool IsValid { get; }
        public GoapPlannerFactSlot Slot { get; }
        public GoapComparison Comparison { get; }
        public GoapEffectOperation EffectOperation { get; }
        public GoapValue ExpectedValue { get; }

        public GoapCompiledCondition(GoapCondition source, GoapPlannerStateLayout layout)
        {
            if (source.IsValid && layout.TryGetSlot(source.Fact, out var slot))
            {
                IsValid = true;
                Slot = slot;
            }
            else
            {
                IsValid = false;
                Slot = default;
            }

            Comparison = source.Comparison;
            EffectOperation = source.EffectOperation;
            ExpectedValue = source.ExpectedValue;
        }
    }

    internal sealed class GoapPlannerStateLayout
    {
        private readonly Dictionary<GoapFact, GoapPlannerFactSlot> _slots;
        private readonly GoapFact[] _booleanFacts;
        private readonly GoapFact[] _scalarFacts;

        public int BooleanWordCount => (_booleanFacts.Length + 63) / 64;
        public int ScalarCount => _scalarFacts.Length;

        public GoapPlannerStateLayout(IReadOnlyList<GoapFact> orderedFacts)
        {
            _slots = new Dictionary<GoapFact, GoapPlannerFactSlot>(orderedFacts.Count);
            var booleanFacts = new List<GoapFact>();
            var scalarFacts = new List<GoapFact>();
            foreach (var fact in orderedFacts)
            {
                if (fact == null || _slots.ContainsKey(fact))
                {
                    continue;
                }

                if (fact.ValueType == GoapFactType.Boolean)
                {
                    _slots.Add(fact, new GoapPlannerFactSlot(true, booleanFacts.Count));
                    booleanFacts.Add(fact);
                }
                else
                {
                    _slots.Add(fact, new GoapPlannerFactSlot(false, scalarFacts.Count));
                    scalarFacts.Add(fact);
                }
            }

            _booleanFacts = booleanFacts.ToArray();
            _scalarFacts = scalarFacts.ToArray();
        }

        public bool TryGetSlot(GoapFact fact, out GoapPlannerFactSlot slot)
        {
            if (fact != null)
            {
                return _slots.TryGetValue(fact, out slot);
            }

            slot = default;
            return false;
        }

        public GoapCompiledCondition[] Compile(IReadOnlyList<GoapCondition> conditions)
        {
            var result = new GoapCompiledCondition[conditions.Count];
            for (var index = 0; index < conditions.Count; index++)
            {
                result[index] = new GoapCompiledCondition(conditions[index], this);
            }

            return result;
        }

        public GoapPlannerState Capture(GoapWorldState source)
        {
            var booleanWords = BooleanWordCount == 0 ? Array.Empty<ulong>() : new ulong[BooleanWordCount];
            for (var index = 0; index < _booleanFacts.Length; index++)
            {
                if (source.GetValue(_booleanFacts[index]).Boolean)
                {
                    booleanWords[index / 64] |= 1UL << (index % 64);
                }
            }

            var scalarValues = ScalarCount == 0 ? Array.Empty<GoapValue>() : new GoapValue[ScalarCount];
            for (var index = 0; index < _scalarFacts.Length; index++)
            {
                scalarValues[index] = source.GetValue(_scalarFacts[index]);
            }

            return new GoapPlannerState(booleanWords, scalarValues);
        }
    }

    internal sealed class GoapCompiledAction
    {
        public GoapActionDefinition Definition { get; }
        public GoapCompiledCondition[] Preconditions { get; }
        public GoapCompiledCondition[] Effects { get; }

        public GoapCompiledAction(GoapActionDefinition definition, GoapPlannerStateLayout layout)
        {
            Definition = definition;
            Preconditions = layout.Compile(definition.Preconditions);
            Effects = layout.Compile(definition.Effects);
        }
    }

    internal sealed class GoapPlannerState : IEquatable<GoapPlannerState>
    {
        private readonly ulong[] _booleanWords;
        private readonly GoapValue[] _scalarValues;
        private readonly int _hashCode;

        public GoapPlannerState(ulong[] booleanWords, GoapValue[] scalarValues)
        {
            _booleanWords = booleanWords;
            _scalarValues = scalarValues;
            _hashCode = BuildHashCode(booleanWords, scalarValues);
        }

        public bool Satisfies(IReadOnlyList<GoapCompiledCondition> conditions)
        {
            for (var index = 0; index < conditions.Count; index++)
            {
                var condition = conditions[index];
                if (!condition.IsValid || !GetValue(condition.Slot).Matches(
                        condition.Comparison,
                        condition.ExpectedValue))
                {
                    return false;
                }
            }

            return true;
        }

        public GoapPlannerState Apply(IReadOnlyList<GoapCompiledCondition> effects)
        {
            var booleanWords = _booleanWords.Length == 0
                ? Array.Empty<ulong>()
                : (ulong[])_booleanWords.Clone();
            var scalarValues = _scalarValues.Length == 0
                ? Array.Empty<GoapValue>()
                : (GoapValue[])_scalarValues.Clone();
            var changed = false;

            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];
                if (!effect.IsValid)
                {
                    continue;
                }

                var current = GetValue(effect.Slot, booleanWords, scalarValues);
                var next = current.Apply(effect.EffectOperation, effect.ExpectedValue);
                if (current.Equals(next))
                {
                    continue;
                }

                SetValue(effect.Slot, next, booleanWords, scalarValues);
                changed = true;
            }

            return changed ? new GoapPlannerState(booleanWords, scalarValues) : null;
        }

        public GoapValue GetValue(GoapPlannerFactSlot slot)
        {
            return GetValue(slot, _booleanWords, _scalarValues);
        }

        public bool Equals(GoapPlannerState other)
        {
            return other != null &&
                   EqualArrays(_booleanWords, other._booleanWords) &&
                   EqualScalarArrays(_scalarValues, other._scalarValues);
        }

        public override bool Equals(object obj)
        {
            return obj is GoapPlannerState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        private static GoapValue GetValue(
            GoapPlannerFactSlot slot,
            IReadOnlyList<ulong> booleanWords,
            IReadOnlyList<GoapValue> scalarValues)
        {
            if (!slot.IsBoolean)
            {
                return scalarValues[slot.Index];
            }

            var enabled = (booleanWords[slot.Index / 64] & (1UL << (slot.Index % 64))) != 0UL;
            return GoapValue.From(enabled);
        }

        private static void SetValue(
            GoapPlannerFactSlot slot,
            GoapValue value,
            ulong[] booleanWords,
            GoapValue[] scalarValues)
        {
            if (!slot.IsBoolean)
            {
                scalarValues[slot.Index] = value;
                return;
            }

            var mask = 1UL << (slot.Index % 64);
            if (value.Boolean)
            {
                booleanWords[slot.Index / 64] |= mask;
            }
            else
            {
                booleanWords[slot.Index / 64] &= ~mask;
            }
        }

        private static int BuildHashCode(
            IReadOnlyList<ulong> booleanWords,
            IReadOnlyList<GoapValue> scalarValues)
        {
            var hash = 17;
            for (var index = 0; index < booleanWords.Count; index++)
            {
                hash = unchecked(hash * 31 + booleanWords[index].GetHashCode());
            }

            for (var index = 0; index < scalarValues.Count; index++)
            {
                hash = unchecked(hash * 31 + GetExactHashCode(scalarValues[index]));
            }

            return hash;
        }

        private static int GetExactHashCode(GoapValue value)
        {
            return value.Type switch
            {
                GoapFactType.Float => HashCode.Combine(value.Type, BitConverter.SingleToInt32Bits(value.Float)),
                GoapFactType.Integer => HashCode.Combine(value.Type, value.Integer),
                GoapFactType.Enum => HashCode.Combine(value.Type, value.Integer),
                _ => HashCode.Combine(value.Type, value.Boolean)
            };
        }

        private static bool EqualArrays(IReadOnlyList<ulong> left, IReadOnlyList<ulong> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Count; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EqualScalarArrays(
            IReadOnlyList<GoapValue> left,
            IReadOnlyList<GoapValue> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Count; index++)
            {
                if (!ExactlyEquals(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ExactlyEquals(GoapValue left, GoapValue right)
        {
            if (left.Type != right.Type)
            {
                return false;
            }

            return left.Type switch
            {
                GoapFactType.Float => BitConverter.SingleToInt32Bits(left.Float) ==
                                      BitConverter.SingleToInt32Bits(right.Float),
                GoapFactType.Integer => left.Integer == right.Integer,
                GoapFactType.Enum => left.Integer == right.Integer,
                _ => left.Boolean == right.Boolean
            };
        }
    }
}
