using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using Backend.Storage;
using Newtonsoft.Json;
using NQ;
using NQ.Interfaces;
using NQutils;
using NQutils.Def;
using NQutils.Storage;
using Orleans;

namespace CustomElements;

public sealed partial class LiveModuleCheck
{
    public async Task RunBlueprint(ulong player, ulong construct)
    {
        var shape = (await compiler.List()).First(s => s.embeddedElements.Any(e =>
            bank.GetDefinition(e.itemTypeId).BaseObject is FuelContainer));
        await definitions.Register(shape); bounds.Register(shape); modules.Remember(shape);
        var elements = client.GetConstructElementsGrain(construct);
        var before = (await elements.GetElementsOfType<Element>()).Select(id => (ulong)id).ToHashSet();
        ElementInfo parent = null, stock = null;
        ulong blueprint = 0, copy = 0;
        bool granted = false;
        try
        {
            await Give(player, shape.itemTypeId, 0, 1); granted = true;
            parent = await Place(player, construct, shape.itemTypeId, 0); granted = false;
            var request = new BlueprintCreate { constructId = construct, box = new BoundingBox
                { min = new Vec3(), max = new Vec3 { x = 32, y = 32, z = 32 } }, enableDRM = false };
            blueprint = await client.GetBlueprintGrain().Create(request, player, maxUse: 1, freeDeploy: false,
                safe: true, goesInInventory: true, singleUseBlueprint: true);
            var info = await client.GetBlueprintGrain().GetBlueprintInfo(blueprint);
            if (info.requiredItems.Any(item => bank.GetDefinition(item.id)?.Name.StartsWith("CustomPart") == true))
                throw new InvalidOperationException("Blueprint charges internal module components separately.");
            stock = await client.GetPlayerGrain(player).AddConstructElement(new ElementInfo
            {
                constructId = construct, elementType = bank.IdFor("ContainerSmall"),
                position = new Vec3 { x = 4, y = 4, z = 4 }, rotation = Quat.Identity
            });
            await StockBlueprintContainer(player, stock.elementId, blueprint, info.requiredItems);
            copy = await client.GetBlueprintGrain().Use(new BlueprintDeploy { blueprintId = blueprint,
                position = new Vec3 { x = 1000000000, y = 1000000000, z = 1000000000 }, rotation = Quat.Identity,
                containerId = stock.elementId,
                constructName = "Custom elements temporary blueprint check", impersonate = EntityId.Player(player) }, player);
            var copiedElements = client.GetConstructElementsGrain(copy);
            var all = new List<ElementInfo>();
            foreach (var id in await copiedElements.GetElementsOfType<Element>())
                all.Add(await copiedElements.GetElement(id));
            var wrapper = all.First(e => BlueprintModules.SamePlacement(parent, e));
            var group = modules.Group(wrapper.elementId);
            if (group == null || group.status != "deployed" || group.children.Count != shape.embeddedElements.Count)
                throw new InvalidOperationException("Blueprint did not create a complete custom module group.");
            if (!group.children.Any(c => bank.GetDefinition(c.element.elementType).BaseObject is FuelContainer))
                throw new InvalidOperationException("Blueprint deployment lost the embedded fuel tank.");
            if (group.children.Any(c => !all.Any(e => e.elementId == c.element.elementId)))
                throw new InvalidOperationException("A blueprint module child is missing from the live construct.");
            if ((await storage.Get(StorageRef.Container(stock.elementId), player)).content.Count != 0)
                throw new InvalidOperationException("Paid blueprint did not consume its exact public ingredient list.");
            var report = new { timestampUtc = DateTime.UtcNow, construct, blueprint, deployedConstruct = copy,
                itemTypeId = shape.itemTypeId, children = group.children.Count, fuelTankPresent = true,
                separateChildIngredients = false, automaticExpansion = true, paidDeployment = true,
                exactIngredientCharge = true, playerMaterialCharge = 0 };
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(reportPath), "custom-elements-blueprint-check.json"),
                JsonConvert.SerializeObject(report, Formatting.Indented));
            Console.WriteLine("CustomElements BLUEPRINT CHECK PASSED " + JsonConvert.SerializeObject(report));
        }
        finally
        {
            if (copy != 0) await DeleteBlueprintFixture(player, copy);
            if (stock != null) await DeleteBlueprintStock(player, stock);
            if (blueprint != 0)
            {
                var inventory = await storage.Get(StorageRef.PlayerInventory(player), player);
                if (inventory.content.Any(slot => slot.content.id == blueprint))
                    await Give(player, bank.IdFor("SingleUseBlueprint"), blueprint, -1);
                await client.GetBlueprintGrain().Delete(blueprint);
            }
            if (parent != null) await client.GetElementManagementGrain().ElementDestroy(player, parent.InConstruct());
            else if (granted) await Give(player, shape.itemTypeId, 0, -1);
            if (!(await elements.GetElementsOfType<Element>()).Select(id => (ulong)id).ToHashSet().SetEquals(before))
                throw new InvalidOperationException("Blueprint fixture cleanup changed original source elements.");
        }
    }

    private async Task StockBlueprintContainer(ulong player, ulong container, ulong blueprint,
        List<ItemRequired> ingredients)
    {
        var inventory = await storage.Get(StorageRef.PlayerInventory(player), player);
        var item = inventory.content.Single(slot => slot.content.id == blueprint).content;
        await using var transaction = await storage.MakeTransaction(Tag.HttpCall("CustomElements.BlueprintCheck"));
        await storage.GiveOrTakeItems(transaction, StorageRef.PlayerInventory(player),
            new[] { new ItemAndQuantity { item = item, quantity = -1 } }, new OperationOptions());
        var contents = ingredients.Select(i => new ItemAndQuantity
            { item = new ItemInfo { type = i.id }, quantity = (long)i.quantity }).ToList();
        contents.Add(new ItemAndQuantity { item = item, quantity = 1 });
        await storage.GiveOrTakeItems(transaction, StorageRef.Container(container), contents,
            new OperationOptions { AllowOverload = true });
        await transaction.Commit();
    }

    private async Task DeleteBlueprintStock(ulong player, ElementInfo stock)
    {
        var contents = await storage.Get(StorageRef.Container(stock.elementId), player);
        if (contents.content.Count > 0)
        {
            await using var transaction = await storage.MakeTransaction(Tag.HttpCall("CustomElements.BlueprintCleanup"));
            await storage.GiveOrTakeItems(transaction, StorageRef.Container(stock.elementId),
                contents.content.Select(slot => new ItemAndQuantity { item = slot.content,
                    quantity = -slot.quantity.quantity }), new OperationOptions());
            await transaction.Commit();
        }
        await client.GetElementManagementGrain().ElementDestroy(player, stock.InConstruct());
    }

    private async Task DeleteBlueprintFixture(ulong player, ulong construct)
    {
        var elements = client.GetConstructElementsGrain(construct);
        var wrappers = new List<ElementInfo>();
        foreach (var id in await elements.GetElementsOfType<Element>())
        {
            var item = await elements.GetElement(id);
            if (modules.Group(item.elementId) != null) wrappers.Add(item);
        }
        foreach (var item in wrappers)
            await client.GetElementManagementGrain().ElementDestroy(player, item.InConstruct());
        await client.GetConstructParentingGrain().DeleteConstruct(construct, hardDelete: true);
    }
}
