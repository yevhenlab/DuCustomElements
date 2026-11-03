using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Backend;
using Newtonsoft.Json;

namespace CustomElements;

public sealed class Design
{
    public ulong itemTypeId { get; set; }
    public ulong creator { get; set; }
    public ulong sourceConstruct { get; set; }
    public string name { get; set; }
}

public sealed class Designs
{
    private readonly string path;
    private readonly object gate = new();
    private readonly Dictionary<ulong, Design> records;

    public Designs(string path)
    {
        this.path = path;
        records = File.Exists(path) ? JsonConvert.DeserializeObject<Dictionary<ulong, Design>>(
            File.ReadAllText(path)) : new();
    }

    public void Seed(string jobsFolder, IGameplayBank bank)
    {
        foreach (var file in new DirectoryInfo(jobsFolder).GetFiles("*.json").OrderBy(f => f.CreationTimeUtc))
        {
            var job = JsonConvert.DeserializeObject<CreationJob>(File.ReadAllText(file.FullName));
            var definition = bank.GetDefinition(job.itemTypeId);
            if (definition == null || !definition.Name.StartsWith("CustomVoxel", StringComparison.Ordinal)) continue;
            Ensure(job.itemTypeId, job.playerId, job.constructId, definition.BaseObject.DisplayName);
        }
    }

    public void Ensure(ulong type, ulong creator, ulong source, string name)
    {
        lock (gate)
        {
            if (records.ContainsKey(type)) return;
            records[type] = new Design { itemTypeId = type, creator = creator, sourceConstruct = source,
                name = Validate(name) };
            Save();
        }
    }

    public string Name(ulong type, string fallback)
    {
        lock (gate) return records.TryGetValue(type, out var record) ? record.name : fallback;
    }

    public static string DefaultName(string constructName, string hash)
    {
        string suffix = " custom " + hash.Substring(0, 6);
        string stem = string.IsNullOrWhiteSpace(constructName) ? "Construct" : constructName.Trim();
        if (stem.Length > 80 - suffix.Length) stem = stem.Substring(0, 80 - suffix.Length).TrimEnd();
        return stem + suffix;
    }

    public bool IsCreator(ulong type, ulong player)
    {
        lock (gate) return records.TryGetValue(type, out var record) && record.creator == player;
    }

    public void CheckCreator(ulong type, ulong player)
    {
        lock (gate)
            if (!records.TryGetValue(type, out var record) || record.creator != player)
                throw new InvalidOperationException("Only the recorded creator can rename this shared custom design.");
    }

    public void Rename(ulong type, ulong player, string name)
    {
        name = Validate(name);
        lock (gate)
        {
            CheckCreator(type, player);
            records[type].name = name;
            Save();
        }
    }

    private static string Validate(string name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 80 || name.Any(char.IsControl))
            throw new InvalidOperationException("Enter a name of 1–80 characters without line breaks or control characters.");
        return name;
    }

    private void Save()
    {
        File.WriteAllText(path + ".pending", JsonConvert.SerializeObject(records));
        File.Move(path + ".pending", path, overwrite: true);
    }
}
