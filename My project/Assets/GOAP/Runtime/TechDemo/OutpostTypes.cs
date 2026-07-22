using System;
using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    public enum OutpostRole
    {
        Lumberjack,
        Forager,
        Guard,
        Builder
    }

    public enum OutpostResourceKind
    {
        Wood,
        Food
    }

    public static class OutpostIds
    {
        public const string Eat = "outpost.eat";
        public const string Sleep = "outpost.sleep";
        public const string Flee = "outpost.flee";
        public const string HarvestWood = "outpost.harvest-wood";
        public const string DeliverWood = "outpost.deliver-wood";
        public const string HarvestFood = "outpost.harvest-food";
        public const string DeliverFood = "outpost.deliver-food";
        public const string TakeWeapon = "outpost.take-weapon";
        public const string Attack = "outpost.attack";
        public const string Repair = "outpost.repair";
        public const string Patrol = "outpost.patrol";

        public const string TreeCategory = "Outpost Tree";
        public const string FoodCategory = "Outpost Food";
        public const string BedCategory = "Outpost Bed";
        public const string ArmoryCategory = "Outpost Armory";
        public const string EnemyCategory = "Outpost Enemy";
    }

    [Serializable]
    public struct OutpostRoleProfile
    {
        [SerializeField] private OutpostRole _role;
        [SerializeField] private GoapAgentProfile _profile;
        [SerializeField] private Material _material;

        public OutpostRole Role => _role;
        public GoapAgentProfile Profile => _profile;
        public Material Material => _material;

        public OutpostRoleProfile(OutpostRole role, GoapAgentProfile profile, Material material)
        {
            _role = role;
            _profile = profile;
            _material = material;
        }
    }
}
