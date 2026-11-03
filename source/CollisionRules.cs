using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using NQ;
using NQ.Grains.Core;
using NQ.Interfaces;
using Orleans;

namespace CustomElements;

public sealed class CollisionRules
{
    private readonly IClusterClient client;
    private readonly IGameplayBank bank;

    public CollisionRules(IClusterClient client, IGameplayBank bank) { this.client = client; this.bank = bank; }

    public void Install(IHookCallManager hooks)
    {
        hooks.Register("ConstructElementCollisionGrain.SetCollidingElements", HookMode.PreCall, this, nameof(Filter));
        hooks.Register("ConstructElementCollisionGrain.SetCollidingElementList", HookMode.PreCall, this, nameof(FilterList));
    }

    public bool IsCustom(ulong itemType) => bank.GetDefinition(itemType)?.Name.StartsWith(
        "CustomVoxel", StringComparison.Ordinal) == true;

    public async Task Filter(string grainKey, ElementColliding report)
    {
        var elements = client.GetConstructElementsGrain(report.element.constructId);
        if (IsCustom((await elements.GetElement(report.element.elementId)).elementType))
        {
            report.colliding.Clear();
            return;
        }
        var retained = new List<ElementId>();
        foreach (var id in report.colliding)
            if (!IsCustom((await elements.GetElement(id)).elementType)) retained.Add(id);
        report.colliding = retained;
    }

    public async Task FilterList(string grainKey, ElementCollidingList report)
    {
        foreach (var element in report.elements) await Filter(grainKey, element);
    }
}
