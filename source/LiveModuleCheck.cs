using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using Backend.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NQ;
using NQ.Interfaces;
using NQutils;
using NQutils.Def;
using NQutils.Storage;
using Orleans;

namespace CustomElements;

// Explicit admin diagnostic: one temporary fixture, normal inventory placement, then complete cleanup.
public sealed partial class LiveModuleCheck
{
    private readonly IClusterClient client;
    private readonly IGameplayBank bank;
    private readonly IItemStorageService storage;
    private readonly Compiler compiler;
    private readonly Definitions definitions;
    private readonly ModuleHooks modules;
    private readonly Bounds bounds;
    private readonly string reportPath;

    public LiveModuleCheck(IClusterClient client, IGameplayBank bank, IItemStorageService storage,
        Compiler compiler, Definitions definitions, ModuleHooks modules, Bounds bounds, string folder)
    {
        this.client = client; this.bank = bank; this.storage = storage; this.compiler = compiler;
        this.definitions = definitions; this.modules = modules; this.bounds = bounds;
        reportPath = Path.Combine(folder, "custom-elements-live-check.json");
    }

    public async Task Run(ulong player, ulong construct)
    {
        var shape = await Prepare();
        var elements = client.GetConstructElementsGrain(construct);
        var before = (await elements.GetElementsOfType<Element>()).Select(id => (ulong)id).ToHashSet();
        ElementInfo parent = null;
        ulong inventoryInstance = 0;
        bool inventoryItem = false;
        try
        {
            await Give(player, shape.itemTypeId, 0, 1); inventoryItem = true;
            parent = await Place(player, construct, shape.itemTypeId, 0); inventoryItem = false;
            var children = await Children(elements, before, parent.elementId);
            if (children.Count != 3 || children.SelectMany(e => e.links).Distinct().Count() != 2)
                throw new InvalidOperationException("Native child count or signal-link restoration failed.");
            var originalIds = children.Select(e => e.elementId).OrderBy(id => id).ToArray();
            var chosen = children[0];
            await client.GetElementManagementGrain().MoveElement(player, new ElementLocation
            {
                elementId = chosen.elementId, location = new RelativeLocation { constructId = construct,
                    position = new Vec3 { x = chosen.position.x + 1, y = chosen.position.y, z = chosen.position.z },
                    rotation = chosen.rotation }
            });
            var moved = await elements.GetElement(parent.elementId);
            if (Math.Abs(moved.position.x - 17) > 0.00001) throw new InvalidOperationException("Group movement failed.");
            await client.GetElementManagementGrain().ElementDelete(player, chosen.InConstruct()); inventoryItem = true;
            inventoryInstance = 0;
            if (!(await elements.GetElementsOfType<Element>()).Select(id => (ulong)id).ToHashSet().SetEquals(before))
                throw new InvalidOperationException("Group pickup left deployed children behind.");
            var stored = await storage.Get(StorageRef.PlayerInventory(player), player);
            if (!stored.content.Any(slot => slot.content.type == shape.itemTypeId && slot.content.id == 0) ||
                stored.content.Any(slot => slot.content.id == parent.elementId))
                throw new InvalidOperationException("An unchanged module did not return to a default stack.");
            parent = await Place(player, construct, shape.itemTypeId, 0); inventoryItem = false;
            children = await Children(elements, before, parent.elementId);
            if (originalIds.Intersect(children.Select(e => e.elementId)).Any())
                throw new InvalidOperationException("A pristine stacked item retained private child instances.");
            if (children.SelectMany(e => e.links).Distinct().Count() != 2)
                throw new InvalidOperationException("Redeployment lost internal signal links.");
            var modified = children[0];
            await elements.UpdateElementProperty(new ElementPropertyUpdate { constructId = construct,
                elementId = modified.elementId, name = "name", value = new PropertyValue
                { stringValue = "CustomElements preservation check" } });
            var modifiedIds = children.Select(e => e.elementId).OrderBy(id => id).ToArray();
            await client.GetElementManagementGrain().ElementDelete(player, parent.InConstruct()); inventoryItem = true;
            inventoryInstance = parent.elementId;
            parent = await Place(player, construct, shape.itemTypeId, inventoryInstance); inventoryItem = false;
            children = await Children(elements, before, parent.elementId);
            if (!modifiedIds.SequenceEqual(children.Select(e => e.elementId).OrderBy(id => id)) ||
                !children.Any(e => e.properties.TryGetValue("name", out var name) &&
                    name.stringValue == "CustomElements preservation check"))
                throw new InvalidOperationException("Modified component state or child instance IDs were lost.");
            await client.GetElementManagementGrain().ElementDestroy(player, parent.InConstruct()); parent = null;
            bool clean = (await elements.GetElementsOfType<Element>()).Select(id => (ulong)id).ToHashSet().SetEquals(before);
            if (!clean) throw new InvalidOperationException("Fixture cleanup changed the original construct elements.");
            var report = new { timestampUtc = DateTime.UtcNow, construct, itemTypeId = shape.itemTypeId,
                nativeChildren = 3, nativeSignalLinks = 2, groupMove = true, groupPickup = true,
                unchangedReturnsToStack = true, modifiedChildInstanceIdsPreserved = true,
                modifiedChildPropertiesPreserved = true, redeployedLinks = true, originalConstructRestored = true,
                materialCharge = 0, inventoryNetChange = 0, nativeClientSignalExecution = "not exercised" };
            File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            Console.WriteLine("CustomElements LIVE CHECK PASSED " + JsonConvert.SerializeObject(report));
        }
        finally
        {
            var remaining = await Children(elements, before, 0);
            if (remaining.Any(e => e.elementType == shape.itemTypeId)) inventoryItem = false;
            foreach (var e in remaining.Where(e => e.elementType == shape.itemTypeId))
                await client.GetElementManagementGrain().ElementDestroy(player, e.InConstruct());
            if (inventoryItem) await Give(player, shape.itemTypeId, inventoryInstance, -1);
        }
    }

    public async Task<Shape> Prepare()
    {
        var shape = await compiler.Prepare(new VoxelData { cells = new JArray(), meshes = new JObject() }, Fixture());
        await definitions.Register(shape); bounds.Register(shape); modules.Remember(shape);
        Console.WriteLine("CustomElements live-check template ready itemType=" + shape.itemTypeId);
        return shape;
    }

    private ModuleSource Fixture()
    {
        var source = new ModuleSource();
        var types = new[] { bank.GetDefinition("ManualSwitch"), bank.GetDefinition("NotOperator"),
            bank.GetDefinition("LightSquareSmall") };
        for (int i = 0; i < types.Length; i++) source.elements.Add(new EmbeddedElement
        {
            key = (ulong)i + 1, itemTypeId = types[i].Id, assetAlias = string.IsNullOrEmpty(types[i].BaseObject.AssetAlias) ?
                types[i].Name : types[i].BaseObject.AssetAlias,
            position = new[] { 16.0 + i, 16, 16 }, rotation = new[] { 0.0, 0, 0, 1 }
        });
        for (int i = 1; i < 3; i++) source.links.Add(new LinkInfo { fromElementId = (ulong)i,
            toElementId = (ulong)i + 1, fromPlug = 0, toPlug = 0, plugType = PlugType.PLUG_SIGNAL });
        return source;
    }

    private async Task<List<ElementInfo>> Children(IConstructElementsGrain elements, HashSet<ulong> before, ulong parent)
    {
        var result = new List<ElementInfo>();
        foreach (var id in await elements.GetElementsOfType<Element>())
            if (!before.Contains(id) && (ulong)id != parent) result.Add(await elements.GetElement(id));
        return result;
    }

    private Task<ElementInfo> Place(ulong player, ulong construct, ulong type, ulong instance) =>
        client.GetElementManagementGrain().ElementAdd(player, new ElementDeploy
        {
            fromInventory = new ItemId { typeId = type, instanceId = instance, ownerId = EntityId.Empty },
            element = new ElementInfo { elementId = instance, elementType = type, constructId = construct,
                position = new Vec3 { x = 16, y = 16, z = 16 }, rotation = Quat.Identity }
        });

    private async Task Give(ulong player, ulong type, ulong instance, long count)
    {
        await using var transaction = await storage.MakeTransaction(Tag.HttpCall("CustomElements.LiveCheck"));
        await storage.GiveOrTakeItems(transaction, StorageRef.PlayerInventory(player), new[]
        {
            new ItemAndQuantity { item = new ItemInfo { type = type, id = instance }, quantity = count }
        }, new OperationOptions());
        await transaction.Commit();
    }
}
