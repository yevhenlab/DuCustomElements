using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NQ;

namespace CustomElements;

public sealed class Settings
{
    public string compilerUrl { get; set; } = "http://custom-elements:8097";
    public int maximumBatch { get; set; } = 1000;
    public bool enabled { get; set; } = false;
}

public sealed class Asset
{
    public string path { get; set; }
    public int kind { get; set; }
    public byte[] data { get; set; }
}

public sealed class Shape
{
    public string hash { get; set; }
    public string name { get; set; }
    public ulong itemTypeId { get; set; }
    public Dictionary<ulong, double> materialVolumes { get; set; }
    public List<Asset> assets { get; set; }
    public JObject visualBounds { get; set; }
    public JObject collisionBounds { get; set; }
    public double[] sourceMeshCenter { get; set; }
    public List<EmbeddedElement> embeddedElements { get; set; } = new();
    public List<LinkInfo> embeddedLinks { get; set; } = new();
}

public sealed class EmbeddedElement
{
    public ulong key { get; set; }
    public ulong itemTypeId { get; set; }
    public string assetAlias { get; set; }
    public double[] position { get; set; }
    public double[] rotation { get; set; }
    public Dictionary<string, PropertyValue> properties { get; set; } = new();
}

public sealed class ModuleSource
{
    public List<EmbeddedElement> elements { get; set; } = new();
    public List<LinkInfo> links { get; set; } = new();
}

public sealed class GenerationRequest
{
    public string nonce { get; set; }
    public string requestId { get; set; }
    public int quantity { get; set; }
}

public sealed class CreationDialog
{
    public ulong constructId { get; set; }
    public ulong elementId { get; set; }
    public string nonce { get; set; } = System.Guid.NewGuid().ToString("N");
    public string requestId { get; set; } = System.Guid.NewGuid().ToString("N");
    public bool showResults { get; set; } = true;
}

public sealed class CreationJob
{
    public string requestId { get; set; }
    public ulong playerId { get; set; }
    public ulong constructId { get; set; }
    public ulong itemTypeId { get; set; }
    public int quantity { get; set; }
    public string status { get; set; } = "pending";
    public bool showResults { get; set; } = true;
    public Dictionary<ulong, long> costs { get; set; } = new();
    public string lastError { get; set; }
}
