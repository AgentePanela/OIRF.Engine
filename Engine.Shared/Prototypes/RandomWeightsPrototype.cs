using System;
using System.Collections.Generic;
using System.Linq;

namespace Engine.Shared.Prototypes;

/// <summary>
/// A prototype defining a weighted random selection table.
/// </summary>
[Prototype("randomWeights")]
public sealed class RandomWeightsPrototype : IPrototype
{
    [DataField("type", required: true)]
    public string Type { get; private set; } = "randomWeights";

    [DataField("id", required: true)]
    public string ID { get; private set; } = default!;

    [DataField("weights", required: true)]
    public Dictionary<string, float> Weights
    {
        get => _weights;
        // Invalidate the cached total below whenever this gets reassigned (initial load or hot reload).
        private set { _weights = value; _totalWeight = -1f; }
    }
    private Dictionary<string, float> _weights = new();

    private float _totalWeight = -1f;

    private float TotalWeight
    {
        get
        {
            if (_totalWeight < 0f)
                _totalWeight = Weights.Values.Sum();
            return _totalWeight;
        }
    }

    /// <summary>
    /// Pick a random item from the weights table using weighted random selection.
    /// </summary>
    public string Pick(Random? random = null)
    {
        if (Weights.Count == 0)
            throw new InvalidOperationException($"Cannot pick from empty weights table in prototype '{ID}'.");

        random ??= Random.Shared;

        var roll = (float)(random.NextDouble() * TotalWeight);
        var acc = 0f;

        foreach (var (key, weight) in Weights)
        {
            acc += weight;
            if (roll <= acc)
                return key;
        }

        // Floating point edge case.
        return Weights.Keys.Last();
    }

    /// <summary>
    /// Pick a random item, returning both key and weight.
    /// </summary>
    public (string Key, float Weight) PickWithWeight(Random? random = null)
    {
        var key = Pick(random);
        return (key, Weights[key]);
    }
}
