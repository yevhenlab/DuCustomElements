using Backend;
using CustomElements;
using NQ;
using NQutils;
using NQutils.Def;
using Newtonsoft.Json.Linq;

await VerifyPreviewDelivery();

static async Task VerifyPreviewDelivery()
{
    var delivery = new PreviewDelivery();
    int sends = 0;
    var messages = new List<string>();
    await delivery.Start(7, nonce =>
    {
        sends++;
        if (delivery.Acknowledge(8, nonce) || delivery.Acknowledge(7, "stale"))
            throw new Exception("A preview ACK crossed player/session boundaries.");
        if (sends == 3) delivery.Acknowledge(7, nonce);
        return Task.CompletedTask;
    }, messages.Add, 5, 5);
    if (sends != 3 || !messages.Last().Contains("client acknowledged"))
        throw new Exception("Preview delivery did not retry until the client acknowledged installation.");
    sends = 0; messages.Clear();
    await delivery.Start(7, _ => { sends++; throw new IOException("not connected"); }, messages.Add, 2, 1);
    if (sends != 2 || messages.Last() != "preview installation unconfirmed")
        throw new Exception("Preview delivery failed to bound unsuccessful startup retries.");
    var firstSend = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var previous = delivery.Start(7, nonce =>
    {
        firstSend.TrySetResult(nonce); return Task.CompletedTask;
    }, _ => { }, 10, 50);
    string stale = await firstSend.Task;
    await delivery.Start(7, nonce =>
    {
        if (delivery.Acknowledge(7, stale) || !delivery.Acknowledge(7, nonce))
            throw new Exception("A reconnect reused the prior preview session.");
        return Task.CompletedTask;
    }, _ => { }, 2, 1);
    await previous;
    Console.WriteLine("Passed: preview startup retries, client ACK, timeout and reconnect isolation.");
}

const string source = "TestHoneycomb:\n  parent: Honeycomb\n  unitMass: 25\n  unitVolume: 10\n  hitpoints: 100\n";
var bank = GameplayBank.CreateFromYaml(source);
int notifications = 0, activeNotifications = 0, overlappingNotifications = 0;
bank.RegisterOnChange(() =>
{
    if (Interlocked.Increment(ref activeNotifications) != 1) Interlocked.Increment(ref overlappingNotifications);
    Thread.Sleep(2);
    Interlocked.Increment(ref notifications);
    Interlocked.Decrement(ref activeNotifications);
});
BankNotifications.Serialize(bank);
BankNotifications.Serialize(bank);
var notify = (OnChange)typeof(GameplayBank).GetField("_onChange",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(bank)!;
Parallel.For(0, 32, _ => notify());
if (notifications != 32 || overlappingNotifications != 0)
    throw new Exception("Gameplay-bank callbacks overlapped or were lost.");
int lateActive = 0, lateOverlap = 0, lateNotifications = 0;
bank.RegisterOnChange(() =>
{
    if (Interlocked.Increment(ref lateActive) != 1) Interlocked.Increment(ref lateOverlap);
    Thread.Sleep(2); Interlocked.Increment(ref lateNotifications); Interlocked.Decrement(ref lateActive);
});
BankNotifications.Serialize(bank);
var refreshedNotify = (OnChange)typeof(GameplayBank).GetField("_onChange",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(bank)!;
Parallel.For(0, 32, _ => refreshedNotify());
if (lateNotifications != 32 || lateOverlap != 0 || notifications != 64)
    throw new Exception("A late-registered native listener escaped gameplay-bank serialization.");
Console.WriteLine("Passed: concurrent gameplay-bank notifications preserve and serialize native listeners.");
var shape = new Shape { hash = new string('a', 64), name = "CustomVoxel" + new string('a', 64),
    materialVolumes = new Dictionary<ulong, double> { [(ulong)GameplayObject.IdFor("TestHoneycomb")] = 2.0 } };
shape.itemTypeId = GameplayObject.IdFor(shape.name);
string yaml = new Definitions(bank).BuildYaml(shape);
bank.Validate(yaml);
var combined = GameplayBank.CreateFromYaml(source + yaml);
var item = (FurnituresAppliances)combined.GetDefinition(shape.itemTypeId).BaseObject;
if (item.UnitMass != 50 || item.UnitVolume != 20 || item.Hitpoints != 200 || item.AssetAlias != shape.name)
    throw new Exception("Generated definition has incorrect native statistics or asset identity.");
if (item.VisibilityLOD != 4)
    throw new Exception("Custom wrappers must remain subscribed through native element LOD 4.");
Console.WriteLine("Passed: native gameplay-bank validation, furniture inheritance, identity, mass, volume and HP.");
foreach (double cubicMetres in new[] { 1.0 / 64, 1.0, 0.5 / 64 })
{
    shape.materialVolumes[(ulong)GameplayObject.IdFor("TestHoneycomb")] = cubicMetres;
    var measured = GameplayBank.CreateFromYaml(source + new Definitions(bank).BuildYaml(shape));
    double actual = ((Element)measured.GetDefinition(shape.itemTypeId).BaseObject).Hitpoints;
    if (actual != 100 * cubicMetres) throw new Exception("Voxel HP does not preserve cubic metre units.");
}
shape.materialVolumes[(ulong)GameplayObject.IdFor("TestHoneycomb")] = 2.0;
if (Designs.DefaultName(" Test Construct ", shape.hash) != "Test Construct custom aaaaaa" ||
    Designs.DefaultName(new string('x', 100), shape.hash).Length != 80)
    throw new Exception("Default custom names lost construct name, short ID or length limit.");
Console.WriteLine("Passed: per-m3 HP for one voxel, 64 voxels and fractional slopes; short-ID default names.");
var category = combined.GetDefinition("CustomVoxelItems");
if (category.BaseObject.DisplayName != "Custom items" || category.BaseObject.Hidden ||
    combined.GetInventoryDefinitions().Any(d => d.Name == "CustomVoxelItems"))
    throw new Exception("Custom item category is hidden from the filter tree or became an inventory item.");

var recordingPub = new TestPub();
var testConfig = new NQutils.Config.Config();
testConfig.pubsub.shards = 1;
typeof(NQutils.Config.Config).GetProperty("Instance")!.SetValue(null, testConfig);
var streamed = new Shape { name = shape.name, assets = new()
{
    new CustomElements.Asset { kind = 2, path = "resources_generated/custom_elements/test.png.nqz", data = new byte[] { 1 } },
    new CustomElements.Asset { kind = 1, data = System.Text.Encoding.UTF8.GetBytes(new JObject
        { ["elements"] = new JObject { [shape.name] = new JObject { ["icon"] = "test.png" } } }.ToString()) }
} };
await new Assets(recordingPub, bank).Send(700, streamed);
if (!recordingPub.operations.Select(e => e.flags).SequenceEqual(new ulong[] { 2, 1, 3, 6 }) ||
    recordingPub.operations[2].hashContext != shape.name)
    throw new Exception("Inventory icon refresh must follow resource, definition and element loads.");
Console.WriteLine("Passed: native inventory icon refresh follows all element resource loads.");

var honeycombId = (ulong)GameplayObject.IdFor("TestHoneycomb");
shape.materialVolumes[honeycombId] = 0.15625;
var costs = Costs.ForCopies(shape, 100, bank);
if (costs[honeycombId] != ItemQuantity.ToQuantity(15.625))
    throw new Exception("Fractional honeycomb volume was not charged exactly for 100 copies.");
shape.materialVolumes[honeycombId] = double.NaN;
try { Costs.ForCopies(shape, 1, bank); throw new Exception("Non-finite cost was accepted."); }
catch (InvalidOperationException) { }
Console.WriteLine("Passed: exact fractional honeycomb costs and invalid-volume rejection.");

var boundsCache = new TestBoundsCache();
var oldBounds = boundsCache.Snapshot;
shape.visualBounds = JObject.FromObject(new { min = new[] { -0.125, -0.25, -0.625 },
    max = new[] { 0.125, 0.25, 0.625 } });
new Bounds(boundsCache).Register(shape);
if (oldBounds.Count != 0 || boundsCache.Snapshot[(ItemType)shape.itemTypeId].max.z != 0.625)
    throw new Exception("Server bounds were not published as an independent snapshot.");
Console.WriteLine("Passed: server bounds publication without mutating concurrent readers' snapshot.");
shape.visualBounds = JObject.FromObject(new { min = new[] { -24, -1, -1 }, max = new[] { 24, 1, 1 } });
new Bounds(boundsCache).Register(shape);
shape.visualBounds["max"][0] = 41;
try { new Bounds(boundsCache).Register(shape); throw new Exception("Oversized server bounds were accepted."); }
catch (InvalidOperationException) { }
Console.WriteLine("Passed: S-core visual bounds and oversized-bound rejection.");

var moduleBank = GameplayBank.CreateFromYaml(source +
    "T5Honeycomb:\n  parent: Honeycomb\n  level: 5\n  hitpoints: 200\n  unitMass: 10\n  unitVolume: 2\n" +
    "TestSwitch:\n  parent: ManualSwitchUnit\n  level: 3\n  hitpoints: 300\n  unitMass: 4\n  unitVolume: 1\n");
var switchType = (ulong)GameplayObject.IdFor("TestSwitch");
shape.materialVolumes = new() { [(ulong)GameplayObject.IdFor("T5Honeycomb")] = 0.5 };
shape.embeddedElements = new() { new EmbeddedElement { key = 1, itemTypeId = switchType,
    position = new[] { 2.0, 0, 1 }, rotation = new[] { 0.0, 0, 0, 1 } } };
var moduleDefinitions = new Definitions(moduleBank);
var sourceDefinitions = new Dictionary<ulong, string> { [switchType] =
    "TestSwitch:\n  parent: ManualSwitchUnit\n  displayName: Test switch\n  level: 3\n  hitpoints: 300\n  unitMass: 4\n  unitVolume: 1\n" };
var moduleYaml = moduleDefinitions.BuildYaml(shape, sourceDefinitions);
moduleBank.Validate(moduleYaml);
var moduleCombined = GameplayBank.CreateFromYaml(source +
    "T5Honeycomb:\n  parent: Honeycomb\n  level: 5\n  hitpoints: 200\n  unitMass: 10\n  unitVolume: 2\n" +
    "TestSwitch:\n  parent: ManualSwitchUnit\n  level: 3\n  hitpoints: 300\n  unitMass: 4\n  unitVolume: 1\n" + moduleYaml);
var moduleParent = (BaseItem)moduleCombined.GetDefinition(shape.itemTypeId).BaseObject;
var childName = Definitions.ChildName(shape, switchType, moduleCombined);
var moduleChild = moduleCombined.GetDefinition(childName).BaseObject;
if (moduleParent.Level != 5 || moduleParent.UnitMass != 9 || !(moduleChild is ManualSwitchUnit) ||
    ((Element)moduleParent).Hitpoints != 100 || ((Element)moduleChild).Hitpoints != 300 ||
    moduleChild.Hidden || !moduleChild.DisplayHidden || ((BaseItem)moduleChild).IsTradable ||
    ((BaseItem)moduleChild).UnitMass != 0 || moduleChild.AssetAlias != childName ||
    ((BaseItem)moduleChild).UnitVolume != 1 || ((BaseItem)moduleChild).Level != 3 ||
    moduleCombined.GetDefinition(switchType).GetChildren().Any() ||
    moduleCombined.GetDefinition(shape.itemTypeId).GetChildren().Any() ||
    moduleCombined.GetDefinition(childName).Parent.Name != "ManualSwitchUnit")
    throw new Exception("Native tier, inheritance, asset alias or grouped mass is incorrect.");
var moduleCosts = Costs.ForCopies(shape, 100, moduleBank);
if (moduleCosts[switchType] != 100) throw new Exception("Embedded DU elements were not charged per copy.");
Console.WriteLine("Passed: highest source tier, per-copy element charging and native child behavior without duplicate mass.");
var cleanParent = new ElementInfo { elementType = shape.itemTypeId, properties = new()
    { ["hitpoints"] = new PropertyValue { doubleValue = 500 } } };
var cleanChild = new ElementInfo { elementType = moduleCombined.GetDefinition(childName).Id,
    properties = new() { ["hitpoints"] = new PropertyValue { doubleValue = 999 },
        ["elementsColliding"] = new PropertyValue { stringValue = "" } } };
var cleanGroup = new ModuleGroup { children = new()
    { new ModuleChild { key = 1, element = cleanChild, active = true } } };
if (!ModuleInventory.IsPristine(moduleCombined, shape, cleanGroup, cleanParent))
    throw new Exception("Automatic placement/talent properties prevented stacking.");
foreach (var change in new Dictionary<string, PropertyValue> {
    ["name"] = new PropertyValue { stringValue = "Changed component name" },
    ["gameplayTag"] = new PropertyValue { stringValue = "Changed script data" },
    ["hitpointsRatio"] = new PropertyValue { doubleValue = 0.5 },
    ["restoreCount"] = new PropertyValue { intValue = 1 } })
{
    cleanChild.properties[change.Key] = change.Value;
    if (ModuleInventory.IsPristine(moduleCombined, shape, cleanGroup, cleanParent))
        throw new Exception("Meaningful component state would be lost when stacking: " + change.Key);
    cleanChild.properties.Remove(change.Key);
}
cleanParent.properties["hitpointsRatio"] = new PropertyValue { doubleValue = 0.5 };
if (ModuleInventory.IsPristine(moduleCombined, shape, cleanGroup, cleanParent))
    throw new Exception("A damaged custom wrapper would be stacked as new.");
cleanParent.properties.Remove("hitpointsRatio");
cleanGroup.links.Add(new LinkInfo { fromElementId = 1, toElementId = 0, plugType = PlugType.PLUG_SIGNAL });
if (ModuleInventory.IsPristine(moduleCombined, shape, cleanGroup, cleanParent))
    throw new Exception("Changed internal wiring would be discarded when stacking.");
Console.WriteLine("Passed: pristine module stacking preserves damage, repair count, settings, scripts and changed links.");
sourceDefinitions[switchType] += "  lightProperties:\n    omni:\n      intensity: 0.5\n";
moduleBank.Validate(moduleDefinitions.BuildYaml(shape, sourceDefinitions, forValidation: true));
if (!moduleDefinitions.BuildYaml(shape, sourceDefinitions).Contains("lightProperties:"))
    throw new Exception("Native client-only metadata was lost from the sibling definition.");
Console.WriteLine("Passed: native client-only metadata is retained while strict server fields are validated.");

string resourcesPath = Path.Combine(Path.GetTempPath(), "du-child-resources-" + Guid.NewGuid() + ".json");
try
{
    File.WriteAllText(resourcesPath, "{\"TestSwitch\":{\"icon\":\"native.png\",\"node\":\"native.node\"}}");
    shape.assets = new() { new CustomElements.Asset { kind = 1, data = System.Text.Encoding.UTF8.GetBytes(
        new JObject { ["elements"] = new JObject { [shape.name] =
            new JObject { ["icon"] = "custom.png", ["node"] = "custom.node" } } }.ToString()) } };
    var childResources = new ChildResources(moduleCombined, resourcesPath).For(new[] { shape });
    var entries = (JObject)childResources["elements"]!;
    if (entries["TestSwitch"] != null || (string)entries[childName]?["icon"] != "native.png" ||
        (string)entries[childName]?["node"] != "native.node" ||
        (string)entries[shape.name]?["icon"] != "custom.png")
        throw new Exception("Child resources reused a vanilla alias or lost native assets.");
    if (moduleCombined.GetDefinition(switchType).BaseObject.AssetAlias == childName)
        throw new Exception("Vanilla source definition was changed.");
    Console.WriteLine("Passed: native child resources use isolated aliases without redefining vanilla assets.");
}
finally { File.Delete(resourcesPath); }

var origin = new Vec3 { x = 10, y = 20, z = 30 };
var quarterTurn = new Quat { z = (float)Math.Sqrt(0.5), w = (float)Math.Sqrt(0.5) };
var local = shape.embeddedElements[0];
var placed = ModuleTransforms.Child(new ElementInfo { constructId = 42, position = origin, rotation = quarterTurn }, local);
var reconstructed = ModuleTransforms.ParentLocation(placed.Location, local);
if (Math.Abs(placed.position.x - 10) > 0.00001 || Math.Abs(placed.position.y - 22) > 0.00001 ||
    Math.Abs(reconstructed.position.z - 30) > 0.00001 || Math.Abs(reconstructed.position.y - 20) > 0.00001)
    throw new Exception("Rotated module deployment or moving through a child changed the relative transform.");
Console.WriteLine("Passed: rotated native-child deployment and inverse movement through a selected child.");

var bpSource = new ElementInfo { elementId = 11, elementType = 123, position = origin, rotation = quarterTurn };
var bpCopy = new ElementInfo { elementId = 21, elementType = 123, position = origin,
    rotation = new Quat { z = -quarterTurn.z, w = -quarterTurn.w } };
var duplicateSource = bpSource.DeepCopy(); duplicateSource.elementId = 12;
var duplicateCopy = bpCopy.DeepCopy(); duplicateCopy.elementId = 22;
var mapped = BlueprintModules.Match(new[] { bpSource, duplicateSource }, new[] { bpCopy, duplicateCopy });
if (mapped[11] != 21 || mapped[12] != 22)
    throw new Exception("Blueprint matching lost quaternion equivalence or reused an overlapping item.");
mapped[13] = 23; mapped[14] = 24;
var bpLinks = new[] {
    new LinkInfo { fromElementId = 11, toElementId = 13, plugType = PlugType.PLUG_SIGNAL },
    new LinkInfo { fromElementId = 13, toElementId = 14, plugType = PlugType.PLUG_SIGNAL },
    new LinkInfo { fromElementId = 11, toElementId = 12, plugType = PlugType.PLUG_SIGNAL },
    new LinkInfo { fromElementId = 13, toElementId = 999, plugType = PlugType.PLUG_SIGNAL } };
var restoredLinks = BlueprintModules.RemapLinks(bpLinks, mapped, new HashSet<ulong> { 13, 14 }, 42);
if (restoredLinks.Count != 2 || restoredLinks[0].fromElementId != 21 || restoredLinks[0].toElementId != 23 ||
    restoredLinks[1].toElementId != 24 || restoredLinks.Any(link => link.constructId != 42))
    throw new Exception("Blueprint lost internal/external module links or repeated native-only links.");
Console.WriteLine("Passed: blueprint instance matching and internal/external module-link remapping.");

string folder = Path.Combine(AppContext.BaseDirectory, "jobs-" + Guid.NewGuid().ToString("N"));
try
{
    var jobs = new Jobs(folder);
    string designsPath = Path.Combine(folder, "designs.json");
    var designs = new Designs(designsPath);
    designs.Ensure(shape.itemTypeId, 700, 42, "Original design");
    designs.Ensure(shape.itemTypeId, 20000, 43, "Must not take ownership");
    try { designs.Rename(shape.itemTypeId, 20000, "Unauthorized"); throw new Exception("Non-creator rename accepted."); }
    catch (InvalidOperationException) { }
    designs.Rename(shape.itemTypeId, 700, "Wing: \"Left\" · v2");
    var reloadedDesigns = new Designs(designsPath);
    if (reloadedDesigns.Name(shape.itemTypeId, "missing") != "Wing: \"Left\" · v2")
        throw new Exception("Shared rename did not persist.");
    moduleBank.Validate(new Definitions(moduleBank, reloadedDesigns).BuildYaml(shape, sourceDefinitions,
        forValidation: true));
    try { designs.Rename(shape.itemTypeId, 700, "Bad\nname"); throw new Exception("Control-character name accepted."); }
    catch (InvalidOperationException) { }
    File.Delete(designsPath);
    Console.WriteLine("Passed: creator-only shared rename, stable item identity, persistence and safe YAML names.");
    var groupStore = new ModuleGroups(Path.Combine(folder, "groups"));
    var group = new ModuleGroup { parent = 123, construct = 42,
        children = new() { new ModuleChild { key = 1, active = true, element = new ElementInfo
            { elementId = 456, elementType = switchType, properties = new()
                { ["name"] = new PropertyValue("Persisted switch") } } } } };
    groupStore.Save(group);
    group.children.Clear();
    var restoredGroup = new ModuleGroups(Path.Combine(folder, "groups")).Get(123);
    if (restoredGroup.children[0].element.properties["name"].stringValue != "Persisted switch" ||
        groupStore.Get(123).children.Count != 1) throw new Exception("Module state was lost or a saved snapshot was mutated.");
    File.Delete(Path.Combine(folder, "groups", "123.json"));
    Directory.Delete(Path.Combine(folder, "groups"));
    Console.WriteLine("Passed: native child properties and ownership survive reload without snapshot mutation.");
    var job = new CreationJob { requestId = Guid.NewGuid().ToString("N"), playerId = 700,
        itemTypeId = shape.itemTypeId, quantity = 3 };
    jobs.Save(job);
    var reopened = new Jobs(folder);
    if (reopened.Get(job.requestId)?.quantity != 3 || reopened.PendingFor(700).Count() != 1 ||
        reopened.PendingFor(701).Any()) throw new Exception("Creation request did not survive reopening.");
    foreach (string status in new[] { "granting", "completed" })
    {
        job.status = status; reopened.Save(job);
        if (reopened.PendingFor(700).Any() || reopened.Get(job.requestId)?.status != status)
            throw new Exception("A granted or ambiguous request was scheduled twice.");
    }
    try { reopened.Get("../invalid"); throw new Exception("Invalid request ID was accepted."); }
    catch (ArgumentException) { }
    Console.WriteLine("Passed: persistent requests, player isolation and duplicate-grant prevention.");
}
finally
{
    foreach (string path in Directory.GetFiles(folder)) File.Delete(path);
    Directory.Delete(folder);
}

sealed class TestBoundsCache
{
    private readonly Dictionary<ItemType, BoundingBox> _elementBoundingBoxes = new();
    public Dictionary<ItemType, BoundingBox> Snapshot => _elementBoundingBoxes;
}

sealed class TestPub : IPub
{
    public List<VoxelEdit> operations = new();
    public Task NotifyTopicBinary(PubSubTopic topic, NQutils.Serialization.AbstractPacket packet,
        IPub.MessagePriority priority = IPub.MessagePriority.Low)
    {
        operations.Add(NQutils.Messages.VoxelCsgApplied.TryParse(packet)!);
        return Task.CompletedTask;
    }
    public Task BindPlayerToTopic(PlayerId player, PubSubTopic topic) => Task.CompletedTask;
    public Task UnbindPlayerFromTopic(PlayerId player, PubSubTopic topic) => Task.CompletedTask;
}

