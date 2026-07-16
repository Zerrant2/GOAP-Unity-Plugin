using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Practice.GOAP
{
    [DisallowMultipleComponent]
    public sealed class GoapProfileSensorBehaviour : GoapSensorBehaviour
    {
        private readonly List<float> _nextSenseTimes = new();
        private readonly HashSet<int> _requestedSensors = new();
        private bool _requestAll = true;

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            var profile = agent != null ? agent.Profile : null;
            if (profile == null)
            {
                return;
            }

            EnsureSchedule(profile.Sensors.Count);
            var now = Time.time;
            for (var index = 0; index < profile.Sensors.Count; index++)
            {
                var definition = profile.Sensors[index];
                if (definition == null || definition.Fact == null || !ShouldSense(definition, index, now))
                {
                    continue;
                }

                Evaluate(definition, agent);
                _requestedSensors.Remove(index);
                _nextSenseTimes[index] = now + definition.Interval;
            }

            _requestAll = false;
        }

        public void RequestSensor(int index)
        {
            if (index >= 0)
            {
                _requestedSensors.Add(index);
                RequestRefresh();
            }
        }

        public void RequestAllSensors()
        {
            _requestAll = true;
            RequestRefresh();
        }

        protected override void OnRefreshRequested()
        {
            _requestAll = true;
        }

        private bool ShouldSense(GoapProfileSensorDefinition definition, int index, float now)
        {
            if (_requestAll || _requestedSensors.Contains(index))
            {
                return true;
            }

            return definition.UpdateMode == GoapSensorUpdateMode.EveryDecision ||
                   (definition.UpdateMode == GoapSensorUpdateMode.Interval && now >= _nextSenseTimes[index]);
        }

        private static void Evaluate(GoapProfileSensorDefinition definition, GoapAgent agent)
        {
            switch (definition.Kind)
            {
                case GoapProfileSensorKind.SmartObject:
                    var count = GoapSmartObject.CountAvailable(
                        definition.SourceId,
                        agent.transform.position,
                        agent,
                        definition.Radius <= 0f ? float.PositiveInfinity : definition.Radius);
                    WriteNumeric(definition, agent, count);
                    break;

                case GoapProfileSensorKind.Inventory:
                    var amount = agent.TryGetComponent<GoapInventory>(out var inventory)
                        ? inventory.GetAmount(definition.SourceId)
                        : 0;
                    WriteNumeric(definition, agent, amount);
                    break;

                case GoapProfileSensorKind.Distance:
                    var target = ResolveTarget(agent, definition.TargetId);
                    var distance = target == null
                        ? float.PositiveInfinity
                        : Vector3.Distance(agent.transform.position, target.position);
                    WriteNumeric(definition, agent, distance);
                    break;

                case GoapProfileSensorKind.Proximity:
                    WriteNumeric(definition, agent, CountNearby(definition, agent));
                    break;

                case GoapProfileSensorKind.Stat:
                    var statValue = agent.TryGetComponent<GoapStatSource>(out var stats) &&
                                    stats.TryGetValue(definition.SourceId, out var value)
                        ? value
                        : 0f;
                    WriteNumeric(definition, agent, statValue);
                    break;

                case GoapProfileSensorKind.Time:
                    WriteNumeric(definition, agent, Time.time * definition.Scale + definition.Offset);
                    break;

                case GoapProfileSensorKind.ComponentProperty:
                    WriteObject(definition, agent, ReadComponentMember(definition, agent));
                    break;

                case GoapProfileSensorKind.Constant:
                    var constant = definition.ConstantValue;
                    agent.SetFact(definition.Fact, constant.IsValid
                        ? constant.Value.ConvertTo(definition.Fact.ValueType)
                        : definition.Fact.DefaultTypedValue);
                    break;
            }
        }

        private static void WriteNumeric(GoapProfileSensorDefinition definition, GoapAgent agent, float rawValue)
        {
            var value = rawValue * definition.Scale + definition.Offset;
            var fact = definition.Fact;
            switch (fact.ValueType)
            {
                case GoapFactType.Integer:
                    agent.SetFact(fact, Mathf.RoundToInt(value));
                    break;
                case GoapFactType.Float:
                    agent.SetFact(fact, value);
                    break;
                case GoapFactType.Enum:
                    agent.SetFact(fact, GoapValue.FromEnum(fact.NormalizeEnumIndex(Mathf.RoundToInt(value))));
                    break;
                default:
                    var matches = GoapValue.From(value).Matches(
                        definition.Comparison,
                        GoapValue.From(definition.Threshold));
                    agent.SetFact(fact, matches);
                    break;
            }
        }

        private static void WriteObject(GoapProfileSensorDefinition definition, GoapAgent agent, object value)
        {
            if (value == null)
            {
                agent.SetFact(definition.Fact, definition.Fact.DefaultTypedValue);
                return;
            }

            try
            {
                switch (definition.Fact.ValueType)
                {
                    case GoapFactType.Boolean:
                        agent.SetFact(definition.Fact, Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                        break;
                    case GoapFactType.Integer:
                        agent.SetFact(definition.Fact, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                        break;
                    case GoapFactType.Float:
                        agent.SetFact(definition.Fact, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                        break;
                    case GoapFactType.Enum:
                        var index = value is Enum enumValue
                            ? Convert.ToInt32(enumValue, CultureInfo.InvariantCulture)
                            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
                        agent.SetFact(
                            definition.Fact,
                            GoapValue.FromEnum(definition.Fact.NormalizeEnumIndex(index)));
                        break;
                }
            }
            catch (Exception)
            {
                agent.SetFact(definition.Fact, definition.Fact.DefaultTypedValue);
            }
        }

        private static int CountNearby(GoapProfileSensorDefinition definition, GoapAgent agent)
        {
            var count = 0;
            var colliders = Physics.OverlapSphere(
                agent.transform.position,
                definition.Radius,
                definition.LayerMask,
                QueryTriggerInteraction.Collide);
            foreach (var candidate in colliders)
            {
                if (candidate == null || candidate.transform.IsChildOf(agent.transform))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(definition.RequiredTag) &&
                    !string.Equals(candidate.tag, definition.RequiredTag, StringComparison.Ordinal))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static object ReadComponentMember(GoapProfileSensorDefinition definition, GoapAgent agent)
        {
            var target = ResolveTarget(agent, definition.TargetId);
            var gameObject = target != null ? target.gameObject : agent.gameObject;
            var componentType = ResolveComponentType(definition.ComponentType);
            if (componentType == null)
            {
                return null;
            }

            var component = gameObject.GetComponent(componentType);
            if (component == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var property = componentType.GetProperty(definition.MemberName, flags);
            if (property != null && property.CanRead)
            {
                return property.GetValue(component);
            }

            return componentType.GetField(definition.MemberName, flags)?.GetValue(component);
        }

        private static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            var type = Type.GetType(typeName);
            if (type != null && typeof(Component).IsAssignableFrom(type))
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null && typeof(Component).IsAssignableFrom(type))
                {
                    return type;
                }

                try
                {
                    foreach (var candidate in assembly.GetTypes())
                    {
                        if (candidate.Name == typeName && typeof(Component).IsAssignableFrom(candidate))
                        {
                            return candidate;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Some optional Unity assemblies cannot expose all types.
                }
            }

            return null;
        }

        private static Transform ResolveTarget(GoapAgent agent, string targetId)
        {
            return agent.TryGetComponent<GoapAgentAuthoring>(out var authoring)
                ? authoring.ResolveTarget(targetId)
                : null;
        }

        private void EnsureSchedule(int count)
        {
            while (_nextSenseTimes.Count < count)
            {
                _nextSenseTimes.Add(0f);
            }

            if (_nextSenseTimes.Count > count)
            {
                _nextSenseTimes.RemoveRange(count, _nextSenseTimes.Count - count);
            }
        }
    }
}
