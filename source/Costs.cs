using System;
using System.Collections.Generic;
using Backend;
using NQ;
using NQutils.Def;

namespace CustomElements;

public static class Costs
{
    public static Dictionary<ulong, long> ForCopies(Shape shape, int count, IGameplayBank bank)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var result = new Dictionary<ulong, long>();
        foreach (var material in shape.materialVolumes)
        {
            if (!(bank.GetDefinition(material.Key)?.BaseObject is Honeycomb) ||
                !double.IsFinite(material.Value) || material.Value <= 0 || material.Value > 262144)
                throw new InvalidOperationException("Invalid source honeycomb material or volume.");
            // Material quantities use native 24-bit fixed-point cubic metres, not whole item counts.
            long quantity = checked(ItemQuantity.ToQuantity(material.Value) * count);
            if (quantity <= 0) throw new InvalidOperationException("Source material amount is too small.");
            result.Add(material.Key, quantity);
        }
        foreach (var element in shape.embeddedElements)
        {
            if (!(bank.GetDefinition(element.itemTypeId)?.BaseObject is Element))
                throw new InvalidOperationException("Unknown embedded element type.");
            result[element.itemTypeId] = checked(result.GetValueOrDefault(element.itemTypeId) + count);
        }
        if (result.Count == 0) throw new InvalidOperationException("The source contains no billable material or elements.");
        return result;
    }
}
