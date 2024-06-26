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
        Func<IChatFlowContext<T>, DateOnly, Result<DateOnly, BotFlowFailure>> validator,
        Func<T, DateOnly, T> mapFlowState)
    {
        ArgumentNullException.ThrowIfNull(chatFlow);
        ArgumentNullException.ThrowIfNull(optionFactory);
        ArgumentNullException.ThrowIfNull(resultMessageFactory);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(mapFlowState);

        return InnerAwaitDate(chatFlow, optionFactory, resultMessageFactory, validator, mapFlowState);
    }

    public static ChatFlow<T> AwaitDate<T>(
        this ChatFlow<T> chatFlow,
        Func<IChatFlowContext<T>, DateStepOption> optionFactory,
        Func<IChatFlowContext<T>, DateOnly, string> resultMessageFactory,
        Func<T, DateOnly, T> mapFlowState)
    {
        ArgumentNullException.ThrowIfNull(chatFlow);
        ArgumentNullException.ThrowIfNull(optionFactory);
        ArgumentNullException.ThrowIfNull(resultMessageFactory);
        ArgumentNullException.ThrowIfNull(mapFlowState);

        return InnerAwaitDate(chatFlow, optionFactory, resultMessageFactory, null, mapFlowState);
    }

    public static ChatFlow<T> AwaitDate<T>(
        this ChatFlow<T> chatFlow,
        Func<IChatFlowContext<T>, DateStepOption> optionFactory,
        Func<T, DateOnly, T> mapFlowState)
    {
        ArgumentNullException.ThrowIfNull(chatFlow);
        ArgumentNullException.ThrowIfNull(optionFactory);
        ArgumentNullException.ThrowIfNull(mapFlowState);

        return InnerAwaitDate(chatFlow, optionFactory, CreateDefaultResultMessage, null, mapFlowState);

        static string CreateDefaultResultMessage(IChatFlowContext<T> context, DateOnly date)
        {
            var text = context.EncodeTextWithStyle(date.ToString("dd.MM.yyyy"), BotTextStyle.Bold);
            return $"Value selected: {text}";
        }
    }

    private static ChatFlow<T> InnerAwaitDate<T>(
        ChatFlow<T> chatFlow,
        Func<IChatFlowContext<T>, DateStepOption> optionFactory,
        Func<IChatFlowContext<T>, DateOnly, string> messageFactory,
        Func<IChatFlowContext<T>, DateOnly, Result<DateOnly, BotFlowFailure>>? validator,
        Func<T, DateOnly, T> mapFlowState)
    {
        return chatFlow.ForwardValue(InnerAwaitDateAsync);

        ValueTask<ChatFlowJump<T>> InnerAwaitDateAsync(IChatFlowContext<T> context, CancellationToken token)
            =>
            context.IsCardSupported()
            ? context.InnerAwaitAsync(
                optionFactory, CreateDateAdaptiveCardActivity, ParseDateFormAdaptiveCard, validator, messageFactory, mapFlowState, token)
            : context.InnerAwaitAsync(
                optionFactory, CreateMessageActivity, ParseDateFromText, validator, messageFactory, mapFlowState, token);
    }

    private static async ValueTask<ChatFlowJump<T>> InnerAwaitAsync<T>(
        this IChatFlowContext<T> context,
        Func<IChatFlowContext<T>, DateStepOption> optionFactory,
        Func<ITurnContext, DateStepOption, IActivity> activityFactory,
        Func<ITurnContext, DateCacheJson, Result<DateOnly, BotFlowFailure>> dateParser,
        Func<IChatFlowContext<T>, DateOnly, Result<DateOnly, BotFlowFailure>>? validator,
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
            return await dateParser.Invoke(context, cacheJson).Forward(InnerValidate).FoldValueAsync(SuccessAsync, RepeatAsync).ConfigureAwait(false);

            Result<DateOnly, BotFlowFailure> InnerValidate(DateOnly date)
                =>
                validator is null ? date : validator.Invoke(context, date);
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
                var invalidDateActivity = context.CreateTextActivity(flowFailure.UserMessage);
                await context.SendActivityAsync(invalidDateActivity, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(flowFailure.LogMessage) is false || flowFailure.SourceException is not null)
            {
                context.Logger.LogError(flowFailure.SourceException, "{logMessage}", flowFailure.LogMessage);

                var properties = new Dictionary<string, string>
                {
                    ["flowId"] = context.ChatFlowId,
                    ["message"] = flowFailure.LogMessage
                };

                if (flowFailure.SourceException is not null)
                {
                    properties["errorMessage"] = flowFailure.SourceException.Message ?? string.Empty;
                    properties["errorType"] = flowFailure.SourceException.GetType().FullName ?? string.Empty;
                    properties["stackTrace"] = flowFailure.SourceException.StackTrace ?? string.Empty;
                }

                context.BotTelemetryClient.TrackEvent($"{context.ChatFlowId}StepAwaitDateFailure", properties);
            }

            return context.RepeatSameStateJump();
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

    private static Activity CreateTextActivity(this ITurnContext context, string text)
    {
        if (context.IsNotTelegramChannel())
        {
            return MessageFactory.Text(text);
        }

        return BuildTelegramParameters(text).BuildActivity();

        static TelegramParameters BuildTelegramParameters(string text)
            =>
            new(text)
            {
                    ParseMode = TelegramParseMode.Html
            };
    }
}