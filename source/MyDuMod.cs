using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Backend;
using Backend.Storage;
using CustomElements;
using Newtonsoft.Json;
using NQ;
using NQ.Interfaces;
using NQ.Grains.Core;
using NQutils;
using NQutils.Def;
using NQutils.Storage;
using NQutils.Sql;
using Orleans;

[assembly: TargetFramework(".NETCoreApp,Version=v6.0")]

public sealed partial class MyDuMod : IMod
{
    private readonly SemaphoreSlim generation = new(1, 1);
    private readonly ConcurrentDictionary<ulong, CreationDialog> dialogs = new();
    private readonly ConcurrentDictionary<ulong, HashSet<ulong>> loginTypes = new();
    private readonly PreviewDelivery previewDelivery = new();
    private IClusterClient client;
    private IGameplayBank bank;
    private IVoxelService voxels;
    private IPub pub;
    private IItemStorageService storage;
    private Compiler compiler;
    private Definitions definitions;
    private Assets assets;
    private Settings settings;
    private string panelPath;
    private Jobs jobs;
    private Bounds bounds;
    private SourceElements sourceElements;
    private ModuleHooks modules;
    private LiveModuleCheck liveCheck;
    private Designs designs;
    private readonly ConcurrentDictionary<ulong, ulong> pendingChecks = new();

    public string GetName() => "CustomElements";

    public Task Initialize(IServiceProvider services)
    {
        string folder = Path.GetDirectoryName(typeof(MyDuMod).Assembly.Location);
        settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(
            Path.Combine(folder, "custom-elements.json")));
        client = (IClusterClient)services.GetService(typeof(IClusterClient));
        bank = (IGameplayBank)services.GetService(typeof(IGameplayBank));
        BankNotifications.Serialize(bank);
        voxels = (IVoxelService)services.GetService(typeof(IVoxelService));
        pub = (IPub)services.GetService(typeof(IPub));
        storage = (IItemStorageService)services.GetService(typeof(IItemStorageService));
        compiler = new Compiler(settings, bank);
        assets = new Assets(pub, bank);
        jobs = new Jobs(Path.Combine(folder, "custom-elements-jobs"));
        designs = new Designs(Path.Combine(folder, "custom-element-designs.json"));
        designs.Seed(Path.Combine(folder, "custom-elements-jobs"), bank);
        definitions = new Definitions(bank, designs);
        bounds = Bounds.FromServices(services);
        sourceElements = new SourceElements(client, bank);
        modules = new ModuleHooks(client, bank, storage, compiler,
            (ISql)services.GetService(typeof(ISql)), folder);
        var hooks = (IHookCallManager)services.GetService(typeof(IHookCallManager));
        modules.Install(hooks);
        new CollisionRules(client, bank).Install(hooks);
        liveCheck = new LiveModuleCheck(client, bank, storage, compiler, definitions, modules, bounds, folder);
        Runtime.RegisterBounds = shape => { bounds.Register(shape); modules.Remember(shape); };
        Runtime.AssetsReady = CompletePending;
        panelPath = Path.Combine(folder, "custom-elements-panel.js");
        return Task.CompletedTask;
    }

    public Task<ModInfo> GetModInfoFor(ulong playerId, bool admin)
    {
        loginTypes[playerId] = new HashSet<ulong>(bank.GetDefinitions().Select(definition => definition.Id));
        StartPreviewDelivery(playerId);
        return Task.FromResult(new ModInfo { name = GetName(), actions = new List<ModActionDefinition>
        {
            new() { id = 10, label = "Custom elements\\Create custom items", context = ModActionContext.Element },
            new() { id = 10, label = "Custom elements\\Create custom items",
                context = ModActionContext.Construct },
            new() { id = 11, label = "Custom elements\\Create 1 custom item", context = ModActionContext.Element },
            new() { id = 11, label = "Custom elements\\Create 1 custom item", context = ModActionContext.Construct },
            new() { id = 12, label = "Custom elements\\Create 100 custom item", context = ModActionContext.Element },
            new() { id = 12, label = "Custom elements\\Create 100 custom item", context = ModActionContext.Construct },
            new() { id = 13, label = "Custom elements\\Rename shared custom item", context = ModActionContext.Element }
        } });
    }

    public async Task TriggerAction(ulong playerId, ModAction action)
    {
        try { await HandleAction(playerId, action); }
        catch (Exception error)
        {
            Console.WriteLine($"CustomElements action failed player={playerId} construct={action.constructId}: {error}");
            await Notify(playerId, error.Message, action.actionId == 20);
        }
    }

    private async Task HandleAction(ulong playerId, ModAction action)
    {
        if (!settings.enabled) throw new InvalidOperationException("Custom Elements is awaiting client integration.");
        if (action.actionId == 13) { await OpenRename(playerId, action); return; }
        if (action.actionId == 14) { await OpenRename(playerId, action); return; }
        if (action.actionId == 21) { await Rename(playerId, action); return; }
        if (action.actionId == 22) { previewDelivery.Acknowledge(playerId, action.payload); return; }
        if (action.actionId == 95 && await client.GetPlayerGrain(playerId).IsAdmin())
        {
            Console.WriteLine("CustomElements preview probe " + action.payload.Substring(0, Math.Min(16000, action.payload.Length)));
            return;
        }
        if (action.actionId == 96 && await client.GetPlayerGrain(playerId).IsAdmin())
        { await InstallPreview(playerId); return; }
        if (action.actionId == 97 && await client.GetPlayerGrain(playerId).IsAdmin())
        {
            await Hud(playerId, "modinjectjs", File.ReadAllText(Path.Combine(
                Path.GetDirectoryName(panelPath), "custom-elements-ui-probe.js")));
            return;
        }
        if (action.actionId == 10)
        {
            await OpenDialog(playerId, action.constructId, action.elementId);
            return;
        }
        if (action.actionId == 11 || action.actionId == 12)
        {
            await CreateFixedQuantity(playerId, action.constructId, action.elementId,
                action.actionId == 11 ? 1 : 100);
            return;
        }
        if (action.actionId == 91 && await client.GetPlayerGrain(playerId).IsAdmin())
        {
            var dialog = await OpenDialog(playerId, action.constructId, action.elementId);
            var test = JsonConvert.DeserializeObject<GenerationRequest>(action.payload);
            test.nonce = dialog.nonce; test.requestId = dialog.requestId;
            await Submit(playerId, action.constructId, action.elementId, test);
            return;
        }
        if (action.actionId == 92 && await client.GetPlayerGrain(playerId).IsAdmin())
        {
            var shapes = await compiler.List();
            if (shapes.Count > 0) await definitions.Refresh(shapes[0]);
            foreach (var shape in shapes)
            {
                await definitions.Register(shape); bounds.Register(shape); modules.Remember(shape);
            }
            await assets.SendAll(playerId, shapes);
            return;
        }
        if (action.actionId == 93 && await client.GetPlayerGrain(playerId).IsAdmin())
        {
            await ValidateSource(playerId, action.constructId);
            var fixture = await liveCheck.Prepare();
            if (!await client.GetPlayerGrain(playerId).IsConnected() ||
                (loginTypes.TryGetValue(playerId, out var known) && known.Contains(fixture.itemTypeId)))
            {
                await liveCheck.Run(playerId, action.constructId);
                await Notify(playerId, "Native module lifecycle checks passed; the temporary elements were removed.", false);
            }
            else
            {
                pendingChecks[playerId] = action.constructId;
                await Notify(playerId, "Verification fixture registered. Reconnect once to run the native module checks.", false);
            }
            return;
        }
        if (action.actionId == 94 && await client.GetPlayerGrain(playerId).IsAdmin())
        {
            await ValidateSource(playerId, action.constructId);
            await liveCheck.RunBlueprint(playerId, action.constructId);
            await Notify(playerId, "Blueprint module check passed; temporary fixtures were removed.", false);
            return;
        }
        if (action.actionId == 98 && await client.GetPlayerGrain(playerId).IsAdmin())
        {
            await ValidateSource(playerId, action.constructId);
            await modules.RepairBlueprintConstruct(playerId, action.constructId);
            await Notify(playerId, "Missing blueprint module components restored.", false);
            return;
        }
        if (action.actionId == 90)
        {
            var report = JsonConvert.DeserializeObject<GenerationRequest>(action.payload);
            if (report != null && dialogs.TryGetValue(playerId, out var opened) && report.nonce == opened.nonce &&
                report.requestId == opened.requestId)
                Console.WriteLine($"CustomElements dialog-visible player={playerId} construct={opened.constructId}");
            return;
        }
        if (action.actionId != 20) throw new InvalidOperationException("Unknown custom element action.");
        var request = JsonConvert.DeserializeObject<GenerationRequest>(action.payload);
        await Submit(playerId, action.constructId, action.elementId, request);
    }

    private async Task<CreationDialog> OpenDialog(ulong player, ulong construct, ulong element)
    {
        var dialog = await ValidateCore(player, construct, element);
        dialogs[player] = dialog;
        string payload = JsonConvert.SerializeObject(new
        {
            constructId = construct.ToString(), elementId = element.ToString(), dialog.nonce, dialog.requestId,
            maximumBatch = settings.maximumBatch
        });
        // Keep script installation and opening in one packet; PubSub does not preserve arrival order.
        await Hud(player, "modinjectjs", File.ReadAllText(panelPath) +
            "\nwindow.customElementsPanel.open(" + payload + ");");
        return dialog;
    }

    private async Task CreateFixedQuantity(ulong player, ulong construct, ulong element, int quantity)
    {
        var dialog = await ValidateCore(player, construct, element);
        dialog.showResults = false;
        dialogs[player] = dialog;
        await Submit(player, construct, element, new GenerationRequest
        {
            nonce = dialog.nonce, requestId = dialog.requestId, quantity = quantity
        });
    }

    private async Task<CreationDialog> ValidateCore(ulong player, ulong construct, ulong element)
    {
        await ValidateSource(player, construct);
        if (element != 0 && !(bank.GetDefinition((await client.GetConstructElementsGrain(construct)
            .GetElement(element)).elementType)?.BaseObject is CoreUnit))
            throw new InvalidOperationException("Right-click the source core unit to create custom items.");
        return new CreationDialog { constructId = construct, elementId = element };
    }

    private async Task Submit(ulong playerId, ulong construct, ulong element, GenerationRequest request)
    {
        if (request == null || !dialogs.TryGetValue(playerId, out var dialog) || request.nonce != dialog.nonce ||
            request.requestId != dialog.requestId || construct != dialog.constructId || element != dialog.elementId ||
            request.quantity < 1 || request.quantity > settings.maximumBatch)
            throw new InvalidOperationException("Invalid generation request.");
        await generation.WaitAsync();
        try
        {
            var previous = jobs.Get(request.requestId);
            if (previous != null)
                await Notify(playerId, $"Request already {previous.status}; no duplicate items created.", dialog.showResults);
            else await Generate(playerId, construct, request, dialog.showResults);
        }
        catch (Exception error)
        {
            Console.WriteLine($"CustomElements failed request={request.requestId}: {error}");
            var failed = jobs.Get(request.requestId);
            if (failed != null) { failed.lastError = error.Message; jobs.Save(failed); }
            await Notify(playerId, error.Message, dialog.showResults);
        }
        finally { generation.Release(); }
    }

    private async Task ValidateSource(ulong player, ulong construct)
    {
        var info = await client.GetConstructInfoGrain(construct).Get();
        if ((info.rData.geometry.size != 32 && info.rData.geometry.size != 64) || (info.kind != ConstructKind.DYNAMIC &&
            info.kind != ConstructKind.STATIC && info.kind != ConstructKind.SPACE))
            throw new InvalidOperationException("Select a player XS or S construct.");
        var owner = await client.GetConstructGrain(construct).GetOwner();
        bool authorized = owner.playerId == player || (owner.organizationId != 0 &&
            await client.GetOrganizationGrain(owner.organizationId).IsLegate(player));
        if (!authorized) throw new InvalidOperationException("Only the owner or an organization legate can export.");
    }

    private async Task Generate(ulong player, ulong construct, GenerationRequest request, bool showResults)
    {
        await ValidateSource(player, construct);
        var raw = await voxels.GetVoxelJson(VoxelObject.Construct(construct), true);
        var module = await sourceElements.Read(construct);
        var shape = await compiler.Prepare(raw, module);
        var costs = Costs.ForCopies(shape, request.quantity, bank);
        string sourceName = (await client.GetConstructInfoGrain(construct).Get()).rData.name;
        string proposedName = Designs.DefaultName(sourceName, shape.hash);
        designs.Ensure(shape.itemTypeId, player, construct, proposedName);
        await definitions.Register(shape);
        bounds.Register(shape);
        modules.Remember(shape);
        foreach (ulong recipient in loginTypes.Keys)
            if (await client.GetPlayerGrain(recipient).IsConnected()) await assets.Send(recipient, shape);
        var job = new CreationJob { requestId = request.requestId, playerId = player, constructId = construct,
            itemTypeId = shape.itemTypeId, quantity = request.quantity, showResults = showResults, costs = costs };
        jobs.Save(job);
        if (!loginTypes.TryGetValue(player, out var known) || !known.Contains(shape.itemTypeId))
        {
            await Notify(player, $"Design registered. Reconnect once to receive {request.quantity} custom items. " +
                "Materials are charged on delivery. The request is saved; do not create it again.", job.showResults);
            Audit(job);
            return;
        }
        await Finish(job);
    }

    private async Task Finish(CreationJob job, bool notify = true)
    {
        await using var transaction = await storage.MakeTransaction(Tag.HttpCall("CustomElements.Generate"));
        var inputs = job.costs.Select(cost => new ItemAndQuantity
            { item = new ItemInfo { type = cost.Key }, quantity = -cost.Value }).ToArray();
        if (inputs.Length > 0)
            await storage.GiveOrTakeItems(transaction, StorageRef.PlayerInventory(job.playerId), inputs,
                new OperationOptions { Requester = job.playerId });
        await storage.GiveOrTakeItems(transaction, StorageRef.PlayerInventoryWithoutPrimary(job.playerId),
            new[] { new ItemAndQuantity { item = new ItemInfo { type = job.itemTypeId }, quantity = job.quantity } },
            new OperationOptions());
        // Preparation failures remain retryable. A failed commit can have an ambiguous outcome.
        job.status = "granting"; jobs.Save(job);
        await transaction.Commit();
        job.status = "completed"; job.lastError = null; jobs.Save(job); Audit(job);
        if (notify)
        {
            await Notify(job.playerId, $"Created {job.quantity} custom items in your inventory.", job.showResults);
        }
    }

    private async Task CompletePending(ulong player)
    {
        if (!settings.enabled || !loginTypes.TryGetValue(player, out var known)) return;
        await generation.WaitAsync();
        try
        {
            foreach (var job in jobs.PendingFor(player))
            {
                if (!known.Contains(job.itemTypeId)) continue;
                try { await Finish(job, notify: false); }
                catch (Exception error)
                {
                    job.lastError = error.Message; jobs.Save(job); Audit(job);
                }
            }
        }
        finally { generation.Release(); }
        await RunPendingCheck(player);
    }

    private async Task RunPendingCheck(ulong player)
    {
        if (!pendingChecks.TryRemove(player, out var construct)) return;
        try
        {
            await liveCheck.Run(player, construct);
            await Notify(player, "Native module placement, movement, pickup and redeployment checks passed.", false);
        }
        catch (Exception error)
        {
            Console.WriteLine($"CustomElements live check failed: {error}");
            await Notify(player, "Module verification needs inspection: " + error.Message, false);
        }
    }

    private Task Result(ulong player, string message) => Hud(player, "CustomElements.result",
        JsonConvert.SerializeObject(new { message }));

    private Task Notify(ulong player, string message, bool dialog) => dialog ? Result(player, message) :
        pub.NotifyTopic(Topics.PlayerNotifications(player), new NQutils.Messages.PopupReceived(
            new PopupMessage { target = player, message = "Custom elements: " + message }));

    private static void Audit(CreationJob job) => Console.WriteLine("CustomElements " + JsonConvert.SerializeObject(job));

    private Task Hud(ulong player, string name, string payload) => pub.NotifyTopic(Topics.PlayerNotifications(player),
        new NQutils.Messages.ModTriggerHudEventRequest(new ModTriggerHudEvent
            { eventName = name, eventPayload = payload }));
}
