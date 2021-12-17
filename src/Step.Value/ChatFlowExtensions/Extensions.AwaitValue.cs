﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace GGroupp.Infra.Bot.Builder;

partial class ValueStepChatFlowExtensions
{
    public static ChatFlow<TNext> AwaitValue<T, TValue, TNext>(
        this ChatFlow<T> chatFlow,
        Func<string, Result<TValue, ChatFlowStepFailure>> valueParser,
        Func<T, TValue, TNext> mapFlowState)
        =>
        InnerAwaitValue(
            chatFlow ?? throw new ArgumentNullException(nameof(chatFlow)),
            valueParser ?? throw new ArgumentNullException(nameof(valueParser)),
            mapFlowState ?? throw new ArgumentNullException(nameof(mapFlowState)));

    private static ChatFlow<TNext> InnerAwaitValue<T, TValue, TNext>(
        ChatFlow<T> chatFlow,
        Func<string, Result<TValue, ChatFlowStepFailure>> valueParser,
        Func<T, TValue, TNext> mapFlowState)
        =>
        chatFlow.Await().ForwardValue(
            Unit.From,
            (context, token) => context.GetRequiredValueOrRepeatAsync(valueParser, token),
            mapFlowState);

    private static async ValueTask<ChatFlowJump<TValue>> GetRequiredValueOrRepeatAsync<T, TValue>(
        this IChatFlowContext<T> context,
        Func<string, Result<TValue, ChatFlowStepFailure>> valueParser,
        CancellationToken cancellationToken)
    {
        var valueResult = await context.Activity.GetRequiredTextOrFailure().Forward(valueParser).MapFailureValueAsync(ToRepeatJumpAsync).ConfigureAwait(false);
        return valueResult.Fold(ChatFlowJump.Next, Pipeline.Pipe);

        ValueTask<ChatFlowJump<TValue>> ToRepeatJumpAsync(ChatFlowStepFailure failure) => context.ToRepeatJumpAsync<TValue>(failure, cancellationToken);
    }
}