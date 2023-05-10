﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;

namespace GarageGroup.Infra.Bot.Builder;

partial class AwaitDateChatFlowExtensions
{
    public static ChatFlow<T> AwaitDate<T>(
        this ChatFlow<T> chatFlow,
        Func<IChatFlowContext<T>, DateStepOption> optionFactory,
        Func<IChatFlowContext<T>, DateOnly, string> resultMessageFactory,
        Func<T, DateOnly, T> mapFlowState)
        =>
        InnerAwaitDate(
            chatFlow ?? throw new ArgumentNullException(nameof(chatFlow)),
            optionFactory ?? throw new ArgumentNullException(nameof(chatFlow)),
            resultMessageFactory ?? throw new ArgumentNullException(nameof(resultMessageFactory)),
            mapFlowState ?? throw new ArgumentNullException(nameof(mapFlowState)));

    public static ChatFlow<T> AwaitDate<T>(
        this ChatFlow<T> chatFlow,
        Func<IChatFlowContext<T>, DateStepOption> optionFactory,
        Func<T, DateOnly, T> mapFlowState)
        =>
        InnerAwaitDate(
            chatFlow ?? throw new ArgumentNullException(nameof(chatFlow)),
            optionFactory ?? throw new ArgumentNullException(nameof(chatFlow)),
            CreateDefaultResultMessage,
            mapFlowState ?? throw new ArgumentNullException(nameof(mapFlowState)));

    public static ChatFlow<T> InnerAwaitDate<T>(
        ChatFlow<T> chatFlow,
        Func<IChatFlowContext<T>, DateStepOption> optionFactory,
        Func<IChatFlowContext<T>, DateOnly, string> messageFactory,
        Func<T, DateOnly, T> mapState)
    {
        return chatFlow.ForwardValue(InnerAwaitDateAsync);

        ValueTask<ChatFlowJump<T>> InnerAwaitDateAsync(IChatFlowContext<T> context, CancellationToken token)
            =>
            context.IsCardSupported()
            ? context.InnerAwaitAsync(optionFactory, CreateDateAdaptiveCardActivity, ParseDateFormAdaptiveCard, messageFactory, mapState, token)
            : context.InnerAwaitAsync(optionFactory, CreateMessageActivity, ParseDateFromText, messageFactory, mapState, token);
    }

    private static async ValueTask<ChatFlowJump<T>> InnerAwaitAsync<T>(
        this IChatFlowContext<T> context,
        Func<IChatFlowContext<T>, DateStepOption> optionFactory,
        Func<ITurnContext, DateStepOption, IActivity> activityFactory,
        Func<ITurnContext, DateCacheJson, Result<DateOnly, BotFlowFailure>> dateParser,
        Func<IChatFlowContext<T>, DateOnly, string> resultMessageFactory,
        Func<T, DateOnly, T> mapFlowState,
        CancellationToken cancellationToken)
    {
        var option = optionFactory.Invoke(context);
        if (option.SkipStep)
        {
            return context.FlowState;
        }

        if (context.StepState is DateCacheJson cacheJson)
        {
            return await dateParser.Invoke(context, cacheJson).FoldValueAsync(SuccessAsync, RepeatAsync).ConfigureAwait(false);
        }

        var dateActivity = activityFactory.Invoke(context, option);
        var resource = await context.SendActivityAsync(dateActivity, cancellationToken).ConfigureAwait(false);

        var cacheValue = BuildCacheValue(option, resource);
        return ChatFlowJump.Repeat<T>(cacheValue);

        async ValueTask<ChatFlowJump<T>> SuccessAsync(DateOnly date)
        {
            if (context.Activity.Value is not null)
            {
                var resultMessage = resultMessageFactory.Invoke(context, date);
                var resultActivity = MessageFactory.Text(resultMessage);

                var cacheResourceId = cacheJson.Resource?.Id;
                await context.SendInsteadActivityAsync(cacheResourceId, resultActivity, cancellationToken).ConfigureAwait(false);
            }
            else if (cacheJson.Resource is not null && context.IsMsteamsChannel())
            {
                var activity = MessageFactory.Text(option.Text);
                activity.Id = cacheJson.Resource.Id;

                await context.UpdateActivityAsync(activity, cancellationToken).ConfigureAwait(false);
            }

            return mapFlowState.Invoke(context.FlowState, date);
        }

        async ValueTask<ChatFlowJump<T>> RepeatAsync(BotFlowFailure flowFailure)
        {
            if (string.IsNullOrEmpty(flowFailure.UserMessage) is false)
            {
                var invalidDateActivity = MessageFactory.Text(flowFailure.UserMessage);
                await context.SendActivityAsync(invalidDateActivity, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(flowFailure.LogMessage) is false)
            {
                context.Logger.LogError("{logMessage}", flowFailure.LogMessage);
                context.BotTelemetryClient.TrackEvent($"{context.ChatFlowId}StepAwaitDateFailure", new Dictionary<string, string>
                {
                    ["flowId"] = context.ChatFlowId,
                    ["message"] = flowFailure.LogMessage
                });
            }

            return context.RepeatSameStateJump<T>();
        }
    }

    private static Task SendInsteadActivityAsync(this ITurnContext context, string? activityId, IActivity activity, CancellationToken token)
    {
        return string.IsNullOrEmpty(activityId) || context.IsNotMsteamsChannel()
            ? SendActivityAsync()
            : Task.WhenAll(DeleteActivityAsync(), SendActivityAsync());

        Task SendActivityAsync()
            =>
            context.SendActivityAsync(activity, token);

        Task DeleteActivityAsync()
            =>
            context.DeleteActivityAsync(activityId, token);
    }

    private static string CreateDefaultResultMessage<T>(IChatFlowContext<T> context, DateOnly date)
    {
        var text = context.EncodeTextWithStyle(date.ToString("dd.MM.yyyy"), BotTextStyle.Bold);
        return $"Выбрано значение: {text}";
    }
}