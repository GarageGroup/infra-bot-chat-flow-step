using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Bot.Schema;

namespace GarageGroup.Infra.Bot.Builder;

internal sealed record class LookupCacheResult
{
    public LookupCacheResult([AllowNull] IReadOnlyCollection<ResourceResponse> resources, LookupValue value)
    {
        Resources = resources ?? [];
        Value = value;
    }

    public IReadOnlyCollection<ResourceResponse> Resources { get; }

    public LookupValue Value { get; }
}