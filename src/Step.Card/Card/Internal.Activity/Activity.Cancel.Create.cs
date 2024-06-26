using System.Web;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;

namespace GarageGroup.Infra.Bot.Builder;

partial class CardActivity
{
    internal static Activity CreateCancellationActivity(this ITurnContext context, string text)
    {
        if (context.IsNotTelegramChannel())
        {
            return MessageFactory.Text(text);
        }

        var telegramParameters = new TelegramParameters(HttpUtility.HtmlEncode(text))
        {
            ParseMode = TelegramParseMode.Html,
            ReplyMarkup = new TelegramReplyKeyboardRemove()
        };

        return telegramParameters.BuildActivity();
    }
}