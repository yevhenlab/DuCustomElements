using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace CustomElements;

public sealed class Jobs
{
    private readonly string folder;

    public Jobs(string folder)
    {
        this.folder = folder;
        Directory.CreateDirectory(folder);
    }

    public CreationJob Get(string id)
    {
        string path = PathFor(id);
        return File.Exists(path) ? JsonConvert.DeserializeObject<CreationJob>(File.ReadAllText(path)) : null;
    }

    public void Save(CreationJob job)
    {
        string path = PathFor(job.requestId);
        string pending = path + ".pending";
        File.WriteAllText(pending, JsonConvert.SerializeObject(job));
        File.Move(pending, path, overwrite: true);
    }

    public IEnumerable<CreationJob> PendingFor(ulong player) => Directory.GetFiles(folder, "*.json")
        .Select(path => JsonConvert.DeserializeObject<CreationJob>(File.ReadAllText(path)))
        .Where(job => job.playerId == player && job.status == "pending");

    private string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid creation request ID.");
        return Path.Combine(folder, id + ".json");
    }
}

public static class Runtime
{
    public static Func<ulong, System.Threading.Tasks.Task> AssetsReady { get; set; }
    public static Action<Shape> RegisterBounds { get; set; }
}
