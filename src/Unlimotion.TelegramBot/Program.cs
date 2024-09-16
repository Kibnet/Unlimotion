using Microsoft.Extensions.Configuration;
using Serilog;
using Telegram.Bot.Types;
using WritableJsonConfiguration;

namespace Unlimotion.TelegramBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create("appsettings.json");
            
            // Настройка логгирования через Serilog
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                Log.Information("Запуск бота");
                await Bot.StartAsync(configuration);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Бот завершился с ошибкой");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}