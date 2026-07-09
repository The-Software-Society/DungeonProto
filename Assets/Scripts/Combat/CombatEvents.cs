using System;
using UnityEngine;

namespace AldenFall.Combat
{
    /// <summary>Static combat event bus for UI (damage numbers, kill toasts).</summary>
    public static class CombatEvents
    {
        /// <summary>worldPos, amount, dealtByPlayer</summary>
        public static event Action<Vector3, float, bool> DamageNumber;

        /// <summary>worldPos, text, color — "Stunned!", "+25", "Burnt!"</summary>
        public static event Action<Vector3, string, Color> StatusText;

        public static void RaiseDamage(Vector3 worldPos, float amount, bool dealtByPlayer)
            => DamageNumber?.Invoke(worldPos, amount, dealtByPlayer);

        public static void RaiseStatus(Vector3 worldPos, string text, Color color)
            => StatusText?.Invoke(worldPos, text, color);
    }
}
