using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Backend;

namespace CustomElements;

public static class BankNotifications
{
    private sealed class Guard
    {
        public readonly object gate = new();
        public OnChange wrapped;
    }

    private static readonly ConditionalWeakTable<IGameplayBank, Guard> installed = new();

    public static void Serialize(IGameplayBank bank)
    {
        lock (installed)
        {
            var field = typeof(GameplayBank).GetField("_onChange", BindingFlags.Instance | BindingFlags.NonPublic);
            if (bank is not GameplayBank || field?.FieldType != typeof(OnChange))
                throw new InvalidOperationException("Unsupported gameplay-bank notification implementation.");
            var listeners = (OnChange)field.GetValue(bank);
            var guard = installed.GetOrCreateValue(bank);
            if (listeners == guard.wrapped && guard.wrapped != null) return;
            // Import invokes listeners directly and through its own Redis subscription concurrently.
            // Native ItemPermissionCache.Fill mutates shared dictionaries and must not overlap itself.
            // Native services can register additional listeners after mods initialize. Enclose those
            // listeners too, reusing the same gate as any wrapper already captured by an active import.
            OnChange serialized = () => { lock (guard.gate) listeners?.Invoke(); };
            field.SetValue(bank, serialized);
            guard.wrapped = serialized;
        }
    }
}
