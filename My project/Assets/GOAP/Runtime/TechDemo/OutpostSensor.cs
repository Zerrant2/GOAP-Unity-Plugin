using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OutpostAgent))]
    public sealed class OutpostSensor : GoapSensorBehaviour
    {
        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            var actor = GetComponent<OutpostAgent>();
            var controller = actor.Controller;
            if (controller == null || agent.Domain == null)
            {
                return;
            }

            var stockpile = controller.Stockpile;
            var camp = controller.Camp;
            var threatened = controller.IsPositionThreatened(transform.position, 6f);
            actor.MarkSafe(!threatened);

            Set(agent, "Hunger", actor.Hunger);
            Set(agent, "Energy", actor.Energy);
            Set(agent, "Health", actor.Health);
            Set(agent, "Food Stockpile", stockpile != null ? stockpile.Food : 0);
            Set(agent, "Wood Stockpile", stockpile != null ? stockpile.Wood : 0);
            Set(agent, "Carry Food", actor.CarryFood);
            Set(agent, "Carry Wood", actor.CarryWood);
            Set(agent, "Has Weapon", actor.HasWeapon);
            Set(agent, "Tree Available", GoapSmartObject.CountAvailable(
                OutpostIds.TreeCategory, transform.position, agent, float.PositiveInfinity) > 0);
            Set(agent, "Food Source Available", GoapSmartObject.CountAvailable(
                OutpostIds.FoodCategory, transform.position, agent, float.PositiveInfinity) > 0);
            Set(agent, "Bed Available", GoapSmartObject.CountAvailable(
                OutpostIds.BedCategory, transform.position, agent, float.PositiveInfinity) > 0);
            Set(agent, "Need Wood", stockpile != null && stockpile.Wood < controller.WoodTarget);
            Set(agent, "Need Food", stockpile != null && stockpile.Food < controller.FoodTarget);
            Set(agent, "Wood Delivered", actor.WoodDelivered);
            Set(agent, "Food Delivered", actor.FoodDelivered);
            Set(agent, "Camp Damaged", camp != null && camp.IsDamaged);
            Set(agent, "Camp Repaired", actor.CampRepaired);
            Set(agent, "Enemy Visible", controller.HasLivingMonsters);
            Set(agent, "Enemy Defeated", actor.EnemyDefeated);
            Set(agent, "Safe", actor.IsSafe);
            Set(agent, "Patrol Done", actor.PatrolDone);
        }

        private static void Set(GoapAgent agent, string factName, bool value)
        {
            var fact = agent.Domain.FindFact(factName);
            if (fact != null)
            {
                agent.SetFact(fact, value);
            }
        }

        private static void Set(GoapAgent agent, string factName, int value)
        {
            var fact = agent.Domain.FindFact(factName);
            if (fact != null)
            {
                agent.SetFact(fact, value);
            }
        }

        private static void Set(GoapAgent agent, string factName, float value)
        {
            var fact = agent.Domain.FindFact(factName);
            if (fact != null)
            {
                agent.SetFact(fact, value);
            }
        }
    }
}
