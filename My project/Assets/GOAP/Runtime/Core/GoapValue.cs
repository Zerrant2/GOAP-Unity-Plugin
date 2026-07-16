using System;
using System.Globalization;

namespace Practice.GOAP
{
    public enum GoapFactType
    {
        Boolean,
        Integer,
        Float,
        Enum
    }

    public enum GoapComparison
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    public enum GoapEffectOperation
    {
        Set,
        Add,
        Subtract
    }

    [Serializable]
    public struct GoapValue : IEquatable<GoapValue>
    {
        private const float FloatEpsilon = 0.0001f;

        private readonly GoapFactType _type;
        private readonly bool _boolean;
        private readonly int _integer;
        private readonly float _float;

        public GoapFactType Type => _type;
        public bool Boolean => _type == GoapFactType.Boolean ? _boolean : Math.Abs(Number) > FloatEpsilon;
        public int Integer => _type == GoapFactType.Integer || _type == GoapFactType.Enum ? _integer : (int)_float;
        public float Float => _type == GoapFactType.Float ? _float : _integer;

        private float Number => _type == GoapFactType.Float ? _float : _integer;

        private GoapValue(GoapFactType type, bool boolean, int integer, float floatValue)
        {
            _type = type;
            _boolean = boolean;
            _integer = integer;
            _float = floatValue;
        }

        public static GoapValue From(bool value)
        {
            return new GoapValue(GoapFactType.Boolean, value, value ? 1 : 0, value ? 1f : 0f);
        }

        public static GoapValue From(int value)
        {
            return new GoapValue(GoapFactType.Integer, value != 0, value, value);
        }

        public static GoapValue From(float value)
        {
            return new GoapValue(GoapFactType.Float, Math.Abs(value) > FloatEpsilon, (int)value, value);
        }

        public static GoapValue FromEnum(int index)
        {
            return new GoapValue(GoapFactType.Enum, index != 0, index, index);
        }

        public GoapValue ConvertTo(GoapFactType type)
        {
            return type switch
            {
                GoapFactType.Boolean => From(Boolean),
                GoapFactType.Integer => From(Integer),
                GoapFactType.Float => From(Float),
                GoapFactType.Enum => FromEnum(Integer),
                _ => this
            };
        }

        public bool Matches(GoapComparison comparison, GoapValue expected)
        {
            var result = Compare(expected);
            return comparison switch
            {
                GoapComparison.Equal => result == 0,
                GoapComparison.NotEqual => result != 0,
                GoapComparison.Less => result < 0,
                GoapComparison.LessOrEqual => result <= 0,
                GoapComparison.Greater => result > 0,
                GoapComparison.GreaterOrEqual => result >= 0,
                _ => false
            };
        }

        public GoapValue Apply(GoapEffectOperation operation, GoapValue operand)
        {
            if (_type == GoapFactType.Boolean || _type == GoapFactType.Enum || operation == GoapEffectOperation.Set)
            {
                return operand.ConvertTo(_type);
            }

            if (_type == GoapFactType.Integer)
            {
                return From(operation == GoapEffectOperation.Add
                    ? Integer + operand.Integer
                    : Integer - operand.Integer);
            }

            return From(operation == GoapEffectOperation.Add
                ? Float + operand.Float
                : Float - operand.Float);
        }

        public bool Equals(GoapValue other)
        {
            if (_type != other._type)
            {
                return false;
            }

            if (_type == GoapFactType.Boolean)
            {
                return _boolean == other._boolean;
            }

            return Math.Abs(Number - other.Number) <= FloatEpsilon;
        }

        public override bool Equals(object obj)
        {
            return obj is GoapValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_type, _boolean, _integer, _float);
        }

        public string ToKeyString()
        {
            return _type switch
            {
                GoapFactType.Boolean => _boolean ? "b1" : "b0",
                GoapFactType.Integer => $"i{_integer}",
                GoapFactType.Float => $"f{_float.ToString("R", CultureInfo.InvariantCulture)}",
                GoapFactType.Enum => $"e{_integer}",
                _ => "?"
            };
        }

        public override string ToString()
        {
            return _type switch
            {
                GoapFactType.Boolean => _boolean ? "True" : "False",
                GoapFactType.Integer => _integer.ToString(CultureInfo.InvariantCulture),
                GoapFactType.Float => _float.ToString("0.###", CultureInfo.InvariantCulture),
                GoapFactType.Enum => $"Enum #{_integer}",
                _ => "?"
            };
        }

        private int Compare(GoapValue other)
        {
            if (_type == GoapFactType.Boolean || other._type == GoapFactType.Boolean)
            {
                return Boolean.CompareTo(other.Boolean);
            }

            if (_type == GoapFactType.Enum || other._type == GoapFactType.Enum)
            {
                return Integer.CompareTo(other.Integer);
            }

            var difference = Number - other.Number;
            if (Math.Abs(difference) <= FloatEpsilon)
            {
                return 0;
            }

            return difference < 0f ? -1 : 1;
        }
    }
}
