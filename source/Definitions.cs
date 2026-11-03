using System;
using System.Globalization;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using Backend;
using NQutils.Def;
using YamlDotNet.RepresentationModel;

namespace CustomElements;

public sealed class Definitions
{
    private readonly IGameplayBank bank;
    private readonly Designs designs;
    private static readonly SemaphoreSlim registration = new(1, 1);

    public Definitions(IGameplayBank bank, Designs designs = null)
    {
        this.bank = bank; this.designs = designs;
    }

    public string DisplayName(Shape shape) => designs?.Name(shape.itemTypeId,
        "Custom voxel " + shape.hash.Substring(0, 12)) ?? "Custom voxel " + shape.hash.Substring(0, 12);

    public async Task Register(Shape shape)
    {
        await registration.WaitAsync();
        try { await RegisterLocked(shape); }
        finally { registration.Release(); }
    }

    public async Task Refresh(Shape shape)
    {
        await registration.WaitAsync();
        try
        {
            await Import(shape);
        }
        finally { registration.Release(); }
    }

    private async Task RegisterLocked(Shape shape)
    {
        if ((ulong)GameplayObject.IdFor(shape.name) != shape.itemTypeId)
            throw new InvalidOperationException("Shape item ID does not match the game hash.");
        var existing = bank.GetDefinition(shape.itemTypeId);
        if (existing != null)
        {
            if (existing.Name != shape.name) throw new InvalidOperationException("Item ID collision.");
            var stats = Statistics(shape);
            var item = (Element)existing.BaseObject;
            if (existing.Parent?.Name == "CustomVoxelItems" && item.Level == stats.tier &&
                item.Hitpoints == stats.hp && item.UnitMass == stats.mass &&
                item.UnitVolume == stats.volume && ChildrenCurrent(shape) &&
                existing.BaseObject.DisplayName == DisplayName(shape)) return;
        }
        await Import(shape);
    }

    private async Task Import(Shape shape)
    {
        BankNotifications.Serialize(bank);
        var originals = await OriginalYaml(shape);
        // Vanilla YAML contains client-only fields unknown to the server's strict validator.
        // Validate the server fields, but retain the complete native metadata in the stored definition.
        bank.Validate(BuildYaml(shape, originals, forValidation: true));
        await bank.Import(BuildYaml(shape, originals), deleteMissing: false);
    }

    private Task<Dictionary<ulong, string>> OriginalYaml(Shape shape) =>
        bank.GetYaml(shape.embeddedElements.Select(e => e.itemTypeId).Distinct().ToArray());

    private (double mass, double hp, double volume, int tier) Statistics(Shape shape)
    {
        double mass = 0, hp = 0, volume = 0;
        int tier = 1;
        foreach (var material in shape.materialVolumes)
        {
            var honeycomb = bank.GetDefinition(material.Key)?.BaseObject as Honeycomb;
            if (honeycomb == null || material.Value <= 0 || !double.IsFinite(material.Value))
                throw new InvalidOperationException("Unknown honeycomb material in shape.");
            mass += honeycomb.UnitMass * material.Value;
            hp += honeycomb.Hitpoints * material.Value;
            volume += honeycomb.UnitVolume * material.Value;
            tier = Math.Max(tier, honeycomb.Level);
        }
        foreach (var element in shape.embeddedElements)
        {
            var child = bank.GetDefinition(element.itemTypeId)?.BaseObject as Element;
            if (child == null) throw new InvalidOperationException("Unknown embedded element definition.");
            mass += child.UnitMass;
            volume += child.UnitVolume;
            tier = Math.Max(tier, child.Level);
        }
        return (mass, hp > 0 ? hp : 1, volume, tier);
    }

    public string BuildYaml(Shape shape, IReadOnlyDictionary<ulong, string> originals = null,
        bool forValidation = false)
    {
        var stats = Statistics(shape);
        var yaml = new StringBuilder();
        yaml.AppendLine("CustomVoxelItems:");
        yaml.AppendLine("  parent: FurnituresAppliances");
        yaml.AppendLine("  displayName: Custom items");
        yaml.AppendLine("  hidden: false");
        yaml.AppendLine("  displayHidden: false");
        foreach (var type in shape.embeddedElements.Select(e => e.itemTypeId).Distinct())
        {
            yaml.AppendLine(BuildChildYaml(shape, type, originals, forValidation));
        }
        yaml.AppendLine(shape.name + ":");
        yaml.AppendLine("  parent: CustomVoxelItems");
        yaml.AppendLine("  displayName: " + Newtonsoft.Json.JsonConvert.SerializeObject(DisplayName(shape)));
        yaml.AppendLine("  assetAlias: " + shape.name);
        yaml.AppendLine("  hidden: false");
        yaml.AppendLine("  displayHidden: false");
        yaml.AppendLine("  isTradable: true");
        yaml.AppendLine("  level: " + stats.tier);
        yaml.AppendLine("  disableWhenColliding: false");
        yaml.AppendLine("  unitMass: " + Number(stats.mass));
        // Honeycomb inventory volume is already defined per cubic metre of world material.
        yaml.AppendLine("  unitVolume: " + Number(stats.volume));
        yaml.AppendLine("  hitpoints: " + Number(stats.hp));
        return yaml.ToString();
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private string BuildChildYaml(Shape shape, ulong type, IReadOnlyDictionary<ulong, string> originals,
        bool forValidation)
    {
        var original = bank.GetDefinition(type);
        string name = ChildName(shape, type, bank);
        var body = new YamlMappingNode();
        if (originals != null && originals.TryGetValue(type, out var source) && !string.IsNullOrWhiteSpace(source))
        {
            var document = new YamlStream();
            document.Load(new StringReader(source));
            body = (YamlMappingNode)((YamlMappingNode)document.Documents[0].RootNode).Children
                .Single(entry => ((YamlScalarNode)entry.Key).Value == original.Name).Value;
        }
        else if (original.Name != original.BaseObject.GetType().Name)
            throw new InvalidOperationException("Missing source definition YAML for " + original.Name);
        if (forValidation)
            foreach (var key in body.Children.Keys.Where(key =>
                !original.HasStaticProperty(((YamlScalarNode)key).Value)).ToList()) body.Children.Remove(key);
        body.Children[new YamlScalarNode("parent")] = new YamlScalarNode(ChildParent(original));
        body.Children[new YamlScalarNode("assetAlias")] = new YamlScalarNode(name);
        body.Children[new YamlScalarNode("unitMass")] = new YamlScalarNode("0");
        // Native blueprints reject hidden definitions, even though these are legitimate components.
        body.Children[new YamlScalarNode("hidden")] = new YamlScalarNode("false");
        body.Children[new YamlScalarNode("displayHidden")] = new YamlScalarNode("true");
        body.Children[new YamlScalarNode("isTradable")] = new YamlScalarNode("false");
        var output = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(new YamlMappingNode(new YamlScalarNode(name), body)))
            .Save(output, assignAnchors: false);
        return output.ToString().Replace("\n...", "").TrimEnd();
    }

    private static string ChildParent(IGameplayDefinition original) =>
        original.Name == original.BaseObject.GetType().Name ? original.Name : original.Parent.Name;

    private bool ChildrenCurrent(Shape shape) => shape.embeddedElements.All(element =>
    {
        var original = bank.GetDefinition(element.itemTypeId);
        var child = bank.GetDefinition(ChildName(shape, element.itemTypeId, bank));
        string alias = ChildName(shape, element.itemTypeId, bank);
        return child?.Parent?.Name == ChildParent(original) && child.BaseObject.AssetAlias == alias &&
            child.BaseObject is BaseItem item && item.UnitMass == 0 && !item.Hidden && item.DisplayHidden &&
            !item.IsTradable;
    });

    public static string ChildName(Shape shape, ulong original, IGameplayBank bank)
    {
        string stem = "CustomPart" + shape.hash + "_" + original;
        for (int salt = 0; salt < 1024; salt++)
        {
            string name = stem + (salt == 0 ? "" : "_" + salt);
            var occupied = bank.GetDefinition(GameplayObject.IdFor(name));
            if (occupied == null || occupied.Name == name) return name;
        }
        throw new InvalidOperationException("Unable to allocate an embedded element type ID.");
    }
}
