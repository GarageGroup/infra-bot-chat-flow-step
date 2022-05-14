﻿using System;
using System.Linq;

namespace GGroupp.Infra.Bot.Builder;

partial class LookupActivity
{
    internal static Optional<LookupCacheResult> GetFromLookupCacheOrAbsent(this IStepStateSupplier flowContext, Guid id)
    {
        return GetLookupCacheOrAbsent().FlatMap(GetLookupValueOrAbsent);

        Optional<LookupCacheJson> GetLookupCacheOrAbsent()
            =>
            flowContext.StepState is LookupCacheJson cache ? Optional.Present(cache) : default;

        Optional<LookupCacheResult> GetLookupValueOrAbsent(LookupCacheJson cache)
            =>
            cache.Values?.GetValueOrAbsent(id).Map(value => CreateItem(cache, value)) ?? default;

        LookupCacheResult CreateItem(LookupCacheJson cache, LookupCacheValueJson cacheValueJson)
            =>
            new(
                resultText: cache.ResultText,
                resources: cache.Resources,
                value: new(id, cacheValueJson.Name, cacheValueJson.Data));
    }
}