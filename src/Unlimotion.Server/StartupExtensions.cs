using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.Revisions;
using Raven.Client.Documents.Session;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Raven.Embedded;
using Unlimotion.Server.Domain;

namespace Unlimotion.Server
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddRavenDbServices(this IServiceCollection services)
        {
            services.AddSingleton<DatabaseRecord>(serviceProvider =>
            {
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var dbName = configuration.GetSection("RavenDb:DatabaseRecord").GetValue<string>("DatabaseName");
                var dbRecord = new DatabaseRecord(dbName);
                return dbRecord;
            });
            services.AddSingleton<DatabaseOptions>(serviceProvider =>
            {
                var dbRecord = serviceProvider.GetRequiredService<DatabaseRecord>();
                var dbOptions = new DatabaseOptions(dbRecord);

                //Задать способ поиска имени коллекции
                dbOptions.Conventions = new DocumentConventions
                {
                    FindCollectionName = type => type.Name,
                };

                return dbOptions;
            });
            services.AddSingleton<ServerOptions>(serviceProvider =>
            {
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
                var serverOptions = configuration.GetSection("RavenDb:ServerOptions").Get<ServerOptions>();

                //Ищем подходящую версию DotNet для запуска процесса RavenDB
                try
                {
                    serverOptions.FrameworkVersion = serviceProvider.GetRequiredService<DotNetVersionHelper>()
                        .GetNearestDotNetVersion(serverOptions.FrameworkVersion);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }

                //Получаем путь запуска приложения, так как RavenDB в Embeddeed режиме запускается через командную строку
                //Следовательно при упаковке в единый файл, данные будут лежать в папке Temp
                //Чтобы этого избежать используем исправления ниже
                var path = environment.ContentRootPath;
                //Исправляем путь к базе
                serverOptions.DataDirectory = Path.Combine(path, serverOptions.DataDirectory);
                //Исправляем путь к логам
                serverOptions.LogsPath = Path.Combine(path, serverOptions.LogsPath);

                return serverOptions;
            });
            services.AddSingleton<EmbeddedServer>(serviceProvider =>
            {
                if (!EmbeddedServer.Instance.IsRunning())
                {
                    var serverOptions = serviceProvider.GetRequiredService<ServerOptions>();
                    EmbeddedServer.Instance.StartServer(serverOptions);
                }

                return EmbeddedServer.Instance;
            });
            services.AddSingleton<IDocumentStore>(serviceProvider =>
            {
                //Запускаем сервер
                var embeddedServer = serviceProvider.GetRequiredService<EmbeddedServer>();

                var databaseOptions = serviceProvider.GetRequiredService<DatabaseOptions>();

                //Создаётся база, если не существует
                var documentStore = embeddedServer.GetDocumentStore(databaseOptions);
                documentStore.Initialize();

                //Получаем настройки базы
                var databaseRecord = documentStore.Maintenance.Server.Send(new GetDatabaseRecordOperation(documentStore.Database));

                //Если настроек ревизий нет
                if (databaseRecord.Revisions == null)
                {
                    documentStore.ConfigureRevisions();
                }
                var session = documentStore.OpenSession();
                var haveAnyChat = session.Query<Chat>().Any();
                if (!haveAnyChat)
                {
                    var firstChat = new Chat()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ChatName = "SkillBoxChat",
                        ChatType = ChatType.Public,
                        OwnerId = ""
                    };
                    session.Store(firstChat);
                    session.SaveChanges();
                }

                return documentStore;
            });
            services.AddTransient<IDocumentSession>(serviceProvider =>
            {
                var session = serviceProvider
                    .GetRequiredService<IDocumentStore>()
                    .OpenSession();
                return session;
            });
            services.AddTransient<IAsyncDocumentSession>(serviceProvider =>
            {
                var session = serviceProvider
                    .GetRequiredService<IDocumentStore>()
                    .OpenAsyncSession();
                return session;
            });
            return services;
        }

        public static void ConfigureRevisions(this IDocumentStore store)
        {
            store.Maintenance.Send(new ConfigureRevisionsOperation(new RevisionsConfiguration
            {
                Default = new RevisionsCollectionConfiguration
                {
                    Disabled = false,
                    PurgeOnDelete = false,
                }
            }));
        }

        public static bool IsRunning(this EmbeddedServer instance)
        {
            // HACK: check RavenDB running state using private field to avoid multiple starts
            // https://github.com/ravendb/ravendb/blob/release/v4.2/src/Raven.Embedded/EmbeddedServer.cs#L45-L46
            const BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
            var result = instance.GetType().GetField("_serverTask", bindingFlags)?.GetValue(instance);
            return result != null;
        }
    }
}